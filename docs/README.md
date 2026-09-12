# TexTrack ERP — Documentation Index

This is the canonical index of the project's root-level documentation. It exists
because the repository accumulated many point-in-time build notes and audit
handoffs; this index sorts what's still authoritative from what's historical
record, so a future contributor or auditor doesn't have to reconstruct that
distinction from git history.

Paths below are relative to the repository root.

## Active Specifications

Living references that current and future work should follow.

- [`JWO_KEYBOARD_WORKFLOW_MASTER_SPEC.md`](../JWO_KEYBOARD_WORKFLOW_MASTER_SPEC.md) — the keyboard workflow/traversal engine contract (Enter/Backspace navigation order, three-tier Escape). Authoritative for all voucher entry pages.
- [`TEXTRACK_UI_STANDARDIZATION_BLUEPRINT.md`](../TEXTRACK_UI_STANDARDIZATION_BLUEPRINT.md) — JWO is the master visual/structural reference for voucher pages; full decision list and workstreams.
- [`VOUCHER_AUDIT_HISTORY_REQUIREMENTS.md`](../VOUCHER_AUDIT_HISTORY_REQUIREMENTS.md) — requirements for the full voucher revision-trail system (implemented per A04, see Historical Archive).
- [`JWO_MANUAL_PROCESS_BOM_UNIFICATION_PLAN.md`](../JWO_MANUAL_PROCESS_BOM_UNIFICATION_PLAN.md) — plan for unifying manual-process allocation into the BOM stage/assignment model. Implemented and save-verified end-to-end; kept active as the design record for that unification (old JWOs display but can't resave — known limitation).

## Architecture

- [`PROJECT_BLUEPRINT.md`](../PROJECT_BLUEPRINT.md) — overall project architecture blueprint.
- [`MODULE_2_ARCHITECT.md`](../MODULE_2_ARCHITECT.md) — Module 2 architecture blueprint.
- [`BROADER_ERP_EXPANSION.md`](../BROADER_ERP_EXPANSION.md) — deferred broader-ERP scope (scrap, wastage, by-products, rejection/rework, customization). Not current-phase work; kept as forward-looking reference.
- [`UI_UX_DESIGN_NOTES.md`](../UI_UX_DESIGN_NOTES.md) — general UI/UX design notes.

## Security / Open Findings

Findings verified against current source as **not yet closed** — kept active
rather than archived. Each was individually re-checked against the codebase
during the 2026-09-11 documentation cleanup, not assumed closed from its own
text.

- [`A03_RECOVERY_HANDOFF_2026-09-09.md`](../A03_RECOVERY_HANDOFF_2026-09-09.md) — local backup/restore drill evidence. Its own text explicitly says **"do not mark fully closed"**: production-equivalent collation/permissions, Windows service identity, TLS/key custody, and a scheduled backup policy remain undone.

## Current Inventory/Architecture Evidence (2026-09-10/11)

Recent implementation records for still-active, still-evolving subsystems —
kept active rather than archived because the features they document continue
to receive changes (e.g. `VoucherAuditHistoryService.cs` was extended for the
Purchase family after these were written).

- [`A09_INVENTORY_PERIOD_AND_CHRONOLOGY_POLICY_2026-09-10.md`](../A09_INVENTORY_PERIOD_AND_CHRONOLOGY_POLICY_2026-09-10.md)
- [`A09_INVENTORY_PERIOD_CONTROL_IMPLEMENTATION_2026-09-10.md`](../A09_INVENTORY_PERIOD_CONTROL_IMPLEMENTATION_2026-09-10.md)
- [`INVENTORY_VALUATION_CURRENT_STATE_AUDIT_2026-09-09.md`](../INVENTORY_VALUATION_CURRENT_STATE_AUDIT_2026-09-09.md)
- [`COMPONENT_VARIANT_MOVEMENT_INTEGRITY_2026-09-10.md`](../COMPONENT_VARIANT_MOVEMENT_INTEGRITY_2026-09-10.md)
- [`NATIVE_INVENTORY_INWARD_FOUNDATION_2026-09-10.md`](../NATIVE_INVENTORY_INWARD_FOUNDATION_2026-09-10.md)
- [`STOCK_ITEM_OPENING_INVENTORY_2026-09-10.md`](../STOCK_ITEM_OPENING_INVENTORY_2026-09-10.md)
- [`NEGATIVE_STOCK_POLICY_IMPLEMENTATION_2026-09-11.md`](../NEGATIVE_STOCK_POLICY_IMPLEMENTATION_2026-09-11.md)
- [`WEBVELLA_PRE_FIFO_ARCHITECTURE_AUDIT_2026-09-11.md`](../WEBVELLA_PRE_FIFO_ARCHITECTURE_AUDIT_2026-09-11.md) — pinned source comparison and adoption/rejection gate for the FIFO workstream.
- [`FIFO_PHASE_1_DESIGN_AND_MIGRATION_CONTRACT_2026-09-11.md`](../FIFO_PHASE_1_DESIGN_AND_MIGRATION_CONTRACT_2026-09-11.md) — authoritative Phase 1 FIFO layer, allocation, migration-safety and acceptance-test contract; no live posting cutover yet.

## Current Release

- [`BUILD_3_1_SHARED_UI_LOGIN_DASHBOARD.txt`](../BUILD_3_1_SHARED_UI_LOGIN_DASHBOARD.txt) — describes the build currently shown in the app footer (v0.6 Build 3.1).
- [`CLAUDE_UI_OVERHAUL_CHANGELOG_2026-09-08.md`](../CLAUDE_UI_OVERHAUL_CHANGELOG_2026-09-08.md) — running changelog for the UI-overhaul workstream; keep updated as that work continues.

## Project Entry Points

- [`README.md`](../README.md)
- [`README_AI_PROJECT_HANDOVER.md`](../README_AI_PROJECT_HANDOVER.md)

## Historical Archive

Superseded or fully-resolved material, moved here (not deleted) so audit
history and past decisions remain reconstructable without digging through git
log. Everything below was individually checked against current source before
being filed as closed — see each subfolder for how.

- **[`archive/build-notes/`](archive/build-notes/)** — release notes and QA
  checklists for builds prior to the current v0.6 Build 3.1 (the `V0.3_*`,
  `V0.4_*`, `V0.5_*`, and `BUILD_1_*`–`BUILD_3_0` series, plus
  `README_FIRST.txt`). The features they describe are now covered by the
  automated test suite; nothing here documents current behavior.
- **[`archive/resolved-fixes/jwo-keyboard/`](archive/resolved-fixes/jwo-keyboard/)** —
  the full deliverables bundle (root-cause analysis, exact code changes, test
  script, completion report) for one resolved JWO keyboard-focus bug. Long
  since shipped.
- **[`archive/audits/`](archive/audits/)** — the September 2026 audit cycle:
  the original independent-audit findings baseline plus every fix/remediation/
  handoff document that closed findings from it (A01, A02, A04, A07, and the
  authentication/voucher-error/rate-variance/P0-hardening fix notes). Each
  fix's key claim was spot-checked against current source before archiving
  (e.g. `DatabaseVersionCompatibilityException` in `Data/DatabaseBootstrapper.cs`
  for A02, `IsFrameworkSequenceMessage` in `Services/VoucherErrorMessages.cs`
  for the Claude audit-fixes changelog, rate-limiter registration in
  `Program.cs` for the September 3 remediation). One document that referenced
  this cycle was kept active instead, because its own findings are not yet
  closed — see **Security / Open Findings** above.

  Also in this folder: `UI_KEYBOARD_FOCUS_FINDINGS_2026-09-04.md`, initially
  kept active on first pass (2026-09-11) after a shallow grep for
  `selectable-grid`/`texTrackFocusById` on the three named files came back
  empty. Re-verified properly by reading each file's actual current markup,
  not just grepping for the finding's original literal markers: the three
  pages have since been restructured, and the finding no longer applies to
  any of them —
  `PendingMaterialIssue.razor` now uses a card layout with a `.pmi-order.selected`
  outline instead of a table row, which gives the same distinct visual
  treatment `selectable-grid`/`selected-row` would;
  `JobWorkerControl.razor` was rebuilt into a paginated, non-interactive batch
  report with no row-selection concept left to highlight;
  and `JobWorkerAgingDetailPanel.razor` is a read-only expansion panel with no
  selection logic of its own — the actual selectable list lives in its parent
  `JobWorkerAging.razor`, which already has `selectable-grid`, a `.selected`
  row class, and syncs the visible selection via `texTrackRevealRow` (defined
  in `Components/App.razor`, also used by `BillOfMaterials.razor` and
  `JobWorkerControlCenter.razor` — an established, working convention distinct
  from the `texTrackFocusById` pairing the original finding checked for).
  No code change was needed; the finding was stale, not open.

## How this index was built (2026-09-11 cleanup)

The repository root had accumulated 95 loose `.md`/`.txt` files across the
project's history. This cleanup did not delete any of them (except one
untracked, gitignored scratch file, `port link.txt`) — every file was either
kept active at the root, or moved with `git mv` into `docs/archive/`, fully
recoverable via git history either way. Application code, migrations, tests,
configuration, scripts, and runtime files were not touched.
