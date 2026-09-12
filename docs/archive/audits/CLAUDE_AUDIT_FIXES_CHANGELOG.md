# Claude — Scoped Audit-Finding Fixes Changelog

Branch: `claude/audit-findings-fixes`
Scope, per Codex's terms (relayed 2026-09-04): temporary, targeted fixes for specific remaining audit findings only — not a takeover of development or a redesign. Smallest necessary fix plus a regression test per finding. No change to business rules, schema, historical data, or production calculations without asking first. Test database only, no deployment. Every fix below is marked **awaiting independent verification** until Codex reviews it.

All work in this document was independently source-verified before being fixed, not accepted on any report's word — including a live-executed reproduction where practical, per standing verification discipline.

---

## Fix 1 — Voucher error translator: bare LINQ framework exceptions could leak verbatim

**Status: awaiting independent verification**

**Finding:** `Services/VoucherErrorMessages.cs` (originally added under `H3-1`/`L2-5`) could not distinguish an application-authored business-validation `InvalidOperationException` from an accidental bare framework one (e.g. `Enumerable.Single()`/`First()` throwing on an empty or duplicate sequence). Both had `InnerException == null`, so both passed the same check and both had their raw `.Message` shown to the end user verbatim.

**Verification before fixing:**
- Live-executed a real `Array.Empty<int>().Single()` call and fed the genuine resulting exception through the real `VoucherErrorMessages.For()` — confirmed the raw string `"Sequence contains no elements"` passed through unchanged (test run, then removed — not left in the tree as a permanent artifact).
- Traced a specific hypothesized live trigger path (a job worker reassigned mid-JWO, then a Material In submitted against the now-superseded assignment) through `MaterialInRepository.cs`. **That specific hypothesis did not hold up** — the code correctly falls through to a clean business message (`"A BOM-based receipt must retain its production stage and assignment version."`) in that case. Retracted rather than left standing.
- Checked whether a concurrent-write race could produce the underlying data anomaly (two `Active` assignment rows for one job worker on one stage) that a raw `.Single()` at `MaterialInRepository.cs:499` could trip on. Confirmed `JobWorkOrderRepository.SaveAsync` (which contains the assignment-supersede-then-add logic) runs under `IsolationLevel.Serializable` — a genuine concurrent race is caught as a `PostgresErrorCodes.SerializationFailure` and already correctly translated to a safe message, not left to reach the bare-exception path.
- **Net conclusion carried into the fix:** the leak *mechanism* is real and demonstrated; no live, currently-reachable trigger path through the application was found. Treated as a defense-in-depth gap worth closing regardless, not a confirmed live exploit.

**Fix (smallest scope — no repository call sites changed):** Added an explicit denylist of the four exact, stable .NET/LINQ runtime message strings (`Sequence contains no elements`, `Sequence contains more than one element`, `Sequence contains no matching element`, `Sequence contains more than one matching element`, all live-verified against this runtime rather than assumed from memory) to `VoucherErrorMessages.cs`. Any bare `InvalidOperationException` carrying one of these exact strings now falls through to the existing generic safe message instead of being shown verbatim. Every other existing `InvalidOperationException`-based business validation message across all repositories is untouched — no call site anywhere was modified.

**Why not the larger fix:** a dedicated `BusinessValidationException` type (so the translator only ever trusts a purpose-built exception, not `InvalidOperationException` generally) would close this more completely, but requires touching every `throw new InvalidOperationException(...)` business-validation call site across `MaterialInRepository.cs`, `MaterialOutRepository.cs`, `JobWorkOrderRepository.cs`, `MasterJobOrderRepository.cs`, and `BillOfMaterialRepository.cs` — dozens of sites, real risk of missing one and silently breaking a currently-correct user-facing message. Left for Codex to design deliberately when development resumes, rather than done invasively under a temporary/scoped mandate.

**Files changed:**
- `Services/VoucherErrorMessages.cs` — added `IsFrameworkSequenceMessage` check.
- `Tests/TexTrack.Web.IntegrationTests/VoucherErrorMessagesTests.cs` — added `Bare_framework_sequence_exceptions_do_not_leak_verbatim` (4 cases, exact strings) and `Real_linq_single_failure_does_not_leak_verbatim` (executes a genuine `.Single()` failure, not a simulated string).

**Test results:** Full solution suite run after the fix: **222/222 passed, 0 failed** (baseline was 217; +5 new regression tests, zero regressions). The pre-existing `Business_dependency_message_survives_but_unknown_internal_errors_do_not` test — which confirms genuine business messages still pass through unmodified — still passes.

**No business rules, schema, historical data, or production calculations were touched by this fix.**

---

## Fix 2 — H1-2: replaced a source-text regression test with a real behavioral test

**Status: awaiting independent verification**

**Finding:** `ProductionHardeningRegressionTests.cs` contained several tests that assert on raw source-code text (reading a `.cs`/`.razor`/`.js` file and checking for specific substrings) rather than exercising real runtime behavior. A source-text match doesn't catch a regression if the underlying *behavior* changes while the matched strings stay intact, or vice versa.

**Scoping done before touching anything:** reviewed all 6 tests in that file.
- `Material_out_stage_assignment_uses_the_migrated_postgresql_column` — already genuinely behavioral (uses EF's real Model API against actual column mapping). No fix needed.
- `Business_reset_source_preserves_identity_and_role_tables` — a true source-text test, and convertible using test infrastructure that already exists in this project (`IdentityTestEnvironment`'s disposable-schema pattern). **Fixed below.**
- The remaining three (`Modal_pruning_keeps_temporarily_hidden_keyboard_contexts_registered`, `Job_work_order_entry_does_not_require_preexisting_transactional_masters`, `Shared_header_filters_cover_reusable_data_grids_and_restore_navigation_state`) are also true source-text tests. Initially flagged as all needing new test infrastructure — **reassessed and corrected in Fix 4 below**, at Codex's direction: one of the two JS tests was closeable with the existing harness after all.

**Fix:** Replaced `Business_reset_source_preserves_identity_and_role_tables` with `DatabaseMaintenanceServiceTests.cs`, containing two real tests:
- `Clear_all_business_data_preserves_identity_audit_and_role_tables_and_reseeds_the_foundation` — actually runs `DatabaseMaintenanceService.ClearAllBusinessDataAsync` against a disposable PostgreSQL schema with real migrated data, a real created Developer user, and asserts: the developer's identity/role/company-membership rows survive, an audit log row is written, business data (ledgers) is truncated, and the mandatory company foundation is reseeded.
- `Clear_all_business_data_is_refused_without_developer_role` — confirms a non-Developer role is refused and business data is left untouched.

**Supporting change:** Added a `ConnectionString` property and a `CreateService(IHttpContextAccessor accessor)` overload to the existing `IdentityTestEnvironment` test helper (in `UserIdentitySecurityTests.cs`) so a caller-supplied identity (matching a specific real created user, needed for `VerifyCurrentUserPasswordAsync`) can be used instead of only the helper's default synthetic one. Purely additive — the existing `CreateService()` overload and every test using it is unchanged.

**Files changed:**
- `Tests/TexTrack.Web.IntegrationTests/DatabaseMaintenanceServiceTests.cs` — new file, 2 tests.
- `Tests/TexTrack.Web.IntegrationTests/ProductionHardeningRegressionTests.cs` — removed the superseded source-text test, left a comment pointing to its replacement.
- `Tests/TexTrack.Web.IntegrationTests/UserIdentitySecurityTests.cs` — additive `ConnectionString` property and `CreateService(IHttpContextAccessor)` overload on `IdentityTestEnvironment`.

**Test results:** Full solution suite: **223/223 passed, 0 failed** (baseline 222 after Fix 1; +2 new, −1 removed = net +1, 223 total). Zero regressions.

**No business rules, schema, historical data, or production calculations were touched by this fix.**

---

## Fix 3 — H2-1: real authorization enforcement tests, including cross-company isolation

**Status: awaiting independent verification**

**Direction from Codex:** use existing test infrastructure and a disposable database; test actual permitted and denied operations (including unauthorized company access where applicable), not just UI visibility; do not alter production authorization logic without reporting the defect and getting approval first; identify the sixth test omitted from the original H1-2 review.

**The sixth test:** `RolePermissionPolicyTests.cs`'s `Protected_surface_types_declare_role_policies` — missed in the original H1-2 review because it lives in a file named for a different finding (H2-1), even though it's the exact same anti-pattern: it read five source files (`Program.cs` and four `.razor` pages) and asserted policy-name substrings, never proving an unauthorized operation was actually blocked. `RepositoryMutationAuthorizationTests.cs` (10 test instances across 7 repositories and 3 services) is the same pattern at larger scale - not rewritten in full (see below for why), but its existence is logged here for visibility.

**No production authorization logic was altered.** `CurrentCompanyContext.RequirePolicy` and every repository's `RequirePolicy(SecurityPolicies.X)` call site were read, not changed. No defect in the actual enforcement mechanism was found - the gap was in test coverage only.

**What was actually proven, with real operations against a disposable PostgreSQL schema (`AuthorizationEnforcementBehaviorTests.cs`):**
- `LedgerRepository.SaveAsync` genuinely refuses an Operator role (`ManageMasters` policy) and throws `UnauthorizedAccessException` *before* any database write - confirmed by asserting the row was never created, not just that an exception happened.
- `LedgerRepository.SaveAsync` genuinely succeeds for a Manager role, verified by reading the saved row back from the database afterward - not assuming success from a return value alone.
- **Cross-company isolation, the specific gap flagged**: a ledger created under company 1 does not appear in company 2's `GetListAsync()` results, and an attempt to update it by ID from company 2's context throws `InvalidOperationException` ("the selected ledger no longer exists") rather than succeeding. The first version of this test used an invalid ledger-group reference and incorrectly passed for the wrong reason (a different, earlier company-scoped check rejected it first) - caught by running it and reading the actual failure, not assumed correct after one green run. Fixed by isolating the specific id-lookup check with a valid same-company ledger group, then re-verified.
- `AssistantSettingsStore.SaveAsync` genuinely refuses a Manager role (`DeveloperOnly` policy) before any file access, using an in-memory `EphemeralDataProtectionProvider` so the test has zero filesystem side effects.

**What remains source-text only, and why (narrowed, not blanket-deferred):** `RolePermissionPolicyTests.cs` now only checks `ViewReports` (enforced solely at the Razor page level - no service-layer `RequirePolicy` call exists to test directly) and `Program.cs`'s `RequireAuthorization(SecurityPolicies.ExportData)` (ASP.NET Core route middleware, not a repository call). Proving either behaviorally needs a `WebApplicationFactory`/`TestServer` or a Blazor component-testing setup - confirmed neither exists anywhere in this project (checked `Tests/TexTrack.Web.IntegrationTests/*.csproj` for `bunit`/`WebApplicationFactory`/`TestServer` references: none found). `RepositoryMutationAuthorizationTests.cs`'s remaining 10 source-text checks across other repositories were left as-is - the shared `RequirePolicy` mechanism they all delegate to is now proven real, but re-proving it once per repository was judged disproportionate to a scoped, temporary fix; flagged for Codex to decide whether to extend the pattern established here to the rest.

**Supporting additions to `IdentityTestEnvironment`** (in `UserIdentitySecurityTests.cs`, purely additive): `CreateCompanyContext(role, companyId)`, `CreateLedgerRepository(role, companyId)`, `CreateAssistantSettingsStore(role, companyId)`, `CreateLedgerGroupAsync(companyId, name)`, `CreateCompanyAsync(name)`. Nothing existing was changed.

**Files changed:**
- `Tests/TexTrack.Web.IntegrationTests/AuthorizationEnforcementBehaviorTests.cs` — new file, 4 tests.
- `Tests/TexTrack.Web.IntegrationTests/UserIdentitySecurityTests.cs` — additive helper methods on `IdentityTestEnvironment`.
- `Tests/TexTrack.Web.IntegrationTests/RolePermissionPolicyTests.cs` — narrowed the source-text test to only the two checks that genuinely still need it; renamed to `Report_and_export_surfaces_declare_their_role_policies`.

**Test results:** Full solution suite: **227/227 passed, 0 failed** (baseline 223 after Fix 2; +4 new). Zero regressions.

**No business rules, schema, historical data, or production calculations were touched by this fix.**

---

## Fix 4 — H1-2 (JS behaviors): reassessed, one closed with existing tooling, one honestly re-scoped

**Status: awaiting independent verification. The JavaScript test I wrote below could not be executed in this environment - Node.js is unavailable in both available shells, same limitation noted earlier in this audit. It was written by careful reading of the harness and production code, not run. Please execute `node --test Tests/JavaScript/textrack-alt-delete.test.cjs` before trusting it.**

**Direction from Codex:** the JS harness under `Tests/JavaScript` already exists - reassess whether it can cover the two JS-checking source-text tests without adding a framework, rather than assuming new infrastructure is needed. Correct: my original assessment was too hasty on one of the two.

**`Modal_pruning_keeps_temporarily_hidden_keyboard_contexts_registered` - closed, no new framework needed.** The existing harness in `Tests/JavaScript/textrack-alt-delete.test.cjs` already has a `FakeElement` with independent `connected`/`visible` flags and direct inspection of `controller.contexts` - everything needed. Read the real production code first (`wwwroot/js/textrack-alt-delete.js:28-35`, the `prune()` function) to confirm exactly what to test: it keys pruning on `isConnected` only, never on visibility, specifically so a modal making the page inert doesn't unregister the suspended context underneath it. Added `'a context hidden by an inert modal stays registered - only detached roots are pruned'` to that file, following the exact pattern of the adjacent existing test. One subtlety caught while writing it: `register()` itself refuses to register on an already-invisible element (line 54) - the test has to register while visible, then hide the element afterward, to exercise the actual real-world sequence (a form is open, then a modal opens over it), not an unreachable state. Removed the superseded C# source-text test from `ProductionHardeningRegressionTests.cs`, replaced with a comment pointing to the new JS test.

**`Shared_header_filters_cover_reusable_data_grids_and_restore_navigation_state` - re-scoped, left as source-text, but for a more precise reason than before.** No existing harness models tables, `sessionStorage`, or `location` the way `textrack-header-filters.js` needs - covering it behaviorally means writing a new harness *file* of comparable size to the existing ones (a real, buildable piece of work), not adding a framework. Left as-is with an updated comment naming this precisely, rather than the earlier vaguer "needs new infrastructure" framing.

**Files changed:**
- `Tests/JavaScript/textrack-alt-delete.test.cjs` — one new test (unexecuted - see status above).
- `Tests/TexTrack.Web.IntegrationTests/ProductionHardeningRegressionTests.cs` — removed the superseded `Modal_pruning...` test; updated the `Shared_header_filters...` comment to the precise reassessment.

**Test results:** C# suite unaffected except for the one removed test: **226/226 passed** (227 − 1). The new JS test itself is **unexecuted** - flagged prominently above, not claimed as verified.

**No business rules, schema, historical data, or production calculations were touched by this fix.**
