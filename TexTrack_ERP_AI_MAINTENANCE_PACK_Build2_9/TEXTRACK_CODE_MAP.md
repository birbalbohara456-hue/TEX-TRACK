# TexTrack ERP Code Map — Build 2.9

Use this map before opening source files.

---

## 1. Job Worker Control

### Presentation, local page state and local keyboard behavior

- `Components/Pages/Reports/JobWorkerControl.razor`
- `Components/Pages/Reports/JobWorkerControl.razor.css`

Responsibilities:

- T-format markup
- left/right report rendering
- filters and shortcuts
- selected row
- local keyboard handling
- report drill-down
- page loading state

### Query and calculation engine

- `Services/OperationalReportingService.cs`
  - `GetJobWorkerControlAsync(...)`

### DTOs

- `Models/OperationalReportingModels.cs`

### Menu and report switching

- `Components/Layout/MainLayout.razor`

### Relevant tests

- `Tests/JavaScript/production-reports.browser.cjs`
- `Tests/JavaScript/production-reports.test.cjs`
- `Tests/JavaScript/reporting-voucher-corrections.browser.cjs`
- `Tests/TexTrack.Web.IntegrationTests/ProductionReportingTests.cs`

### Do not inspect for ordinary T-format visual/paging defects

- `Services/MaterialInRepository.cs`
- `Services/MaterialOutRepository.cs`
- `Services/JobWorkOrderRepository.cs`
- `Data/Migrations/*`
- `Domain/Entities.cs`
- master repositories

---

## 2. Closing Stock

### Presentation, hierarchy state and local keyboard behavior

- `Components/Pages/Reports/InventoryClosingStock.razor`
- `Components/Pages/Reports/InventoryClosingStock.razor.css`

Current page size in Build 2.9: approximately 116 lines.

Responsibilities:

- group summary markup
- item/colour/size detail markup
- selected row key
- expand/collapse state
- Enter, Shift+Enter, Alt+F1, arrows and Esc
- group → item hierarchy
- navigation to Stock Register
- filter panel state

### Query and valuation engine

- `Services/OperationalReportingService.cs`
  - `GetClosingStockGroupsAsync(...)`
  - `GetClosingStockItemsAsync(...)`

### DTOs

- `Models/OperationalReportingModels.cs`
  - Closing Stock group/item/variant/filter models

### Menu return and nested Reports menu

- `Components/Layout/MainLayout.razor`

### Relevant tests

- `Tests/JavaScript/reporting-voucher-corrections.browser.cjs`
- `Tests/JavaScript/production-reports.browser.cjs`
- `Tests/TexTrack.Web.IntegrationTests/ProductionReportingTests.cs`

### Do not inspect for Esc/focus/hierarchy-only defects

- `Services/MaterialInRepository.cs`
- `Services/MaterialOutRepository.cs`
- `Services/JobWorkOrderRepository.cs`
- `Data/Migrations/*`
- stock-posting code
- voucher numbering

---

## 3. Stock Register

### Presentation and local keyboard behavior

- `Components/Pages/Reports/StockRegister.razor`
- `Components/Pages/Reports/StockRegister.razor.css`

Responsibilities:

- direct register filters
- item/Godown summary
- movement detail
- selected row key
- Enter, Shift+Enter, Alt+F1 and Esc
- origin-aware return behavior
- source voucher drill-down

### Query engine

- `Services/OperationalReportingService.cs`
  - `GetStockRegisterSummaryAsync(...)`
  - `GetStockRegisterDetailAsync(...)`
  - report lookup methods

### DTOs

- `Models/OperationalReportingModels.cs`

### Relevant tests

- `Tests/JavaScript/reporting-voucher-corrections.browser.cjs`
- `Tests/TexTrack.Web.IntegrationTests/ProductionReportingTests.cs`

---

## 4. Reports menu and report-to-report navigation

### Primary file

- `Components/Layout/MainLayout.razor`

Responsibilities:

- Alt+R
- Reports menu
- nested Inventory Reports submenu
- submenu Esc
- menu row restoration
- report route navigation

### Supporting files only when evidence requires

- `Components/Keyboard/KeyboardContextHost.razor`
- `Components/Keyboard/KeyboardContextScope.razor`
- `Components/Keyboard/KeyboardContextMode.cs`
- `Components/Keyboard/KeyboardContextType.cs`

Do not change shared keyboard infrastructure for a page-local bug unless the failure reproduces across unrelated pages.

---

## 5. Material In

### Posting/business engine — protected

- `Services/MaterialInRepository.cs`

Responsibilities:

- validation
- save transaction
- FIFO Material Out allocation
- automatic stock posting
- RM consumption
- FG receipt
- audit/linking
- persisted read projections

### Models

- `Models/MaterialInModels.cs`

### Route/create page — mixed presentation and orchestration

- `Components/Pages/Vouchers/MaterialIn.razor`
- `Components/Pages/Vouchers/MaterialIn.razor.css`

Build 2.9 status:

- posting engine is separated
- create page remains a large mixed Razor file, approximately 558 lines
- markup, keyboard handlers, loading, validation, automatic-consumption recalculation, request construction and save orchestration remain together

### Separated child presentation components

- `Components/Vouchers/MaterialIn/MaterialInVoucherList.razor`
- `Components/Vouchers/MaterialIn/MaterialInVoucherList.razor.css`
- `Components/Vouchers/MaterialIn/MaterialInPersistedView.razor`
- `Components/Vouchers/MaterialIn/MaterialInPersistedView.razor.css`

### Future visual separation target

Keep `MaterialIn.razor` as route/state orchestration and extract presentation into focused components such as:

- Material In header
- FG parent section
- variant child rows
- component rows
- cost summary
- action bar

Do not move posting logic out of `MaterialInRepository.cs`.

---

## 6. Material Out

### Page

- `Components/Pages/Vouchers/MaterialOut.razor`

### Engine

- `Services/MaterialOutRepository.cs`

### Models

- `Models/MaterialOutModels.cs`

For persisted-JWO display defects, inspect the page first. Inspect repository query methods only if the saved JWO is not returned.

---

## 7. Job Work Out Order

### Page

- `Components/Pages/Vouchers/JobWorkOutOrder.razor`

### Engine

- `Services/JobWorkOrderRepository.cs`

### Models

- `Models/JobWorkOrderModels.cs`

For focus/navigation defects, inspect only the Razor page and focused browser tests first.

---

## 8. Shared application registration

- `Program.cs`
  - service registration
- `Components/App.razor`
  - application styles/scripts
- `Components/Routes.razor`
  - route host
- `Data/TexTrackDbContext.cs`
  - EF mappings/context
- `Data/DatabaseBootstrapper.cs`
  - migration runner

Open these only when the requested change genuinely concerns registration, application bootstrap, EF mapping or migration execution.

---

## 9. Migrations

- `Data/Migrations/001_initial_foundation.sql`
- through
- `Data/Migrations/013_material_in_foundation.sql`

Rules:

- Never edit an applied migration.
- New schema change uses the next verified forward-only migration number.
- UI, keyboard and ordinary report layout fixes require no migration.
