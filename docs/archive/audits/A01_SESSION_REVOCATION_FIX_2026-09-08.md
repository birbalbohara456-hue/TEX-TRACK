# A01 — persisted session revocation: implementation and audit handoff

Date: 8 September 2026

## Scope and preservation of existing work

This patch addresses A01 from the comparative backend audit: an already-open Blazor circuit could retain authenticated claims and continue invoking protected repositories after its account was deactivated. Cookie validation on a later HTTP request was insufficient.

This is a backend authentication patch, not a UI overhaul. No Razor, CSS, JavaScript, production entity, SQL migration, or production calculation was edited for this patch. Claude's existing UI changes and other dirty/untracked work were not reverted, staged, committed, or pushed. The running application was not stopped or restarted. Production-mode configuration and deployment remain unchanged.

A02 (future database-schema compatibility), the remaining audit findings, and production freeze approval are separate work. This document does not assert that all audit findings are closed.

## Enforcement model

1. `PersistedSessionValidator` reads current persisted authority using a fresh EF context. It requires an authenticated principal, positive user/company/year identifiers, a nonexpired session-expiry claim, an active user with the current security stamp, active membership of the selected active company, the same persisted role set as the session, and an active financial year belonging to that company.
2. `CurrentCompanyContext.RequirePolicyAsync` retains the existing role-policy check and also awaits this live validator. There is no positive session-validation cache. Failure prevents the guarded operation from proceeding. Database failures are not converted into permission to continue.
3. Master and voucher repositories await that check before mutations. Their public data reads, operational reporting, voucher history, existing Tally exchange entry points, and assistant evidence/settings reads are also checked. Tally mappings and export numbering are unchanged.
4. User creation, deactivation, and password reset now require persisted Developer authority, rather than merely a Developer claim. Maintenance password verification also requires a valid current session.
5. The cookie-validation callback and an explicitly registered `TexTrackRevalidatingAuthenticationStateProvider` use the same validation rules. The provider rechecks open circuits every 30 seconds and uses the framework's anonymous-state transition when validation returns false or fails.

The timer updates the visible authentication state. It is NOT the security boundary for starting another protected operation: service calls check independently without waiting for the timer.

## Exact boundary and limitations

- After deactivation/password reset/access withdrawal has committed, a subsequent guarded operation is refused using the old principal, even when the original HTTP context has disappeared.
- An operation already authorized and in flight when revocation occurs may finish. This patch does not serialize revocation against every business transaction or forcibly roll back already-running operations.
- Existing data already displayed in a browser cannot be recalled. Further protected reads are checked; the visible circuit authentication state is revalidated periodically.
- Ordinary logout has not been redesigned into an account-wide or per-device revocation registry. This patch covers persisted account/access changes and session expiry; do not infer a new cross-tab/device logout guarantee.
- The provider's timer is behavior-tested, but an interactive two-browser smoke test remains a release check. No live browser session or live user account was changed during implementation.
- Internal transaction helpers such as stock posting, lifecycle checks, and voucher-number allocation remain behind their guarded repository entry points. They are not newly exposed application endpoints.
- Synthetic business-rule fixtures explicitly inject a test-only validator. They do not prove session security. The new session-revocation tests use persisted users and real authentication/validation instead.

## Session compatibility

New authenticated principals carry `textrack:session_expires_at`, aligned with the existing non-sliding two-hour cookie lifetime. Old cookies without that claim fail closed and require a fresh login after the updated application is started. No stored password, identity, voucher, or stock row is migrated or rewritten to introduce this claim.

The live application has not been restarted, so the source fix takes effect when the updated build is launched normally. Coordinate that restart with UI work and save any open entry first.

## Review map

| File or area | Review focus |
| --- | --- |
| `Services/PersistedSessionValidator.cs` | Fresh persisted checks, expiry, company/year membership, role equality, failure behavior |
| `Services/DatabaseStatus.cs` | `CurrentCompanyContext.RequirePolicyAsync`; no claims-only authorization fallback |
| `Services/UserIdentityService.cs` | Expiry issuance; cookie validation; user-administration and maintenance-password checks |
| `Services/TexTrackRevalidatingAuthenticationStateProvider.cs` | Open-circuit revalidation; timer is not the operation boundary |
| `Program.cs` | Scoped validator/provider registrations and matching cookie lifetime |
| `Services/*Repository.cs`, `OperationalReportingService.cs` | Awaited guards at public reads and mutations; existing calculations remain unchanged |
| `Services/TallyXmlExchangeService.cs`, `TallyXmlExporter.cs` | Authentication guard only; no mapping redesign |
| `Services/AssistantSettingsStore.cs`, `TexTrackAssistantEvidenceProvider.cs` | No fresh settings/evidence reads through a revoked session |
| `Tests/TexTrack.Web.IntegrationTests/SessionRevocationTests.cs` | Real persisted-session regression cases and circuit-provider behavior |
| `Tests/TexTrack.Web.IntegrationTests/SessionTestSupport.cs` | Explicit business-only test double and isolated circuit accessor |
| Existing identity/context/maintenance/lifetime tests | Fixtures updated for the required validator; no permissive production constructor added |

The existing master repository dispatch methods now await their returned tasks so authorization is awaited before dispatch. Their selected business handlers are unchanged.

## Verification

Tests use disposable PostgreSQL schemas through the existing test harness. Build and test output is isolated under `Tests/.a01-artifacts`; it does not overwrite the running application's binaries. The artifact directory is locally ignored, not a source/deployment payload.

- Initial isolated build: succeeded, zero warnings and errors.
- Targeted identity/revocation/authorization/maintenance tests: 30 passed, zero failed.
- First full run: 194 passed, one failed. The failure was a legacy source-text assertion expecting `companyContext`/`cancellationToken` where the stock-field repository uses `company`/`ct`. The assertion was updated to recognize that repository's actual awaited guard. No production calculation failure was reported in that run.
- Final full-suite result: **195 passed, zero failed, zero skipped**, in 1 minute 31 seconds. The final test build emitted no compiler warnings or errors. Evidence: `Tests/.a01-artifacts/results/a01-full-final.trx`.

Reproduction command from the repository root:

```powershell
dotnet test Tests/TexTrack.Web.IntegrationTests/TexTrack.Web.IntegrationTests.csproj --artifacts-path Tests/.a01-artifacts --logger "trx;LogFileName=a01-full-final.trx" --results-directory Tests/.a01-artifacts/results
```

New behavioral coverage includes the original stale-circuit write reproduction; refusal of ledger reads/deletes, report lookup, JWO save, MO save/update/cancel and MI save/delete after revocation; deactivation, password reset, membership disabling/removal, role removal, company/year deactivation, expired/legacy sessions; fresh-login recovery; revoked Developer administration; maintenance password verification; and anonymous circuit state on invalidation or validation failure.

## Independent release smoke test

Use a disposable test account and test company, not an owner's working account:

1. Start the updated build and sign in afresh in two separate browser sessions.
2. Open a voucher in the test user's session. In the administrator session, deactivate that user or reset its password.
3. Before the 30-second UI timer, attempt another protected operation with the old screen. It must be refused, with no resulting business mutation.
4. Confirm the circuit loses authenticated state on periodic revalidation. Repeat with fresh data reads and a reset followed by successful login with the new password.
5. Verify the same entry/report workflows still work for an active authorized user and Claude's current layouts remain intact.

An independent reviewer should verify this patch against the current working tree, not assume the broad `git diff` consists solely of A01: this repository already contained extensive earlier changes.
