# Prompt — Installable app + push notifications (enterprise, on-premise)

Add push notifications as a **third delivery channel** alongside the existing email and WhatsApp
channels, and make the React SPA **installable**, so a member taps an icon on their home screen and
receives notifications with the app closed.

This is a single-tenant, licensed, on-premise product (`License.GymIdentifier`, `License.MachineId`,
a local SQL Server instance, backups to a local folder). Every decision below follows from that.

## Non-negotiable constraints — read these before designing anything

1. **No Firebase project, no vendor SDK, no service-account credential.** Use the **W3C Web Push
   Protocol (RFC 8030) with VAPID (RFC 8292)** directly. It is a browser standard: the server signs a
   JWT with its own VAPID key pair and POSTs an encrypted payload to the endpoint URL the browser
   handed it. Chrome, Edge, Firefox, Opera and Safari 16.4+ all support it. Firebase Cloud Messaging
   gives this product **nothing** that the standard does not — it only earns its place if a *native*
   Android/iOS app is built later, which is why `IPushSender` below is an interface with a pluggable
   provider rather than a hard dependency. Adding a Google project, a service-account JSON to
   distribute with every on-premise install, and 200 KB of SDK to the bundle, for a capability the
   browser already has, is the wrong trade for a self-hosted product.

2. **HTTPS is mandatory and is probably a blocker today.** Service workers and the Push API require a
   secure context. `http://` works *only* on `localhost`. The current `Cors:AllowedOrigins` lists
   plain-HTTP origins, so a gym running this on a LAN box over `http://192.168.x.x` will get **no
   push at all, silently** — the API call just never resolves. Before writing any code, establish how
   each install gets a certificate (reverse proxy with Let's Encrypt on a real hostname, or an
   internal CA), and if the answer is "it does not", say so and stop: the feature cannot ship to
   those installs. Document the requirement in `docs/ARCHITECTURE.md` and fail loudly at startup with
   a clear log line when the API is not served over TLS.

3. **Push is additive and never the sole channel for anything that matters.** iOS delivers web push
   only on 16.4+ *and* only after the member has added the app to their home screen. A large share of
   members will never have it. Payment confirmations, renewal reminders and anything time-critical
   must continue to go by email/WhatsApp regardless of push. Push is a nudge, not a system of record.

4. **The install must degrade to nothing.** An on-premise box may have no outbound internet at all.
   With no route to the browser vendors' push services, the app must fall back silently to the
   existing in-app `Notification` rows and the email/WhatsApp channels — logged once at startup, not
   as an error per message.

## Part 1 — Installable SPA (PWA)

In `frontend/`:

- `public/manifest.webmanifest`: gym name from settings, `short_name`, `start_url: "/"`,
  `display: "standalone"`, `id`, theme/background colours drawn from the existing CSS custom
  properties, and icons at 192×192, 512×512 plus a 512×512 `maskable` variant. Link it from
  `index.html` with `theme-color` and the Apple touch-icon tags — iOS ignores manifest icons.
- A **versioned** service worker (`public/sw.js`) registered from `src/main.tsx` after the app mounts,
  so a failed registration can never block first paint. Precache the app shell keyed by build hash;
  **never cache API responses** — a stale membership or payment figure is worse than a spinner. On
  activate, delete caches from prior versions. Do not call `skipWaiting()` unconditionally: show the
  member an "Update available — reload" affordance, so a deploy mid-session cannot swap the bundle
  under an open form.
- An **install prompt**: capture `beforeinstallprompt`, stash it, show a dismissible
  "Add {gym} to your home screen" bar on the member dashboard, and remember the dismissal in
  `localStorage`. iOS never fires that event — detect iOS Safari and show the
  "Share → Add to Home Screen" instructions instead.
- The install bar and the notification-permission ask are **two separate steps, in that order**.
  Never call `Notification.requestPermission()` on load: a dismissal permanently blocks the origin in
  Chrome, and one badly-timed prompt costs every future notification for that member forever. Ask
  only after an explicit "Turn on reminders" tap, having first shown what they will receive.

## Part 2 — Subscriptions, consent and retention

- `MemberPushDevice`: `MemberId`, `Endpoint` (unique), the `P256dh` and `Auth` subscription keys, a
  platform/user-agent string, `CreatedAtUtc`, `LastSeenUtc`, `LastSuccessAtUtc`, `FailureCount`,
  `IsActive`, and `ConsentGivenAtUtc` / `ConsentWithdrawnAtUtc`.
- **Encrypt `P256dh` and `Auth` at rest.** They are the keys that decrypt the member's notification
  payloads; a database backup on a gym's file share must not hand them over in the clear. Use the
  same protection route as the other secrets in this codebase — configuration-supplied key, never a
  key in the database.
- **Consent is a record, not a browser setting.** The DPDP Act 2023 requires demonstrable consent:
  store when it was given, what for, and when it was withdrawn. "Turn off reminders" must deactivate
  the row server-side, not merely hide the UI. Deleting a member — including through the existing
  recycle bin purge — must delete their devices.
- Endpoints: `POST /api/push/devices` and `DELETE /api/push/devices` (authenticated). Resolve the
  member **from the JWT**, never from a body field, or one member could subscribe to another's
  notifications. Re-register on every app start so a rotated endpoint never goes stale; registration
  must be idempotent on `Endpoint`.
- **Retention**: purge devices inactive beyond a configured window (default 180 days) in the daily
  job. A dead subscription kept forever is a liability with no upside.

## Part 3 — The sending side, made durable

- `IPushSender` in `GymManagement.Application/Interfaces/`, mirroring `IEmailSender` / `ISmsSender` /
  `IWhatsAppSender` exactly: `IsEnabled`, `ProviderName`, `SendAsync(PushMessage, ct)` returning a
  delivery result, transport failure surfacing as an exception. Providers selected by
  `Push:Provider`: `None` (default outside Development), `File` (writes to `logs/push-drop`; the
  Development default, so building this can never buzz a real phone), and `WebPush` (the standard
  protocol above). Leave room for an `Fcm` provider without building it.
- **VAPID keys come from configuration only** — `Push__Vapid__PrivateKey` via environment variables,
  user secrets or the platform secret store — never `appsettings.json` and never the settings table,
  which any administrator reads back over `GET /api/settings`. Generate the pair per install, not once
  for the product: one leaked key must not compromise every gym. Document the **rotation procedure**,
  because rotating VAPID keys invalidates every existing subscription and requires all members to
  re-subscribe — the step everyone discovers too late.
- **Add a transactional outbox.** Today's channels are fire-and-forget after commit, which is
  acceptable for a receipt but not for a channel whose whole promise is reliable delivery: a push
  attempted while the box is rebooting is simply lost. Write a `NotificationOutbox` row in the *same
  transaction* as the business change, and have a `BackgroundService` drain it with exponential
  backoff, a capped attempt count, and a dead-letter state an operator can see and re-drive. Use a
  SQL Server table and a hosted service — **no Redis, RabbitMQ or Hangfire**: an on-premise install
  cannot be asked to run extra infrastructure.
- Wire push into the existing `IMemberNotifier` as a third channel: same `MemberNotificationLog`
  dedup row (add `PushSent`), same independent per-channel failure handling, same
  `Notifications:Member:<Occasion>:Push` toggle. Every occasion already built — payment, renewal,
  diet plan, streak, birthday, festival — gains push without new dispatch code.
- **Prune dead subscriptions.** `404` or `410 Gone` from the push service means the member
  uninstalled: deactivate that device immediately. `429` and `503` carry `Retry-After` — honour it
  rather than hammering. Anything else increments `FailureCount`, and a device deactivates after a
  configured threshold.
- **Batch and bound.** A festival wish to 2,000 members is 2,000 HTTPS requests; cap concurrency,
  respect per-service rate limits, and make the whole sweep cancellable at shutdown.

## Part 4 — Admin-triggered push to one member

- `POST /api/notifications/push`, permission-gated to admin/staff: `{ memberId, title, body, link? }`,
  enqueued to the outbox for every active device, returning per-device outcomes. **Audit-log it** with
  the existing `IAuditService` — a staff member pushing to a member's phone is an action that must be
  attributable, and this is the endpoint most open to misuse.
- Rate-limit it per operator through the existing `RateLimiting` configuration. Without a cap, one
  compromised staff account can spam every member.
- A "Send reminder" action on the member row and detail page in `frontend/src/pages/admin/`, plus a
  history panel reading `MemberNotificationLog` so staff can see what already went out.
- Extend `DailyAlertsHostedService` so the existing expiry reminders enqueue push alongside
  email/text.

## Part 5 — Receiving

- Foreground: `onMessage`-equivalent renders an in-app toast, not a system banner — a system
  notification for the app you are looking at is noise.
- Background: the service worker's `push` handler shows the notification; a `notificationclick`
  handler focuses an existing tab if one is open, otherwise opens the deep link. This is what makes
  "tap the icon, land on the right screen" work.
- **Nothing sensitive in a payload.** It renders on a locked screen and is decrypted client-side but
  visible to anyone holding the phone. Plan names and dates are fine; no amounts owed, no card
  details, no UPI PIN, no OTP, no password.

## Observability and operations

- Structured logs per attempt with the outcome and the masked endpoint — reuse the existing `Mask`
  helper; no raw endpoints or keys in logs, which are read by more people than a mailbox is.
- Counters for enqueued / sent / failed / dead-lettered per channel, and an admin Settings panel
  reporting the resolved provider per channel via the existing `Describe()` pattern, so no secret is
  ever returned over the API.
- A health check covering: VAPID keys present, outbox depth under threshold, dead-letter count zero.
- Startup log lines stating plainly whether push is on, and if off, exactly why (no TLS, no keys, no
  provider) — an operator must never have to guess.

## Tests, migrations, deliverables

Unit tests for: registration is idempotent on endpoint; `410 Gone` deactivates that device and leaves
the member's other devices untouched; a member with no devices is skipped without error; the dedup row
still blocks a second send when only push succeeded; outbox retry backs off and dead-letters at the
cap; consent withdrawal stops delivery. Plus the EF migration, DI registration in
`GymManagement.Infrastructure/DependencyInjection.cs`, `appsettings.json` defaults carrying the same
`_comment` credential warning the `Email`, `Sms` and `WhatsApp` sections already carry, the admin UI,
and the TLS requirement written into `docs/ARCHITECTURE.md`.

Run `dotnet build`, `dotnet test` and `npm run build`, and report the results.
