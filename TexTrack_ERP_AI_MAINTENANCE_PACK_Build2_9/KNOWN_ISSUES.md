# Known Issues — Build 2.9 Manual Test Findings

These are the currently confirmed issues. Fix them as a small targeted patch, not as another full ERP rewrite.

---

## JWC-PAGE-001 — Job Worker Control hides rows inside a JWO block

### Current behavior

The side-by-side T-format is visually correct, but not every Material Issued/Material Received row is visible. The JWO block appears to clip or limit its child content.

### Expected behavior

- Show every MO row.
- Show every MI FG parent.
- Show every colour/size child.
- Show all partial MO/MI transactions.
- Let the complete JWO block grow naturally.
- Never hide rows with fixed height or overflow clipping.

### Paging requirement

Page complete JWO/Batch blocks, not individual child rows.

- Suggested default: 10 JWO blocks per page.
- Optional page sizes: 10, 25, 50.
- Never split one JWO block across pages.
- Filters reset to page 1.
- PgUp/PgDn change pages.
- Home/End select first/last JWO block on the current page.

### Primary review files

- `Components/Pages/Reports/JobWorkerControl.razor`
- `Components/Pages/Reports/JobWorkerControl.razor.css`

### Review only if server paging is required

- `Services/OperationalReportingService.cs` — only `GetJobWorkerControlAsync(...)`
- `Models/OperationalReportingModels.cs` — only JWC paging/filter DTOs

### Relevant tests

- `Tests/JavaScript/production-reports.browser.cjs`
- `Tests/JavaScript/production-reports.test.cjs`
- `Tests/TexTrack.Web.IntegrationTests/ProductionReportingTests.cs`

### Prohibited files

- Material In posting engine
- Material Out posting engine
- JWO engine
- migrations
- master repositories

---

## INV-ESC-001 — Esc from an inventory report loses focus and does not return correctly

### Reproduction

1. Open Reports.
2. Open Inventory Reports.
3. Open Closing Stock or Stock Register.
4. Press Esc.

### Current behavior

- filter panel/page state may change,
- report remains open,
- Inventory Reports submenu is not restored,
- keyboard focus is lost,
- mouse click is required.

### Expected Esc priority

1. If filter editor/panel is open: close it and focus the report row/root.
2. Next Esc from report main view: return to Inventory Reports submenu.
3. Esc in Inventory Reports submenu: return to Reports menu.
4. Esc in Reports menu: close menu and restore origin focus.

### Primary review files

- `Components/Pages/Reports/InventoryClosingStock.razor`
- `Components/Pages/Reports/StockRegister.razor`
- `Components/Layout/MainLayout.razor`

### Supporting file only if page-local repair fails

- shared KeyboardContext components

### Relevant tests

- `Tests/JavaScript/reporting-voucher-corrections.browser.cjs`
- `Tests/JavaScript/production-reports.browser.cjs`

### Prohibited files

- `OperationalReportingService.cs` unless data itself is wrong
- repositories
- migrations
- stock posting

---

## CS-BACK-001 — Closing Stock Esc skips a hierarchy level

### Reproduction

1. Open Closing Stock group summary.
2. Enter Finished Goods or another stock group.
3. View the correct item/colour/size detail.
4. Press Esc.

### Current behavior

Esc can jump directly to Stock Group Summary and skip the immediate parent item level.

### Expected hierarchy

- Source voucher → same Stock Register movement row
- Stock Register → same colour/size or item row
- Colour/size detail → same item row in selected group
- Item summary → same stock-group row
- Stock-group summary → Inventory Reports submenu

Each Esc pops exactly one level.

### State that must be restored

- selected stable row key
- scroll position
- expansion state
- filters
- keyboard focus

### Primary review file

- `Components/Pages/Reports/InventoryClosingStock.razor`

### Possible menu support

- `Components/Layout/MainLayout.razor`

### Technical direction

Maintain an explicit page-local navigation state/stack. Do not reset the report directly to root. Use stable row keys rather than numeric list indexes.

### Prohibited files

- Closing Stock query engine
- database entities
- migrations
- stock calculation logic

---

## ARCH-MI-001 — Material In create visual layer remains only partially separated

### Current status

- Posting engine is separated in `Services/MaterialInRepository.cs`.
- List and persisted view are separate child components.
- Create page remains approximately 558 lines in `MaterialIn.razor`.
- Markup, local keyboard behavior, loading, validation, auto-consumption recalculation, request construction and save orchestration are still combined.

### Future target

Extract visual child components while preserving the working engine:

- Header
- Finished Good section
- Variant rows
- Component rows
- Cost summary
- Action bar

### Primary file

- `Components/Pages/Vouchers/MaterialIn.razor`

### Protected file

- `Services/MaterialInRepository.cs` posting/save logic

### Status

Deferred. Do not combine this architectural refactor with the immediate report Esc/paging patch unless specifically approved.

---

## MIG-CLEAN-001 — Clean-database Material In movement-kind verification remains required

### Risk

Build 2.9 contains migrations 001–013. Earlier development encountered a PostgreSQL check-constraint error when inserting Material In movement kinds.

### Required verification

On a new empty PostgreSQL database:

1. Run all packaged migrations.
2. Create JWO.
3. Create MO.
4. Create and save MI.
5. Confirm `MaterialInConsumption` and `MaterialInFinishedGoods` stock movements save.

### Status

Verification item, not a UI bug. Do not alter old migrations without reproducing the issue on a clean database.
