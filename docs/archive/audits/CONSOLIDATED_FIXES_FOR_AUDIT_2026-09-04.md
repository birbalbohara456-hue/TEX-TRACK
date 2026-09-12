# TexTrack — Consolidated Fixes for Independent Review

Prepared: 2026-09-04

Repository: `C:\Users\VICTUS\Desktop\c#\Google\TexTrackERP`

## 1. Read this first

This consolidates the documented September 3–4 remediation work, the MI process-charge/rate-variance build, and the latest authentication and voucher-error fixes. It is not an exhaustive release history for every earlier UI change, nor a certification that every audit finding is resolved.

The latest recorded verification is **217 .NET/PostgreSQL tests passed, 0 failed, 0 skipped**, plus **88 JavaScript checks across five test files passed**. These counts are cumulative, not additive. Application and test compilation succeeded. Tests use disposable database schemas. No live browser acceptance, production backup restoration rehearsal, live migration, or server restart was performed in the latest passes.

The working tree contains accumulated uncommitted and untracked work. No immutable release snapshot/hash is supplied by this document. Before independent review, capture a complete snapshot including relevant untracked files and record its hash; a commit alone may not contain the fixes. Do not audit a moving target or treat this document as proof of implementation.

## 2. Request to Claude

Independently verify the claims below against current code and runtime tests. Review one area at a time. For each finding, report: audit ID if applicable, severity, exact file/method, reproducible scenario, evidence, status (verified fixed / partial / unresolved / not applicable), and the smallest safe correction. Do not implement changes as part of the audit unless separately asked.

Preserve valid BOM expansion, revision pinning, permanent stage identity, versioned assignments, FIFO consumption allocations, transactional stock posting, and company isolation. Do not weaken controls merely to make tests pass. Identify coverage gaps even when all existing tests pass.

## 3. Database migrations and integrity

### Implemented

- Packaged migration filenames and SHA-256 identities are checked against recorded history before skipping applied migrations.
- Unknown provenance mismatches refuse normal connected startup; migration history is not silently rewritten.
- A closed compatibility manifest recognizes two specific legacy migration identities (011 and 014), preserving the original recorded identities.
- Packaged migration numbers must form a contiguous sequence starting at one, without duplicates or gaps.
- A PostgreSQL session advisory lock serializes initialization; each migration and its history row commit in the same transaction.
- Migration 025 adds composite relationships for child BOM/revision pairing and MO/MI stage/assignment pairing after preflight checks.
- Migration 026 rejects unknown movement kinds or mismatched BOM current revisions before restoring the eight-kind movement constraint and BOM/current-revision composite relationship.
- Migration 027 adds nullable expected/actual process-rate snapshots and a persisted charge-entry mode. Legacy mode defaults to Total; historical rates/amounts are not rewritten.

### Inspect

`Data/DatabaseBootstrapper.cs`, `Data/TexTrackDbContext.cs`, `Data/Migrations/025_audit_integrity_hardening.sql`, `026_production_freeze_constraints.sql`, `027_process_charge_rate_snapshots.sql`.

Tests: `DatabaseBootstrapperTests.cs`, `MigrationChainTests.cs` under `Tests/TexTrack.Web.IntegrationTests`.

Audit references: C1-1, C1-2, H1-3, M4-4. Do not interpret migration-chain tests as complete parity verification of every EF table, index, type, and constraint; H1-1 coverage requires independent review.

### Deployment caution

Additive/preflight migrations are not automatically safe for every historical database. Rehearse the actual pending chain on a restored production backup, inspect failed preconditions, compare historical totals, and assess locking/runtime before deployment. Startup applies pending migrations: restarting with this working tree is a deployment action.

## 4. Production integrity and reporting

### Implemented

- MO cancellation is blocked when an active MI allocation consumes it; the guard identifies linked MI vouchers.
- MO/MI dates are validated against the parent JWO date.
- MI receipt ceilings apply per colour/size variant, not merely the aggregate quantity.
- BOM-driven MI requires proportional component consumption so output cannot capitalize component value while understating consumption.
- Manual/non-BOM receipts persist nullable stage identities rather than the UI's synthetic stage sentinel.
- JWO completion is recalculated after MO/MI lifecycle operations. Final receipts determine finished-output completion; intermediate output must not be counted as finished goods.
- Pending Finished Goods, Job Worker Control, Aging and T-format final-output totals exclude intermediate receipts.
- Shared atomic voucher numbering is used by JWO, MJO, MO and MI; Never-reset numbering continues across financial years. Number previews do not reserve numbers.
- Canonical stock-movement semantics are shared; zero-quantity stock postings are rejected. Value-only adjustments are not disguised as physical movements.
- Structural BOM mutations use serializable transactions plus a transaction advisory lock; concurrency conflicts receive a reload/retry response.
- Database maintenance preserves audit history and records the authorized clear operation.

### Inspect

`Services/MaterialOutRepository.cs`, `MaterialInRepository.cs`, `JobWorkOrderRepository.cs`, `JobWorkOrderStatusUpdater.cs`, `VoucherSequenceAllocator.cs`, `StockPostingService.cs`, `StockMovementSemantics.cs`, `BillOfMaterialRepository.cs`, `DatabaseMaintenanceService.cs`, and the operational reporting services.

Relevant tests include `ProductionReportingTests.cs`, `MultiLevelBomTests.cs`, `VoucherLifecycleAndStockPostingTests.cs`, `VoucherSequenceAllocatorTests.cs` and database bootstrap/migration tests. Review actual assertions rather than assuming each file covers every listed case.

Audit references: C2-1, C4-1, C4-2, C4-3, H4-1, H4-2, H4-3. M4-2 must be checked end-to-end: validating against JWO date alone is not proof that every MI allocation rejects a later-dated supplying MO, including alterations/reversals.

## 5. MI process charges and Rate Variance tab

### Implemented

- Both process rate/unit and total process charges are editable. Last edited field controls calculation.
- PerUnit mode recalculates total when quantity changes. Total mode preserves total and recalculates rate.
- Shared UI/server calculator uses existing four-decimal precision with midpoint rounding away from zero; authoritative values are normalized server-side rather than trusting the client-derived field.
- Zero quantity avoids division; existing positive-charge requirements remain.
- Optional expected rate is stored on a JWO stage/jobber assignment. Changing the expected rate creates an assignment version, preserving permanent stage identity and prior history.
- Rate-only amendments recalculate JWO status instead of resetting an active job to Open.
- MI snapshots expected rate from the referenced assignment, actual rate, and entry mode. Alterations preserve the original expected snapshot, including historical NULL.
- Process-charge audit entries record quantity, mode, expected/actual rate and total.
- Rate Variance is a tab in Job Worker Control, not a separate report menu. It shows expected rate, actual rate, and signed actual-minus-expected difference with JWO/jobber/stage/MI context.
- This report intentionally includes intermediate-stage process receipts. This is different from final-finished-output completion reporting.
- Report filtering respects company, financial year, as-on date, and cancellation state. It uses shared header filters, pagination, horizontal scrolling and voucher links.
- Unknown historical expected rate displays Not recorded with no fabricated variance. Historical actual rate can be derived from saved charges/quantity. Manual receipts have no invented stage expectation.

These are process charges, not MO material rates or total FG valuation. Actual-versus-expected variance is informational, not a posting block.

### Inspect

`Models/ProcessChargeCalculation.cs`, `Models/JobWorkRateVarianceRow.cs`, `Domain/Entities.cs`, `Data/TexTrackDbContext.cs`, the JWO/MI repositories, `Services/OperationalReportingService.cs`, `Components/Pages/Vouchers/MaterialIn.razor`, `JobWorkOutOrder.razor`, and `Components/Pages/Reports/JobWorkerControlCenter.razor` with scoped CSS.

Tests: `ProcessChargeCalculationTests.cs`, rate cases in `ProductionReportingTests.cs`, assignment-history cases in `MultiLevelBomTests.cs`, and `Tests/JavaScript/production-reports.test.cjs`.

UI acceptance still required: 50 units at 12 gives 600; entering total 650 gives rate 13; changing quantity to 100 in Total mode retains 650; entering rate 14 gives 1400; reopen, inspect mode/snapshots, and verify cancellation removes the report row.

## 6. Authentication fixes

Audit references: H2-2 and H2-3.

- Authentication takes a parameterized account-row lock before loading tracked state. Concurrent attempts for one account cannot overwrite failure counts.
- Account state and authentication audit events commit together.
- Five counted failures trigger lockout. Correct credentials do not bypass an active lockout.
- Expired lockouts reset the attempt window rather than immediately re-locking after one wrong password.
- Events: LoginFailure, LoginLockout (transition), LoginBlocked, LoginSuccess. Unknown/inactive accounts and missing access/FY also record failures.
- Events are account-scoped with CompanyId NULL. Known identities use immutable user IDs; unknown attempts are Anonymous. Submitted passwords, hashes, cookies, tokens and arbitrary unknown usernames are not logged.
- Existing hashing, claims, role/company checks and legacy hash upgrade remain.

Inspect `Services/UserIdentityService.cs` and `UserIdentitySecurityTests.cs`. The new concurrent test runs ten wrong-password attempts and verifies five failures, five blocked attempts, one transition, and persisted count five; it also checks expiry recovery, successful reset/audit and unknown-user audit.

Limits: no new audit UI, IP/source capture, external tamper-resistant storage, or retention system was added. Review authentication/account-administration races and operational logging coverage independently.

Earlier documented hardening includes rate-limited login/setup endpoints, Secure production cookies, production AllowedHosts validation, authenticated JWO assignment actors, and generated development database secrets. These are not substitutes for deployment verification.

## 7. Voucher database-error disclosure fixes

Audit references: H3-1 / L2-5.

- Shared `Services/VoucherErrorMessages.cs` translates direct or wrapped database exceptions.
- Duplicates, linked-record conflicts, concurrency/serialization/deadlocks, permissions and interruption receive actionable messages.
- Handled JWO/MJO/MO/MI result paths and JWO/MO/MI UI handlers no longer display deepest provider exception text.
- MI's save helper delegates to the translator instead of unwrapping exceptions.
- Removed misleading advice that every MO cancellation database failure requires restarting and running migrations.
- Standalone InvalidOperationException messages remain as existing business validations; nested storage failures take precedence and are sanitized.

Inspect the translator, four voucher repositories and three voucher UI components. `VoucherErrorMessagesTests.cs` includes wrapped/direct failure cases and a real PostgreSQL missing-column error.

Limits: this is targeted sanitization, not universal exception hardening. Unexpected standalone framework InvalidOperationExceptions remain a review concern because there is no dedicated user-safe validation exception type. No new global error boundary or persistent diagnostic logging system was introduced. Exceptions still propagated internally retain their original details; presentation is translated.

## 8. Business-scope corrections: do not undo these

### MJO is consolidation only

The user explicitly clarified that MJO groups related existing JWOs, sometimes retrospectively when staff identifies related challans without consistent batch records. It is not an independently entered customer-order target or production authorization.

Therefore the audit's blanket MJO allocation-ceiling requirement (M4-1) is **not applicable to the agreed workflow**, not an unimplemented mandatory fix. Grouping must not change stock or imply stage quantities are unique finished output. The implementation/report behaviour still warrants verification for consistency with this scope.

### Discussed, not delivered in these fixes

- Optional Ordered By (Customer) ledger field on JWO.
- Material availability considering physical stock and outstanding other-JWO demand, without hard reservations.
- Separate visibility of pending purchase receipts and eventual shortage-to-PO workflow.
- Child-stage Quantity to Produce, defaulting to full required quantity; explicit use-existing-stock option and mixed supply.

The user requires earlier bugs to be resolved first. Do not classify these proposals as implemented capabilities or add them during the audit.

### Broader ERP future scope

Scrap, wastage, by-products, rejection/rework and controlled extensibility/customizations are deferred in `BROADER_ERP_EXPANSION.md`. They must not expand the current garment-production freeze scope.

## 9. Outstanding work and review boundaries

Not all original findings have a documented closure. Reconcile the full original audit against the current code, especially:

- Complete EF/schema/index parity coverage and runtime replacements for source-text-only tests.
- Component variant identity in material issue/consumption postings; material-rate policy and valuation behaviour.
- Cross-voucher chronology including edits and consumed MO dates; cross-financial-year report coverage.
- Other authentication/session/assistant boundary findings not closed by the targeted patches above.
- Standalone framework error exposure and diagnostic logging gaps.
- Financial-year constraints and other remaining schema/maintenance findings.
- Launch environment, backup/restore drills, service packaging, Data Protection persistence, health checks, production logging, proxy configuration and deployment validation.

These areas are not all equivalent: distinguish proven defects from business-policy decisions, future features and deployment requirements. PostgreSQL RLS, universal automatic retries/idempotency, complete accounting/perpetual valuation, external audit storage, and full ERP readiness are not claimed.

Do not declare the production engine or full application production-ready solely because the current tests pass.

## 10. Verification history and commands

| Recorded checkpoint | .NET/PostgreSQL results |
| --- | --- |
| Production-freeze remediation | 203 passed |
| MI rates and Rate Variance | 209 passed |
| Authentication fix | 210 passed |
| Voucher-error fix (latest) | 217 passed |

Latest JavaScript results: 88 passed across `job-worker-master.test.cjs`, `production-reports.test.cjs`, `shared-list-scroll.test.cjs`, `textrack-alt-delete.test.cjs`, `voucher-history-workflow.test.cjs`. Source checks and mocked browser tests are not live browser acceptance.

From repository root, after confirming test database configuration targets disposable schemas:

```powershell
dotnet test TexTrack.sln --no-restore -p:UseSharedCompilation=false -p:UseAppHost=false -p:OutputPath=.review-bin\ -p:IntermediateOutputPath=.review-obj\ --verbosity minimal
node --test Tests/JavaScript/job-worker-master.test.cjs Tests/JavaScript/production-reports.test.cjs Tests/JavaScript/shared-list-scroll.test.cjs Tests/JavaScript/textrack-alt-delete.test.cjs Tests/JavaScript/voucher-history-workflow.test.cjs
```

Isolated outputs avoid overwriting running application binaries; they do not make production startup safe. Generated output directories from the completed fix passes were removed. Existing unrelated whitespace and other accumulated working-tree changes were preserved.

## 11. Source notes consolidated here

- `AUDIT_REMEDIATION_2026-09-03.md`
- `BUILD_MI_RATE_VARIANCE_2026-09-04.md`
- `BUGFIX_AUTHENTICATION_2026-09-04.md`
- `BUGFIX_VOUCHER_ERRORS_2026-09-04.md`

Original review baseline: `INDEPENDENT_AUDIT_FINDINGS_PHASES_1-8_2026-09-03.md`. Preserve its historical findings; use this handoff and fresh evidence to determine current status.
