# Authentication audit findings — remediation

## Scope

Fixes independent audit findings H2-2 (missing authentication events) and H2-3 (lost concurrent failed-login increments). No production, material-demand, customer-field, or broader ERP features are introduced.

## Implementation

`Services/UserIdentityService.cs` now starts a transaction and takes a parameterized PostgreSQL row lock on the matching account before loading its tracked state. Concurrent authentication attempts for the same account serialize; other accounts do not share this lock. The failure count, lockout, successful-login changes, and corresponding audit entries commit together.

An expired lockout starts a fresh failure-count window. Correct credentials still cannot bypass an active lockout. Existing password verification, role/company checks, claims, and legacy password-hash upgrade remain in place.

Audit actions: `LoginFailure`, `LoginLockout` (threshold transition), `LoginBlocked` (attempt during lockout), and `LoginSuccess`. Lockout-triggering failures record both the failure and the transition. Unknown accounts, inactive accounts, and missing role/company/financial-year access also produce failure events.

Events are account-scoped (`CompanyId = null`) because authentication precedes company selection. Known accounts use the immutable user ID; unknown attempts use Anonymous without storing arbitrary submitted usernames. Passwords, credential hashes, cookies, and tokens are not logged. No new audit-view UI, network-source tracking, retention system, or external tamper-resistant storage is claimed by this patch.

## Regression coverage

`UserIdentitySecurityTests` includes ten parallel failed logins: five counted failures, one lockout transition, five blocked attempts, and a persisted failure count of five. It also checks expired-lockout recovery, successful-login reset/audit, unknown-account audit, and absence of the submitted password in event descriptions.

The existing tests also cover salted passwords, normal login/claims, active-lockout rejection of correct credentials, deactivation, and developer-only account administration.

## Deployment boundary

Verification: targeted identity tests passed 5/5; full .NET/PostgreSQL suite passed 210/210 with zero failures or skips. Tests ran in disposable schemas using isolated build outputs. The JavaScript suites were not rerun because this patch changes no UI/JavaScript. Whitespace checks passed for the touched tracked files.

No new migration is required for this patch. No live database migration or server restart was performed. The repository still contains previously pending migrations; deploying the whole working tree requires their separately documented backup/rehearsal steps.

## Scope correction

MJO allocation ceilings are not an outstanding defect: the user clarified that MJO consolidates existing JWOs and is not a production authorization. Historical audit findings are preserved, with this correction recorded in the remediation/build notes.
