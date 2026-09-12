# TexTrack P0 Production-Security Hardening — 2026-08-28

Status values are intentionally restricted to: `NOT STARTED`, `IMPLEMENTED`, `TESTED`, `ADVERSARIALLY TESTED`, `VERIFIED`, `BLOCKED`.

| P0 Item | Risk | Status | Code Evidence | Test Evidence | Remaining Work |
|---|---|---|---|---|---|
| 1. Real user identity | Authentication bypass and unrecoverable/forged identity | ADVERSARIALLY TESTED | `Domain/Entities.cs`, `Data/TexTrackDbContext.cs`, `021_persisted_user_identity.sql`, `Services/UserIdentityService.cs`, cookie stamp validation in `Program.cs` | `UserIdentitySecurityTests.cs`, `DeveloperAccessTests.cs`; invalid password, lockout, inactive user, reset, stale-session and direct non-Developer administration paths | User-administration UI/workflow and deployment validation; deployment blocker |
| 2. Role and permission model | Privilege escalation | ADVERSARIALLY TESTED | Least-privilege matrix in `Services/SecurityPolicies.cs`; explicit policies on routed surfaces; repository/service guards on master/voucher writes, cancel/delete, Tally import/export, user administration and AI configuration | `RolePermissionPolicyTests.cs`, `CurrentCompanyContextSecurityTests.cs`, `RepositoryMutationAuthorizationTests.cs`; Operator bypass of cancel/delete and Manager bypass of user administration are denied | Runtime HTTP adversarial coverage and final privileged-command inventory; deployment blocker |
| 3. Authentication and authorization enforcement | Anonymous/direct-URL/API access | TESTED | Authenticated fallback policy in `Program.cs`; `AuthorizeRouteView`; explicit page/API policies; `CurrentCompanyContext.RequirePolicy` enforces repository commands | `AuthenticationBoundaryTests.cs`; focused role/repository mutation/actor suite 27/27 | Runtime HTTP adversarial coverage of every route/API and mutation; deployment blocker |
| 4. Tenant isolation and IDOR prevention | Cross-company disclosure or mutation | TESTED | `CurrentCompanyContext` derives company/FY only from authenticated persisted membership claims and fails closed | `CurrentCompanyContextSecurityTests.cs` and tenant boundary tests | Cross-company read/update/delete IDOR tests for every repository and endpoint; deployment blocker |
| 5. Secrets hygiene | Credential disclosure | TESTED | Credential-free tracked `appsettings.json`; local development credentials in .NET User Secrets | `SecretHygieneTests.cs` | Production secret-store provisioning, rotation and deployment validation; deployment blocker |
| 6. CSRF and request boundaries | Forged cookie-authenticated actions and open redirects | TESTED | ASP.NET antiforgery on login/logout; `SecurityRequestGuard`; tokenized logout form | `RequestBoundarySecurityTests.cs` | Runtime missing/invalid-token HTTP tests and exhaustive state-changing endpoint inventory; deployment blocker |
| 7. Audit actor integrity | Forged or misleading audit history | TESTED | `CurrentCompanyContext.Actor` binds writes to immutable user ID plus authenticated name; `ActorFor` retains import/export source; core master, voucher, stock, BOM and Tally mutation services no longer write generic actors | `AuditActorIntegrityTests.cs`, `CurrentCompanyContextSecurityTests.cs`; missing actor identity fails closed and generic actor assignments are rejected | Add durable user foreign-key columns to audit records and validate legacy-row migration/retention; deployment blocker |
| 8. Durable background jobs | Lost/duplicated long-running work after crash | NOT STARTED | No durable leases, retries, idempotency or restart recovery verified | None | Introduce the smallest durable execution model required; deployment blocker |
| 9. Health endpoints | Undetected application/database/dependency failure | NOT STARTED | No separated liveness/readiness endpoints | None | Add non-sensitive liveness/readiness and failure monitoring; deployment blocker |
| 10. Rate limiting and abuse controls | Authentication attacks, enumeration and resource exhaustion | NOT STARTED | No ASP.NET rate limiter registered | None | Protect login, AI, expensive search/import/export and exposed APIs; deployment blocker |
| 11. Versioned API boundary | Breaking/insecure future mobile integration | NOT STARTED | Existing endpoints use unversioned `/api/...` routes | None | Define authenticated `/api/v1` compatibility boundary; deployment blocker |

## Verification snapshot

- Full solution build: passed, 0 warnings, 0 errors.
- Full .NET suite: passed 160/160 using isolated build outputs because the running app held the normal output DLL open.
- Full JavaScript suite: passed 77/77.
- Focused role, repository mutation and actor-integrity suite: passed 27/27 (included in the .NET total).
- Service-lifetime regression suite: passed 2/2; `AssistantSettingsStore` is scoped with `CurrentCompanyContext`, and scope validation resolves it successfully.
- Real 5091 launcher smoke test: passed; the configured BOM-scale build started and served `/_framework/blazor.web.js` with HTTP 200.
- Initial runtime boundary probes: anonymous `/` and `/api/stock-item-photos/1` requests redirect to authenticated login; login POST without an antiforgery token returns HTTP 400. Exhaustive runtime route/mutation coverage remains open.
- No P0 item is marked `VERIFIED`; verification requires the outstanding adversarial and deployment checks listed above.

## Deployment blockers

1. Complete runtime HTTP adversarial authorization coverage and the final privileged-command inventory.
2. Complete adversarial cross-company IDOR coverage for reads and every mutation.
3. Add durable authenticated-user foreign keys to audit records and validate legacy audit retention/migration.
4. Add durable, idempotent background-job execution for long-running operations.
5. Add liveness/readiness endpoints, rate limits, and a versioned external API boundary.
6. Validate production secrets, HTTPS/cookie behavior, reverse-proxy configuration, backups and recovery in the deployment environment.

## Current verdict

**DO NOT DEPLOY**
