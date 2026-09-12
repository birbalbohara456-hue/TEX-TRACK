# JWO: Unify Manual Process Allocation into the BOM Stage/Assignment model

Date: 2026-09-08. Status: plan only, nothing implemented. Written for whoever picks this up
next (Codex or Claude) to execute.

## 1. Why

Job Work Out Order's Material Allocation popup currently has two parallel, unequal ways to
describe "what work happens to this finished good":

1. **BOM production route** (`fg.BillOfMaterialId` set) — builds a real
   `JobWorkOrderBomStage` per production step, each with a versioned
   `JobWorkOrderStageAssignment` (job worker, output godown, expected completion date,
   expected rate). This assignment history is what the Rate Variance report actually reads:
   the expected rate is **snapshotted onto the Material In line** at receipt time
   (`Services/MaterialInRepository.cs` ~845-847) and compared against the actual rate in
   `OperationalReportingService.GetJobWorkRateVarianceAsync`.
2. **Manual process allocation** (no BOM selected) — creates a thin `JobWorkOrderProcess` row:
   just `ProcessId` + `ExpectedRate` + `RateBasis`. No job worker, no output godown, no
   expected date, no versioned assignment history. Confirmed via full-codebase grep: this
   `ExpectedRate`/`RateBasis` pair is **never read by any calculation or report** - it is
   dead weight that only looks like a working costing control. The project's own
   `docs/archive/audits/PRE_BETA_FIX_HANDOFF_2026-09-06.md` (line ~111) already flags this as legacy: "Legacy
   manual-process `ExpectedRate` / `RateBasis`... do not treat the legacy field as a costing
   control until it is either wired to a defined rule or removed from the UI."

The fix isn't to wire up a second, parallel rate-tracking system for manual mode. It's to
delete the parallel system entirely: manual allocation should produce the **same**
`JobWorkOrderBomStage` + `JobWorkOrderStageAssignment` records a BOM-driven stage would,
just with no BOM behind it. Evidence this was the intended design all along:
`JobWorkOrderBomStage.SourceBomId` is **already nullable** (`long? SourceBomId`) - the schema
was built to allow a stage with no source BOM. `JobWorkOrderProcess` looks like older code
that predates the richer stage/assignment system and was never migrated onto it.

One field. One screen. One report. No duplicate concept to keep in sync.

## 2. Current state (exact references)

**Entities** (`Domain/Entities.cs`):
- `JobWorkOrderBomStage` (~line 745-780): `OutputStockItemId/Variant/Uqc/Quantity`, `ProcessId`,
  `AssignedJobWorkerId`, `OutputGodownId`, `IsFinalStage`, `StageNumber`/`StageLevel`/`StagePath`,
  `SourceBomId` (nullable) / `SourceBomVersion` (nullable), plus `AssignmentHistory` (collection
  of `JobWorkOrderStageAssignment`).
- `JobWorkOrderStageAssignment` (~line 796-811): `BomStageId`, `AssignmentVersion`, `JobWorkerId`,
  `ExpectedCompletionDate`, `ExpectedProcessRate`, `Status`, `Reason`, `ValidFromUtc`/`ValidToUtc`.
  This is a proper version-per-reassignment history, not a single mutable row.
- `JobWorkOrderProcess` (~line 884-893) - **the entity to retire**: `FinishedGoodId`, `ProcessId`,
  `ExpectedRate`, `RateBasis`.

**Edit models** (`Models/JobWorkOrderModels.cs`):
- `JobWorkBomStageEditModel` (~line 65-92) - the richer model, keep and reuse.
- `JobWorkProcessEditModel` (~line 94-102) - **the model to retire**.

**Repository** (`Services/JobWorkOrderRepository.cs`):
- `BuildBomStagePreviewAsync` (~line 22+) - builds the `JobWorkBomStageEditModel` list from a
  BOM template + output quantity + optional default job worker/godown. This is the pattern a
  "build one manual stage from scratch" method should mirror, minus the BOM template lookup.
- ~line 940: where manual `JobWorkOrderProcess` rows get persisted on save - this whole branch
  goes away.
- ~line 1003-1030: where a new `JobWorkOrderBomStage` + its first `JobWorkOrderStageAssignment`
  (`AssignmentVersion = 1`) get constructed and saved - this is the exact shape a
  manually-created stage should also end up as.
- ~line 586: where an existing stage gets **re-assigned** (job worker/rate change), appending a
  new `JobWorkOrderStageAssignment` with an incremented `AssignmentVersion` rather than mutating
  the old one - manual stages need this same versioning when edited after creation.

**UI** (`Components/Pages/Vouchers/JobWorkOutOrder.razor`):
- `BomChanged` (~line 1312-1330) - triggers `BuildBomStagePreviewAsync` when a BOM is picked.
- The "Manual process allocation" markup (`.jwo-process-row` loop, inside `.jwo-process-allocation`,
  only rendered `@if (group.BillOfMaterialId is null)`) - this whole block and its keyboard
  wiring (`ProcessTextChanged`, `ProcessTextCommitted`, `ResolveProcessText`, `SelectProcess`,
  `RemoveProcess`, `FocusProcessRate`, `CompleteProcessLine`, `AdvanceProcessItem`,
  `AddColourLine`... check each for JobWorkProcessEditModel-specific logic) gets replaced.
- The "Production stages" markup (`.jwo-bom-stages` block, `@if (group.BomStages.Count > 0)`)
  and its stage-field methods (`SelectStageJobWorker`, `StageProcessTextChanged`,
  `SelectStageProcess`, `SelectStageGodown`, `StageGodownTextChanged`) already do everything a
  unified screen needs - reuse these directly for manually-created stages too.

## 3. Target state

- No more `@if (group.BillOfMaterialId is null) { manual process rows } else { BOM stages }`
  branch in the UI. Always render the same Production Stages table
  (Item / Qty / Job Worker / Process / Output Godown / Expected Date / Stage / Expected Rate).
- When no BOM is selected, the user builds this table by hand: an explicit "Add Stage" action
  creates one `JobWorkBomStageEditModel` row (mirroring the shape `BuildBomStagePreviewAsync`
  would produce for a single BOM step, but with `SourceBomId = null`, `SourceBomVersion = null`,
  `IsFinalStage = true` by default for a single-stage manual flow, output item/quantity taken
  from the finished good itself since there's no BOM decomposition to describe intermediate
  outputs).
- Saving persists these as ordinary `JobWorkOrderBomStage` + `JobWorkOrderStageAssignment`
  rows - **identical code path** to the BOM-driven save, just with a null `SourceBomId`.
- Rate Variance, Job Worker Control (aging/exceptions/portfolio), and any other report already
  built on `JobWorkOrderBomStage`/`JobWorkOrderStageAssignment` automatically start covering
  manually-allocated JWOs too, with no separate reporting work needed.
- `JobWorkOrderProcess`/`JobWorkProcessEditModel` and every method that only exists to serve
  them get deleted, not deprecated-in-place - confirm nothing else references them first
  (`grep -rn "JobWorkOrderProcess\|JobWorkProcessEditModel"`).

## 4. Migration / historical data

Per project memory, TexTrack is currently **dev/test-only with no real customer data** - this
significantly lowers migration risk right now, but confirm that's still true before assuming it.
Plan (subject to the user's call, this is a schema change and needs explicit sign-off):

- New migration: for every existing `job_work_order_processes` row, create a corresponding
  `JobWorkOrderBomStage` (`SourceBomId = null`) + one `JobWorkOrderStageAssignment`
  (`AssignmentVersion = 1`, rate/job-worker left null since the legacy field was never real
  data anyway) so no existing JWO silently loses its recorded process.
- Then drop the `job_work_order_processes` table (or rename/retain read-only if there's any
  reason to keep it queryable during a transition - ask the user).
- Do **not** attempt this against a copy of real production data without a rehearsal, per the
  general migration-safety posture already established for this project (see the Codex audit
  finding A02 about migration compatibility gates - unrelated bug, same spirit of caution).

## 5. Suggested implementation order

1. Add the "create one manual stage from scratch" repository method (no BOM template, just
   finished-good item/quantity + a blank job worker/process/godown/date/rate to fill in).
2. Wire the UI: replace the manual process-row markup with the stage-row markup, add an
   "Add Stage" button/keyboard action for manual mode (BOM mode already auto-populates via
   `BomChanged`).
3. Update `JobWorkOrderRepository`'s save path so a finished good with manually-created stages
   persists through the exact same `JobWorkOrderBomStage`/`JobWorkOrderStageAssignment` code as
   a BOM-driven one - delete the `JobWorkOrderProcess` save branch.
4. Write the data migration for existing rows, then drop `JobWorkOrderProcess` and
   `JobWorkProcessEditModel` and every method that only served them.
5. Re-run the full JWO test suite plus a manual smoke test: create a JWO with manual allocation,
   save, reopen (edit mode), confirm the stage/assignment shows correctly, then run a Material In
   against it and confirm Rate Variance picks it up exactly like a BOM-driven JWO would.

## 6. Explicitly not in this task

- The colour/size field horizontal-alignment bug in the finished-goods grid (owner row vs
  "+Colour" variant row not lining up) - unrelated CSS issue, fix separately.
- Any change to the BOM-driven path's existing behavior - it stays exactly as-is; only the
  manual path changes to match it.
