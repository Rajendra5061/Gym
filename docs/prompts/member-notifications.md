# Prompt — Automatic member notifications over email and WhatsApp

Build a single, automatic member-notification system in this repo so that a member is reached on
their own email address and their own WhatsApp number, with no staff action, on each of these six
occasions:

| # | Occasion | When it fires | Channel |
|---|----------|---------------|---------|
| 1 | **New membership payment** | a first payment for a member is confirmed (`PaymentStatus.Paid`) | email + WhatsApp |
| 2 | **Renewal payment** | a payment is confirmed for a member who already had a subscription | email + WhatsApp |
| 3 | **Diet plan** | a `DietPlan` is created for the member, or its meals are revised | email + WhatsApp |
| 4 | **Attendance streak** | the current streak reaches a milestone (7, 14, 21, 30, 50, 100 days) | WhatsApp, email optional |
| 5 | **Birthday wish** | on the day and month of `Member.DateOfBirth`, during the daily run | email + WhatsApp |
| 6 | **Festival wish** | on a configured festival date, to every active member | email + WhatsApp |

## What already exists — reuse it, do not reinvent it

- `IEmailSender` / `EmailMessage` (`GymManagement.Application/Interfaces/IEmailSender.cs`) — HTML plus
  plain-text body, attachments, `IsEnabled`, `SendAsync` returning `EmailDeliveryResult`.
- `ISmsSender` / `SmsMessage` (`GymManagement.Application/Interfaces/ISmsSender.cs`) and the generic
  HTTP gateway adapter in `GymManagement.Infrastructure/Messaging/SmsSenders.cs` — `{to}` / `{text}`
  placeholders in URL and body template, credentials carried in headers.
- `ExpiryReminderMailer` (`GymManagement.Infrastructure/Services/ExpiryReminderMailer.cs`) — the
  reference implementation of a scheduled, two-channel, idempotent, never-throwing mailer.
- `ExpiryReminderEmail` (`GymManagement.Domain/Entities/ExpiryReminderEmail.cs`) — the dedup-row
  pattern: unique `(MemberId, SentOnDate)` index, per-channel `EmailSent` / `SmsSent` flags.
- `PaymentReceiptMailer` — the "send only after the money transaction has committed, stamp the row
  after a successful send, never throw" pattern, already called from `PaymentService` and
  `SubscriptionService`.
- `DailyAlertsHostedService` — the daily timer driven by `Notifications:DailyAlerts:Enabled` and
  `:Hour`, which deliberately never fires on start-up.
- `DashboardService.ComputeStreaks(sortedDays, today)` — current and best streak are already computed.
- `ISettingsService.GetGymBrandingAsync` — gym name for the message signature.

## Requirements

### 1. WhatsApp is its own channel, not SMS

Add `IWhatsAppSender` alongside `ISmsSender`, mirroring its contract exactly (`IsEnabled`,
`ProviderName`, `SendAsync` returning a delivery result, transport failure surfacing as an
exception). WhatsApp Business messaging outside the 24-hour customer-service window requires an
**approved template**, not free text, so `WhatsAppMessage` must carry a template name, a language
code and an ordered list of body parameters — not a pre-rendered string. Ship three providers,
selected by `WhatsApp:Provider`:

- `None` — accepts and discards; the default everywhere outside Development, so a fresh checkout
  messages nobody and throws nothing.
- `File` — writes one readable `.txt` per message to `logs/whatsapp-drop`; the Development default,
  so building this feature can never buzz a real phone.
- `Http` — a generic gateway adapter in the same shape as `SmsHttpOptions` (URL, method, content
  type, body template with `{to}` / `{template}` / `{params}` placeholders, verbatim headers,
  timeout), so it works against the WhatsApp Cloud API, Twilio, MSG91 or Gupshup with no code change.

Credentials come from configuration only — `WhatsApp__Http__Headers__Authorization` via environment
variables or user secrets. **Never from the database**: the settings table is readable by any
administrator over `GET /api/settings`.

Normalise recipient numbers to E.164 before sending, using a configurable default country code
(`WhatsApp:DefaultCountryCode`, default `+91`). A number that cannot be normalised is skipped and
logged, never sent malformed.

### 2. One dispatcher, six occasions

Add `IMemberNotifier` in the Application layer with one method per occasion — for example
`NotifyPaymentAsync(paymentId, isRenewal, ct)`, `NotifyDietPlanAsync(dietPlanId, ct)`,
`NotifyStreakAsync(memberId, streakDays, ct)`, `NotifyBirthdayAsync(memberId, ct)`,
`NotifyFestivalAsync(memberId, festivalKey, ct)`.

Every one of them:

- **Never throws.** A dead relay, a refused gateway or a bad address is logged and dropped. The
  payment, the diet plan and the check-in are already recorded; a failed wish must not undo them.
- **Attempts each channel independently.** Email failing must not stop WhatsApp, and vice versa.
- **Is idempotent.** Add one `MemberNotificationLog` entity — member, occasion kind, a
  `DeduplicationKey`, the local `SentOnDate`, and `EmailSent` / `WhatsAppSent` flags — with a unique
  index on `(MemberId, Kind, DeduplicationKey)`. A restart, a retried request, a manual re-run or two
  instances racing must never double-message a member. Use the payment id as the key for #1 and #2,
  the diet plan id plus its revision for #3, the milestone value for #4, and the year for #5 and #6.
- **Sends nothing sensitive.** Amounts, plan names and dates are fine; no card number, no CVV, no UPI
  PIN, no payer VPA, no password — ever.

### 3. Triggering

- **#1 / #2 payment** — call the notifier from the same places `IPaymentReceiptMailer` is already
  called in `PaymentService` and `SubscriptionService`, after the transaction has committed. Decide
  new-versus-renewal by whether the member has any earlier subscription; the two messages differ in
  wording ("Welcome to {gym}" versus "Membership renewed until {date}").
- **#3 diet plan** — call from `DietPlanService` after a create and after a meal-list update.
- **#4 streak** — evaluate on check-in in `AttendanceService`, reusing `ComputeStreaks`. Only
  milestone crossings notify; a 9-day streak sends nothing.
- **#5 birthday / #6 festival** — extend `DailyAlertsHostedService.RunOnceAsync` with a third,
  independently-failing block that runs a new `IWishesDispatcher`. Birthdays match day and month
  against `Member.DateOfBirth` for members with `MemberStatus.Active`; a 29 February birthday is
  greeted on 28 February in non-leap years. Festivals come from a `Festivals` configuration section
  — a list of `{ Key, Name, Date, Greeting }` — plus an admin-editable settings entry, so a gym can
  add Diwali, Eid, Christmas or Sankranti without a deployment.

### 4. Opt-out and admin control

- Add opt-out flags to `Member`, keeping the marketing/wishes opt-out separate from the transactional
  one. Payment receipts and diet plans are transactional; birthday and festival wishes are not, and a
  member who has opted out of wishes must receive none of them.
- Extend the admin Settings page (`frontend/src/pages/admin/SettingsPage.tsx`) with a Notifications
  section showing, per occasion, which channels are on, plus the resolved provider names. Reuse the
  `Describe()` pattern from `IEmailSender` so no secret is ever returned over the API.
- Configuration keys live under `Notifications:Member:<Occasion>:{Email,WhatsApp}`, each defaulting
  to `true` but inert while the underlying provider is `None`.

### 5. Message content

Write both an HTML body (inline styles only, no external images, scripts or web fonts) and a
standalone plain-text alternative for every email, exactly as `MailMessageFactory` already does.
Keep WhatsApp bodies to one short paragraph. Sign every message with the gym name from
`ISettingsService.GetGymBrandingAsync`, falling back to "your gym".

### 6. Tests

Add unit tests under `backend/tests/GymManagement.UnitTests/Services/` covering: dedup blocks the
second send; a throwing email sender still lets WhatsApp through; an opted-out member gets no wishes
but still gets receipts; 29 February falls back to 28 February; a non-milestone streak sends nothing;
a member with neither email nor phone is skipped without error.

## Deliverables

Migrations for the new entity and the `Member` columns, DI registration in
`GymManagement.Infrastructure/DependencyInjection.cs`, `appsettings.json` defaults carrying the same
kind of `_comment` warning the `Email` and `Sms` sections already have, and the Settings UI section.

Run `dotnet build` and `dotnet test`, and report the results.
