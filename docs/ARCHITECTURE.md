# Architecture

A complete gym management product: a **React single-page application** talking to an
**ASP.NET Core 8 Web API** backed by **SQL Server**. Clean architecture, JWT authentication,
role- and permission-based authorization, server-side validation, transactional billing, audit
logging, soft delete with a recycle bin, Excel/PDF reporting and a trial/licence system.

> The original WPF desktop client was removed in favour of the React frontend. Everything below
> the API boundary is unchanged and was already verified working end to end.

---

## 1. Repository layout

The repository is split into `backend/` and `frontend/` at the top level. Every path in this
document is relative to the repository root, and every command below is meant to be run from
there.

```
README.md
backend/
├─ GymManagement.sln                Solution: the four src projects, both test projects, SetPassword.
├─ Directory.Build.props            Shared build settings for every project under backend/.
├─ global.json                      Pins the .NET SDK.
├─ src/
│  ├─ GymManagement.Domain          Entities, enums, permission catalogue. No dependencies.
│  ├─ GymManagement.Application     DTOs, service interfaces, FluentValidation validators.
│  ├─ GymManagement.Infrastructure  EF Core, DbContext, migrations, service implementations, JWT,
│  │                                BCrypt, licensing, backup, Excel/PDF exporters.
│  └─ GymManagement.Api             Controllers, middleware, filters, authorization, Program.cs.
├─ tests/
│  ├─ GymManagement.UnitTests
│  └─ GymManagement.IntegrationTests
└─ tools/SetPassword                Local utility for setting a user's password.
frontend/                           React + TypeScript + Vite application.
docs/                               This document, the test catalogue and UI screenshots.
```

Project references inside `backend/` are all relative to one another, so the split changed no
`.csproj` and no solution path.

Dependency direction is strictly inward:

```
Api ──► Infrastructure ──► Application ──► Domain
frontend ──► (HTTP only) ──► Api
```

The browser never reaches SQL Server; every operation goes through the API.

---

## 2. Frontend (`frontend/`)

| Concern | Choice |
|---|---|
| Build | Vite 5 |
| UI | React 18 + TypeScript (`strict: true`) |
| Routing | React Router 6 |
| Charts | Recharts |
| Styling | Design tokens plus hand-written stylesheets — no CSS framework |
| State | React hooks + a small `AuthContext`; no global store |
| Imports | `@/…` resolves to `frontend/src/…` (declared in both `tsconfig.json` and `vite.config.ts`) |

```
frontend/src/
├─ main.tsx             mounts <App/> and imports styles/base.css
├─ App.tsx              every route, plus the Protected gate that enforces sign-in and area
├─ api/
│  ├─ client.ts         fetch wrapper: envelope unwrapping, JWT, refresh-and-retry, ApiError
│  ├─ types.ts          DTO types mirroring the backend
│  └─ endpoints/        one module per API area — members, trainers, plans, subscriptions,
│                       payments, expenses, attendance, workouts, operations, notifications,
│                       reports, dashboard, system, member
├─ auth/AuthContext.tsx session restore, sign-in/out, `can` / `canAny` / `isInRole`, gym branding
├─ components/          ui.tsx (PageCard, Field, Pill, Pager, Modal, Loading, …), icons.tsx,
│                       PublicNav.tsx
├─ layouts/AppLayout.tsx  the signed-in shell: grouped navbar + <Outlet/>
├─ lib/format.ts        money, date, initials, relative time
├─ pages/
│  ├─ public/           home, admin sign-in, member sign-in, forgot password  (+ public.css)
│  ├─ admin/            the staff console — members, plans, subscriptions, payments, attendance,
│  │                    workouts, equipment, enquiries, feedback, reports, expenses,
│  │                    notifications, users, roles, audit, recycle bin, settings
│  │                    (+ admin.css, billing.css, ops.css)
│  └─ member/           self-service: dashboard, profile, membership, attendance, payments,
│                       workout plans, feedback  (+ member.css)
└─ styles/              tokens.css, base.css
```

**Stylesheets.** Two are global and five belong to a page group:

| File | Scope |
|---|---|
| `styles/tokens.css` | The palette, radii, spacing and type scale as CSS custom properties. |
| `styles/base.css` | Resets, app chrome (navbar, menus), page shell, tables, forms, buttons. `@import`s `tokens.css` and is imported once from `main.tsx`. |
| `pages/public/public.css` | Marketing home page and the sign-in screens. Also used by `PublicNav`. |
| `pages/admin/admin.css` | Dashboard, members and trainers. |
| `pages/admin/billing.css` | Plans, subscriptions, payments and attendance. |
| `pages/admin/ops.css` | Equipment, enquiries, feedback, expenses, notifications and reports. |
| `pages/member/member.css` | Every member self-service screen. |

Each page imports the stylesheet for its own group, so the group sheets are code-split with the
pages that need them rather than loaded up front.

### Admin navigation

The admin area has **19 destinations**, which do not fit on one row at the 1280px minimum width
the layout targets. `AppLayout.tsx` therefore renders seven top-level entries, five of which are
dropdown menus:

| Entry | Contents |
|---|---|
| Dashboard | direct link |
| Members | Members · Trainers |
| Billing | Membership Plans · Subscriptions · Payments · Expenses |
| Activity | Attendance · Workout Plans · Equipment |
| Engagement | Enquiries · Feedback · Notifications |
| Reports | direct link |
| System | Users · Roles · Audit Logs · Recycle Bin · Settings |

Every entry carries the permission code of the screen it opens. Entries the signed-in user cannot
reach are dropped, and a group whose children are all dropped disappears with them, so a Staff or
Trainer account sees a shorter bar rather than menus that lead to 403s. The open menu closes on a
route change, on `Escape` and on a click outside the nav; the group whose section is currently
active is highlighted.

The member area has seven destinations, which do fit on one row, so none of them are grouped.

**Auth flow.** `client.ts` attaches the bearer token, and on a 401 performs a single
refresh-and-retry, collapsing concurrent 401s into one refresh call. A failed refresh clears the
session and notifies `AuthContext`, which bounces the user to sign-in. Tokens live in
`localStorage`; the password is only ever held in the sign-in form's local state.

**Authorization in the UI is a convenience, not a control.** `can(...)` hides and disables
controls, but every endpoint re-checks the permission server-side.

---

## 3. Running it

Two processes, in this order:

```bash
# 1. API — migrations and seed data are applied on start
#    (set Database:AutoMigrate=false in production and apply them in a release pipeline)
dotnet run --project backend/src/GymManagement.Api
#    https://localhost:7135  ·  Swagger at /swagger  ·  health at /health

# 2. frontend
cd frontend && npm install && npm run dev
#    http://localhost:5173  ·  /api and /health proxy to the API
```

The Vite dev server proxies `/api` and `/health` to `https://localhost:7135` with `secure: false`,
so the browser sees a same-origin URL and the local self-signed certificate is accepted in
development. For production, build with `npm run build` and serve `frontend/dist` behind the same
origin as the API (or configure CORS via `Cors:AllowedOrigins`).

Tests:

```bash
dotnet test backend/GymManagement.sln    # unit + integration
cd frontend && npm run typecheck         # the only frontend gate; there is no test runner
```

### Sign-in

| | |
|---|---|
| User name | `admin` |
| Password | `123@` |

`123@` was set for local development at the owner's request. It does **not** satisfy the password
policy the API enforces on password *changes* (minimum 8 characters, at least one letter and one
digit), so the change-password screen will refuse to reuse a value of that shape. Sign-in itself
does not apply the policy. Use a strong password before any real deployment — either through the
UI or with:

```bash
dotnet run --project backend/tools/SetPassword -- <userName> <newPassword>
```

---

## 4. Database

```
Server=.\SQLEXPRESS01;Database=GymDatabase;Integrated Security=True;TrustServerCertificate=True;MultipleActiveResultSets=True
```

Override with the `ConnectionStrings__DefaultConnection` environment variable.

Migrations live in `backend/src/GymManagement.Infrastructure/Data/Migrations`. Both EF Core
commands need the same project pair — the migrations assembly and the startup project that
supplies configuration:

```bash
dotnet ef migrations add <Name> --project backend/src/GymManagement.Infrastructure --startup-project backend/src/GymManagement.Api
dotnet ef database update       --project backend/src/GymManagement.Infrastructure --startup-project backend/src/GymManagement.Api
```

> **Stop the API before running these.** A running instance locks the output assemblies, and the
> command fails on a file copy — nothing to do with the database. To read the migration list
> without stopping anything, add `--no-build`, which uses the assemblies already on disk:
>
> ```bash
> dotnet ef migrations list --no-build --project backend/src/GymManagement.Infrastructure --startup-project backend/src/GymManagement.Api
> ```
>
> That is the command used to confirm the paths above; it prints
> `20260817154806_InitialCreate` and `20260818135043_AddOperationsModules`.

Seeded on first run, in this order: the permission catalogue, four system roles (Admin, Staff,
Trainer, Member), gym settings, system settings, six payment methods, expense categories, six
membership plans, an exercise library, seven pieces of equipment, two sample enquiries and the
administrator account. Every step is guarded, so seeding is safe to repeat.

### Adding a permission code — read this first

`DbSeeder.SeedRolesAsync` grants a role its default permission set **only when that role has no
permissions at all**:

```csharp
var hasPermissions = await _db.RolePermissions.AnyAsync(rp => rp.RoleId == role.Id, ct);
if (hasPermissions) continue;
```

That guard exists so an administrator's customisations are never overwritten by a restart, and it
has a consequence that is easy to miss:

> **Adding a new code to `Permissions.All` does not grant it to any existing role.** The permission
> row itself is inserted — `SeedPermissionsAsync` adds codes that are missing — but every role that
> already holds at least one permission is skipped, including the four system roles. Adding the
> code to `Permissions.ForRole(...)` changes nothing for a database that has already been seeded.

This already bit the operations modules: `equipment.*`, `enquiries.*` and `feedback.*` appeared in
the catalogue and on the endpoints, while the existing Staff and Member roles held none of them,
so those screens 403'd for everyone except Admin (who implicitly holds every permission). The
grants had to be applied deliberately — through the roles and permissions screen, or with SQL
against `RolePermissions` — before Staff and Member behaved as `ForRole` describes.

When you add a permission, do all three: add it to `Permissions.All`, add it to the relevant
`ForRole` arrays for the benefit of a fresh database, and grant it to the existing roles in any
database that is already seeded.

The defaults `ForRole` describes are: Admin every code, Staff 22, Trainer 10, Member 2
(`notifications.view` and `feedback.submit`).

---

## 5. Security

- **Passwords** — BCrypt, work factor 12. Never stored or logged in plain text.
- **JWT** — short-lived access token plus a rotating refresh token; only a SHA-256 hash of the
  refresh token is persisted. Presenting an already-rotated token revokes the whole family.
- **Lockout** — configurable threshold and window; every attempt is recorded.
- **Authorization** — `[HasPermission("code")]` on each endpoint, resolved by a policy provider.
  Admins implicitly hold every permission. See §4 before adding a new code.
- **Validation** — FluentValidation runs in a global action filter before any controller code.
- **Money** — every amount is `decimal`, and the client never decides a payable total: it requests
  a quote and the server recalculates from the plan on save.
- **Errors** — a global middleware maps exceptions to status codes and returns a generic message
  for unhandled failures; stack traces never reach a client.
- **Rate limiting** — 200 requests/minute globally, 10/minute on sign-in and forgot-password.
- **Secrets** — `appsettings.json` holds development placeholders only. `Jwt:Secret` and
  `License:Secret` must come from environment variables or a secret store; the API refuses to
  start if `Jwt:Secret` is missing or shorter than 32 characters.

---

## 6. API surface

`/api/auth` · `/api/users` · `/api/roles` · `/api/members` · `/api/trainers` · `/api/exercises` ·
`/api/workouts` · `/api/attendance` · `/api/membership-plans` · `/api/subscriptions` ·
`/api/payments` · `/api/expenses` · `/api/equipment` · `/api/enquiries` · `/api/feedback` ·
`/api/reports` · `/api/notifications` · `/api/audit` · `/api/settings` · `/api/recycle-bin` ·
`/api/dashboard`

Every response uses the same envelope, which `client.ts` unwraps:

```json
{ "success": true, "message": null, "errorCode": null, "validationErrors": null,
  "traceId": "00-…", "timestampUtc": "2026-08-18T12:00:00Z", "data": { } }
```

---

## 7. Operations modules

Three modules were added after the initial release: **Equipment**, **Enquiries** and **Feedback**.
All three entities derive from `SoftDeletableEntity`, live in
`Domain/Entities/OperationsEntities.cs`, and their tables were created by the
`20260818135043_AddOperationsModules` migration (`Equipment`, `Enquiries`, `Feedback`, with the
foreign keys and the filtered indexes each screen sorts and filters on).

On the frontend all three share one endpoint module, `api/endpoints/operations.ts`, and one
stylesheet, `pages/admin/ops.css`.

### Equipment — the asset register

Fixed and loose gym assets: name, unique asset tag (`Code`, e.g. `EQP-TRD-001`), free-text
category, serial number, manufacturer, quantity, location, purchase date and cost, warranty
expiry, last-serviced and next-service-due dates, notes and an active flag. Condition is an enum:
`New`, `Good`, `NeedsService`, `UnderRepair`, `Retired`.

| Endpoint | Permission |
|---|---|
| `GET /api/equipment`, `GET /api/equipment/categories`, `GET /api/equipment/{id}` | `equipment.view` |
| `POST /api/equipment`, `PUT /api/equipment/{id}`, `DELETE /api/equipment/{id}`, `POST /api/equipment/{id}/restore` | `equipment.manage` |

Reached from **Activity → Equipment**. The seeder inserts seven sample records when the table is
empty.

### Enquiries — the lead pipeline

A prospective member captured at the desk, by phone or from the website: name, phone, optional
email, source (`WalkIn`, `Phone`, `Website`, `Referral`, `SocialMedia`, `Other`), the plan they
asked about, a message, follow-up date, the staff member chasing the lead and free-text notes.
Status runs `New → Contacted → FollowUp → Converted → Lost`.

| Endpoint | Permission |
|---|---|
| `GET /api/enquiries`, `GET /api/enquiries/{id}` | `enquiries.view` |
| `POST /api/enquiries`, `PUT /api/enquiries/{id}`, `POST /api/enquiries/{id}/convert/{memberId}`, `DELETE /api/enquiries/{id}`, `POST /api/enquiries/{id}/restore` | `enquiries.manage` |

Converting a lead records the member it became on the enquiry, so a signed-up lead stays linked to
the record that produced it. Reached from **Engagement → Enquiries**.

### Feedback — member suggestions and complaints

A member's subject, message and optional 1–5 star rating, with a status of `New`, `Reviewed`,
`Resolved` or `Dismissed`, and — once staff answer — the response text, the responder and the
timestamp. `IsPrivate` defaults to true, meaning the item is visible only to its author and to
staff.

This is the one module with three permissions, because members participate in it directly:

| Endpoint | Permission |
|---|---|
| `GET /api/feedback`, `GET /api/feedback/{id}` | `feedback.view` |
| `GET /api/feedback/mine`, `POST /api/feedback` | `feedback.submit` |
| `PUT /api/feedback/{id}`, `POST /api/feedback/{id}/respond`, `DELETE /api/feedback/{id}`, `POST /api/feedback/{id}/restore` | `feedback.manage` |

`feedback.submit` is what lets a member post their own feedback and read it back; it is one of the
two permissions the Member role holds by default. Staff reach the queue from **Engagement →
Feedback**, members from their own **Feedback** screen.

---

## 8. Known limitations

- **No email or SMS provider is wired up.** Nothing in the solution sends a message: there is no
  SMTP, SendGrid or Twilio client anywhere. Forgot-password returns the reset token in the API
  response, with a 30-minute lifetime, so an administrator can hand it over. `NotificationService`
  is an in-app notification centre — rows in a table that the UI reads — not a delivery channel.
  Connect a real provider before production.
- **UPI has no automatic verification.** A static QR cannot confirm a transfer, so a UPI payment is
  created as `AwaitingConfirmation` unless it is explicitly marked confirmed, and staff verify the
  reference. Integrate a payment gateway with server-side webhooks for real reconciliation.
- **Licence validation uses the local clock** plus a backwards-clock tamper check against a stored
  high-water mark. Every input — the clock, the local database, the shared secret in configuration
  — sits on the customer's machine. A commercial deployment should validate against a server-side
  licensing service.
- **`AuditService` shares the request's `DbContext`**, so an audit row participates in the caller's
  transaction — intentional, but it rolls back with a failed operation.
- **The recycle bin does not cover the operations modules.** `RecycleBinService` enumerates ten
  entity types (Members, Trainers, Subscriptions, Payments, Exercises, WorkoutPlans,
  WorkoutSessions, Expenses, Users, MemberDocuments). Equipment, Enquiries and Feedback soft-delete
  like everything else, but they do not appear in the recycle bin and can only be brought back
  through their own `POST /{id}/restore` endpoint.
- **The admin console is desktop-only.** The navbar is built for a 1280px minimum on a single row;
  its only breakpoint tightens spacing and hides the user name below 1400px, and there is no
  collapsed or hamburger fallback. The public and member screens do carry small-screen
  breakpoints.
- **The frontend has no automated tests.** `npm run typecheck` is the only gate — `package.json`
  defines `dev`, `build`, `preview` and `typecheck`, and no test runner is installed. Backend
  coverage lives in `backend/tests/`.
