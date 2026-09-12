# TexTrack ERP — Independent Audit Findings (Phases 1–8)

**Date:** 2026-09-03 (original audit); revised 2026-09-03 following independent cross-review
**Auditor:** Independent enterprise ERP audit (Claude), conducted phase-by-phase against current source code, live PostgreSQL databases, and running application instances.
**Revision note:** This document was cross-reviewed by a second independent reviewer (Codex), who identified 2 critical and ~18 additional findings this audit's original Phase 2/4/5 passes missed — concentrated in Material Out/In relational integrity, production valuation, and a complete functional break for manually-allocated Job Work Out Orders. **Every one of those claims was independently re-verified against current source before being added here** — each carries its own exact file/line evidence, verified fresh, not merely copied from the reviewer's report. Two of the reviewer's claims were corrected on verification (see FR4-1 and FR1-3 below) rather than accepted as originally stated. This revision changes the Phase 4 verdict from **PASS WITH HARDENING** to **FAIL**, and changes the overall Phase 8 classification's severity profile accordingly (the tier itself, DEVELOPMENT ONLY, does not change — it was already the floor).

## How to use this document

- Work phase by phase, in order. Within a phase, fix Critical → High → Medium → Low.
- Each finding lists: severity, classification, exact file(s), what's wrong, the failure scenario, the smallest safe fix, and the regression test required. Implement the fix **and** the test together.
- **Do not** re-architect working business logic. Every phase's "Strengths" section lists what was independently verified as correct — much of it via live tests run against real PostgreSQL — and must be preserved exactly.
- Findings added in this revision are marked **[Added on cross-review]** and carry the fresh verification evidence, not just the original reviewer's claim.
- Where a finding says "PLAUSIBLE RISK," "HARDENING RECOMMENDATION," or "DESIGN DECISION" rather than "PROVEN DEFECT," no live exploit was reproduced, or the behavior is a defensible business choice pending confirmation — fix accordingly, not as an emergency.
- After each phase's fixes land, re-run that phase's live tests before moving to the next phase.

---

## PHASE 1 — Architecture, Database Schema & Migrations

**Verdict: CONDITIONAL PASS** (unchanged)

### CRITICAL — C1-1: Migration provenance drift blocks application startup against a real, previously-migrated database

- **Files:** `Data/DatabaseBootstrapper.cs:52-64`, `Data/Migrations/011_stock_movement_kind_length.sql`, `Data/Migrations/014_material_in_stock_movement_kinds.sql`
- **Proven live:** Running `DatabaseBootstrapper.InitializeAsync()` against the real `textrack_dev` database throws `Migration integrity check failed for version 011` — the database recorded migration 011 as `011_jwo_component_rate_material_out_cancel_fix.sql` (different file, different SHA-256) than what's on disk today. Same for version 014.
- **Fix (revised per cross-review — the original recommendation was unsafe):** Do **not** verify-then-overwrite the historical `schema_versions.file_name`/`checksum` to match today's files — doing so erases the true historical record of what SQL actually executed and falsely implies the renamed file is what ran. Instead:
  1. Preserve the originally recorded migration identities exactly as they are.
  2. Build an explicit **compatibility manifest** — a small, reviewed, checked-in list recognizing specific legacy `(version, file_name, checksum)` triples as approved-equivalent to specific current files, based on a manual/tooled inspection proving they produced the same schema effect.
  3. Have `DatabaseBootstrapper` consult this manifest (not silently rewrite history) when it encounters a recognized legacy identity, and continue applying forward migrations normally.
  4. For any genuine schema difference between the legacy and current version of a migration, introduce a new forward migration to reconcile the difference — never retroactively edit the historical row.
- **Regression test:** A `DatabaseBootstrapperTests` case seeding `schema_versions` with the legacy identity, confirming the manifest recognizes it without rewriting `schema_versions`, and that migration application continues correctly through 025.
- **Risk of fix:** None to business logic — deployment tooling only.

### CRITICAL — C1-2: `stock_movements.movement_kind` has no database-level enumeration constraint

- **Files:** `Data/Migrations/015_fix_mi_movement_kind.sql`, `Services/StockMovementSemantics.cs`, `Services/StockPostingService.cs:107-119`
- Migration 015 dropped `ck_stock_movement_kind` entirely; no later migration restores it. Confirmed live against `textrack_dev`: zero check constraints on `movement_kind`.
- **Fix:** New migration re-adding `CHECK (movement_kind IN (...))` with the full current 8-value vocabulary. Verify against `SELECT DISTINCT movement_kind FROM stock_movements` in production before deploying.
- **Regression test:** Insert a row with `movement_kind = 'Bogus'`, assert constraint violation.

### HIGH — H1-1: EF-model-vs-database drift check covers only 9 tables and only nullability

- **File:** `Tests/TexTrack.Web.IntegrationTests/MigrationChainTests.cs:139-175`
- **[Added on cross-review — concrete confirmed instance of this exact gap]:** `TexTrackDbContext.cs:471` declares `entity.HasIndex(x => new { x.FinishedGoodId, x.StockItemId }).IsUnique();` on `JobWorkOrderComponent` — but this exact constraint was **dropped** from the database in `Data/Migrations/019_multilevel_bom_wip.sql:87` and replaced with a different unique index (`uq_jwo_component_stage_line` on `(bom_stage_id, finished_good_id, line_number)`) specifically to allow the same stock item to appear as a component at multiple BOM stages. The EF model has been silently wrong about this constraint since migration 019 shipped. (Verified this does not cause a live save failure — EF's `HasIndex().IsUnique()` metadata isn't independently enforced client-side against the real, different DB constraint — but it is a confirmed, concrete case of exactly the undetected drift this finding warns about.)
- **Fix:** Extend `MigrationChainTests`' comparison to cover all tables and include uniqueness/index comparison, not just nullability on 9 tables. Update `TexTrackDbContext.cs:471` to match the real constraint or remove the stale index declaration.
- **Regression test:** The extended comparison itself; a specific assertion that `JobWorkOrderComponent`'s EF index metadata matches the live `uq_jwo_component_stage_line` constraint.

### HIGH — H1-2: Several regression tests assert source-code text, not runtime behavior

- **File:** `Tests/TexTrack.Web.IntegrationTests/ProductionHardeningRegressionTests.cs`
- Unchanged from original audit — see prior finding detail. Replace with functional tests.

### HIGH — H1-3: Migration runner does not detect a missing or duplicated migration number **[Added on cross-review]**

- **File:** `Data/DatabaseBootstrapper.cs` (full file reviewed)
- **Confirmed:** `InitializeAsync` orders migration files alphabetically by filename and applies each in order — there is no check that the parsed version numbers form a complete, gap-free, non-duplicated sequence before applying. A migration file accidentally omitted from a release package (e.g., `003_x.sql` missing between `002_` and `004_`) would be silently skipped with no error; the bootstrapper simply has nothing to apply for that number. A genuinely duplicated version number (two files both parsing to version 11) is *incidentally* caught today by the existing checksum/filename comparison on the second file — but it fails with the misleading message "Applied migrations are immutable... Restore the original migration file," which is confusing for a duplicate-numbering problem rather than tampering.
- **Fix:** Before applying any migrations, validate that the discovered version numbers are a contiguous sequence starting at 1 with no duplicates and no gaps, and fail fast with a clear, specific message if not.
- **Regression test:** A migration directory missing a number in the middle of the sequence; assert a clear "missing migration N" error rather than silently proceeding or a confusing integrity-check message.

### MEDIUM — M1-1: `DatabaseMaintenanceService.ClearAllBusinessDataAsync` re-parses frozen migration files at runtime and has a dead duplicate seed call

- Unchanged from original audit.

### MEDIUM — M1-2: `financial_years` has no date-order, overlap, or single-active constraint

- Unchanged from original audit.

### LOW — L1-1, L1-2

- Unchanged from original audit (ConcurrencyToken column-width metadata mismatch; stray `.bak` file).

### Strengths to preserve (Phase 1)

- Unchanged from original audit — extensive real `CHECK` constraints, migration 019's BOM-cycle trigger, migration 025's composite-FK technique (note: this exact technique is what's *missing* from `bill_of_materials.current_revision_id` — see Phase 4 FR4-11), `DatabaseBootstrapper`'s advisory-lock/transactional/checksum discipline verified live.

---

## PHASE 2 — Security, Authentication & Tenant Isolation

**Verdict: CONDITIONAL PASS** (unchanged tier; findings strengthened)

### CRITICAL — C2-1: "Clear All Business Data" destroys the audit trail and doesn't log itself doing it

- Unchanged from original audit.

### HIGH — H2-1: Authorization "tests" are string-matches, not enforcement tests

- Unchanged from original audit.

### HIGH — H2-2: Login successes, failures, and lockouts are never recorded as security audit events **[Added on cross-review]**

- **File:** `Services/UserIdentityService.cs` (full file reviewed)
- **Confirmed:** Searched every `AuditLogs.Add` call site in the codebase (12 files). `UserIdentityService.cs` is not among them. `AuthenticateAsync` never writes an audit entry for a successful login, a failed attempt, or a lockout being triggered — every existing audit entry is a business-voucher action (Create/Update/Cancel/Delete). Combined with C2-1 (the audit log itself can be wiped) and Phase 6's finding of no persistent logging, an authentication attack pattern — repeated failed logins, a lockout, a suspicious login from an unusual context — leaves **no trace anywhere** in this system once it happens, beyond the transient `FailedLoginCount`/`LockoutEndUtc` fields on the user row itself (which get reset to zero on the next successful login, erasing even that trace).
- **Fix:** Add `AuditLog` entries for login success, login failure, and lockout-triggered events in `AuthenticateAsync`.
- **Regression test:** Authenticate with a wrong password N times to trigger lockout; assert corresponding `AuditLog` rows exist.

### HIGH — H2-3: Login lockout counter update is non-atomic — concurrent failed attempts can lose updates to each other **[Added on cross-review]**

- **File:** `Services/UserIdentityService.cs:154-165`
- **Confirmed:** `AuthenticateAsync` reads `user.FailedLoginCount`, increments it in memory (`user.FailedLoginCount++`), and calls `SaveChangesAsync()`. `ApplicationUser` is configured in `ConfigureIdentity` without `ConfigureAuditProperties` and carries no `ConcurrencyToken`/optimistic-concurrency check at all (confirmed by re-reading its full EF configuration). Two truly concurrent failed-login requests for the same account both read the same starting count, both increment independently in memory, and the second `SaveChangesAsync()` simply overwrites the first's result — a classic lost update. My original live verification of lockout (`UserIdentitySecurityTests`) only ever tested **sequential**, awaited attempts, which cannot surface this race — that's precisely why it wasn't caught originally.
- **Failure scenario:** A distributed or multi-threaded brute-force script sending genuinely parallel guesses can cause some increments to be silently lost, meaning more than `MaxFailedAttempts` (5) real wrong-password attempts may be needed to actually trigger the lockout — weakening the control's real-world effectiveness under exactly the attack pattern it exists to stop.
- **Fix:** Perform the increment atomically at the database level (e.g., `UPDATE application_users SET failed_login_count = failed_login_count + 1 WHERE id = @id AND ...` via a single statement, or wrap the read-increment-write in a `Serializable`/row-locked transaction) rather than a read-modify-write over a plain EF-tracked entity.
- **Regression test:** Fire N concurrent wrong-password `AuthenticateAsync` calls (N > `MaxFailedAttempts`) and assert lockout is reliably triggered exactly once the true count is reached, with no lost increments.

### MEDIUM — M2-1 through M2-4

- Unchanged from original audit (missing `UseForwardedHeaders`; 256MB Tally upload; login page DB-status disclosure; AI assistant raw error leakage to non-Developer roles).

### MEDIUM — M2-5: Developer-configurable AI provider URL has no restriction against private/loopback network targets **[Added on cross-review]**

- **File:** `Services/AssistantSettingsStore.cs:82-92` (`Validate`)
- **Confirmed:** `Validate` checks only that the URL is absolute, scheme is http/https, and has no embedded userinfo — no check against loopback (`127.0.0.1`), link-local, or RFC1918 private ranges. This is a real server-side-request-forgery primitive: a Developer-role account can point the assistant's outbound HTTP calls at any internal-only address reachable from the server, with the "evidence" text embedded as the request body.
- **Severity note:** This requires an already Developer-privileged account — it is not anonymously or remotely exploitable. Classified Medium rather than Critical for that reason, but still worth closing since a compromised or malicious Developer account shouldn't gain a network-probing primitive as a side effect of a business feature.
- **Fix:** Reject loopback/private/link-local address targets in `Validate` unless an explicit administrative override is set.
- **Regression test:** Attempt to save `http://127.0.0.1:...` or `http://169.254.169.254/...` as the provider URL; assert rejection.

### MEDIUM — M2-6: Existing sessions revalidate account-active status and security stamp, but not current role or company membership **[Added on cross-review]**

- **File:** `Services/UserIdentityService.cs:217-224` (`IsPrincipalValidAsync`)
- **Confirmed as a real gap in scope; not currently exploitable.** `IsPrincipalValidAsync` checks only `x.IsActive && x.SecurityStamp == stamp`. I searched the entirety of `UserIdentityService.cs` for any method that changes an *existing* user's roles or company memberships after creation, and found **none** — `CreateUserCoreAsync` sets them once at creation; there is no "change role" or "change company access" admin feature anywhere in this codebase today. This means the scenario Codex describes (a role changed mid-session without invalidating the cookie) cannot currently be triggered through the application, because there is nothing yet that changes a role.
- **Classification: Design Decision / forward-looking gap**, not a live vulnerability today.
- **Fix (before any role/company-management feature is built):** Either have such a feature bump `SecurityStamp` when it changes roles/company access (forcing re-authentication), or extend `IsPrincipalValidAsync` to re-read and compare current roles/company memberships against the cookie's claims on each validation.
- **Regression test:** Once a role-change feature exists, change a logged-in user's role and assert their existing session either re-authorizes with the new role or is invalidated — never continues operating under the stale role.

### LOW — L2-1 through L2-4

- Unchanged from original audit.

### LOW — L2-5: Raw database/exception details are surfaced through more UI paths than originally scoped **[Added on cross-review — broadens Phase 3 H3-1]**

- **Confirmed:** Beyond the three Material Out/In methods flagged in Phase 3 (H3-1), the pattern of returning `ex.GetBaseException().Message` directly as the user-facing failure reason also appears in `MasterJobOrderRepository.cs` (`SaveAsync`, `DeleteAsync`, `CancelAsync`) and `JobWorkOrderRepository.DeleteAsync`. This is a broader, systemic pattern across the voucher repositories, not isolated to Material Out/In.
- **Fix:** Apply the same targeted `PostgresException`/`DbUpdateException` catch-and-translate pattern already used correctly in `JobWorkOrderRepository.SaveAsync`/`BillOfMaterialRepository.SaveAsync` across all repositories that currently return raw exception text.

### LOW — L2-6: One-time recovery code is written through the general logging pipeline, not console-only as documented **[Added on cross-review]**

- **File:** `Services/UserIdentityService.cs:283` (`IdentityBootstrapper.InitializeAsync`)
- **Confirmed:** `logger.LogCritical("...one-time recovery code {RecoveryCode}...", recoveryCode)` is emitted through the standard `ILogger` pipeline, which under `Program.cs`'s configured providers (`AddConsole()`, `AddDebug()`) writes to **both** the console **and** the .NET Debug/Trace output stream. The code's own comment on `FirstRunRecoveryService` states the code "is printed to the local server console" only — the actual implementation is broader than that stated intent. Debug/Trace output can be captured by any process with debug-capture privileges on the same machine (e.g., DebugView), independent of whether the console window itself is visible to that process.
- **Fix:** Write the recovery code directly to `Console.WriteLine` (bypassing the general logging pipeline) if console-only exposure is the actual intended design, or update the code comment to accurately reflect that it also reaches Debug/Trace listeners.

### Strengths to preserve (Phase 2)

- Unchanged from original audit — all verified-live strengths stand (password hashing, timing-safe login, CSRF, fallback authorization, tenant isolation, AI assistant execution boundary, no XXE, SQL-injection resistance, clean dependency scan).

---

## PHASE 3 — Transactions, Concurrency & Failure Atomicity

**Verdict: PASS WITH HARDENING** (unchanged)

All findings unchanged from original audit (H3-1 raw-error surfacing on Material Out/In's highest-concurrency paths; M3-1 no idempotency key; M3-2 `MasterJobOrderRepository.DeleteAsync` isolation-level inconsistency; M3-3 Developer can alter a cancelled voucher). See Phase 2's L2-5 above for the broader scope of the raw-error-surfacing pattern this phase first identified.

Strengths unchanged — voucher numbering proven race-free live, consistent Serializable isolation, real optimistic concurrency, no partial commits by construction.

---

## PHASE 4 — BOM / JWO / Material Out / Material In

**Verdict: FAIL** (revised from PASS WITH HARDENING — this phase's original verdict is retracted)

This phase's original review verified the BOM expansion, revisioning, and stage-assignment machinery correctly and thoroughly — that verification stands and none of it should be touched. But the original review did not adequately test the interaction *between* Material Out, Material In, and voucher lifecycle (cancel/delete) once material has actually moved, and missed a complete functional break for an entire supported JWO-creation mode. Both are corrected below with full evidence, independently re-verified.

### CRITICAL — C4-1: Cancelling a Material Out voucher does not check whether a Material In has already consumed it **[Added on cross-review]**

- **Files:** `Services/MaterialOutRepository.cs:741-790` (`CancelAsync`), compare to `MaterialOutRepository.cs:805-849` (`DeleteAsync`), `Services/MaterialInRepository.cs:588-602`
- **Confirmed precisely.** `CancelAsync`'s only downstream-activity check is `lifecycle.GetActiveLinkDescriptionsAsync(..., VoucherLinkScope.Downstream, ...)`, which looks for `VoucherLinks` where **this Material Out voucher is the source**. I traced every `VoucherLinks.Add` call in the entire codebase: links are created as `JWO_TO_MATERIAL_OUT`, `JWO_TO_MI`, and `JWO_TO_MO` (Tally variant) — **the JWO is always the source; Material Out and Material In are only ever linked as siblings under their common parent JWO.** There is no direct Material-Out-to-Material-In link anywhere in the schema or the code. This means `CancelAsync`'s check can *never* find anything, for any Material Out voucher, regardless of whether it has been consumed.
  The actual consumption relationship lives entirely in `MaterialInMaterialOutAllocation` (populated during Material In's FIFO allocation, `MaterialInRepository.cs:588-594`) — a table `CancelAsync` never queries. By direct contrast, `MaterialOutRepository.DeleteAsync` (lines 820-825, in the *same file*) has the **correct** guard: it queries `MaterialInMaterialOutAllocations` directly and blocks with `"Material Out {number} cannot be deleted because Material In {number} consumes its issued material."` This proves the team built the correct check once — for Delete — and never applied it to Cancel.
- **Failure scenario:** Issue material via Material Out. Receive/consume it via Material In (finished goods now posted into stock, valued using that consumption). Cancel the original Material Out. `stockPosting.ReverseVoucherPostingsAsync` posts negative reversal movements as if the material was never issued — while the Material In's consumption records, finished-goods receipt, and their stock postings remain completely untouched. The ledger now shows finished goods in stock that are traceably costed against raw material the ledger simultaneously says was never dispatched.
- **Business consequence:** A direct, reproducible "impossible physical state" — exactly the failure class this audit was specifically tasked with finding in Phase 4, and the original pass missed it.
- **Smallest safe fix:** Add the identical guard already present in `DeleteAsync` to `CancelAsync` — query `MaterialInMaterialOutAllocations` for this voucher's lines with a non-cancelled consuming voucher, and block cancellation with the same clear message.
- **Regression test:** Issue → consume via Material In → attempt to cancel the Material Out; assert rejection naming the consuming Material In voucher.
- **Risk of fix:** None — this closes a gap without touching any working path; cancelling an *unconsumed* Material Out continues to work exactly as before.

### CRITICAL — C4-2: A Material In receipt's cost is not validated against the BOM-implied consumption ratio, allowing arbitrarily wrong valuation **[Added on cross-review]**

- **File:** `Services/MaterialInRepository.cs:460-535` (ceiling validation), `:605-611` (valuation)
- **Confirmed precisely.** The two validation loops in `SaveAsync` are independent: the finished-goods loop (lines 470-506) checks cumulative received-vs-supported using a ratio-adjusted bottleneck across *cumulative* issued/required quantities; the consumption loop (lines 529-535) checks cumulative consumed-vs-available. Neither loop, nor anything between them, checks that *this specific receipt's* consumed quantity is proportionate to *this specific receipt's* received output quantity against the BOM's stated ratio. At line 609: `materialValue = consumedValueByStage.GetValueOrDefault((stage.Id, input.StageAssignmentId))` — this pulls directly from whatever was declared as consumed in the `consInputs` loop of the *same* voucher, with no proportionality check.
- **Failure scenario (your example, confirmed reproducible against this code):** A BOM calls for 10kg of fabric per 10 pieces. 10kg has already been issued via Material Out (satisfying the ceiling check). A single Material In voucher declares `ReceivedQuantity = 10 pieces` and `ConsumedQuantity = 0.01kg` in the same submission — both individually pass their respective cumulative ceiling checks. The resulting `MaterialInFinishedGood.MaterialValue` is costed from only the 0.01kg consumption, producing a silently, materially wrong (understated) cost basis for all 10 pieces.
- **Business consequence:** Materially wrong stock/production valuation with no error raised — directly matching the audit brief's "wrong valuation" and "wrong accounting" concern categories, and invisible during casual UI testing since both individual numbers look plausible in isolation.
- **Smallest safe fix:** Add a proportionality check comparing this receipt's consumed-to-received ratio against the BOM's required-quantity-per-unit-of-output ratio for the relevant component(s), within a defined tolerance (to allow legitimate real-world process loss/yield variance), and reject or flag receipts falling outside it.
- **Regression test:** Attempt the exact 10kg/0.01kg/10-piece scenario above; assert rejection or an explicit override-required flag, not a silent low-cost posting.
- **Risk of fix:** Requires a business decision on acceptable yield-variance tolerance before implementing — flag this to the business owner rather than picking an arbitrary tolerance unilaterally.

### CRITICAL — C4-3: A manually-allocated (non-BOM) Job Work Out Order can be created and issued material against, but can never receive a Material In — a complete functional dead end **[Added on cross-review]**

- **Files:** `Services/JobWorkOrderRepository.cs:821-863` (`ValidateFinishedGoodsAsync`, BOM optional), `Services/MaterialOutRepository.cs:270-287` (null-safe component handling), `Services/MaterialInRepository.cs:402-436` (stage resolution — not null-safe)
- **Confirmed precisely, and more severe than the summary suggested.** `ValidateFinishedGoodsAsync` treats `fg.BillOfMaterialId` as fully optional — the entire BOM/stage validation block is gated behind `if (fg.BillOfMaterialId is long bomId) {...}`, and manually-entered components (`fg.Components`) are validated unconditionally regardless. `AddFinishedGoodsAsync` correspondingly only calls `AddBomStageSnapshot` when a BOM was selected — a manual JWO's `JobWorkOrderComponent` rows are created with `BomStageId = null` by construction, with no `JobWorkOrderBomStages` rows created for that finished good at all.
  `MaterialOutRepository.SaveAsync`'s component load is genuinely null-safe for this case: `AssignedJobWorkerId = x.BomStage != null ? x.BomStage.AssignedJobWorkerId : null`, correctly falling back to the JWO's own `PartyLedgerId` when there is no stage. **Material issuance against a manual JWO works correctly.**
  `MaterialInRepository.SaveAsync` has no equivalent fallback. Its "compatibility" stage-resolution block (lines 413-422) filters `x.BomStageId != null` — a manual component's null `BomStageId` is excluded, so resolution never succeeds. The later mandatory check (lines 428-436) requires every resolved stage ID to match a real `JobWorkOrderBomStages` row: `if (stages.Count != requestedStageIds.Count || ...) throw new InvalidOperationException("One or more production stages do not belong to the selected JWO.")`. For a manual JWO, this is **unconditionally unreachable success** — it will always throw.
- **Business consequence:** Once material has been issued against a manually-allocated JWO (which the system fully permits and validates), there is **no way to ever record its return** through Material In. This is not a data-integrity risk (the failure is a clean, fail-safe rejection, not a corruption) — it is a complete, silent product capability gap: an entire, explicitly-supported creation path (JWOs without a BOM) is fundamentally incompatible with half the workflow it exists to support.
- **Smallest safe fix:** Give `MaterialInRepository.SaveAsync`'s stage/assignment resolution the same null-safe fallback `MaterialOutRepository` already has — when a component/finished-good has no `BomStageId`, resolve directly against the JWO's own `PartyLedgerId`/component identity instead of requiring a `JobWorkOrderBomStages` row to exist.
- **Regression test:** Create a JWO with manually-entered components (no BOM), issue material via Material Out, then attempt to save a Material In receipt against it; assert success rather than "One or more production stages do not belong to the selected JWO."
- **Risk of fix:** Low, but touches core Material In validation — test thoroughly against both manual and BOM-based JWOs to confirm neither path regresses.

### HIGH — H4-1: Job Work Out Order status can read "Completed" based solely on material issued, without any finished goods ever received **[Added on cross-review]**

- **File:** `Services/MaterialOutRepository.cs` (`UpdateJwoStatusAsync`, code already read in original Phase 4 pass but not flagged): `jwo.Status = issued <= 0 ? "Open" : issued >= required ? "Completed" : "PartiallyProcessed";`
- **Confirmed exactly.** This computation uses only `issued` (cumulative Material Out) versus `required` (BOM total) — it never references Material In's `ReceivedQuantity` at all. A JWO shows `"Completed"` the moment all raw material has been dispatched to the job worker, regardless of whether a single finished good has come back.
- **Business consequence:** A status field meant to represent production completion instead represents dispatch completion — directly misleading for any dashboard, report, or business decision relying on JWO status to mean "the job is done."
- **Smallest safe fix:** Compute completion from finished-goods receipt (Material In) against ordered quantity, not from material issuance. Consider a distinct intermediate status (e.g., "Materials Dispatched") if that state is itself worth surfacing.
- **Regression test:** Issue 100% of required material via Material Out with zero Material In receipts; assert JWO status is not `"Completed"`.

### HIGH — H4-2: Cumulative final-stage receipts are not limited per colour/size variant **[Added on cross-review]**

- **File:** `Services/MaterialInRepository.cs:487-492`
- **Confirmed.** The final-stage check verifies variant quantities sum to the current receipt's total and are drawn only from pre-declared allowed variants — it never sums a given `StockItemVariantId`'s *cumulative* received quantity across all Material In vouchers against that specific variant's own ordered quantity (`JobWorkOrderSizeAllocation.Quantity`). Only the stage-level aggregate output ceiling (checked elsewhere) constrains the total.
- **Failure scenario:** A JWO orders 5 Black-Medium and 5 Black-Large (10 total). Nothing stops a sequence of receipts cumulatively recording 10 Black-Medium and 0 Black-Large, as long as the aggregate stage ceiling of 10 isn't exceeded.
- **Smallest safe fix:** Add a per-variant cumulative ceiling check alongside the existing aggregate stage-output check.
- **Regression test:** Attempt to over-receive one variant while under-receiving a sibling variant within the same aggregate ceiling; assert rejection.

### HIGH — H4-3: Job Worker Control and T-Format reports count intermediate-stage receipts as final finished-goods receipts **[Added on cross-review]**

- **Files:** `Services/OperationalReportingService.cs:186-201` (`GetJobWorkerControlAsync`), `Services/ProductionReportingService.cs:306-313` (`GetTFormatAsync`)
- **Confirmed in both.** Neither query filters `x.BomStageId == null || x.BomStage.IsFinalStage` on its `MaterialInFinishedGoods` read — both sum every stage's receipt (intermediate and final alike) into what's displayed and totaled as "received"/"finished goods." The Job Worker **Aging** report's equivalent query (verified correct in the original Phase 5 pass) does apply this exact filter, proving the team already knows it's required — it just wasn't applied consistently to these two reports.
- **Business consequence:** For any multi-stage JWO, these two reports overstate apparent finished-goods completion by including semi-finished intermediate receipts as if they were final output — directly undermining the "report reconciliation" guarantee Phase 5 otherwise verified held elsewhere.
- **Smallest safe fix:** Add the same `(x.BomStageId == null || x.BomStage.IsFinalStage)` filter to both queries.
- **Regression test:** A multi-stage JWO with an intermediate-stage receipt and no final-stage receipt yet; assert Job Worker Control and T-Format both show zero finished-goods received, matching the Aging report's existing correct behavior.

### HIGH — H4-4: Client-entered Material Out rates are never validated and propagate unchecked through the entire valuation chain **[Added on cross-review]**

- **File:** `Services/MaterialOutRepository.cs:318,898`
- **Confirmed.** `Rate` is validated only as `>= 0`. It flows unmodified: `MaterialOutLine.Rate` → `MaterialInMaterialOutAllocation.RateSnapshot` → `MaterialInConsumption.Value`/`Rate` → `MaterialInFinishedGood.MaterialValue`. No check anywhere compares it to the stock item's own cost fields or a recent-average rate.
- **Business consequence:** A data-entry error or deliberate manipulation at the very first step of the chain (Material Out) silently and permanently corrupts the costed value of everything downstream for that production lot.
- **Smallest safe fix:** Add a sanity-check/warning (not necessarily a hard block, pending business input) when an entered rate deviates substantially from the stock item's last-known rate or standard cost.
- **Regression test:** Enter an obviously wrong rate (e.g., off by 100x) and assert the system at minimum surfaces a warning before posting.

### MEDIUM — M4-1: No ceiling ties a JWO's variant allocation back to Master Job Order demand

- Unchanged from original audit.

### MEDIUM — M4-2: No cross-voucher chronological validation — Material In can be dated before its supplying Material Out; Material Out can be dated before its parent JWO **[Added on cross-review]**

- **Files:** `Services/MaterialInRepository.cs:387`, `Services/MaterialOutRepository.cs:239`
- **Confirmed.** Both methods validate `request.VoucherDate` only against the current financial year's start/end dates — neither compares against the parent JWO's `VoucherDate` or, for Material In, the specific Material Out voucher(s) being consumed.
- **Business consequence:** Reports and audit trails relying on chronological ordering (e.g., "how long did material sit before being consumed") can show negative or nonsensical durations; not a data-corruption risk, but a real integrity/reporting-accuracy gap.
- **Smallest safe fix:** Add validation that a Material Out's date is not earlier than its JWO's date, and a Material In's date is not earlier than the latest date among the Material Out vouchers it draws from.
- **Regression test:** Attempt to date a Material In before its consumed Material Out's date; assert rejection.

### MEDIUM — M4-3: Component variant identity is not recorded on raw-material stock movements **[Added on cross-review]**

- **Files:** `Services/MaterialOutRepository.cs` (`AddPostedMovements` — zero `StockItemVariantId` usages anywhere in the file), `Services/MaterialInRepository.cs:598-602` (`MaterialInConsumption` posting)
- **Confirmed, precise scope.** Material Out's source/destination postings and Material In's consumption posting never populate `StockMovement.StockItemVariantId`, even when the underlying stock item has colour/size variants defined. By contrast, `MaterialInFinishedGoods` postings (lines 616-621) do carry variant identity when applicable. This is an asymmetry specific to raw-material/component movements, not finished goods.
- **Business consequence:** Closing-stock/stock-register reports cannot break down quantity/value by colour/size for outgoing or consumed component stock at the movement level — only for finished-goods receipts.
- **Smallest safe fix:** Thread `StockItemVariantId` through `MaterialOutLine`/`MaterialInConsumption` postings when the component stock item carries variants, matching the pattern already used for finished goods.

### MEDIUM — M4-4: `bill_of_materials.current_revision_id` has no compound constraint tying it to a revision of that same BOM

- **File:** `Data/Migrations/022_bom_jwo_revision_foundation.sql:85-86`
- **Confirmed.** Plain `FOREIGN KEY (current_revision_id) REFERENCES bill_of_material_revisions(id)` — no compound key ensuring the referenced revision's `bom_id` matches. The exact composite-FK-pairing technique needed here is already used elsewhere in this codebase (migration 025, for stage-assignment pairing) — it simply wasn't applied here. Application code (`BillOfMaterialRepository.SaveAsync`) always sets this correctly by construction today; this is a defense-in-depth gap, not a proven live defect.
- **Fix:** Add a unique index `(id, bom_id)` on `bill_of_material_revisions` (mirroring migration 025's pattern) and a compound FK from `bill_of_materials(current_revision_id, id)` to it.

### MEDIUM — M4-5: JWO revision history is physically deleted, though only reachable for JWOs with no production history

- **File:** `Services/JobWorkOrderRepository.cs:703`
- **Confirmed**, with the important nuance that `DeleteAsync` (where this occurs) is only reachable when `GetActiveLinkDescriptionsAsync(..., VoucherLinkScope.Any, ...)` returns zero links — meaning only JWOs with **no** downstream Material Out/In activity can ever be deleted this way. Real production revision history can never reach this code path today.
- **Fix:** If retaining pre-production revision history is desired for audit completeness even for deleted JWOs, soft-delete revisions instead of hard-deleting.

### Strengths to preserve (Phase 4) — unchanged, all still verified correct on re-inspection

- Multi-level BOM expansion quantity math, verified live.
- Concurrent BOM cycle rejection under a real `Task.WhenAll` race, verified live.
- Immutable BOM revisions with correct no-op detection, verified live.
- Child-revision pinning / old-order isolation, verified live.
- Stage identity survives reassignment with correct versioned history, verified live.
- The "ratio-adjusted bottleneck" ceiling logic itself (as distinct from C4-2's proportionality gap) is correctly implemented for what it does — capping maximum producible output by the scarcest cumulative input.
- FIFO consumption allocation with full value-snapshot traceability.
- Cancellation-as-reversal (not deletion) pattern, and delete correctly blocked once any downstream link exists — **except** for the specific Cancel-after-consumption gap in C4-1, which sits directly alongside this otherwise-correct pattern.

---

## PHASE 5 — Inventory, Valuation, WIP & Report Reconciliation

**Verdict: FAIL** (revised from PASS WITH HARDENING, due to H4-3 above being a report-reconciliation defect within this phase's direct scope, and in light of Phase 4's downgrade affecting confidence in figures this phase's reports display)

### MEDIUM — M5-1, M5-2

- Unchanged from original audit (dead-but-live FY-truncated Closing Stock implementation; Job Worker Aging's single-financial-year scoping).

### Note on H4-3

Job Worker Control and T-Format's intermediate-stage-as-final-receipt aggregation (Phase 4, H4-3) is as much a Phase 5 "report reconciliation" defect as a Phase 4 production-chain defect — it is filed under Phase 4 to keep it beside the Material In logic it stems from, but fixing it is squarely this phase's reporting-layer responsibility, and the fix should be verified against the same live-test rigor Phase 5's other reports already meet.

### LOW findings and Strengths

- Unchanged from original audit. The 39/39 live-passing `ProductionReportingTests.cs` suite and the "one canonical aging engine" architecture remain genuine, verified strengths — they simply don't cover the two specific reports found broken in H4-3, which use their own separate queries rather than the canonical aging engine.

---

## PHASE 6 — Deployment, Backup, Restore & Operations

**Verdict: FAIL** (unchanged)

All findings unchanged from original audit: C6-1 (launcher forces Development mode), C6-2 (no backup/restore tooling), H6-1 (stale script reference), H6-2 (no production packaging/service hosting), H6-3 (no persistent logging), M6-1 (Data Protection key persistence), M6-2 (no health check), M6-3 (stray `.bak` files).

**Test-baseline correction [Added on cross-review]:** The original audit asserted the project's tests were healthy without checking the JavaScript suite. A second reviewer reported 43/87 JavaScript tests passing (44 failing). This audit could not execute the suite directly (no Node.js available in this environment) but confirmed the specific mechanism cited: `Tests/JavaScript/textrack-alt-delete.test.cjs`'s `vm.createContext({...})` (line 157) does not define `MutationObserver`, and `wwwroot/js/textrack-alt-delete.js:1134` calls `new MutationObserver(sync)` **unguarded** — a different, correctly-guarded reference exists at line 725 (`typeof MutationObserver === 'function'`), showing the codebase knows how to guard this call and simply didn't at line 1134. This file alone accounts for 49 of the suite's roughly 88 test cases and would fail immediately on `vm.runInContext` in this harness. This is treated as **Confirmed** based on direct mechanism verification, though the exact pass/fail count was not independently reproduced.

- **Fix:** Add `MutationObserver` (a minimal stub sufficient for the test's needs, or a real polyfill) to the `vm.createContext` in the shared test harness, and audit for any other unguarded browser-global references the harness doesn't provide. Re-run the full JS suite and confirm a true 100% pass rate before relying on "tests pass" as a release gate.
- **Regression test:** The JS suite itself, once fixed, is the regression test — add a CI step that actually runs it, since its absence from this audit's original verification suggests it isn't part of anyone's routine check today either.

Strengths unchanged.

---

## PHASE 7 — Enterprise Architecture & Maintainability

**Verdict: PASS WITH HARDENING** (unchanged)

All findings and strengths unchanged from original audit (M7-1 triplicated voucher-numbering logic; CS7-1 no accounting/ledger-posting engine exists).

---

## PHASE 8 — Final Readiness Classification

**Overall classification: DEVELOPMENT ONLY** (tier unchanged — it was already the floor of the scale — but the case for it is now substantially stronger, and the path out of it is longer)

### What changed in this revision

The original Phase 8 synthesis rested on four cross-phase Critical findings in deployment, security-operations, and audit-trail integrity, atop what was assessed as fundamentally sound BOM/JWO/Material Out/In business logic. That second half of the picture no longer holds unqualified. **Three additional Critical findings (C4-1, C4-2, C4-3) are now confirmed directly in the production/inventory transaction chain itself** — the part of the system this audit most heavily relied on live testing to validate. Two of those three are genuine data/valuation-integrity defects (a cancellable-after-consumption Material Out, and an unvalidated consumption-to-output ratio silently corrupting cost), and the third is a complete, confirmed functional dead end for an entire supported feature (manually-allocated JWOs can never receive material back). None of this invalidates the specific things Phase 4's live tests *did* prove correct — the BOM expansion math, concurrent cycle protection, and stage-assignment versioning are all still verified sound — but it means the phase's overall claim of "the core transaction chain is trustworthy" cannot stand, and neither can any deployment-readiness judgment that assumed it.

### Updated Critical findings register (all phases)

1. **[Phase 1] C1-1** — real customer databases can become permanently unable to start after a migration rename.
2. **[Phase 1] C1-2** — no DB-level enforcement of the stock-movement-kind vocabulary.
3. **[Phase 2] C2-1** — the destructive data-wipe feature also destroys the audit trail, unlogged.
4. **[Phase 4] C4-1** — cancelling a Material Out after its material has been consumed reverses the dispatch while leaving the consumption and finished-goods receipt intact — a direct, reproducible impossible-state defect.
5. **[Phase 4] C4-2** — Material In valuation is not checked for proportionality between what's consumed and what's received in the same transaction, allowing silently, arbitrarily wrong production costing.
6. **[Phase 4] C4-3** — manually-allocated Job Work Out Orders can be created and issued material against, but can never receive material back via Material In — a complete, confirmed functional break.
7. **[Phase 6] C6-1** — the shipped launcher forces every real deployment into Development mode, silently disabling security hardening verified correct in the code.
8. **[Phase 6] C6-2** — no backup or restore tooling exists anywhere.

### Blockers to INTERNAL PILOT READY (revised)

The original five-item list is retained, **and now must include the three new production-chain criticals before any pilot — internal or otherwise — touches real production data**, since C4-1 and C4-2 are live data-corruption paths, not merely operational gaps:

1. Fix C1-1 (migration-provenance compatibility manifest, per the corrected approach above).
2. Fix C2-1 (exclude `audit_logs` from the wipe; self-audit the reset).
3. **Fix C4-1 (Material Out cancel-after-consumption guard) — new, and non-negotiable before any real material movement is trusted to this system, even internally.**
4. **Fix C4-3 (manual-JWO/Material-In incompatibility) — new; otherwise an entire creation mode is a trap for whoever hits it first.**
5. Fix C6-1 minimally (stop forcing Development mode).
6. Establish at least a manual backup runbook (partial C6-2).
7. Fix H6-1 (stale script reference).

### Blockers to CONTROLLED CUSTOMER PILOT READY (revised)

The original seven-item list is retained, **with C4-2 (valuation proportionality) and H4-1/H4-2/H4-3 added** — a paying pilot customer's production-cost and completion-status figures must be trustworthy before they're shown to someone making real business decisions on them:

1. H6-2 (real packaging + Windows Service hosting).
2. H6-3 (persistent logging).
3. Full C6-2 (automated, drilled backup/restore).
4. M6-1 (Data Protection key persistence).
5. H3-1 / L2-5 (stop surfacing raw Postgres/exception errors across all affected repositories, not just the original three).
6. M6-2 (health-check endpoint).
7. M5-1 and M4-1 (dead FY-truncated Closing Stock code; MJO allocation ceiling).
8. **C4-2 (valuation proportionality check) — new.**
9. **H4-1, H4-2, H4-3 (JWO completion status, per-variant receipt ceilings, Job Worker Control/T-Format intermediate-receipt aggregation) — new; a pilot customer will make real staffing/scheduling/costing decisions off these exact numbers.**
10. **H2-2, H2-3 (security-event audit logging; atomic lockout) — new; a pilot deployment is the first time this system faces a real, if small, external attack surface.**

### Blockers to PRODUCTION READY and ENTERPRISE DEPLOYMENT READY

Unchanged in structure from the original synthesis, with the following additions:
- **Production Ready** must additionally include: H4-4 (MO rate sanity-checking), M4-2 (chronological validation), M4-3 (component variant identity in postings), M4-4 (BOM current-revision compound constraint), H1-3 (migration sequence-completeness check), and a genuinely green JavaScript test suite (the `MutationObserver` fix and a full re-run).
- **Enterprise Deployment Ready** is unchanged in substance — CS7-1 (no accounting engine) remains the dominant scoping caveat for any "enterprise ERP" claim.

### What must NOT change

Unchanged from the original synthesis: the recursive BOM expansion math, stage/assignment versioning model, the *mechanism* of the ratio-adjusted bottleneck logic (as distinct from the proportionality gap layered on top of it in C4-2), FIFO consumption allocation, the migration runner's transactional/checksum discipline, and the tenant-isolation discipline applied throughout the repository layer. Every fix above should be additive — a new check, a new guard, a new audit entry — not a rewrite of any of this.
