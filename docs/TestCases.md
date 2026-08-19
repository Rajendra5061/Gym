# Gym Management System — Test Cases

Manual and acceptance test cases covering section 24 of the requirements. Automated coverage lives
in `tests/GymManagement.UnitTests` and `tests/GymManagement.IntegrationTests`.

Legend — **A** = automated, **M** = manual/UI, **API** = exercised directly against the API.

---

## 1. Authentication

| # | Case | Steps | Expected result | Type |
|---|---|---|---|---|
| A-01 | Valid login | POST `/api/auth/login` with `admin` and the correct password | 200; access token, refresh token, roles and permissions returned; `LoginAttempts` row with `Success`; audit row `Login` | A, API |
| A-02 | Invalid password | Same user, wrong password | 401 with the generic message "Invalid credentials."; `FailedLoginAttempts` incremented; `LoginAttempts` row `InvalidPassword` | A, API |
| A-03 | Unknown user | Login as `nobody` | 401 with the **same** generic message as A-02 (no account enumeration); attempt row `UserNotFound` | A, API |
| A-04 | Account lockout | Fail login `MaxFailedLoginAttempts` times (default 5) | The 5th failure sets `LockoutEndUtc`; the next attempt returns 401 naming the minutes remaining; attempt row `AccountLocked` | A, API |
| A-05 | Lockout expiry | Wait out the lockout window (or clear it via the users screen "Unlock") then log in correctly | Login succeeds; failed counter reset to 0 | M |
| A-06 | Inactive account | Deactivate a user, then attempt login | 401 "This account is not active."; attempt row `AccountInactive` | A, API |
| A-07 | Expired access token | Call a protected endpoint with an expired token | 401 and a `Token-Expired` response header | A, API |
| A-08 | Refresh token rotation | POST `/api/auth/refresh-token` with a valid refresh token | New access + refresh token pair; the old token row is revoked with reason `Rotated` and `ReplacedByTokenHash` set | A, API |
| A-09 | Refresh token reuse | Present a refresh token that was already rotated | 401; **all** of that user's active refresh tokens are revoked (reuse detection) | A, API |
| A-10 | Forgot password | POST `/api/auth/forgot-password` for a real user | 200 with a reset token and the "no email provider configured" message; only the token **hash** is stored, with a 30-minute expiry | A, API |
| A-11 | Forgot password, unknown user | Same call for a non-existent user | 200 with an identically shaped response (no enumeration); no token stored | A, API |
| A-12 | Reset password, valid token | POST `/api/auth/reset-password` with the token and a strong password | 200; the new password works; the token is cleared; all refresh tokens revoked; audit `PasswordReset` | A, API |
| A-13 | Reset password, bad/expired token | Same with a wrong or stale token | 400/401; the password is unchanged | A, API |
| A-14 | Change password | POST `/api/auth/change-password` with the correct current password | 200; refresh tokens revoked; audit `PasswordChanged` | A, API |
| A-15 | Change password, weak new value | New password `abc` | 400 with a validation error naming `NewPassword` | A, API |
| A-16 | Change password, mismatch | Confirm ≠ new | 400 naming `ConfirmPassword` | A, API |
| A-17 | Change password, unchanged value | New password equals the current one | 400 explaining the new password must differ | A, API |
| A-18 | Forced change at first sign-in | Sign in as the seeded `admin` | The client opens the change-password dialog with cancel hidden and will not proceed until the password is changed | M |
| A-19 | Logout | POST `/api/auth/logout` | 200; the refresh token no longer works; audit `Logout` | A, API |
| A-20 | Login rate limit | Send 11 login requests within a minute from one IP | The 11th returns 429 with the standard envelope | M, API |

> OTP validation is listed in the requirements but is not implemented in this build: the
> forgot-password flow uses a signed single-use reset token instead. Wiring an email/SMS provider is
> a prerequisite for an OTP step.

## 2. Authorization

| # | Case | Steps | Expected result | Type |
|---|---|---|---|---|
| Z-01 | Unauthenticated access | Call `/api/members` with no token | 401 | A, API |
| Z-02 | Role restriction | As a Trainer, POST `/api/members` | 403 (`members.create` not granted) | A, API |
| Z-03 | Permission grant | Grant `members.create` to Trainer on the roles screen, re-login, retry | 201/200 — the endpoint now succeeds | M |
| Z-04 | Admin implicit access | As Admin, call any endpoint | Allowed without an explicit grant | A, API |
| Z-05 | Member self-scope | As a Member, GET `/api/dashboard/member/{someoneElseId}` | 403 "You may only view your own dashboard." | A, API |
| Z-06 | Menu gating | Sign in as Staff | The sidebar hides Users, Roles, Audit, Recycle Bin and Settings; action buttons for missing permissions are disabled | M |
| Z-07 | SQL injection attempt | Search for `'; DROP TABLE Members;--` on the members screen | Treated as a literal search string; no rows match; the table still exists | A, API |
| Z-08 | Oversized page request | GET `/api/members?pageSize=100000` | Page size clamped to 200 | A, API |

## 3. Members

| # | Case | Steps | Expected result | Type |
|---|---|---|---|---|
| M-01 | Create member | Fill the add-member form and save | Member created with a generated code `GYM-{year}-{seq}`; audit `Create`; a `NewMemberRegistration` notification is raised | A, M |
| M-02 | Duplicate member | Create a second member with the same phone | 409 conflict; the form shows the message | A, M |
| M-03 | Create with login account | Tick "create a login account" with an email supplied | Member + `User` created atomically with the Member role, a temporary password returned, and must-change-password set | A |
| M-04 | Create with account, no email | Tick the box but leave email empty | 400 validation error on `Email` | A |
| M-05 | Invalid date of birth | Date of birth in the future, or age under 5 | 400 validation error | A |
| M-06 | Update member | Change the address and save | Row updated; audit `Update` records old and new values | A, M |
| M-07 | Deactivate / reactivate | Use the status action | `MemberStatus` changes; audit `Deactivate` / `Reactivate` with the reason | A, M |
| M-08 | Soft delete blocked | Delete a member holding an active subscription or an outstanding balance | 422 business-rule error explaining why | A |
| M-09 | Soft delete allowed | Delete a member with no active subscription and nothing outstanding | `IsDeleted` set; the row disappears from the list; the linked user account is deactivated | A, M |
| M-10 | Restore | Restore the member from the recycle bin | The member reappears with its history intact | A, M |
| M-11 | Search and filter | Search by code, name and phone; filter by status, gender, trainer, plan, joining range, expiring-soon, outstanding | All filtering happens server-side; the pager totals update | M |
| M-12 | Sorting and paging | Sort by name, end date and days remaining; page through the grid | Sorting and paging are applied by the API (verified by the SQL emitted) | M |
| M-13 | Member profile | Open member details | Header shows the plan, days remaining and outstanding; the tabs show subscriptions, payments, attendance, workouts, measurements and documents | M |
| M-14 | Document upload | Upload a 2 MB PDF | Stored under the uploads folder; listed with size and type | M |
| M-15 | Document too large / wrong type | Upload a 12 MB file, then a `.exe` | Both rejected with a validation message | A |
| M-16 | Measurements and progress | Add several measurements over different dates | BMI is computed; the weight and BMI charts plot the trend | M |

## 4. Trainers

| # | Case | Expected result | Type |
|---|---|---|---|
| T-01 | Create trainer | Code `TRN-{year}-{seq}` generated; optional login account created with the Trainer role | A, M |
| T-02 | Assign members | Selected members get `AssignedTrainerId`; one transaction; a single audit entry lists the ids | A, M |
| T-03 | Delete with assignments | 422 — the operator must reassign the members first | A |
| T-04 | Workload view | Assigned members, sessions in period, distinct members trained and total minutes are correct for the chosen range | M |

## 5. Membership plans

| # | Case | Expected result | Type |
|---|---|---|---|
| P-01 | Create each duration type | Day/Week/Month/Quarter/HalfYear/Year/Custom all compute the right end date from a given start date | A |
| P-02 | Duplicate plan name | 409 conflict | A |
| P-03 | Invalid values | Negative price, zero duration, tax > 100, discount > 100 all rejected with field-level messages | A |
| P-04 | Delete plan in use | 422 when active or pending subscriptions exist | A |
| P-05 | Price change isolation | Changing a plan's price leaves existing subscriptions' stored amounts untouched | A |

## 6. Subscriptions

| # | Case | Expected result | Type |
|---|---|---|---|
| S-01 | Quote | `POST /api/subscriptions/quote` returns plan amount, registration fee, discount, max allowed discount, tax and final amount; the UI shows the same figures | A |
| S-02 | Create subscription | Subscription + optional payment created in **one transaction**; code generated; history row `Created`; audit `SubscriptionCreated` | A |
| S-03 | Client cannot set the total | Post a create request with a tampered amount | The server recalculates from the plan; the stored `FinalAmount` matches the quote, not the request | A |
| S-04 | Excess discount | Request a discount above the plan's `MaxDiscountPercent` | 400 validation error quoting the maximum | A |
| S-05 | Overlapping subscription | Create a second subscription overlapping an active one | 422 telling the operator to renew instead | A |
| S-06 | Invalid start date | More than 30 days in the past or 90 in the future | 400 validation error | A |
| S-07 | Renewal | Renew an active subscription | New subscription with `IsRenewal` and `PreviousSubscriptionId` set; start date defaults to the day after the old end date; history `Renewed` | A |
| S-08 | Renewal after expiry | Renew a subscription that ended last month | Start date defaults to today, not to the stale end date | A |
| S-09 | Upgrade | Change to a dearer plan with proration | Old subscription closed as `Upgraded` with an adjusted end date; the new one carries the prorated credit; both get history rows | A |
| S-10 | Downgrade | Change to a cheaper plan | Same flow, marked `Downgraded` | A |
| S-11 | Freeze | Freeze for 7 days on a plan allowing it | Status `Frozen`; end date extended by 7 days; history `Frozen` | A |
| S-12 | Freeze not allowed | Freeze on a plan with `MaxFreezeDays = 0` | 422 business-rule error | A |
| S-13 | Freeze over allowance | Request more days than the plan's remaining allowance | 422 quoting the remaining days | A |
| S-14 | Resume early | Resume before the freeze end date | Only the actual frozen days are added; the over-extension is removed; `FrozenDaysUsed` updated | A |
| S-15 | Cancellation | Cancel with a reason | Status `Cancelled`, reason and timestamp stored; history `Cancelled` | A |
| S-16 | Cancellation without a reason | Cancel with a blank reason | 400 validation error | A |
| S-17 | Cancellation with refund | Cancel with "refund remaining" ticked | A `PaymentRefund` row is created as `Pending` for the unused prorated value, never exceeding what was actually paid | A |
| S-18 | Auto-expiry | Run `process-expiries` with a term ended beyond its grace period | Status `Expired`; member status refreshed; `MembershipExpired` notification raised | A |
| S-19 | Auto-expiry idempotency | Run `process-expiries` twice on the same day | The second run creates no duplicate notifications | A |
| S-20 | Grace period | Check in one day after the end date on a plan with a 3-day grace | Check-in is allowed | A |
| S-21 | Payment pending | Create a subscription with no payment | `PaymentStatus = Pending`, outstanding equals the final amount, and a `PaymentPending` notification is raised | A |

## 7. Payments

| # | Case | Expected result | Type |
|---|---|---|---|
| Y-01 | Cash payment | Payment created as `Paid`; unique receipt number; subscription `PaidAmount` and `PaymentStatus` recalculated; audit `PaymentCreated` | A |
| Y-02 | Part payment | Paying less than the total sets `PartiallyPaid` and leaves the correct outstanding balance | A |
| Y-03 | Overpayment | Amount above the outstanding balance | 422 quoting the outstanding figure | A |
| Y-04 | Zero/negative amount | 400 validation error | A |
| Y-05 | UPI reference required | UPI payment with no transaction reference | 400 — the method requires a reference | A |
| Y-06 | Duplicate transaction reference | Record the same reference twice | 409 conflict (enforced by a unique filtered index as well as the service check) | A |
| Y-07 | Awaiting confirmation | UPI payment saved without "mark confirmed" | Status `AwaitingConfirmation`; it does **not** count toward `PaidAmount` | A |
| Y-08 | Confirm payment | Confirm the above | Status `Paid`; the subscription totals update; audit `PaymentConfirmed` | A |
| Y-09 | UPI intent | Request a UPI intent | Returns a `upi://pay?…` deep link, the payee details, a unique reference, `RequiresManualVerification = true` and the instruction text; **no** payment row is created | A |
| Y-10 | UPI without configuration | Request an intent with no UPI id in gym settings | 422 telling the operator to configure UPI first | A |
| Y-11 | No card data | Inspect the payment form, the DTOs and the database | There is nowhere to enter or store a card number, CVV or UPI PIN | M |
| Y-12 | Receipt | Open a receipt | Shows the gym header, member, plan, amount breakdown, amount in words, method, reference and collector | M |
| Y-13 | Receipt PDF | Download the receipt | A valid A5 PDF is produced and opens correctly | M |
| Y-14 | Refund request | Raise a refund within the refundable balance | `PaymentRefund` created `Pending` (or approved when the caller holds `payments.refund`) | A |
| Y-15 | Refund over balance | Refund more than `FinalAmount - RefundedAmount` | 422 business-rule error | A |
| Y-16 | Refund approval | Approve a pending refund | `RefundedAmount` increased; payment marked `Refunded`/`PartiallyRefunded`; the subscription's paid amount and status recalculated; audit `PaymentRefunded` | A |
| Y-17 | Refund rejection | Reject a pending refund | Status `Rejected`; no money figures change | A |
| Y-18 | Delete a paid payment | Attempt to delete | 422 — the operator is told to raise a refund instead | A |
| Y-19 | Transaction rollback | Force a failure part-way through subscription + payment creation | Neither row is committed; no receipt number is consumed permanently | A |
| Y-20 | Outstanding report | Open the outstanding tab | Lists every unpaid subscription ordered by days overdue | M |
| Y-21 | Daily collection | Run the daily payment report for today | Total matches the sum of today's confirmed payments and the dashboard's "today's revenue" card | M |

## 8. Attendance

| # | Case | Expected result | Type |
|---|---|---|---|
| N-01 | Valid check-in | Member with a usable subscription | Row created `CheckedIn`; today's counters increase; audit written | A, M |
| N-02 | Duplicate check-in | Check the same member in twice today | 409 saying the member is already inside; enforced by the unique `(MemberId, AttendanceDate)` index too | A |
| N-03 | Duplicate after check-out | Check in again after checking out today | 409 saying attendance is already recorded for today | A |
| N-04 | Expired membership | Check in a member whose subscription has ended beyond its grace period | 422 explaining the membership must be renewed | A |
| N-05 | Expired with override | Retry with the override box ticked as a user holding `attendance.manage` | Allowed; the override and the operator are recorded in the notes; a `MembershipExpired` notification is raised | A |
| N-06 | Override without permission | Same request as a Staff user without `attendance.manage` | Still refused | A |
| N-07 | Check-out | Check a member out | `CheckOutTime` and whole-minute `DurationMinutes` recorded; status `CheckedOut` | A, M |
| N-08 | Check-out twice | 409 conflict | A |
| N-09 | Check-in by code | Enter the member code instead of selecting a member | Resolves the same member | A |
| N-10 | Invalid method | Post an unrecognised check-in method | 400 validation error | A |
| N-11 | History and filters | Filter by member and date range | Correct rows; server-side paging | M |
| N-12 | Summary and charts | Open the attendance screen | Today's check-ins, currently-in-gym, checked-out, average duration and peak hour are correct; the hourly bar chart and 30-day trend render | M |

## 9. Workouts and activity

| # | Case | Expected result | Type |
|---|---|---|---|
| W-01 | Exercise library CRUD | Create, edit and delete exercises; duplicate names rejected with 409 | A, M |
| W-02 | Delete a referenced exercise | 422 when a workout plan still uses it | A |
| W-03 | Workout plan with lines | Create a plan with several exercises, days, sets and reps | Lines saved with sequential `DisplayOrder`; totals shown | A, M |
| W-04 | Invalid plan lines | Sets 0, reps 0, day 8 | 400 validation errors | A |
| W-05 | Assign a plan | Assign a plan to a member who already has an active one | The previous assignment is deactivated in the same transaction; the new one is active | A |
| W-06 | Log a session | Record a session with exercise lines | Total volume computed; duration derived from start/end when blank; calories estimated from the exercise rates when blank | A, M |
| W-07 | Future session date | 400 validation error | A |
| W-08 | Progress charts | Open a member's progress | Weight, BMI, session volume and calories series render from real data | M |

## 10. Reports

| # | Case | Expected result | Type |
|---|---|---|---|
| R-01 | Every report runs | Run all 14 report types with a date range | Each returns columns, rows and totals with no error | A |
| R-02 | Date filtering | Narrow the range | Row counts and totals shrink accordingly | M |
| R-03 | Revenue grouping | Switch Group By between Day, Week and Month | Buckets and labels change; the totals stay equal | A |
| R-04 | Reconciliation | Compare the revenue report with the dashboard revenue cards and the daily payment report for the same period | The figures agree | M |
| R-05 | Excel export | Export any report | A valid `.xlsx` with a title row, styled headers, frozen panes, auto-filter, typed cells and a totals row | A, M |
| R-06 | PDF export | Export any report | A valid landscape A4 PDF with the gym header, table, totals and page numbers | A, M |
| R-07 | Export completeness | Export a report with more rows than one page | The file contains every row, not just the visible page | A |
| R-08 | Permission gating | As a Staff user, open Reports | Financial reports and the export buttons are unavailable without `reports.financial` / `reports.export` | M |
| R-09 | Profit and loss | Run for a period with payments, refunds and expenses | Net profit = revenue − refunds − expenses; the charts match the totals | A |

## 11. Notifications

| # | Case | Expected result | Type |
|---|---|---|---|
| F-01 | Expiry reminder | Run `generate-alerts` with a subscription ending inside the reminder window | A `MembershipExpiringSoon` warning is created | A |
| F-02 | De-duplication | Run `generate-alerts` twice in a day | No duplicates (unique `DeduplicationKey`) | A |
| F-03 | Payment notifications | Take a full payment, then a partial one | `PaymentSuccessful` and `PaymentPending` respectively | A |
| F-04 | Mark read | Mark one and then all as read | `IsRead` and `ReadAtUtc` set; the shell badge count drops | A, M |
| F-05 | Scope | Sign in as another user | Only that user's notifications plus broadcasts are visible | A |
| F-06 | Failure isolation | Force the notification insert to fail during a payment | The payment still succeeds; the failure is logged only | A |

## 12. Audit log

| # | Case | Expected result | Type |
|---|---|---|---|
| U-01 | Login/logout logged | Both appear with user, IP and device | A |
| U-02 | Member changes logged | Create, update, deactivate and delete each write a row with old/new values | A |
| U-03 | Subscription changes logged | Create, renew, freeze, resume and cancel each write a row | A |
| U-04 | Payment changes logged | Create, confirm and refund each write a row | A |
| U-05 | Role/config changes logged | Editing role permissions or gym settings writes a row | A |
| U-06 | Filtering | Filter by user, action, entity, entity id and date range | Correct rows; the details pane shows the JSON diff | M |
| U-07 | Read-only | Attempt to edit an audit row | No API or UI path exists to modify or delete audit rows | M |

## 13. Recycle bin

| # | Case | Expected result | Type |
|---|---|---|---|
| B-01 | Deleted records listed | Soft-delete a member, an exercise and an expense | All three appear under their entity types with deleted-at and deleted-by | A, M |
| B-02 | Restore | Restore a selection | Rows return to their modules; audit `Restore` | A, M |
| B-03 | Purge requires confirmation | Purge without the confirmation text | 400 — `PERMANENTLY DELETE` is required | A |
| B-04 | Purge requires permission | Purge as a user lacking `recyclebin.purge` | 403 | A |
| B-05 | Purge blocked by dependencies | Purge a member that still has payments | 422 explaining dependents must be purged first | A |
| B-06 | Purge succeeds | Purge a record with no dependents | Physically deleted; audit `Delete` | A |

## 14. Settings, licence and backup

| # | Case | Expected result | Type |
|---|---|---|---|
| G-01 | Gym settings round-trip | Change the name, currency and reminder days, then save and reload | Values persist; audit `ConfigurationChanged`; receipts and the shell header pick up the new name | A, M |
| G-02 | Settings validation | Closing time before opening time; a malformed UPI id | Rejected with field-level messages | A |
| G-03 | Start trial | Start a 30-day trial | Status `Trial` with the expiry set; the member quota is applied | A |
| G-04 | Trial not restartable | Start a trial twice | 409 conflict | A |
| G-05 | Activate a valid key | Activate a correctly signed key | Status `Active`; limits and features applied; audit `LicenseActivated` | A |
| G-06 | Tampered key | Alter one character of a key and activate | 402 licence error | A |
| G-07 | Expired key | Activate a key whose expiry has passed | 402 licence error | A |
| G-08 | Clock tamper | Wind the system clock back more than a day | `ClockTamperDetected` is set, the licence reads invalid, and the UI shows the warning strip | M |
| G-09 | Member quota | Reach the licensed member limit, then create another member | 402 naming the limit | A |
| G-10 | Create backup | Run a backup to a folder writable by the SQL Server service account | `.bak` written; history row with the file size and success flag; audit `BackupCreated` | M |
| G-11 | Backup to an unwritable folder | Target a folder the SQL Server service cannot write | A clear error is surfaced and a failed history row is recorded | M |
| G-12 | Restore confirmation | Restore without typing the database name | 400 validation error | A |
| G-13 | Restore | Restore a valid backup after typing the database name | Database restored; history updated; audit `BackupRestored`; the API is restarted afterwards | M |

## 15. Dashboard

| # | Case | Expected result | Type |
|---|---|---|---|
| D-01 | All cards populated | Open the dashboard on a seeded database | Every KPI card shows a plausible value with the configured currency symbol | M |
| D-02 | Card accuracy | Cross-check total/active members, today's attendance and today's revenue against the corresponding lists | Figures agree | M |
| D-03 | Charts render | Revenue (day/week/month toggle), membership growth, attendance trend, plan distribution and payment-method distribution | All render, including zero-filled gaps | M |
| D-04 | Empty database | Open the dashboard before any transactions exist | Zeros and "no data" chart states — no exceptions | M |
| D-05 | Member portal | Sign in as a Member | Own profile, active subscription, days remaining, outstanding, visits, payments, workout plan and notifications only | M |

## 16. Performance and robustness

| # | Case | Expected result | Type |
|---|---|---|---|
| X-01 | Large dataset | Seed ~10,000 members with subscriptions, payments and attendance | List screens stay responsive; paged queries return promptly; memory stays flat (no full-table loads) | M |
| X-02 | Server-side paging proven | Inspect the SQL emitted for a member list request | `OFFSET`/`FETCH` with the filters and sort applied in SQL | A |
| X-03 | API unavailable | Stop the API, then use the client | A clear "cannot reach the API" message; no crash | M |
| X-04 | Session expiry | Let the access token expire, then act in the client | The refresh token is used transparently; on refresh failure the user is returned to the login screen | M |
| X-05 | Concurrent code generation | Create several members/payments simultaneously | No duplicate member codes or receipt numbers (unique indexes plus the probe loop) | A |
| X-06 | Cancellation | Navigate away mid-request | The request is cancelled without an unhandled exception | M |
| X-07 | Unhandled error surface | Force a 500 | The client shows a generic message; the server log holds the detail; no stack trace reaches the client | A |

---

## Running the automated tests

```bash
dotnet test                                              # everything
dotnet test tests/GymManagement.UnitTests                # unit only
dotnet test tests/GymManagement.IntegrationTests         # integration only
```

Integration tests host the API in-process with `WebApplicationFactory<Program>` against the EF Core
in-memory provider, so they need no SQL Server instance. Cases marked **M** are executed against a
running API and desktop client.
