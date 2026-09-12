# TexTrack ERP Project Blueprint

**Authoritative folder:** `D:\MINI ERP C#\Codex\TEXTRACK`  
**Last verified:** 28 July 2026  
**Production stack:** ASP.NET Core Blazor Interactive Server, .NET 10, EF Core 10, PostgreSQL/Npgsql

This is the source-of-truth navigation map for maintaining TexTrack. Start here before adding, removing, or changing a feature. Historical release notes explain how the project evolved; current C# source, SQL migrations, and tests define how it behaves now.

## 1. Fast change-impact map

| Requested change | Primary files | Inspect with them | Keep untouched unless requirements explicitly include data/business changes |
|---|---|---|---|
| Global keyboard shortcut, Enter, Backspace, modal focus | `wwwroot/js/textrack-alt-delete.js`, `Components/Keyboard/*` | Active page `.razor`, `Tests/JavaScript/textrack-alt-delete.test.cjs`, `KeyboardContextComponentTests.cs` | Repositories, migrations, posting |
| Shared shell/menu/navigation | `Components/Layout/MainLayout.razor`, `Components/Routes.razor` | `Components/App.razor`, shared CSS | Voucher repositories |
| Shared visual theme | `wwwroot/css/app.css` | Page-scoped `.razor.css` files | Business services and database |
| JWO UI only | `JobWorkOutOrder.razor`, `JobWorkOutOrder.razor.css` | Shared keyboard JS and JWO browser tests | `JobWorkOrderRepository`, entities, migrations |
| JWO data/save/load | `JobWorkOrderRepository.cs`, `JobWorkOrderModels.cs` | JWO page, `Entities.cs`, `TexTrackDbContext.cs`, migration chain | MO/MI posting unless dependency is proven |
| Material Out UI only | `MaterialOut.razor`, `MaterialOut.razor.css` | Shared keyboard files | Posting, stock rules, migrations |
| Material Out posting/cancel | `MaterialOutRepository.cs`, `MaterialOutModels.cs` | Entities, EF mapping, migrations, integration tests | JWO/MI visuals |
| Material In UI only | `MaterialIn.razor`, `MaterialIn.razor.css`, `Components/Vouchers/MaterialIn/*` | Shared keyboard files | MI posting/FIFO logic |
| Material In receipt/consumption | `MaterialInRepository.cs`, `MaterialInModels.cs` | Entities, EF mapping, MO allocation, migrations/tests | Visual redesign |
| Ledger Groups/Ledgers | Relevant master page, `LedgerRepository.cs`, `LedgerModels.cs` | Entities/mapping and tests | Stock posting |
| Flat master CRUD | Relevant master page, `MasterRepository.cs`, `MasterModels.cs` | Dependency tests | Voucher engines |
| Job Worker master/defaults | `JobWorkers.razor`, `JobWorkerRepository.cs`, `JobWorkerMasterModels.cs` | JWO/MO lookup flows, Godown dependencies | Historical voucher rewrites |
| Godown master | `Godowns.razor`, `MasterRepository.cs` | Job Worker defaults, JWO/MO/MI dependencies | Stock history |
| Stock Item/UQC | `StockItems.razor`, `StockItemRepository.cs`, `StockItemModels.cs` | Entities/mapping, UQC tests | Stock Group rules unless requested |
| Voucher Types/numbering | `VoucherTypes.razor`, `VoucherTypeRepository.cs` | Voucher defaults, migrations/tests | Voucher posting rules |
| Operational report data | `OperationalReportingService.cs`, `OperationalReportingModels.cs` | Matching report page/test | Posting engine without proof |
| Production report data | `ProductionReportingService.cs`, `ProductionReportingModels.cs` | `ProductionReports.razor`, reporting tests | Voucher writes |
| Report layout/focus only | Matching `.razor` + `.razor.css` | Shell return-navigation | Reporting SQL/calculation |
| Database schema | New numbered SQL migration, entity, EF mapping | Bootstrapper and clean-schema tests | Editing applied migrations |

## 2. Runtime architecture

```text
Browser
  -> Components/App.razor
  -> Components/Routes.razor
  -> Components/Layout/MainLayout.razor
  -> Page/component (.razor + scoped .razor.css)
  -> Edit/list/report model in Models/
  -> Repository or reporting service in Services/
  -> Data/TexTrackDbContext.cs
  -> PostgreSQL schema created by Data/Migrations/*.sql

Browser keyboard events
  -> wwwroot/js/textrack-alt-delete.js (one capture-phase listener)
  -> Components/Keyboard/KeyboardContextScope.razor
  -> active List/Form/Modal callback only
```

Rules:

- UI-only work stays in Razor, scoped CSS, shared CSS, UI state, and shared keyboard JavaScript.
- Validation, transactions, dependency locks, stock/accounting posting, and calculations live in services/repositories.
- Persisted identity is ID-based. Display-name changes must not rewrite historical records.
- Never add page-specific document/window keyboard listeners. Register through the shared context.
- Never edit an already-applied migration to introduce a new feature; add the next forward migration.

## 3. Root files and directories

| Path | Job |
|---|---|
| `TexTrack.sln` | Builds the production web project and PostgreSQL integration-test project together. |
| `TexTrack.Web.csproj` | .NET 10 web definition, EF/Npgsql dependencies, excludes `Tests/**` from production compile, copies SQL migrations. |
| `Program.cs` | Application entry point, DI registrations, database bootstrap, middleware, static files, antiforgery, Blazor endpoints. |
| `appsettings.json` | PostgreSQL connection string and logging configuration. Treat credentials as sensitive. |
| `.gitattributes` | Repository line-ending/text rules. |
| `.gitignore` | Excludes generated build, IDE, diagnostic, environment, coverage, and packaging artifacts. |
| `STOP_TEXTRACK.bat` | Local convenience script for stopping a TexTrack process; inspect the exact target before use. |
| `Components/` | Blazor shell, pages, reusable UI, scoped page styles, keyboard registration components. |
| `Data/` | EF mapping, migration bootstrapper, ordered PostgreSQL SQL migrations. |
| `Domain/` | Persistent entity definitions. |
| `Models/` | UI/edit/list/result/report DTOs; no direct database execution. |
| `Services/` | Master/voucher persistence, validation, dependencies, transactions, posting, and reporting queries. |
| `wwwroot/` | Shared application CSS and capture-phase keyboard JavaScript. |
| `Properties/` | Local .NET launch profile. |
| `Tools/` | Setup/support scripts. |
| `Tests/` | JavaScript unit/browser tests and .NET PostgreSQL integration/component tests. |
| `TexTrack_ERP_AI_MAINTENANCE_PACK_Build2_9/` | Preserved maintenance protocol, baseline, module manifest, known issues, and older code map. |
| `BUILD_*.txt`, `V0.*.txt` and named diagnostics | Historical release notes and manual verification evidence. They are not runtime source. |
| `PROJECT_BLUEPRINT.md` | This current navigation and ownership map. Update it whenever files/modules are added or removed. |

## 4. Application shell and shared keyboard system

| File | Exact responsibility |
|---|---|
| `Components/App.razor` | Root HTML document; loads the compiled scoped CSS bundle, shared CSS, keyboard JS, and Blazor boot script. Asset query versions are updated when browser cache invalidation is needed. |
| `Components/Routes.razor` | Router, route resolution, focus-on-navigation behavior, and default layout selection. |
| `Components/_Imports.razor` | Namespaces and component imports shared by Razor files. |
| `Components/Layout/MainLayout.razor` | Main 42px application shell, menus, company/fiscal-year display, route navigation, shell dialogs and focus return. |
| `Components/Keyboard/KeyboardContextHost.razor` | Hosts shared keyboard-context infrastructure for the render tree. |
| `Components/Keyboard/KeyboardContextScope.razor` | Registers one page/modal context with mode, root, delete, accept, cancel, Esc and initial-focus callbacks; unregisters on disposal. |
| `Components/Keyboard/KeyboardContextMode.cs` | Declares List, Create and Alteration modes. |
| `Components/Keyboard/KeyboardContextType.cs` | Declares List, Form and Modal ownership categories. |
| `Components/Keyboard/VoucherDateInput.razor` | Shared Tally-style voucher date entry and focus/navigation contract. |
| `Components/Keyboard/VoucherDateInput.razor.css` | Scoped shared date-control appearance. |
| `wwwroot/js/textrack-alt-delete.js` | Single capture-phase keyboard engine: context registry, Alt+A/D/X, Esc, Enter/Backspace traversal, pristine/edited state, lookup closing/reopening, modal routing, dirty snapshot and focus helpers. |
| `wwwroot/css/app.css` | Global theme, shell, lists, forms, dialogs, lookup baseline, buttons, tables and shared responsive rules. |

## 5. Pages

### 5.1 General pages

| File | Job |
|---|---|
| `Components/Pages/Home.razor` | Main dashboard/home route and entry navigation. |
| `Components/Pages/Error.razor` | Unhandled-error display page. |

### 5.2 Masters (`Components/Pages/Masters`)

| File | Route/job |
|---|---|
| `LedgerGroups.razor` | `/masters/ledger-groups`; Ledger Group list/create/alter/delete. |
| `Ledgers.razor` | `/masters/ledgers`; Ledger list/create/alter/delete and dependency-aware validation. |
| `JobWorkers.razor` | `/masters/job-workers`; production party master, Ledger linkage and default Godowns. |
| `Godowns.razor` | `/masters/godowns`; Godown master and dependency-safe delete UI. |
| `StockItems.razor` | `/masters/stock-items`; item, UQC, colour/size mappings and variants. |
| `StockGroups.razor` | `/masters/stock-groups`; hierarchical stock-group master. |
| `StockCategories.razor` | `/masters/stock-categories`; stock-category master. |
| `Colours.razor` | `/masters/colours`; colour master. |
| `Sizes.razor` | `/masters/sizes`; size master. |
| `Uqc.razor` | `/masters/uqc`; unit-of-quantity master. |
| `Processes.razor` | `/masters/processes`; manufacturing/process master. |
| `TaxClassifications.razor` | `/masters/tax-classifications`; tax-classification master. |
| `VoucherTypes.razor` | `/masters/voucher-types`; voucher identities, system mappings and numbering configuration. |
| `MasterPage.razor` | Shared generic flat-master list/form UI used by applicable master routes. |

### 5.3 Vouchers (`Components/Pages/Vouchers`)

| File | Route/job |
|---|---|
| `JobWorkOutOrder.razor` | `/vouchers/job-work-out-order`; JWO list/create/alter/delete/cancel, finished goods, size matrix, processes and component-allocation UI. Planning only; no stock posting. |
| `JobWorkOutOrder.razor.css` | Edge-to-edge JWO form, two-layer component allocation, lookups, rows and action bars. |
| `MaterialOut.razor` | `/vouchers/material-out`; material issue against pending JWO, create/alter/delete/cancel and E-Way UI. |
| `MaterialOut.razor.css` | Edge-to-edge Material Out visual system and lookup/action/modal styling. |
| `MaterialIn.razor` | `/vouchers/material-in`; FG receipt, actual component consumption, list/create/persisted view and keyboard orchestration. |
| `MaterialIn.razor.css` | Edge-to-edge Material In entry workspace, sections, fields and action bar. |
| `VoucherPlaceholder.razor` | Shared placeholder presentation for voucher engines not implemented yet. |
| `Contra.razor` | `/vouchers/contra`; placeholder route. |
| `Journal.razor` | `/vouchers/journal`; placeholder route. |
| `MasterJobOrder.razor` | `/vouchers/master-job-order`; placeholder route. |
| `Payment.razor` | `/vouchers/payment`; placeholder route. |
| `Purchase.razor` | `/vouchers/purchase`; placeholder route. |
| `Receipt.razor` | `/vouchers/receipt`; placeholder route. |
| `Sales.razor` | `/vouchers/sales`; placeholder route. |
| `StockJournal.razor` | `/vouchers/stock-journal`; placeholder route. |

Material In child components:

| File | Job |
|---|---|
| `Components/Vouchers/MaterialIn/MaterialInVoucherList.razor` | Saved Material In rows, selection and list callbacks. |
| `MaterialInVoucherList.razor.css` | List-only layout. |
| `Components/Vouchers/MaterialIn/MaterialInPersistedView.razor` | Read-only display of a saved Material In voucher. |
| `MaterialInPersistedView.razor.css` | Persisted-view styling. |

### 5.4 Reports (`Components/Pages/Reports`)

| Files | Route/job |
|---|---|
| `JobWorkerControl.razor` + `.razor.css` | `/reports/job-worker-control`; side-by-side JWO material issued/received control view. |
| `InventoryClosingStock.razor` + `.razor.css` | `/reports/inventory/closing-stock`; hierarchical group/item/colour/size quantity and value. |
| `StockRegister.razor` + `.razor.css` | `/reports/inventory/stock-register`; movement register, running quantity and voucher drill-down. |
| `ProductionReports.razor` + `.razor.css` | `/reports/pending-finished-goods`; pending production, consumption and reconciliation views. |

## 6. Persistence and domain

| File | Exact responsibility |
|---|---|
| `Domain/Entities.cs` | All persisted entities: company/year, masters, voucher headers, JWO/MO/MI rows, stock movements, cancellation and process links. |
| `Data/TexTrackDbContext.cs` | EF `DbSet` declarations and table/key/length/index/relationship/delete-behavior configuration. |
| `Data/DatabaseBootstrapper.cs` | Finds numbered SQL files, validates order/checksums, applies each transactionally, records versions in `schema_versions`. |
| `Services/DatabaseStatus.cs` | Scoped database connection/availability state displayed by the UI. |

### 6.1 SQL migrations (`Data/Migrations`)

| File | Job |
|---|---|
| `001_initial_foundation.sql` | Company, financial year and initial masters. |
| `002_ledger_master.sql` | Ledger persistence foundation. |
| `003_inventory_master_engine.sql` | Inventory masters and integrity. |
| `004_corrected_stock_item_engine.sql` | Corrected Stock Item/UQC/Godown engine. |
| `005_voucher_core_jwo.sql` | Shared voucher header and initial JWO schema. |
| `006_jwo_tally_alignment.sql` | JWO Godown and valuation alignment. |
| `007_universal_voucher_numbering_material_out_foundation.sql` | Universal numbering and Material Out foundation/lines. |
| `008_universal_voucher_cancellation.sql` | Voucher cancellation metadata. |
| `009_material_out_posting.sql` | Material Out details and stock-movement posting. |
| `010_material_out_alter_cancel.sql` | MO alteration/delete/cancel reversal support. |
| `011_stock_movement_kind_length.sql` | Expands movement-kind storage for cancellation names. |
| `012_job_worker_master.sql` | Job Worker production fields, Tally name and default Godowns. |
| `013_material_in_foundation.sql` | Material In receipt, consumption, allocation and movement links. |
| `014_material_in_stock_movement_kinds.sql` | Adds MI consumption/FG-receipt movement kinds. |
| `015_fix_mi_movement_kind.sql` | Removes the earlier restrictive movement-kind check. |
| `016_job_work_order_processes.sql` | Persists process allocation per JWO finished-good row. |

The filename prefix is the applied migration version. Do not renumber, remove, or rewrite an applied migration without a dedicated migration-chain recovery task.

## 7. Models (`Models`)

| File | Job |
|---|---|
| `MasterModels.cs` | General master list/edit/input/result DTOs. |
| `LedgerModels.cs` | Ledger Group and Ledger DTOs. |
| `JobWorkerMasterModels.cs` | Job Worker list/edit/default-Godown DTOs. |
| `StockItemModels.cs` | Stock Item edit model, mappings, variants and UQC lock state. |
| `JobWorkOrderModels.cs` | JWO header, FG, size, component, process, lookup and result DTOs. |
| `MaterialOutModels.cs` | MO header/line/statutory/E-Way/result DTOs. |
| `MaterialInModels.cs` | MI receipt, variant, consumption, allocation, view and result DTOs. |
| `OperationalReportingModels.cs` | Job Worker Control, Closing Stock and Stock Register data/filter models. |
| `ProductionReportingModels.cs` | Pending-production, consumption and reconciliation models. |

## 8. Services and repositories (`Services`)

| File | Job |
|---|---|
| `MasterRepository.cs` | Shared CRUD/search/validation for flat masters. |
| `LedgerRepository.cs` | Ledger Groups and Ledgers, validations, dependencies and persistence. |
| `JobWorkerRepository.cs` | Job Worker search/create/update/defaults/usage locks/delete. |
| `StockItemRepository.cs` | Items, variants, mappings and transactional UQC immutability. |
| `VoucherTypeRepository.cs` | Voucher Type identity, numbering, immutable system mappings and dependencies. |
| `JobWorkOrderRepository.cs` | JWO lookup/list/load/save/delete/cancel; FG, sizes, components and processes. Does not post stock. |
| `MaterialOutRepository.cs` | MO load/save/alter/delete/cancel and atomic source/destination stock movements. |
| `MaterialInRepository.cs` | MI receipts, MO-based component consumption, FIFO allocation and atomic RM/FG stock posting. |
| `OperationalReportingService.cs` | Job Worker Control, Closing Stock and Stock Register queries/drill-down. |
| `ProductionReportingService.cs` | Pending/production/consumption/reconciliation queries. |
| `DatabaseStatus.cs` | UI-visible database availability state. |

## 9. Tests

### 9.1 .NET (`Tests/TexTrack.Web.IntegrationTests`)

| File | Job |
|---|---|
| `TexTrack.Web.IntegrationTests.csproj` | xUnit/Npgsql test project referenced by the solution. |
| `AssemblyInfo.cs` | Test assembly settings. |
| `MigrationChainTests.cs` | Replays the complete SQL chain into an isolated PostgreSQL schema and verifies required objects. |
| `GodownMasterStabilizationTests.cs` | Godown validation/search/update/delete/dependencies/rollback. |
| `JobWorkerMasterStabilizationTests.cs` | Job Worker validation, stable identity, defaults, dependencies and deletion. |
| `VoucherTypeMasterStabilizationTests.cs` | System/custom type rules, numbering, dependencies and rollback. |
| `StockItemUqcImmutabilityTests.cs` | UQC change locks, manipulated requests and transaction rollback. |
| `ProductionReportingTests.cs` | JWO/MO/MI reconciliation, movements, cancellation, report calculations and drill-down identity. |
| `KeyboardContextComponentTests.cs` | Registration, replacement, disposal and command-routing contract. |

### 9.2 JavaScript (`Tests/JavaScript`)

| File | Job |
|---|---|
| `textrack-alt-delete.test.cjs` | Shared capture listener, active context, Alt+A/D/X, Esc, modal/line delete, focus, lookup and Backspace tests. |
| `production-reports.test.cjs` | Static/report UI contract tests. |
| `job-worker-master.test.cjs` | Job Worker/JWO/MO integration contract tests. |
| `keyboard-context.browser.cjs` | Real-browser list/create/alter context lifecycle across screens. |
| `godown-master.browser.cjs` | Keyboard-only Godown workflow. |
| `job-worker-master.browser.cjs` | Keyboard-only Job Worker defaults and MO integration workflow. |
| `voucher-types.browser.cjs` | Voucher Type real-browser workflow. |
| `production-reports.browser.cjs` | Real-browser report navigation and output checks. |
| `reporting-voucher-corrections.browser.cjs` | Material In and corrected-voucher browser regression workflow. |

Current verified totals: 44 JavaScript unit/static tests and 68 .NET integration/component tests.

## 10. Maintenance and historical documentation

### 10.1 Maintenance pack

| File | Job |
|---|---|
| `README_FIRST.md` | Entry instructions for the Build 2.9 maintenance pack. |
| `CURRENT_BASELINE.md` | Build 2.9 baseline statement. |
| `BUILD_2_9_SOURCE_FACTS.md` | Verified source facts from that build. |
| `FROZEN_MODULES.md` | Modules declared frozen at that point. |
| `KNOWN_ISSUES.md` | Known issues recorded for that baseline. |
| `TEXTRACK_CODE_MAP.md` | Older module/file map; this blueprint supersedes it for current navigation. |
| `TEXTRACK_MODULE_MANIFEST.json` | Machine-readable Build 2.9 module manifest. |
| `AI_CHANGE_PROTOCOL.md` | Safe-change protocol. |
| `AI_TASK_TEMPLATE.md` | Task template. |
| `BUILD_CHANGELOG.md` | Maintenance-pack change history. |

All paths above are under `TexTrack_ERP_AI_MAINTENANCE_PACK_Build2_9/`.

### 10.2 Root historical files

As of the 2026-09-11 documentation cleanup, these are preserved as evidence
under `docs/archive/` rather than loose at the repository root — see
[`docs/README.md`](docs/README.md) for the full index and which subfolder each
lives in (`build-notes/`, `resolved-fixes/jwo-keyboard/`, or `audits/`). None
are loaded at runtime.

When an old note conflicts with current code/tests, current code/tests win.

## 11. Build, test and browser commands

```powershell
Set-Location 'D:\MINI ERP C#\Codex\TEXTRACK'
dotnet restore TexTrack.sln
dotnet build TexTrack.sln --no-restore
dotnet test Tests\TexTrack.Web.IntegrationTests\TexTrack.Web.IntegrationTests.csproj --no-build --no-restore
```

JavaScript tests use Node's built-in runner:

```powershell
node --test Tests\JavaScript\*.test.cjs
```

Real-browser scripts require Playwright, a Chrome/Chromium executable, the PostgreSQL test fixtures they expect, and a running isolated TexTrack URL supplied through `TEXTRACK_BROWSER_BASE_URL`.

## 12. Safe feature workflow

1. Find the feature in section 1.
2. Read the primary file and its model/service/test dependencies.
3. Confirm whether the request is UI-only or changes persisted meaning.
4. Preserve stable IDs and historical voucher references.
5. For async form transitions, set explicit pending focus before the query and focus only after the target is rendered.
6. Add the smallest relevant test first or alongside the change.
7. Run restore, full build, JavaScript tests, PostgreSQL tests, and the focused real-browser workflow.
8. Update this blueprint if file ownership, routes, migrations, or implemented modules changed.

## 13. Generated and non-production content policy

Safe to regenerate and exclude from source handovers:

- `.vs/`
- every `bin/` and `obj/`
- `TestResults/`, coverage output and logs
- `artifacts/` packaging output
- `node_modules/`

The detached AI Studio React/Vite prototype (`src/`, `assets/`, `index.html`, `metadata.json`, `package.json`, `tsconfig.json`, `vite.config.ts`, `.env.example`, AI-Studio `README.md`) was removed from the production root on 28 July 2026 after verification that `TexTrack.Web.csproj` did not reference it. Restore such a prototype only outside the production root and label it design-reference only.

## 14. Implemented versus placeholder modules

Implemented:

- Ledger Groups, Ledgers, UQC, Stock Groups, Stock Categories, Stock Items
- Colours, Sizes, Processes, Tax Classifications, Godowns, Voucher Types, Job Workers
- Job Work Out Order, Material Out, Material In
- Job Worker Control, Closing Stock, Stock Register and production reports
- Global keyboard-context and Tally-style voucher navigation

Placeholder only:

- Contra, Journal, Master Job Order, Payment, Purchase, Receipt, Sales, Stock Journal

Do not describe a placeholder route as an implemented accounting/posting engine.