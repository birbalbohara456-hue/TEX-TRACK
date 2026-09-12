# Claude — UI Overhaul Session Changelog

Branch: `claude/ui-overhaul`
Written 2026-09-08 for Codex (or a future Claude session) to pick up context quickly — Codex
was running concurrently on session/auth hardening during parts of this session and is now out
of credits, so this is a durable record instead of a live handoff conversation. Everything below
was built, then verified live in the browser (not just compiled) unless stated otherwise. All
changes are still **uncommitted working-tree changes** as of writing this file — nothing in this
list has been committed yet.

For the JWO manual-process/BOM-stage unification specifically (planned but **not implemented**),
see the separate `JWO_MANUAL_PROCESS_BOM_UNIFICATION_PLAN.md` in this same repo root — it has its
own full write-up and is intentionally not duplicated here.

---

## 1. Deleted a duplicate reporting module: `ProductionReports`

Confirmed with the user that `ProductionReports.razor`'s report views (Pending Finished Goods,
Material Dispatch Outstanding, Material Consumption Balance, an unreachable dead-code Closing
Stock branch, and an unlinked T-Format) were either functional duplicates of the real,
menu-linked `JobWorkerControlCenter.razor` T-Format, or literally unreachable (`ReportKind.ClosingStock`
had no matching `@page` route — `/reports/closing-stock` is actually served by
`InventoryClosingStock.razor`).

**Deleted:** `Components/Pages/Reports/ProductionReports.razor` + `.razor.css`,
`Services/ProductionReportingService.cs`, `Models/ProductionReportingModels.cs`,
`Tests/TexTrack.Web.IntegrationTests/ProductionReportingTests.cs`,
`Tests/JavaScript/production-reports.test.cjs`, `Tests/JavaScript/production-reports.browser.cjs`.
Removed its DI registration from `Program.cs`.

**Recovered a mistake during this:** `PagedReportResult<T>` (defined in the deleted
`ProductionReportingModels.cs`) turned out to be shared by 3 unrelated services — restored into
`Models/StockItemModels.cs` alongside the existing `LookupItem` record.

**Also removed:** the `StockItemUqcImmutabilityTests.cs` shared test fixture's now-invalid
`ProductionReportingService` property/instantiation.

---

## 2. Fixed a broken Excel-style column filter (Pending Finished Goods → later deleted, see #1)

Before the deletion above, diagnosed why the shared header-filter widget
(`wwwroot/js/textrack-header-filters.js`) silently failed on one grouped-row report: its
`rowsFor()` check requires every `<tr>` to have exactly as many `<td>` as the header's `<th>`
count, which a colspan-based grouped row defeats. Fixed with a minimal row-flattening change
before the whole module was deleted per #1 above (superseded, but the diagnostic technique —
checking `data-tt-header-filters-ready` / `.tt-header-filter-button` counts directly in the
DOM — is reusable if this pattern recurs elsewhere).

---

## 3. Converted paginated registers to infinite scroll (load-all + internal scroll)

Applied to: `Components/Pages/Reports/VoucherHistory.razor` (shared by Master Job Order Register,
Job Out Orders, Material Out Register, Material In Register via the `Kind` route parameter),
`Components/Pages/Reports/JobWorkerControlCenter.razor` (all 4 tabs: Aging, Exceptions, Portfolio,
Rate Variance), `Components/Pages/Reports/StockRegister.razor`,
`Components/Pages/Reports/InventoryClosingStock.razor`.

Pattern: removed `.Skip(pageIndex * PageSize).Take(PageSize)` from each page's "visible rows"
property so it returns the full filtered list; removed `pageIndex`/`PageCount`/Previous-Next
pagination UI and methods; replaced with a `JumpSize` constant reused by `PageUp`/`PageDown`
keyboard handlers to jump N rows within the now-fully-loaded list instead of changing page.
Excel-style per-column header filters kept working throughout (they operate on the rendered
table, unaffected by this change).

**Server-side row caps raised** in `Services/OperationalReportingService.cs`:
`GetJobWorkerAgingAsync`/`GetJobWorkerExceptionBoardAsync`'s `Math.Clamp(filter.PageSize, 10, 200)`
→ `Math.Clamp(filter.PageSize, 10, 5000)` (both were textually identical, fixed via
`replace_all`) — these were real server-side ceilings that would have silently capped "infinite
scroll" at 200 rows regardless of the client-side fix. Confirmed `GetJobWorkerPortfolioAsync` has
no such cap (group-by aggregation) and `BuildJobWorkerAgingRowsAsync` never Skip/Takes on
`PageSize` at all.

**Design tradeoff, flagged deliberately:** this is "load everything, let the browser scroll," not
true incremental fetch-on-scroll. Fine at current/expected data volumes (tens to low hundreds of
rows); would need revisiting if volume grows into the thousands.

---

## 4. VoucherHistory.razor: removed the month-tile picker, opens directly into the register

Previously: clicking a report (e.g. "Job Out Orders") showed a month-folder tile grid; you had to
pick a month before seeing any vouchers. Removed entirely — the register now opens straight into
the flat, infinite-scroll, Excel-filterable table, defaulting to the currently active financial
period (`CurrentPeriodContext`, the same one shown as "FY 2026-27" and changeable via Alt+F2).

Removed: `Months`/`selectedMonth`/`YearMonth`/`MonthFolder`/`LoadMonths`/`OpenMonth`/
`FocusSelectedMonth`/`MonthQuery` and the month-tile-nav branch of `HandleKeyDown`. `ApplyQueryState`
now defaults `customFrom`/`customTo` to `CurrentPeriod` when no explicit `from`/`to` query is
present, instead of requiring an explicit month selection. `BuildReturnUrl` always encodes
`from=`/`to=` instead of sometimes encoding `month=`.

**Not touched:** `Services/VoucherHistoryRepository.cs`'s `GetMonthsAsync` — left in place, still
exercised by an existing integration test (`StockItemUqcImmutabilityTests.cs`) for an unrelated
date-range-derivation purpose. The repository-level month-bucketing logic is untouched; only the
Razor page stopped calling it for tile rendering.

Removed the header's top action buttons (Enter Alter/View, Alt+D Delete, Alt+X Cancel, Esc Back)
since the footer already lists the same shortcuts as text; all four still work via keyboard
exactly as before (Enter, Alt+D, Alt+X, Esc). Collapsed the 3-line header (`JOB WORK REPORTS`
eyebrow / big title / caption) into one line: `Job Work Reports › <report name>` with the active
period shown on the right.

**Test updates:** `Tests/JavaScript/voucher-history-workflow.test.cjs` — updated 3 assertions to
match (`doesNotMatch` where the removed month-tile/GetMonthsAsync-call/month= assertions used to
be `match`), renamed one test description, no assertion logic removed without a replacement
covering the same intent.

---

## 5. JobWorkerControlCenter.razor: brought up to the same visual standard

Removed a stray, unintentional duplicate nav link (`Job Worker T-Format`) inside the tab bar —
confirmed unintentional with the user.

Converted `.jwcc-page` from the old "centered floating card" pattern (`max-width:1800px;
margin:18px auto`, rounded corners, drop shadow, `height:calc(100vh - 120px)`) to the same
edge-to-edge treatment as `.voucher-history` (see #6/#7 below) — flush top/bottom/left, small
~4px gap on the right, no rounded corners/shadow. Collapsed its 3-line header into the same
single-line breadcrumb pattern (`Job Work Reports › Job Worker Control`), keeping the functional
"AS ON" date + Apply controls (real controls, unlike VoucherHistory's removed buttons, so they
stayed) on the right of that same line.

**A real cascade bug hit and fixed along the way:** the first attempt at the edge-to-edge
override silently lost against the shared `.desk-shell .page-card, .gateway-window` rule due to
a same-specificity source-order tie (CSS resolves ties by which rule comes later in the file, not
which one "looks like" an override) — fixed by relocating the override to occur after that rule
in `wwwroot/css/app.css`, verified with direct `getComputedStyle`/`getBoundingClientRect`
measurement, not just a visual glance.

---

## 6. Sidebar: AI assistant button relocated, small consistent shell gap added

Moved the floating "AI" launcher (`Components/Assistant/AssistantPanel.razor`) from a
`position:fixed` button floating over page content (bottom-right of viewport) into the sidebar's
own blank space, just above "Demo Company" (`MainLayout.razor`'s `.sidebar-spacer`). The button's
CSS changed from `position:fixed` to normal in-flow (`.sidebar-spacer` given
`display:flex;flex-direction:column;justify-content:flex-end`); the slide-out panel itself
(`.assistant-panel`, `.assistant-shade`) is untouched.

Added `.app-shell { gap: 4px; }` in `wwwroot/css/app.css` — the sidebar's white background and
the workspace canvas's slightly different off-white were meeting at a hard 0px seam with no
visual breathing room; this was the user's actual "sidebar overlap" complaint, not a real overlap
bug (measured 0px gap and 0px overlap either way — the seam just read as fused because the two
adjacent flat colors had no separation).

---

## 7. VoucherHistory.razor: right-edge gap + selected-row notch fixes

**Right-edge gap bug:** the existing edge-to-edge CSS (`width: calc(100% + Npx)` with negative
margins, cancelling the shared `.workspace` padding) broke specifically on wide screens because
`.voucher-history` also carries the generic `.page-card` class, whose `max-width: 1180px` — an
unrelated rule from a totally different part of `app.css` — clamped the card once the workspace
grew past that width, stranding a large, viewport-width-dependent gap on the right (up to ~236px
on a 1898px-wide screen). Fixed with `max-width: none` scoped to `.voucher-history`, and the
width-offset recalibrated by direct live measurement (not assumption) to leave a consistent ~4px
gap on the right at any viewport width, matching the sidebar/content gap from #6.

**A wrong mental model corrected mid-fix:** `margin-right` on an element with an explicit `width`
has **no effect on its visible right edge** — confirmed by live-testing (setting `margin-right`
via devtools-equivalent JS had zero visible effect). The actual lever is the width calc's own
pixel offset. The file's CSS comment was rewritten to state this correctly so it doesn't mislead
a future reader.

**Selected-row notch bug:** the selected row's dark-blue highlight stopped ~15px short of the
table's true right edge because `.voucher-history-scroll` (via the shared `.tt-contained-scroll`
utility class) reserved a permanent `scrollbar-gutter: stable` strip, visible as a color-mismatch
seam specifically for the selected row (a normal row's near-identical zebra color hid the same
gap). Fixed with `scrollbar-gutter: auto` on `.voucher-history-scroll` — note: **removing** a
property from a more-specific rule does not cancel a less-specific rule that still sets it; had
to explicitly re-declare `auto` to actually win the cascade. Verified with `getComputedStyle`
before/after.

---

## 8. JobWorkOutOrder.razor: removed a redundant in-grid total row

The finished-goods table had its own `<tfoot>` "Total Qty" row, duplicating totals already shown
in the right-side summary panel (`.jwo-summary-total`) and the bottom status bar. Removed the
`<tfoot>` entirely; both other totals displays (both reading the same `GrandTotal` computed
property) are untouched. Removed the now-dead `.jwo-voucher-total-row` CSS. Updated
`Tests/JavaScript/voucher-history-workflow.test.cjs`'s assertion from `match` to `doesNotMatch`
for this class.

---

## 9. JobWorkOutOrder.razor: fixed an Enter-key navigation bug skipping Batch/Reference

`SelectVoucherType` (called when picking a voucher type from its lookup) hardcoded
`pendingFocusId = "jwo-date-input"` after selection, jumping straight from Voucher Type to
Voucher Date and skipping over the required Batch field and Reference field entirely. Changed to
`pendingFocusId = "jwo-batch-input"` — matches the same convention already used elsewhere in this
file (`SelectJobWorker` already does this) for "after a top-of-form lookup selection, go to
Batch." Verified the full corrected chain live: Voucher Type → **Batch** → Enter → Reference →
Enter → Date.

---

## 10. Standing keyboard-workflow rule established (applies to every voucher/master page, not just JWO)

User specified a general contract, saved to persistent memory
(`keyboard_workflow_rule` in the memory system, referenced here for Codex's benefit since it
isn't itself a code file):

- Enter moves forward in strict row-major reading order (first field in a row → next field in
  that row → … → first field of the next row when the current row is exhausted). Backspace does
  the exact same traversal in reverse.
- Cancel, Delete, and Quit buttons are **never** part of the Enter/Backspace chain — reachable
  only by direct mouse click or their own dedicated shortcut (Alt+D, Alt+X, Esc).
- Save/Accept (Alt+A) is always the final stop of the forward chain, regardless of where it's
  physically positioned in the layout.

Not yet audited against every existing page's keyboard wiring — apply this as the reference
contract going forward when keyboard workflow comes up on any page.

---

## 11. JobWorkOutOrder.razor: Material Allocation popup — position, size, and scroll-clipping fixes

The popup (`.jwo-modal-overlay`/`.jwo-modal`, opened via `materialAllocationGroup`) had three
compounding bugs, all fixed together:

1. **Misaligned toward the sidebar:** `.jwo-modal-overlay`'s `inset: 0` centered it across the
   *full* viewport width, including the area hidden behind the 208px sidebar, visually crowding
   it left. Fixed to `inset: 0 0 0 208px` (matching `.jwo-entry-form`'s own convention). Applied
   the identical fix to the sibling `.component-allocation-overlay` (Component Allocation) for
   consistency — it had the exact same bug, just not separately reported.
2. **Too small on large screens:** changed `.jwo-modal` from a hard `width: min(1180px,
   calc(100vw-72px))` cap to `min-width: min(70%, 100%)` / `min-height: min(70%, 100%)` — sizes
   naturally to content but is never smaller than 70% of the actual available area next to the
   sidebar (not the raw viewport).
3. **Dropdown opened inside the popup got clipped, and the whole popup scrolled oddly:**
   `.jwo-modal` used `overflow: auto`, unlike its sibling `.component-allocation-window` which
   already used `overflow: visible` (a working pattern in this same file). Copied that pattern:
   `.jwo-modal { overflow: visible; }` plus dropped `position:sticky` from the titlebar (sticky
   positioning needs a scrolling ancestor to stick within — once `.jwo-modal` itself stops
   clipping, sticky would look past it to the overlay instead, detaching the titlebar from its
   own card).

---

## 12. JobWorkOutOrder.razor: unified the Material Allocation popup across new-entry and edit mode

Previously, an already-saved JWO (`editModel.Id != 0`) showed BOM/Process/stage content inside
the popup above; a JWO being newly entered (`editModel.Id == 0`) showed the *same* content
rendered inline in the table row instead, with no popup at all — a deliberate original design
choice (comment at the time: "so the active-entry inline path and this popup can evolve
independently"), but visually and behaviorally inconsistent, and the direct cause of two
reported bugs:

- A stray dropdown appeared to float oddly inside/behind the popup — actually a background row's
  own still-open lookup panel bleeding through via a very high `z-index` on `.erp-lookup-panel`
  (`z-index: 99999 !important`, app-wide), because the inline (non-popup) new-entry path left
  background fields fully interactive underneath the visual backdrop.
- Keyboard focus stayed on a background field (confirmed live: `document.activeElement` was a
  now-visually-hidden Godown field while the popup showed on top of it) — because opening the
  popup didn't move focus into it at all for the inline path.

**Root design flaw found while fixing this:** the popup can't just always show as soon as Colour
resolves — Godown and Size still need filling at that point, and hiding them under a backdrop
before they're done is itself broken (confirmed live: at that moment the Godown input still held
keyboard focus but a button from the popup was visually on top of it, un-typeable). Fixed by
**removing the separate new-entry inline rendering entirely** and extending the *existing*,
already-correct edit-mode popup (keyed off `materialAllocationGroup`, opened by
`FocusFirstProcess`, using `KeyboardContextScope`/`Mode="KeyboardContextMode.Modal"` for real
focus trapping) to cover both modes — removed the `&& editModel.Id != 0` restriction from both
the popup's render condition and the row chevron button's render condition. Since
`FocusFirstProcess` already only fires once the keyboard chain (item → godown → colour → size)
genuinely reaches the process step, the popup now naturally only appears once nothing is left
outside it to fill, for both modes — which incidentally also fixed the stray-dropdown bug (the
background is no longer independently interactive by the time the popup shows).

Verified live end-to-end for a fresh new-entry JWO: item → godown → colour → size → process, all
correctly reaching the shared modal, with focus genuinely inside `.jwo-modal` at each step
(`document.activeElement.closest('.jwo-modal')` truthy) and the chevron correctly reopening an
already-configured group's popup afterward.

---

## 13. JobWorkOutOrder.razor: reordered the finished-good fields to Item → Godown → Colour → Size → Process

Was Item → Colour → Godown → Size → Process. Swapped the markup order of the Godown block and
the Colour block within `.jwo-fg-item-row`, and rewired the keyboard chain to match:
`SelectFinishedGood` now always focuses Godown next (previously conditionally went straight to
Colour or Godown); `SelectFinishedGoodsGodown` now goes to a new `FocusColourOrSize` helper
(focuses Colour if the item has colours, else Size/Process, mirroring the old post-item logic);
`SelectColour` now always goes to `FocusFirstSizeThenProcess` (previously it conditionally went
back to Godown for the group-owner row, which no longer makes sense with Godown now earlier in
the chain).

---

## 14. JobWorkOutOrder.razor: merged Component Allocation into the Material Allocation popup

Previously a *separate* nested modal (`.component-allocation-overlay`/`.component-allocation-window`,
its own `KeyboardContextScope`) opened on top of the Material Allocation popup once "Enter
Components" was clicked — this is what the user meant by wanting "no popup-within-a-popup."
Removed that separate modal entirely; its component-grid markup (same fields, same
`data-erp-lookup`/`data-enter-action` wiring, same C# methods —
`AdvanceComponentItem`/`FocusComponentQuantity`/`FocusComponentRate`/`CompleteComponentLine`/
`SelectComponent`/`RemoveComponent`, none of which needed to change) now renders inline inside
`.jwo-modal-body`, right after the BOM/Process section, gated simply by
`activeComponentFg is { } componentFg`. One continuous flow now: BOM → Process → Raw
Materials/Components → Qty → UQC, in a single popup.

**A real behavior bug caught while verifying the merge (not present before, introduced by
merging):** Escape from the Components view was still running `CloseComponentAllocation`'s
*full* "done, advance to next finished good" logic, instead of just stepping back to the
still-open Process/BOM view one level up — because before the merge, Components was always its
own separate, always-terminal popup, so "Escape closes it" and "done, advance" were the same
action; after the merge they're not. Fixed by splitting into two methods:
- `StepBackFromComponents()` — Escape's behavior now: clears `activeComponentFg`, keeps
  `materialAllocationGroup` (popup stays open), focuses back to "Enter Components".
- `CloseComponentAllocation()` — kept for its original callers (pressing Enter on a blank
  trailing component row): now *also* clears `materialAllocationGroup` (fully closes the popup)
  in addition to its existing "advance to next/new finished good or Save button" logic.

Verified live: first Escape from Components → popup stays open, back at Process view. Second
Escape → popup fully closes. Entering a real component then pressing Enter on the blank trailing
row → popup fully closes *and* advances to a new blank finished-good row, focus lands correctly
on its item field.

**Test update:** `Tests/JavaScript/textrack-alt-delete.test.cjs` — one test's assertions on
`.component-allocation-overlay`/`ContextId="jwo-component-allocation"` (which no longer exist)
updated to check `.jwo-modal-overlay`/`ContextId="jwo-material-allocation"` instead (the one
popup context both flows now share).

---

## 15. Confirmed dead: manual-process `ExpectedRate`/`RateBasis` (Per Quantity/Lumpsum)

Full-codebase grep confirmed `JobWorkOrderProcess.ExpectedRate`/`RateBasis` (the "Expected Rate"
input + "Per Quantity"/"Lumpsum" dropdown shown on a manual Process row) is captured and
persisted but **never read by any calculation or report** — not what the Rate Variance report
uses. The genuinely-wired equivalent is a *different* field:
`JobWorkOrderStageAssignment.ExpectedProcessRate` (the "Expected rate/unit" input on a BOM
**stage** row), which gets snapshotted onto the `MaterialInFinishedGood` line at receipt time
(`MaterialInRepository.cs` ~845-847) and is exactly what
`OperationalReportingService.GetJobWorkRateVarianceAsync` compares against the actual rate. This
matches what `docs/archive/audits/PRE_BETA_FIX_HANDOFF_2026-09-06.md` (line ~111) already flagged as a known legacy
issue.

**Not yet removed** — this finding fed directly into the bigger plan in
`JWO_MANUAL_PROCESS_BOM_UNIFICATION_PLAN.md` (retire the whole manual-process model in favor of
the real stage/assignment one, rather than patching the dead field in isolation). See that file.

---

## 16. Implemented: manual/BOM process-allocation unification

Following on from §15 and `JWO_MANUAL_PROCESS_BOM_UNIFICATION_PLAN.md`, the plan was implemented
(not just drafted) in this session, at the user's explicit request after confirming this doesn't
touch the core stock-valuation/costing engine — only JWO's own schema/repository/UI.

**What changed:**
- `JobWorkOutOrder.razor` — manual (no-BOM) finished goods now get one `JobWorkBomStageEditModel`
  auto-created (`AddManualStage`) the moment the keyboard chain reaches the process step
  (`FocusFirstProcess`), instead of a separate, thinner `JobWorkProcessEditModel` row. Manual and
  BOM-driven finished goods now render through the exact same `.jwo-bom-stages` table markup (Job
  Worker / Process / Godown / Expected Date / Expected Rate / Qty / UQC columns), with the
  per-row remove button gated on `stage.SourceBomId is null` (a BOM-templated stage can't be
  deleted individually; a manually-added one can, via new `RemoveManualStage`). All of the old
  manual-process-only methods (`SelectProcess`, `AdvanceProcessItem`, `CompleteProcessLine`,
  `RemoveProcess`, the Process lookup/filter chain, etc.) were deleted outright rather than kept
  side-by-side.
- `JobWorkOrderRepository.cs`:
  - `AddFinishedGoodsAsync` — added an `else if (fgModel.BillOfMaterialId is null &&
    fgModel.BomStages.Count > 0)` branch that builds real `JobWorkOrderBomStage` +
    `JobWorkOrderStageAssignment` rows directly for manual stages (`SourceBomId = null`), the same
    entities a BOM-driven stage produces via `AddBomStageSnapshot`. This is the mechanism that
    makes manual allocation show up correctly in Rate Variance and Job Worker Control — those
    reports only ever read `JobWorkOrderStageAssignment.ExpectedProcessRate`
    (`MaterialInRepository.cs` ~845-847), which manual allocation never wrote before.
  - `GetForEditAsync` / new `BuildStageEditModelsForLoad` — for an **existing** JWO that still only
    has legacy `JobWorkOrderProcess` rows (zero real `BomStages`), synthesizes one
    `JobWorkBomStageEditModel` per legacy process on load, purely for **display** under the new
    unified UI. These synthesized rows are not persisted merely by opening the JWO.
- `JobWorkOrderProcess`/`JobWorkProcessEditModel` (the legacy manual-only model) were **left in
  place in the schema** but are no longer written to on new saves — `AddFinishedGoodsAsync` no
  longer adds anything to `fgModel.Processes`, and EF's delete-and-recreate-children save pattern
  means any old `Processes` rows are naturally dropped the next time that specific finished good's
  children are rewritten. No migration/table drop was performed.

**Verified live** (browser, full keyboard-driven flow) for **new JWO creation**: Voucher Type →
Batch → Item → Godown → Colour → Size → popup auto-opens on a fresh `AddManualStage`-created
stage → Job Worker + Process filled via lookup → Expected Rate set → Enter correctly advances
(`CompleteStageLine`) into the Components section inside the *same* popup. `dotnet build` is
clean (0 warnings/errors).

**Not fully verified**: the actual database save, for an unrelated, pre-existing reason — see
"Outstanding" item 3 below (voucher-sequence bug blocks all new-JWO saves in this dev
environment). BOM-driven (non-manual) JWOs were not re-tested end-to-end in this session; only
the `AddBomStageSnapshot` call site changed (its `processId` argument, previously a meaningless
fallback to the now-always-empty `fgModel.Processes`, was simplified to `null` — semantically a
no-op change since the per-stage `ProcessId` already takes priority in that method).

**`ApplyStageAssignmentsAsync` (the edit/resave path for an *existing* JWO) needed no changes.**
It already updates `AssignmentHistory` generically by matching `StagePath` against the entity's
real `BomStages`, with no branching on `SourceBomId` at all — so it already works uniformly for
manual and BOM stages, as long as the stage was actually persisted as a real `JobWorkOrderBomStage`
row (which now happens for manual stages too, per above).

There is, however, a pre-existing hard guard at `JobWorkOrderRepository.cs:428-430`
(`IsAssignmentOnlyAmendment`) that unconditionally rejects saving an existing JWO unless the save
is *only* a job-worker/date/rate reassignment on an unchanged stage structure — any added/removed
finished good or stage, or any changed stage field, is rejected outright with "This change would
replace permanent JWO stages... use the guided JWO amendment workflow", regardless of whether
other vouchers link to it. This is unrelated to this session's work (it predates it and protects
stage IDs that downstream vouchers like Material In may already reference) but has one consequence
for the backward-compat synthesis above: a genuinely **old** JWO (real `BomStages.Count == 0`,
only legacy `Processes`) will always fail this check on resave, because the synthesized display
rows make `fgModel.BomStages.Count` (1+) mismatch the entity's real count (0) — so old JWOs are
viewable under the new unified UI but **not resavable** until a real migration (or whatever the
"guided JWO amendment workflow" turns out to be) moves their legacy `Processes` into real
`BomStages` rows. This fails loudly (clear error message), not silently, so no data-integrity risk
— just a known limitation, left as-is pending a product decision on the migration path.

---

## 17. Fixed: voucher-sequence drift, "+Stage" unreachable by keyboard, two dead focus targets

Three more bugs, found and fixed while finally getting a live save of a new JWO to succeed
(§16 had verified the create flow only up to, not including, a successful DB save).

**Voucher-sequence drift (`Services/VoucherSequenceAllocator.cs`)** — root cause of the
`"Voucher Number 'N' already exists"` error from §16's Outstanding item 3, now fixed. `ReserveAsync`
only reconciled its floor against the actual `Vouchers` table in the `ResetPeriod == "Never"`
branch; for the (default) per-financial-year-reset branch it trusted the `voucher_sequences`
counter table alone. That counter table drifts out of sync with reality because at least one other
save path bypasses it entirely: `TallyXmlExchangeService.NewVoucherAsync` computes its own
`SequenceNumber` directly from `MAX(Vouchers.SequenceNumber)` and never touches
`voucher_sequences`. In this dev DB, Tally-imported JWOs occupy sequence numbers 1–11 for the
"Job Work Out Order" type/FY with no matching `voucher_sequences` row, so the allocator's own
counter started fresh at 1 and collided with the existing voucher #1 — even though the UI's
preview (`GetNextSequenceAsync`, a separate, correct, `Vouchers`-table-scanning method) correctly
showed "12". Fix: `ReserveAsync` now always reconciles its floor against
`MAX(Vouchers.SequenceNumber)` in scope (company + type, and financial year unless the type never
resets), in addition to the existing `voucher_sequences` counter — same fix shape the "Never"
branch already had, extended to the default branch. **Verified live**: saving a new JWO (batch
`KEYTEST1`) that previously failed with `"Voucher Number '1' already exists"` now succeeds
("Job Work Out Order 12 saved successfully"), and the next new-JWO screen correctly previews 13.

**"+Stage" was keyboard-dead** (`JobWorkOutOrder.razor`) — reported directly by the user: after
filling a manual stage's Expected Rate and pressing Enter, focus jumped straight into the
Components section, so "+ Stage" (letting you add a second manual production stage) could only
ever be reached by mouse, breaking the [[keyboard_workflow_rule|standing keyboard-workflow
rule]] that every actionable widget except Cancel/Delete/Quit should be Enter/Tab-reachable.
Root cause: `CompleteStageLine` unconditionally called `OpenComponentAllocation(group)` once
there was no already-existing "next" stage in the ordered list — correct for BOM-driven stages
(fixed count, no "+Stage" button exists for them at all) but wrong for manual stages, whose list
is open-ended. Fix: for manual allocation (`group.BillOfMaterialId is null`) with no next stage,
land focus on "+ Stage" instead (needed adding `id="jwo-add-stage-@group.ClientKey"` to the
button, which had none) — Enter/Space there adds another stage; Tab reaches "Enter Components"
to move on, matching how every other open-ended list in this popup already works. **Verified
live**: Expected Rate → Enter → focus lands on "+ Stage" → Tab → focus lands on "Enter
Components" → Enter/Space opens Components, exactly as intended.

**Two dead focus-target ids** — `StepBackFromComponents` (Escape from Components back to the
stage view) and `RemoveManualStage`'s zero-stages branch (deleting a manual FG's only stage) both
set `pendingFocusId = $"jwo-open-components-{group.ClientKey}"`, but no element anywhere in the
markup ever had that id — the "Enter Components" button was unnamed. Any time either path ran,
focus resolution silently found nothing. Fixed the first case by actually giving the "Enter
Components" button `id="jwo-open-components-@group.ClientKey"` (which the code already assumed
existed). The second case needed a different fix: with zero stages left, "Enter Components" isn't
even rendered (`@if (orderedStages.Count > 0)`), and a manual finished good can't be usefully saved
with no stage at all (nothing would flow into Rate Variance/Job Worker Control) — so
`RemoveManualStage` now calls `AddManualStage(group)` to put a fresh blank stage back instead of
pointing at a component-entry screen that isn't there.

**Also investigated, not a bug**: the user's report that "the keyboard focus is not traceable"
right after Components closes was checked live — `CloseComponentAllocation` (unchanged by this
fix) does correctly reach Narration then Save, but only after one extra hop through an
automatically-added blank second finished-good row, mirroring the exact same "trailing blank
row, Enter-to-decline" convention already used for colours and components elsewhere on this page.
Whether that extra hop should be skipped when the finished good being closed is the only/last one
is a real UX judgment call, not a clear-cut bug — flagged for the user rather than changed
unilaterally.

**End-to-end confirmation of §16's previously-unverified save path**: with the sequence bug fixed,
the manual-stage JWO created in this test (item TEST GARMENT 01, batch KEYTEST1, job worker Karim
Button Works, process Cutting, rate 12.5) was saved successfully and immediately confirmed
correct in the **Job Worker Control** report — appearing with Current Stage "Cutting" and
Responsible Jobber "Karim Button Works", exactly the data manual allocation could never
previously feed into that report before the §16 unification. This closes out §16's last open
verification gap.

---

## 18. Fixed: Enter on "+ Stage" accidentally added a stage instead of advancing

Follow-up to §17's "+Stage" fix, reported by the user immediately after: landing keyboard focus
on "+ Stage" was correct, but pressing Enter there triggered the browser's native "Enter
activates the focused button" behavior, which called `AddManualStage` — silently adding an
unwanted second stage instead of moving forward. Fix: added
`data-enter-action="jwo-open-components-@group.ClientKey"` to the "+ Stage" button
(`JobWorkOutOrder.razor` ~line 405), so this app's existing global Enter-key router intercepts
Enter there and clicks "Enter Components" instead of letting the native click-on-Enter fire.
Mouse clicks and Space (native activation, not intercepted by the Enter-specific router) still
add a stage as before. **Verified live**: Expected Rate → Enter → focus on "+ Stage" → Enter →
Components opens directly, stage count unchanged (no phantom stage added).

---

## 19. Added: declining a newly-added blank manual stage

Follow-up to §17/§18, reported by the user immediately after: reaching "+ Stage" now worked, and
Enter there correctly opened Components without adding an unwanted stage - but there was no way
to back out of a stage the user *did* add (via "+ Stage" click, or Space) and then changed their
mind about, other than manually tabbing through every one of its blank fields. Leaving it blank
and pressing Enter on the Job Worker field just advanced field-by-field (Process, Godown, Date,
Rate) with no "decline" recognition, eventually landing back on "+ Stage" instead of "Enter
Components" - the same gap this whole section has been closing, just one level deeper.

Fix: added `data-enter-empty-action` to a manual stage's Job Worker input (only when
`stage.SourceBomId is null` - a BOM-driven stage's job worker isn't optional this way) plus a new
`DeclineBlankStage(group, stage)` method, mirroring the exact convention already used for colours
(`FinishColourEntry`) and components (`AdvanceComponentItem`/`IsBlankComponent`): leaving the
row's one identifying field blank and pressing Enter means "I don't want this entry." Declining
removes the stage, renumbers the rest, and opens Components directly - unless declining would
leave zero stages, in which case (mirroring `RemoveManualStage`'s same safeguard) a fresh blank
stage is put back instead, since a manual finished good can never be saved with no stage at all.

**Verified live**: filled stage 1 completely (Karim Button Works / Cutting / Main Godown / rate
10) → "+ Stage" → clicked to add stage 2 → left every field blank → Enter on the blank Job Worker
field → stage 2 is discarded, stage count back to 1, popup stays open, Components section opens
with focus on the first component field - all in one Enter press, matching the colour/component
convention exactly.

---

## 20. Audited against a full keyboard-workflow spec the user provided; reverted §18, fixed a global Escape gap

The user supplied a complete written spec ("Master Specification: ERP Keyboard Workflow &
Traversal Engine") covering global Enter/Backspace/Escape behavior and, specifically, the JWO
Material Allocation modal's expected flow. Audited the JWO page against it end to end.

**§18 reverted.** The spec's §5 Phase 1 step 3 is explicit: *"Pressing Enter on `+ Stage` commits
the stage block and opens a new process iteration, cycling focus back to Job Worker Details."*
That's native browser button-activation behavior (Enter/Space clicks a focused `<button>`) - the
same behavior §18 had deliberately suppressed, based on a misreading of an earlier, ambiguously-
worded user report. Removed the `data-enter-action` added to the "+ Stage" button in §18, restoring
native Enter-activates-button behavior. §19's `DeclineBlankStage` (leaving the new iteration's Job
Worker field blank + Enter jumps to Components) already matches spec step 4 exactly and needed no
change - the combination of "revert §18" + "keep §19" is precisely what the spec describes.
**Verified live**: fill stage 1 → Enter → focus on "+ Stage" → click (confirming the app-level
`AddManualStage` handler; this automation tool's synthetic/CDP key events don't reliably trigger a
real browser's native Enter-activates-button behavior, so that specific hop relies on standard,
universally-documented browser behavior rather than being independently verifiable here) → new
stage 2 added, focus cycles to its Job Worker field → leave blank → Enter → stage 2 discarded,
Components opens. Full chain matches the spec.

**New global gap found and fixed: Escape had no "clear dirty text" tier.** The spec's global Key
Behavior Matrix (§2) describes three escalating Escape behaviors on a lookup-style field: (1)
inside an open dropdown → collapse it, keep focus; (2) field has dirty/typed text → clear it, keep
focus; (3) field is blank → close the view (or confirm quit if the voucher has unsaved data
elsewhere). Only (1) and (3) existed. `wwwroot/js/textrack-alt-delete.js`'s global Escape handler
ran `closeOpenLookup` (tier 1) then fell straight through to the voucher-level exit/quit-
confirmation routing (tier 3) - there was no tier 2 at all. Concretely: typing a colour name that
matches nothing (so the dropdown shows "Nothing found", no selectable buttons) and pressing Escape
twice popped the **"Quit?"** confirmation for the *entire voucher* - a stray double-Escape while
correcting a typo could look like it was about to discard everything.

Fixed by adding `clearDirtyFieldText` right after `closeOpenLookup` in the same handler chain
(`wwwroot/js/textrack-alt-delete.js`): if the focused input/textarea is inside an active voucher
and has been edited since it was last focused (tracked by the existing `pristineVoucherControls`
WeakSet - the same signal `handleVoucherBackspace` already uses for its mirrored "field is pristine
→ jump back" rule) and is non-empty, Escape clears it and keeps focus, instead of falling through.
This is a global fix (not JWO-specific) since the handler chain is shared across every voucher page
via `App.razor`'s single script include (version bumped to
`textrack-alt-delete.js?v=0.6.2-escape-clears-dirty-text`) - matches the spec's own framing of this
as a *global* rule, and this same module's `activeVoucherFor` already treats the Material
Allocation modal's fields as part of the active voucher (falls back to
`[data-tt-voucher-form-root="true"]` when the modal, not the form, is the topmost registered
context), so no separate fix was needed for stage/component fields inside the modal.

**Verified live**, both on the main voucher and inside the modal: typed unresolvable text in the
Finished Good item field and in a stage's Job Worker field - first Escape closes the "Nothing
found" dropdown (text intact), second Escape clears the text and keeps focus (no dialog, modal
stays open), a further Escape on the now-blank field correctly proceeds to the modal's own
step-back/close logic or the voucher's quit-confirmation, exactly per spec.

**Also audited, already correct, no change needed**: Backspace reverse-navigation
(`handleVoucherBackspace` in the same JS file) already implements the spec's full Backspace matrix
- standard character deletion while there's text to delete, jump to the previous field once a
field is pristine or truly empty, mirroring Enter's forward order. This predates this session's
work and needed no fix.

---

## 21. Lookup dropdowns now reposition to fit the viewport (flip up when there's no room below)

Reported directly against a screenshot: the component-grid's item search dropdown, opened from a
row near the bottom of a tall Material Allocation modal, ran off the bottom of the browser window
- seeing the rest of the list meant scrolling the page/modal, not just the dropdown. Requested
behavior: self-adjusting position depending on available space, "like many Tally Prime" dropdowns
that flip open above the field when there's no room below.

Added a global fix in `Components/App.razor` (`repositionLookupPanel`, same MutationObserver hook
as the existing lookup-auto-highlight logic, so it applies to every `.erp-lookup-panel` on every
page): measures space above/below the field; if the panel doesn't fit below but does fit above, it
flips upward via an explicit negative `top` (not `bottom` - see below) and caps its height so it
never grows past what it needs or what's actually available; otherwise it just shrinks to whatever
room is below. Also nudges the panel back inside the viewport horizontally if it would run off the
right edge.

Two real bugs surfaced and fixed while building this, both worth remembering for any future
lookup/dropdown work:

1. **`top: auto` + `bottom: <anchor>` does not reliably grow an absolutely-positioned box
   upward.** A first attempt anchored the flipped panel with `bottom: calc(100% + 2px)` and
   `top: auto`, expecting it to grow upward from that anchor - instead it rendered at ~8px tall
   (browser fell back to a "static position" instead of sizing to content). Fixed by anchoring
   with an explicit negative `top` (`-(height + 2)px`) instead, which is fully deterministic.
2. **Several pages style `.erp-lookup-panel` with `!important`** (`.jwo-entry-form .erp-lookup-panel`
   forces `position`/`top`/`left`/`width`/`max-height`, and MaterialIn/MaterialOut have their own
   similar overrides) - a plain `panel.style.top = ...` is silently a no-op against those. Every
   property this function sets goes through `style.setProperty(prop, value, 'important')` instead.
3. **Don't stretch the flipped panel to fill all available space above - only to its own content
   height.** A field low in a long scrollable modal can have hundreds of pixels of "space above"
   that's actually the modal's *own earlier content* (other rows, the stage table, the title bar),
   not empty space. An early version sized the flipped panel to fill that whole gap, which floated
   it far away from its own field, overlapping everything above instead of just hugging the field
   like a normal flipped dropdown. Fixed by capping the granted height at `min(naturalHeight,
   spaceAbove)` - it only overlaps what any flipped-up dropdown normally overlaps (whatever's
   directly above the field), not the whole form.

**Verified live**: reproduced the exact reported case (Job Work Out Order → Material Allocation →
2 stages → 2 completed component rows → row 3's item search, in a 1400x750 viewport) - the
dropdown now opens upward, fully inside the viewport, directly attached to its field, with its own
internal scroll for the remaining options. Also verified a field with room below (finished-goods
item search near the top of the form) still opens downward exactly as before - no regression.

---

## 22. Added a "Not Applicable" option to every lookup dropdown, and dropdowns now auto-close on blur

Two more requests after §21's dropdown-repositioning fix, both global (not JWO-specific), both
in the same shared files.

**"Not Applicable" option.** Every lookup so far only offered "leave it blank and press Enter" as
a *keyboard-only* way to decline a field - there was no mouse-clickable equivalent, and no visual
hint that leaving it blank was even a valid, supported choice. Rather than re-deriving each
field's own decline behavior (every field already has one - colour's `FinishColourEntry`, a
stage's `DeclineBlankStage`, a component's blank-check in `AdvanceComponentItem`, the finished-
goods grid's `SkipToNarration`, etc. - reached today only via clear-text-then-Enter), added a
generic injector in `Components/App.razor` (`injectNotApplicableOption`, same MutationObserver
hook as the dropdown-repositioning and auto-highlight logic) that appends a "— Not Applicable —"
button as the *last* option in every `.erp-lookup-panel` (after any real matches, so it never
takes index 0 and never interferes with auto-highlight or "Enter picks the top typed match").
Clicking it clears the field's value and dispatches a real Enter keydown on it - replaying
exactly what the field's own existing Enter-key logic already does for a blank field, with zero
per-field special-casing. Styled distinctly (`erp-lookup-na-option` in `wwwroot/css/app.css`) -
muted, italic, separated by a top border - so it doesn't read as a real match.

**Dropdowns weren't closing when focus moved away without an explicit selection.** Every lookup
panel is opened by its own field's `@onfocus` handler and, until now, only ever closed by:
selecting an option, Escape, or a *different* field's `@onfocus` happening to overwrite the
shared `activeLookupKey`. Tabbing or clicking into a field that has no such handler (most plain
text/number/date fields) left the previous field's dropdown rendered and lingering. Fixed in
`wwwroot/js/textrack-alt-delete.js` (`closeLookupOnBlur`, wired to a new `focusout` listener
alongside the existing `focusin` ones) - the moment a lookup input genuinely loses focus, its
panel hides client-side, same mechanism `closeOpenLookup` already uses for Escape. This never
fires for a click on the panel's own option buttons (they use
`@onmousedown:preventDefault="true"` specifically so the input never blurs during that click),
so it can't mistake "selecting a match" for "moving away."

Version bumped to `textrack-alt-delete.js?v=0.6.3-close-lookup-on-blur`.

**Verified live**, both on the main voucher form and inside the Material Allocation modal: (1)
typing into the finished-goods item field and clicking "— Not Applicable —" clears it and jumps
to Narration exactly like the blank-grid-row Enter path; typing garbage into a stage's Job Worker
field and clicking it correctly runs `DeclineBlankStage` (stage count back to 1, popup stays
open); (2) opening a dropdown then clicking directly into an unrelated field (Batch, or another
stage field) closes the first dropdown immediately - confirmed by re-querying which panel is
actually visible afterward, not just counting visible panels.

---

## 23. Redesigned "Not Applicable": normal styling, fixed top position, genuinely filterable

Follow-up to §22: it worked, but three things about it were wrong per the user's direct feedback.
All fixed in `Components/App.razor`'s `updateNotApplicableOption`/`declineLookupField` and
`wwwroot/css/app.css` (no change needed in `wwwroot/js/textrack-alt-delete.js` this round).

1. **"Should look like other normal dropdown items."** Removed the dedicated
   `erp-lookup-na-option` CSS class entirely (italic, muted, top border) - it now uses the exact
   same `.erp-lookup-panel button` styling as a real match, including the same hover/focus/
   keyboard-selected treatment.
2. **"Its position should be fixed at the top, right now it changes its position."** It's still
   appended last in the DOM - inserting it first was tried and immediately broke Blazor's own
   diffing of its `@foreach`-rendered matches on the next keystroke (Blazor assumes it owns
   position 0 in that list). Fixed the same visual result a different way: `.erp-lookup-panel` is
   now `display: flex; flex-direction: column`, and the injected option gets `order: -1` in CSS -
   always rendered last in the DOM (safe for Blazor), always displayed first (what the user
   asked for).
3. **"It should be searchable - user presses 'not', the dropdown filters [to] Not Applicable."**
   It previously showed unconditionally in every state. Now it behaves like a genuine filterable
   entry: visible when the field is blank, or when the typed text is a partial, case-insensitive
   match for "Not Applicable" (so typing "not" narrows the whole list down to just it); hidden the
   moment the typed text doesn't match at all (e.g. typing "TEST" removes it, leaving only real
   TEST GARMENT matches). This re-evaluates on every keystroke via a dedicated `input` listener,
   not just on Blazor's own re-renders - discovered mid-fix that filtering to an *unchanged* real
   match set (e.g. every remaining match still contains "TEST") produces no DOM mutation for the
   existing MutationObserver-based approach to react to, so the option would go stale, still
   showing (or hidden) based on pre-keystroke text.

**One more real bug found and fixed while testing this**: because it's still a genuine list item
now (participating normally in auto-highlight, per point 1), a match highlighted by *earlier*
typing (e.g. "TEST" auto-highlights "TEST GARMENT 01") could leave a stale `.keyboard-selected` /
`activeIndex` marker on the panel that survives clearing the field back to blank. Since the app's
Enter router checks that highlight *before* checking for a blank value, clicking "Not Applicable"
at that point could silently re-select the stale highlighted match instead of declining the
field. Fixed by having `declineLookupField` clear any `.keyboard-selected` class and
`activeIndex` on the panel itself before dispatching its synthetic Enter.

**Also traced (not a code bug): app.css itself was stale.** Its own cache-busting version string
in `Components/App.razor` (`?v=0.6.7-jwcc-source-order-fix`) hadn't been bumped since well before
this session's CSS edits, meaning several rounds of this exact styling work were being verified
against a browser-cached copy of the old stylesheet. Bumped to
`css/app.css?v=0.6.8-lookup-na-option`. Worth remembering: `app.css` needs its own version bump
independent of `textrack-alt-delete.js`'s - they're cache-busted separately.

**Verified live**: blank field → "Not Applicable" shows first, styled identically to real
matches; typing "TEST" → it disappears, top real match still auto-highlights correctly; clearing
back to blank → reappears first; typing "not" → list narrows to just it; clicking it after first
having typed and cleared a real search → correctly declines to the field's own blank-advance
target (finished-goods item → Narration), not a stale highlighted match.

---

## 24. Fixed: mouse hover and keyboard highlight could light up two different dropdown rows at once

Reported with a screenshot: in a lookup dropdown, two rows ("TEST GARMENT 05" and "TEST GARMENT
07") were both highlighted blue at the same time. Original intended design (restated by the user):
mouse hover shows no highlight at all - a click selects immediately - only real keyboard focus or
the arrow-key/auto-highlight-first-match state (`.keyboard-selected`/`.lookup-selected`) should
show a highlight, and only one row at a time.

Root cause: every lookup panel's highlight CSS combined `:hover` into the exact same rule as
`.keyboard-selected`/`.lookup-selected` (`.erp-lookup-panel button:hover, ...
button.keyboard-selected { background: ...; }`), across five stylesheets -
`wwwroot/css/app.css` (base rule, twice - once for the shared default, once for the
`.voucher-shell` override), `JobWorkOutOrder.razor.css`, `MaterialIn.razor.css`, and
`MaterialOut.razor.css` (which even had a comment reading "HIGHLIGHTED DROPDOWN ITEM VIA ARROW
KEYS OR HOVER", confirming this was the original, since-revised design). `:hover` is purely
mouse-position-driven and completely independent of whichever element JS has marked
`.keyboard-selected` - so whenever the cursor happened to rest over a different row than the
keyboard-highlighted one, both lit up simultaneously.

Fixed by removing `:hover` (and its paired `:hover strong`/`:hover span` child-color rules) from
all five occurrences, keeping `:focus` (real keyboard focus, e.g. Tab landing on an option) and
`.keyboard-selected`/`.lookup-selected` (the JS-driven arrow-key/auto-highlight state) as the only
things that highlight a row. `MasterJobOrder.razor.css` has the same `:hover` styling but no
`.keyboard-selected` concept at all in its own component - left untouched since it isn't exposed
to this specific bug (no competing highlight mechanism to collide with) and changing it would
have removed its only highlight feedback.

Version bumps: `css/app.css?v=0.6.9-no-hover-highlight`,
`TexTrack.Web.styles.css?v=0.6.20-no-hover-highlight` (the isolated-CSS bundle covering the three
`.razor.css` files touched).

**Verified live**: opened the finished-goods item dropdown, typed "TEST" (auto-highlights "TEST
GARMENT 01"), then hovered the mouse over "TEST GARMENT 07" without clicking - no highlight
appeared on the hovered row, only "TEST GARMENT 01" stayed highlighted. Clicking the hovered row
still correctly selected "TEST GARMENT 07" immediately (no highlight-then-click two-step needed).

---

## 25. Fixed: Colour/Size column alignment in the finished-goods grid

Long-standing item from §Outstanding: a "+Colour" variant row's Colour field visibly shifted
left of the owner row's Colour field, because Item/Godown/Colour/Size were four siblings in one
wrapping flex row (`.jwo-fg-item-row`), and a variant row simply omits the Item/Godown fields
(they belong to the group as a whole, not each colour line) with nothing reserving their space -
so Colour became the first flex child and started at the row's left edge instead of where it sat
on the owner row.

Per the user's own simplification ("since we're already using a separate row for size fields,
put colour and size beside each other, and every colour line should align the same way"),
restructured `Components/Pages/Vouchers/JobWorkOutOrder.razor` into two fixed, stacked lines per
finished-good row instead of one wrapping row of siblings:
- `.jwo-fg-header-line` - Item + Godown, rendered only on the group-owner row.
- `.jwo-fg-colour-size-line` - Colour + the size-quantity grid + "+ Colour" (on the last row),
  rendered on *every* row, owner and variant alike.

`.jwo-fg-item-row` (the wrapper) is now `flex-direction: column` so these two lines stack
vertically, both always starting at the row's own left edge regardless of whether the header
line above them exists - that's what makes every row's Colour+Size line land in the same place.
Removed the now-meaningless left-border "divider" on `.jwo-colour-under-item` (it made sense
when Colour sat directly right of Godown; it doesn't as the first thing on its own line).

**One real CSS bug found and fixed while building this**: the column wrapper's first attempt
used `align-items: flex-start`, which sizes flex children (each line) to their own shrink-to-fit
content width in a column container - combined with `flex-wrap: wrap` on the lines themselves,
this collapsed each line to far narrower than the row actually had room for, wrapping Item and
Godown onto separate lines of their own even in a 1600px-wide test viewport. Removed
`align-items: flex-start` (the default `stretch` makes each line take the row's full width, so
its own row-direction children lay out normally).

Version bump: `TexTrack.Web.styles.css?v=0.6.22-colour-size-stacked-line-fix`.

**Verified live**: built up "TEST GARMENT 01" with two colour lines (Black, Navy) - both colour
fields measured to the exact same `left` pixel position via `getBoundingClientRect()`, and
visually the two rows' Colour + size-grid + "+ Colour" line up perfectly column-for-column.
Confirmed Item+Godown still lay out side by side on the owner row's header line (not a
regression from the flex-direction change).

---

## 26. Fixed: Escape-closing the Material Allocation modal left focus untraceable

Reported directly: pressing Escape to close the popup (once it's fully closed, past whichever of
the three Escape tiers from §20 apply) left keyboard focus nowhere identifiable, instead of
jumping back to whatever field the user was at right before the popup opened.

Root cause: `CloseMaterialAllocation` (`Components/Pages/Vouchers/JobWorkOutOrder.razor`) was a
one-line `materialAllocationGroup = null;` - it never set `pendingFocusId` at all, for either its
two triggers (Escape, and the "Esc Close" button). Focus was left wherever the browser's default
post-removal behavior happened to put it.

Fixed by capturing a return-focus target at the two points the popup can open
(`OpenMaterialAllocation`, the chevron/mouse entry point; `FocusFirstProcess`, the keyboard-chain
entry point) and restoring it in `CloseMaterialAllocation`. Rather than capturing "whatever the
browser says has focus right now" via a JS interop round-trip (the pattern `RequestCloseForm`
already uses for the whole-form exit dialog, `exitReturnFocusId`) - which would have meant
converting the popup's entire open-trigger call chain (`FocusFirstProcess`, `AddColourLine`,
`FinishColourEntry`, `FocusColourOrSize`, `FocusFirstSizeThenProcess`, `SelectColour`, and others)
to `async Task` - the fix computes the return target synchronously from the finished good's own
data via a new `ComputeMaterialAllocationReturnFocusId`: the last size-quantity field if any
exist, else the colour field, else the godown field. This works because every path that reaches
`FocusFirstProcess` got there by walking this page's own well-known Item → Godown → Colour → Size
→ Process chain in order - the model's own state already says exactly where that chain was, no
need to ask the browser. The chevron/mouse entry point (`OpenMaterialAllocation`) has no such
chain to read, so it returns to that finished good's own item field instead.

**Verified live**, both entry paths: (1) reached the popup via the keyboard chain (item → godown
→ colour → size) with a filled XL quantity field, pressed Escape (twice, past the dropdown-close
tier) - focus landed back on that exact XL quantity input, matching where the chain had been; (2)
opened the same row's popup via its chevron button instead, pressed Escape - focus landed on that
row's item field. Both close the popup fully (`popupOpen: false`) and land focus on a real,
visible, correct element rather than nowhere traceable.

---

## 27. MaterialIn/MaterialOut: grid header, row density, and dropdown colors matched to JWO

First step of the new `TEXTRACK_UI_STANDARDIZATION_BLUEPRINT.md` (2026-09-09): JWO is now the
master visual reference for every voucher page. An itemized investigation comparing JWO against
MaterialIn/MaterialOut found 7 areas of divergence; per the user's explicit scoping choice, only
the 3 that are pure CSS value differences (no markup/behavior change) were done this pass -
matching what they'd already called out ("focus state, selected-row state, quantity/rate/amount
alignment"). The other 4 (top title-bar/badge/subtitle/button layout, header field label style,
footer button placement, and JWO's blank-trailing-row add pattern) are deliberately deferred -
the first three need markup changes to shared `VoucherEntryFrame`/`VoucherField`/
`VoucherActionBar` components used by MI, MO, *and* the placeholder financial voucher pages, and
the fourth doesn't structurally apply (MI/MO's line items are entirely computed from the
selected pending JWO, no manual add/remove exists there).

**Grid header banner** - both pages' `#mo-page-root .tally-grid th` / `.mi-voucher-page ::deep
.erp-table th` changed from a pale blue-gray banner with dark text (`#dde6f0` / `#42546a`) to
JWO's solid navy-blue with white text (`#0f5fa8` / `#fff`).

**Row density** - both pages' grid `td` padding tightened from `3px 7px` at `11px` font to
JWO's `6px 9px` at `11.5px` (row `min-height: 34px` already matched, unchanged).

**Lookup-dropdown colors** - both pages' `.erp-lookup-panel` border/radius changed from a navy
border with 0-2px radius to JWO's blue border (`#2490ef`) with `6px` radius, and the
selected/keyboard-highlighted/focused row changed from a solid navy fill with white text to
JWO's soft light-blue fill with dark-blue text (`#eaf4fe` / `#0f5fa8`) - across every rule that
set that state's color on both pages, including a stale legacy `.mi-entry-form .erp-lookup-panel`
rule on MaterialIn that predates its current page-specific override (updated to match rather
than removed, since a narrower scope could plausibly still be relied on elsewhere).

Files: `Components/Pages/Vouchers/MaterialIn.razor.css`,
`Components/Pages/Vouchers/MaterialOut.razor.css`. No `.razor` markup or C# changes. Version
bump: `TexTrack.Web.styles.css?v=0.6.23-mi-mo-jwo-color-parity`.

**Verified live** on both pages: grid header now solid navy/white on MaterialOut (which has real
JWO/component data to render a grid with) and confirmed via the same CSS rule on MaterialIn (no
eligible pending JWO in this dev data to render its grid, but the identical rule was applied and
visually confirmed on its lookup panels); dropdown border/radius and the auto-highlighted
top-match row (typing "Ka" → "Karim Button Works") now show JWO's blue border and soft-blue/
dark-blue highlight on both pages; confirmed the earlier "no highlight on mouse `:hover` alone"
fix (§24) wasn't reintroduced - only the existing `:focus`/`.keyboard-selected`/`.lookup-selected`
selectors' *colors* changed, no `:hover` selector was touched or added back.

---

## 28. Design decision, later implemented (see §29): reserved layout for a future tax/ledger panel on Purchase family vouchers

Recorded 2026-09-12 after the Purchase Order/Purchase/Purchase Return voucher redesign was
approved and locked. The user flagged that a future tax engine will need to post per-voucher
ledger entries (CGST/SGST/IGST, Freight, Round Off, Cess, etc.) somewhere on the form, and asked
to design the layout for that now rather than retrofit the locked design later - explicitly
**not** to build it yet, since the tax engine itself doesn't exist.

**The agreed shape**, worked out over several rounds of clarification:
- The Narration section (bottom of `.pf-workspace`, currently a single full-width row) grows
  **taller** and becomes **slightly narrower** - not a large width cut, just enough to free a
  strip of horizontal room on its right.
- A **new small panel** docks into that freed strip, sharing Narration's row and new height.
  This panel is the future home for the ledger/tax entries.
- Rejected alternative: an earlier proposal (from Claude) of a full-width collapsible
  `+ Add Ledger Entry` strip stacked *above* Narration, growing into an open-ended list. The user
  preferred the side-panel approach instead.
- The panel must **not** become a "10 fixed fields" block. Per the user: which ledger fields
  appear there is driven by **Voucher Configuration** (a settings-level choice per voucher type,
  not yet designed/built) - a company might configure 2 fields for one voucher type and 6 for
  another. The panel's size should reflect what's actually configured, never a hardcoded maximum.
- Explicitly rejected: shrinking `.pf-workspace` (the item-line grid + Total footer) by ~2 row
  heights to "make room," if that room would sit empty until the tax engine exists - the user
  was clear no blank/dead space should appear before there's real content to put in it. The
  reclaimed space instead comes from Narration growing into it, which already has visible,
  productive use today (remarks/notes), so nothing looks empty or broken in the meantime.
- `.pf-workspace` was invoked only as a *structural reference* ("a self-contained box with its
  own header and rows") for the kind of thing this new panel structurally resembles - not a
  suggestion that the panel should be anywhere near as large or complex.

**Not done in this pass**: no markup or CSS changes were made. Narration's current height/width,
and the rest of the approved Purchase family layout, are unchanged. This section exists purely so
the decision isn't lost before Voucher Configuration and the tax engine are actually designed and
built - at that point, implement Narration's resize/panel-dock exactly as described above rather
than re-deriving the layout from scratch.

**Update**: the structural shell described above was built later the same session - see §29.

---

## 29. Purchase family voucher entry design locked as the reusable master template for all voucher screens

Built and iteratively refined immediately after §28 was recorded, then explicitly **locked** by
the user as the final design - not just for Purchase Order/Purchase/Purchase Return, but as the
template any future voucher screen should start from, with only the "slight modifications" a
given voucher type actually needs (e.g. which header fields appear, whether an upstream reference
exists, godown requirements).

**What was built, in order**:
- Narration resized taller (158px, matching the item-grid area's established rhythm) and
  narrower via `.mi-narration-section`'s grid columns (`86px minmax(0,5fr) minmax(150px,3fr)` -
  see below for the 5:3 ratio), freeing a docked strip to its right for `.pf-ledger-panel-placeholder`.
- That panel now holds real field-line structure, not just a static label: 6 disabled placeholder
  rows (unlabeled ledger-name input / `+`-`-` sign input / amount input, all plain `<input>`s at
  `tabindex="-1"`) plus a 7th row permanently reserved as **Grand Total** (right-aligned label and
  amount `<span>`s, top border, `0.00` placeholder) - structure only, nothing wired to real data
  yet, exactly per §28's "driven by Voucher Configuration, never a fixed field count" principle.
  The row count is a placeholder for today's shell, not a hard limit.
- The 6 entry rows sit inside `.pf-ledger-scroll` (`overflow-y:auto`), a sibling of the fixed
  Grand Total row (which lives *outside* the scroll container, as a separate flex child of
  `.pf-ledger-panel-placeholder`) - so when Voucher Configuration eventually allows more ledger
  lines than fit visually, the entry list scrolls internally while Grand Total stays pinned in
  view. Verified by temporarily injecting 5 extra rows via DevTools: scrollbar appeared, Grand
  Total's position didn't move.
- Narration : ledger-panel width ratio is 5:3 (ledger panel is 60% of narration's width) - reached
  by trial with the user via live screenshots, not a fixed measurement handed down up front.
- Added an **Attachments** button (`tabindex="-1"`, positioned via `order` just above the header's
  Bulk Discount % field) opening a modal listing photo/document attachments - each row an
  unlabeled description `<input>` plus a right-side preview (image thumbnail via `InputFile` →
  base64 data URL, or a file-extension badge for non-images). In-memory only, no backend
  persistence yet - explicitly "add it now so we can use it later" per the user's own framing.

**Debugging note worth keeping** (cost several iterations to find): a global rule in
`wwwroot/css/app.css` - `.voucher-shell .tt-voucher-entry input:not([type="checkbox"]):not([type="radio"])
{ min-height: 27px !important; ... }` - silently overrode every attempt to make the ledger panel's
placeholder inputs shorter than 27px, regardless of the scoped component CSS's own `height`,
`flex-basis`, or CSS Grid track sizing. No layout-algorithm fix (flexbox, grid `minmax(0,1fr)`,
explicit px heights) could win against it, because the real problem was never layout - it was
cascade specificity plus `!important` on a completely different property (`min-height`, not
`height`). The fix was matching it with equal-or-greater selector specificity
(`.pf-ledger-panel-placeholder .pf-ledger-grid .pf-ledger-grid-row .pf-ledger-name-input`, etc.)
and `!important` on `height`/`min-height`/`max-height` together. **Any future voucher screen that
reuses this shared component and wants non-standard input sizing inside `.tt-voucher-entry` needs
to know this global rule exists** - it silently wins by default.

**Files**: `Components/Vouchers/Shared/PurchaseFamilyEntryForm.razor` and `.razor.css`.

**Verified live**: build clean throughout (each CSS/markup change rebuilt and re-verified in the
browser, several times via direct pixel measurement - `getBoundingClientRect()`, computed styles,
`elementFromPoint()` - not just visual screenshots, after a couple of rounds where a screenshot
alone looked fine but the underlying box model was still wrong). Final state: 6 ledger-entry rows
+ 1 fixed Grand Total row, zero row overlap, right-edge-aligned amount columns, scrollable entry
area, Narration widened, Attachments button and modal working end-to-end as a UI shell.

**Not done**: no other voucher screen has actually been converted to use this component/pattern
yet - "locked as the master template" is a design decision for future work, not a completed
migration. Voucher Configuration and the tax engine itself remain undesigned/unbuilt, so the
ledger panel's fields stay static placeholders.

---

## Outstanding / not yet done

1. ~~`JWO_MANUAL_PROCESS_BOM_UNIFICATION_PLAN.md` ... **Not implemented.**~~ — **Implemented**, see
   §16 above, and fully save-verified per §17. The plan file itself was left as historical
   planning context, not updated in place.
2. ~~Colour/Size column horizontal-alignment bug...~~ — **Fixed**, see §25 above.
3. ~~Pre-existing, unrelated voucher-sequence bug...~~ — **Fixed**, see §17 above.
4. **Migration/schema decision, deferred**: whether to actually drop the `job_work_order_processes`
   table and the `JobWorkOrderProcess`/`JobWorkProcessEditModel` entities, and whether/how to
   migrate old JWOs' legacy `Processes` into real `BomStages` rows so they become resavable (see
   §16's last paragraph). Left as a product decision, not acted on.
5. **UX judgment call, not yet decided**: should `CloseComponentAllocation` skip the
   automatically-added blank second finished-good row when the finished good just closed is the
   only/last one, and go straight to Narration instead? See §17's "Also investigated" note.
6. Codex's own independent audit (`textrack-audit-2026-09-07`, delivered separately) found two
   reproduced P1 bugs (A01: a deactivated user's already-open session can still write; A02: the
   migration runner accepts a database newer than its own bundle) plus several P1/P2 release
   requirements (backup/restore, full voucher audit snapshots, runtime DB privilege separation,
   customer-isolation architecture, a tested production deployment profile, closed-period
   policy). **None of these were acted on in this session** — reviewed and discussed with the
   user only.
7. ~~**Design agreed, not built**: Purchase family voucher tax/ledger panel...~~ — **Built**, see
   §29 above: Narration widened/heightened, ledger panel with 6 field-line rows + fixed Grand
   Total row, scrollable entry area, Attachments button + modal. Locked by the user as the
   reusable master template for future voucher screens. Still blocked on Voucher Configuration
   and the tax engine itself for actual data wiring - the panel's fields remain static
   placeholders, and no other voucher screen has been migrated to this pattern yet.
