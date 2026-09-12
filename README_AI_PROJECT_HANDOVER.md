# TexTrack ERP — Current AI Project Handover

Last verified: 02-Sep-2026 (Asia/Calcutta)  
Current product stage: Production workflow substantially implemented; automated Tally XML hierarchy/mapping stabilization is complete, with manual TallyPrime round-trip acceptance still pending.

## 1. Authoritative project and safety rules

The latest user-owned project inspected for this document is:

`C:\Users\VICTUS\Desktop\c#\Google\TexTrackERP`

The read-only working snapshot used to generate and verify this document is:

`C:\Users\VICTUS\Documents\Codex\2026-07-23\figma-plugin-figma-openai-curated-remote-2\TexTrackERP_aug08_snapshot`

The Desktop project is newer than the older Codex working copy named `TexTrackERP_aug01`. Never overwrite the Desktop project with that older folder. Reconcile individual changes against the latest Desktop files.

Mandatory rules for the next AI:

1. Copy the Desktop project into a normal writable working folder before editing.
2. Treat the Desktop project as the source of truth unless the user supplies a newer ZIP/folder.
3. Preserve unrelated dirty files and user changes.
4. Use the narrowest possible change set.
5. Never edit an already-applied migration. Add a new forward-only migration after inspecting the complete chain.
6. Do not modify posting, voucher, report, keyboard, or styling code for an XML-only defect unless a concrete call path proves it is required.
7. Run focused tests first and the complete accepted suite before producing a release.
8. Do not claim a Tally round trip passed unless a newly exported XML file was actually imported into TallyPrime and inspected there.
9. Save new user deliverables on the Desktop when permission is available.

## 2. Product overview

TexTrack ERP is a keyboard-first garment production, accounts, and inventory system with a Tally-inspired workflow.

Technology:

- C# / .NET 10
- ASP.NET Core Blazor Interactive Server
- Entity Framework Core 10
- PostgreSQL through Npgsql
- Razor components and scoped CSS
- One global JavaScript capture-phase keyboard router
- SQL-file migration runner executed during application startup

Production workflow:

`Master Job Order (optional grouping) → Job Work Out Order → Material Out → Material In → operational and inventory reports`

Current company context is Demo Company, company ID 1. The active financial year currently defaults to 01-Apr-2026 through 31-Mar-2027.

## 3. Verified build status

Verification was run against the authoritative Desktop project on 01-Sep-2026.

- .NET SDK: 10.0.302
- Restore: passed
- Full solution build: passed with 0 warnings and 0 errors
- .NET/PostgreSQL tests: 173 passed, 0 failed, 0 skipped
- JavaScript tests: 86 passed, 0 failed
- Migration files present: 001 through 018, with no missing sequence number

Commands:

```powershell
dotnet --info
dotnet restore TexTrack.sln
dotnet build TexTrack.sln --no-restore -p:UseAppHost=false
dotnet test TexTrack.sln --no-restore -p:UseAppHost=false
& 'C:\Users\VICTUS\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe' --test Tests\JavaScript\*.test.cjs
```

These automated results verify TexTrack parsing, persistence, export hierarchy, balances, UQC safeguards, and downstream Material Out linkage. They do not replace importing a newly exported file into TallyPrime and inspecting it there.

## 4. Current progress by module

### Implemented and currently covered by tests

- Master maintenance: Ledger Groups, Ledgers, Job Workers, Godowns, UQC, Stock Groups, Stock Categories, Stock Items, Colours, Sizes, Processes, Tax Classifications, and Voucher Types.
- Stock Item UQC immutability after real transactional use, including server-side enforcement and rollback.
- Job Work Out Order creation, alteration, cancellation, deletion rules, multiple finished goods, multiple colours under one design group, sizes, processes, component allocation, and master-order linking.
- Master Job Order creation, alteration, unrestricted cancellation/deletion, finished-good planning, and linking existing JWOs.
- Material Out creation, alteration, cancellation, deletion protection, posting, E-Way/GST details, JWO component issue tracking, and stock movements.
- Material In creation and alteration in the same voucher screen, mandatory process charges, automatic raw-material consumption, FIFO allocation against Material Out, cancellation/deletion, and stock posting.
- Month-first voucher histories for JWO, MJO, Material Out, and Material In.
- Job Worker Control: the primary management container with Aging, Exceptions, and Portfolio tabs. Aging is one row per JWO/final finished good and uses actual outstanding-Material-Out lot age through MI-to-MO allocations; JWO creation does not start material aging and fully consumed MO lots do not continue aging. The tab supports one to three custom cut-offs, UQC-safe slab totals, column filters, keyboard drill-down, and no persisted aging values.
- Job Worker Exceptions: a tab in Job Worker Control derived from the canonical Aging engine, with centralized Critical/High/Attention thresholds, multiple explicit reason labels, completed-history isolation, quick views, UQC-safe quantities, deterministic priority sorting, and keyboard drill-down. Exception priorities are calculated at report time and are never stored.
- Job Worker Portfolio: a tab in Job Worker Control with one row per actual stage-assigned job worker, aggregating active JWO, exception counts, material-aging risk and UQC-safe pending FG. Opening a portfolio row filters the Exceptions tab to that job worker.
- Job Worker T-Format remains a separate detailed reconciliation report at /reports/job-worker-control-t-format; Pending Material Issue, Closing Stock, Stock Register, and legacy production reports remain separate.
- Global keyboard ownership: Alt+M, Alt+T, Alt+E, Alt+R, Alt+A, Alt+D, Alt+X, F2, Alt+F2, Esc, Enter, arrows, and Tally-style Backspace behavior.
- Flexible date entry such as day-only and day/month/year with `.`, `/`, `-`, or space separators.
- Settings page for manual company-aware Tally XML import/export, preview, history, exceptions, and pending variant allocation.

### Intentionally incomplete modules

- Contra, Payment, Receipt, Journal, Sales, Purchase, and Stock Journal routes are placeholders. Their pages delegate to `VoucherPlaceholder.razor`; they are not complete accounting engines.
- Tally integration is manual XML file exchange only. There is no live TallyPrime HTTP/cloud connection.
- Interactive resolution of `Variant Allocation Pending` remains future work.
- Tally deletions cannot be inferred from a missing XML voucher and remain the user's reconciliation responsibility.

### Tally XML stabilization completed on 08-Aug-2026

- Authentic JWO `VOUCHERCOMPONENTLIST.LIST` rows are parsed as children of their owning finished good.
- `GODOWNNAME` and `DESTINATIONGODOWNNAME` are mapped independently.
- JWO import persists component allocation, allowing matching Material Out XML to import afterward.
- JWO export emits one top-level `ALLINVENTORYENTRIES.LIST` per finished good and nests raw materials inside its batch.
- Finished-good amounts are negative; nested components are positive; component value determines the balancing finished-good value and rate.
- Stock Item reuse is allowed only when the XML UQC matches the persisted TexTrack UQC; a mismatch is rejected atomically.
- The old flat TexTrack XML shape remains import-compatible where signs allow finished-good/component roles to be inferred.
- Automated verification: 96 .NET/PostgreSQL tests and 69 JavaScript structural/unit tests passed.

### Remaining manual Tally acceptance

A newly exported JWO must still be imported into TallyPrime 7.1 and visually checked for finished-good rows, nested components, destination Godown, signs, totals, and subsequent Material Out linkage. Do not call the real Tally round trip passed until this is done.

#### TALLY-UQC-VISIBILITY-006 — manual observation requires confirmation

An imported short UQC correctly reuses an existing TexTrack UQC according to the automated integration test, but the user previously reported that imported UQC information was not visible in the master UI. Reproduce this in the current UI before changing code; it may be display/mapping rather than duplicate creation.

#### MO-PARTIAL-DELETE-007 — policy noted, not finalized

The user observed that when one Material Out requirement is issued in multiple vouchers, deletion may be blocked for all parts while cancellation is allowed. This was explicitly deferred. Diagnose dependency rules before changing repository behavior.

### Current Tally reference files available on 08-Aug-2026

- `C:\Program Files\TallyPrime\Job Work Out Order_1.xml`: authentic nested JWO reference.
- `C:\Program Files\TallyPrime\Job Work Out Order_2.xml`: Tally re-export of the previously malformed flat TexTrack voucher.
- `C:\Program Files\TallyPrime\Material Out_1.xml` and `Material Out_2.xml`: authentic in/out movement direction references.
- `C:\Users\VICTUS\Desktop\MiniERP\TexTrack_JWO_20260801_20270331.xml`: previously malformed flat TexTrack export retained for comparison.
- `C:\Program Files\TallyPrime\cancelled jwo.xml`: cancellation reference.
- focused master XML files under `C:\Program Files\TallyPrime\`.

Earlier `*_1175.xml` and `TexTrack_JWO_20260401_20270331.xml` paths were supplied during development but were no longer present at the last verification.

Use the authentic Tally exports as the schema reference. Do not infer Tally XML structure from the malformed TexTrack export.

## 5. Runtime architecture

### Startup flow

1. `Program.cs` reads `ConnectionStrings:TexTrackDatabase`.
2. It registers Razor Interactive Server, the pooled EF context factory, state contexts, repositories, reporting services, and Tally XML services.
3. `DatabaseBootstrapper.InitializeAsync()` connects to PostgreSQL.
4. Migration SQL files are ordered by filename, executed transactionally, and recorded in `schema_versions` with SHA-256 checksums.
5. Static files, antiforgery, exception handling, and Razor routes are activated.
6. `/api/tally-xml/export/{package}` exports either `jwo` or combined `material` XML for a supplied date range.

### Layer responsibilities

- `Components/`: presentation, page-local state, focus, navigation, and command ownership.
- `Models/`: edit models, lookups, report DTOs, request/result objects, and XML transport models.
- `Services/`: database queries, business validation, transactions, posting, reporting, and XML exchange.
- `Domain/`: persisted EF entity graph.
- `Data/`: EF mapping, migration runner, and forward-only SQL migrations.
- `wwwroot/`: global theme and global keyboard JavaScript.
- `Tests/`: integration, structural, keyboard, and optional browser tests.

## 6. Root file map

- `TexTrack.sln`: solution containing the web project and integration-test project.
- `TexTrack.Web.csproj`: .NET 10 web project; references EF Core and Npgsql; copies SQL migrations to output; excludes `Tests/**` from the web build.
- `Program.cs`: authoritative runtime composition root and Tally export endpoint.
- `Program.cs.bak`: non-runtime backup; currently mirrors the composition root. Do not edit unless deliberately refreshing or removing backups.
- `appsettings.json`: PostgreSQL connection string and application configuration. Treat credentials as sensitive.
- `Properties/launchSettings.json`: local development URLs and launch profile.
- `.gitattributes`: repository line-ending/text behavior.
- `.gitignore`: ignored build, IDE, and temporary artifacts.
- `.github/copilot-instructions.md`: repository-specific instructions for GitHub Copilot/AI contributors; reconcile it with this newer handover when instructions conflict.
- `STOP_TEXTRACK.bat`: local helper for stopping the running application.
- `Tools/Setup-PostgreSql.ps1`: creates/configures the local PostgreSQL database expected by development builds.
- `PROJECT_BLUEPRINT.md`: historical broad architecture blueprint; verify against current code before relying on it.
- `MODULE_2_ARCHITECT.md`: detailed architecture proposal for the voucher/report module; contains requirements and implementation gates, not proof that every item is implemented.
- As of the 2026-09-11 documentation cleanup, the old `README_FIRST.txt`, `INDEX.txt`, `DELIVERABLES_MANIFEST.txt`, `IMPLEMENTATION_CHECKLIST.txt`, `PROJECT_COMPLETION_REPORT.txt`, `SUMMARY_READY_FOR_TESTING.txt`, `ROOT_CAUSE_ANALYSIS.txt`, `QUICK_REFERENCE_LINE_NUMBERS.txt`, `VERIFICATION_PROJECT_FILES_COMPLETE.txt`, every `BUILD_*.txt`/`V0.*.txt` release note and checklist, and the `JWO_KEYBOARD_*.txt` bug-fix bundle moved from the repository root into `docs/archive/` — see [`docs/README.md`](docs/README.md) for the full index and which subfolder each lives in. They remain evidence of earlier intent, not authoritative current behavior; this Markdown file supersedes them for current status. One caveat worth carrying forward explicitly: `docs/archive/build-notes/BUILD_3_0_TALLY_XML_EXCHANGE.txt`'s verification statement predates later real-Tally round-trip defects and must not be treated as final acceptance.

Generated/non-source directories:

- `.git/`: version-control metadata.
- `bin/`: compiled output; regenerate, do not edit.
- `obj/`: intermediate build output; regenerate, do not edit.
- `.build-work/`: temporary isolated build output; not product source.

## 7. Components directory map

### Application shell

- `Components/App.razor`: HTML document shell; loads global CSS and `textrack-alt-delete.js`.
- `Components/Routes.razor`: router and default layout host.
- `Components/_Imports.razor`: shared Razor namespaces and component imports.
- `Components/Layout/MainLayout.razor`: top navigation, Masters/Entries/Reports/Settings menus, nested report menus, current company/year display, active-period modal, menu focus restoration, and shell shortcuts.

### Shared keyboard components

- `Components/Keyboard/KeyboardContextHost.razor`: cascading keyboard host used by page scopes.
- `Components/Keyboard/KeyboardContextScope.razor`: registers/disposes one page or modal context with the global JavaScript router.
- `Components/Keyboard/KeyboardContextMode.cs`: create/alter/list/modal mode contract.
- `Components/Keyboard/KeyboardContextType.cs`: keyboard context category contract.
- `Components/Keyboard/FlexibleDateParser.cs`: parses Tally-style flexible dates.
- `Components/Keyboard/FlexibleDateInput.razor`: reusable flexible date field, including Enter movement.
- `Components/Keyboard/VoucherDateInput.razor`: voucher-specific date editor used by implemented vouchers.
- `Components/Keyboard/VoucherDateInput.razor.css`: scoped date appearance and focus styling.

### Master pages

- `Components/Pages/Masters/MasterPage.razor`: generic list/edit page used by simple masters.
- `Components/Pages/Masters/LedgerGroups.razor`: binds the generic master page to Ledger Groups.
- `Components/Pages/Masters/StockGroups.razor`: binds the generic page to hierarchical Stock Groups.
- `Components/Pages/Masters/Uqc.razor`: UQC list/edit shell.
- `Components/Pages/Masters/StockCategories.razor`: Stock Category shell.
- `Components/Pages/Masters/Godowns.razor`: specialized Godown list/edit flow and keyboard handling.
- `Components/Pages/Masters/Colours.razor`: Colour master shell.
- `Components/Pages/Masters/Sizes.razor`: ordered Size master shell.
- `Components/Pages/Masters/Processes.razor`: ordered Process master shell.
- `Components/Pages/Masters/TaxClassifications.razor`: Tax Classification shell.
- `Components/Pages/Masters/Ledgers.razor`: full Ledger list/edit page with group, contact, GST, balance, and bank fields.
- `Components/Pages/Masters/JobWorkers.razor`: Ledger-backed Job Worker master with default Material Out/Material In Godowns and Tally name.
- `Components/Pages/Masters/StockItems.razor`: Stock Item, UQC, tax, colour, size, and variant editing; disables UQC when transactionally locked.
- `Components/Pages/Masters/VoucherTypes.razor`: Voucher Type identity, parent, posting, Tally name, and numbering configuration.

### Voucher pages

- `Components/Pages/Vouchers/JobWorkOutOrder.razor`: full JWO create/alter screen, typeable lookups, multiple finished-good/design colour rows, sizes, processes, component allocation, save/quit/cancel/delete, and return URL handling.
- `Components/Pages/Vouchers/JobWorkOutOrder.razor.css`: scoped full-screen JWO theme and layout.
- `Components/Pages/Vouchers/MasterJobOrder.razor`: Master Job Order plan and existing-JWO linking screen.
- `Components/Pages/Vouchers/MasterJobOrder.razor.css`: scoped Master Job Order layout.
- `Components/Pages/Vouchers/MaterialOut.razor`: Material Out create/alter orchestration, Job Worker/JWO/Godown lookups, issue grid, E-Way details, dialogs, and keyboard flow.
- `Components/Pages/Vouchers/MaterialOut.razor.css`: scoped Material Out visual layer.
- `Components/Pages/Vouchers/MaterialIn.razor`: Material In create/alter orchestration, finished-good receipt quantities, process/total charges, automatic material consumption presentation, narration, lifecycle actions, and keyboard flow.
- `Components/Pages/Vouchers/MaterialIn.razor.css`: scoped Material In visual layer.
- `Components/Pages/Vouchers/VoucherPlaceholder.razor`: shared placeholder for accounting vouchers not yet implemented.
- `Components/Pages/Vouchers/Contra.razor`: Contra route wrapper.
- `Components/Pages/Vouchers/Payment.razor`: Payment route wrapper.
- `Components/Pages/Vouchers/Receipt.razor`: Receipt route wrapper.
- `Components/Pages/Vouchers/Journal.razor`: Journal route wrapper.
- `Components/Pages/Vouchers/Sales.razor`: Sales route wrapper.
- `Components/Pages/Vouchers/Purchase.razor`: Purchase route wrapper.
- `Components/Pages/Vouchers/StockJournal.razor`: Stock Journal route wrapper.
- `Components/Pages/Vouchers/ai_studio_code.html`: design/reference artifact only; not part of the compiled Blazor application.

### Shared voucher presentation

- `Components/Vouchers/Shared/VoucherEntryFrame.razor`: common voucher page/frame structure.
- `Components/Vouchers/Shared/VoucherField.razor`: reusable aligned voucher label/value field.
- `Components/Vouchers/Shared/VoucherActionBar.razor`: bottom action and keyboard-hint strip.
- `Components/Vouchers/Shared/VoucherCommandButton.razor`: styled command button.
- `Components/Vouchers/Shared/VoucherCommandRail.razor`: optional right-side command rail retained for screens that need it.
- `Components/Vouchers/MaterialIn/MaterialInVoucherList.razor` and `.css`: Material In saved-voucher list presentation.
- `Components/Vouchers/MaterialIn/MaterialInPersistedView.razor` and `.css`: legacy persisted/read-only presentation retained for compatibility; current route opens saved MI in the main alteration screen.

### Report pages

- `Components/Pages/Reports/VoucherHistory.razor`: shared month-first JWO/MJO/MO/MI register, custom period, selection, alteration return URL, deletion, and cancellation actions.
- `Components/Pages/Reports/VoucherHistory.razor.css`: shared register styling and paging/footer layout.
- `Components/Pages/Reports/JobWorkerControl.razor`: combined filters and batch/JWO T-format report rendering.
- `Components/Pages/Reports/JobWorkerControl.razor.css`: T-format blocks, side-by-side material/FG tables, cost summaries, totals, and paging.
- `Components/Pages/Reports/PendingMaterialIssue.razor`: fully pending/partially issued/all report and JWO drill-down.
- `Components/Pages/Reports/PendingMaterialIssue.razor.css`: scoped pending-issue report styling.
- `Components/Pages/Reports/InventoryClosingStock.razor`: Stock Group → Item → Godown hierarchy, filters, UQC-safe totals, and Stock Register drill-down.
- `Components/Pages/Reports/InventoryClosingStock.razor.css`: Closing Stock styling.
- `Components/Pages/Reports/StockRegister.razor`: item/Godown stock movements, voucher-number search, voucher drill-down, and origin-aware Esc return.
- `Components/Pages/Reports/StockRegister.razor.css`: Stock Register styling.
- `Components/Pages/Reports/ProductionReports.razor`: older report routes for pending FG, dispatch outstanding, consumption balance, and legacy T-format.
- `Components/Pages/Reports/ProductionReports.razor.css`: styling for those legacy reports.

### Settings and basic pages

- `Components/Pages/Settings/TallyXmlExchange.razor`: XML file selection, preview, company confirmation, apply, export, exception, variant-task, and history tabs.
- `Components/Pages/Settings/TallyXmlExchange.razor.css`: Settings/Tally exchange theme.
- `Components/Pages/Settings/DataMaintenance.razor`: authenticated Developer-only full database reset UI. It requires the exact phrase `CLEAR TEXTRACK DATABASE` and Developer password re-entry.
- `Services/DeveloperAccessService.cs`: validates the single PBKDF2-protected Developer credential and exposes the server-side Developer role.
- `Services/DatabaseMaintenanceService.cs`: atomically truncates all ERP business tables, preserves `schema_versions` and external Developer access, and restores the mandatory Demo Company/system master foundation. Any failure rolls the entire reset back.

Developer access is intentionally separate from ordinary ERP use. The initial local credential is configured as ID `DEVELOPER`; change the PBKDF2 hash in `DeveloperAccess:PasswordHash` before deployment. A Developer may open, update, or delete a cancelled voucher, but normal downstream dependency protections remain enforced. Clearing the database is the explicit escape hatch when every dependent record must be removed.
- `Components/Pages/Home.razor`: landing dashboard and keyboard navigation to main modules.
- `Components/Pages/Error.razor`: application error route.

## 8. Domain and database map

### `Domain/Entities.cs`

Contains all persisted entities and relationships:

- ownership/audit contracts: `IAuditableEntity`, `ICompanyOwnedEntity`, `INamedMasterEntity`
- company/time: `Company`, `FinancialYear`
- masters: `LedgerGroup`, `Ledger`, `StockGroup`, `StockCategory`, `Uqc`, `Godown`, `Colour`, `SizeMaster`, `ProcessMaster`, `TaxClassification`, `StockItem`, `StockItemColour`, `StockItemSize`, `StockItemVariant`, `VoucherType`
- voucher aggregate: `Voucher`, `VoucherLink`, `AuditLog`
- JWO/MJO: `JobWorkOrderFinishedGood`, `JobWorkOrderSizeAllocation`, `JobWorkOrderComponent`, `JobWorkOrderProcess`, `MasterJobOrderFinishedGood`, `MasterJobOrderAllocation`
- Material Out: `MaterialOutLine`, `MaterialOutDetail`
- Material In: `MaterialInDetail`, `MaterialInFinishedGood`, `MaterialInFinishedGoodAllocation`, `MaterialInConsumption`, `MaterialInMaterialOutAllocation`
- inventory: `StockMovement`
- Tally: `TallyCompanyLink`, `TallyExchangeBatch`, `TallySyncRecord`, `TallyImportException`, `TallyVariantAllocationTask`

### Data files

- `Data/TexTrackDbContext.cs`: all `DbSet` declarations, PostgreSQL table/column mapping, keys, relationships, indexes, precision, constraints, audit stamping, and the current-company query behavior.
- `Data/DatabaseBootstrapper.cs`: startup migration runner and database status reporting.
- `Data/DatabaseBootstrapper.cs.bak`: non-runtime backup.

### Migration execution order and responsibility

1. `001_initial_foundation.sql`: companies, financial years, base masters, audit logs.
2. `002_ledger_master.sql`: ledgers and ledger indexes.
3. `003_inventory_master_engine.sql`: early inventory master enhancements and uniqueness/indexes.
4. `004_corrected_stock_item_engine.sql`: Stock Items, colour/size mappings, variants; removes obsolete Godown hierarchy/type columns.
5. `005_voucher_core_jwo.sql`: Voucher aggregate, JWO finished goods/sizes/components, voucher links.
6. `006_jwo_tally_alignment.sql`: JWO source/destination Godowns and XML rates/amounts; voucher-type parent.
7. `007_universal_voucher_numbering_material_out_foundation.sql`: numbering sequences and `material_out_lines`; this is the authentic migration previously missing from an older package.
8. `008_universal_voucher_cancellation.sql`: universal cancellation fields.
9. `009_material_out_posting.sql`: Material Out details and stock movements.
10. `010_material_out_alter_cancel.sql`: Material Out lifecycle movement kinds/indexes.
11. `011_stock_movement_kind_length.sql`: expands movement-kind storage.
12. `012_job_worker_master.sql`: Job Worker role/Tally name/default Godowns on Ledgers.
13. `013_material_in_foundation.sql`: Material In aggregate, FIFO source allocations, and MI movement links.
14. `014_material_in_stock_movement_kinds.sql`: allows Material In movement kinds.
15. `015_fix_mi_movement_kind.sql`: repairs the MI movement-kind check constraint.
16. `016_job_work_order_processes.sql`: per-finished-good JWO process rows.
17. `017_master_job_order_multicolour_linking.sql`: MJO link, design-group key, MJO plan/allocation tables.
18. `018_tally_xml_exchange.sql`: company links, exchange batches, sync records, import exceptions, and variant tasks.

Do not renumber, replace, or silently “clean up” this chain.

## 9. Model file map

- `Models/MasterModels.cs`: generic master kinds, definitions, list/edit DTOs, operation result, and Voucher Type edit/list/parent models.
- `Models/LedgerModels.cs`: Ledger Group choices and Ledger list/edit DTOs.
- `Models/JobWorkerMasterModels.cs`: Job Worker list/edit/lookups.
- `Models/StockItemModels.cs`: Stock Item list/edit/lookups; `IsUqcLocked` is non-persisted UI state.
- `Models/JobWorkOrderModels.cs`: JWO list/edit aggregate, finished goods, processes, sizes, components, and all lookups.
- `Models/MasterJobOrderModels.cs`: MJO list/edit plan, colour/size lines, linked JWO rows, and link lookup.
- `Models/MaterialOutModels.cs`: MO defaults, pending JWO projections, edit/save lines, E-Way details, and results.
- `Models/MaterialInModels.cs`: MI defaults, pending order/FG/variant data, availability, save/edit/view/list DTOs, process charge and consumption inputs.
- `Models/OperationalReportingModels.cs`: Job Worker Control, Pending Material Issue, current Closing Stock, Stock Register, filters, UQC totals, and movement DTOs.
- `Models/ProductionReportingModels.cs`: legacy production reports and legacy Closing Stock/T-format DTOs.
- `Models/TallyXmlModels.cs`: parsed XML masters/vouchers/lines, preview rows, apply result, history, exception, variant task, and export file.
- `Models/VoucherLayoutProfile.cs`: reusable voucher field/layout metadata intended to standardize field positions across voucher types.

## 10. Service and repository map

- `Services/DatabaseStatus.cs`: database connectivity state plus `CurrentCompanyContext` company/year identity.
- `Services/CurrentPeriodContext.cs`: scoped active report period and validation.
- `Services/MasterRepository.cs`: generic master queries/saves/deletes, hierarchy-cycle checks, Godown rules, UQC/Colour/ordered master handling, and audit logging.
- `Services/LedgerRepository.cs`: Ledger CRUD, normalization, GST/PAN/contact/balance validation, dependency-safe deletion, and audit.
- `Services/JobWorkerRepository.cs`: Ledger-backed Job Worker CRUD, default Godown validation, role enforcement, transaction rollback, and lookups.
- `Services/StockItemRepository.cs`: Stock Item CRUD, colour/size/variant synchronization, real transactional-usage query, immutable-UQC enforcement, rollback, and audit.
- `Services/VoucherTypeRepository.cs`: Voucher Type CRUD, parent/nature/posting/numbering validation, historical-use protection, and sequence checks.
- `Services/JobWorkOrderRepository.cs`: JWO list/lookups/defaults/edit/save/cancel/delete, finished-good validation, size/process/component persistence, numbering, lifecycle restrictions, and transaction rollback.
- `Services/MasterJobOrderRepository.cs`: MJO list/lookups/defaults/edit/save/cancel/delete, JWO linking, generated plan comparison, and unrestricted lifecycle actions requested by the user.
- `Services/MaterialOutRepository.cs`: MO list/defaults/lookups/pending orders/create/update/cancel/delete, numbering, component issue validation, E-Way details, paired stock movements, downstream dependency checks, JWO status updates, and rollback.
- `Services/MaterialInRepository.cs`: MI defaults/lookups/pending orders/list/view/edit/create/update/cancel/delete, required process charges, receipt validation, automatic consumption, FIFO MO allocations, paired stock movements, status updates, audit, and rollback.
- `Services/OperationalReportingService.cs`: current Job Worker Control, Pending Material Issue, Closing Stock hierarchy, Stock Register, UQC totals, and report lookups.
- `Services/ProductionReportingService.cs`: legacy pending-FG, dispatch, consumption, movement details, JWO detail, T-format, and Closing Stock queries.
- `Services/TallyXmlParser.cs`: sanitizes and parses supported Tally masters/vouchers, including recursive JWO component lists and separate source/destination Godowns.
- `Services/TallyXmlExchangeService.cs`: preview classification, atomic apply, missing-master creation, JWO/MO/MI import, company scoping, sync state, cancellation/deletion handling, exceptions, and variant tasks.
- `Services/TallyXmlExporter.cs`: exports focused masters plus JWO or combined MO/MI vouchers and records exchange/sync history; JWO components are nested under their owning finished-good batch with balanced Tally signs.
- `Services/TallySyncStateTracker.cs`: marks an altered synced voucher as `UpdatedNotExported`.

## 11. API and route map

API:

- `GET /api/tally-xml/export/{package}?from=YYYY-MM-DD&to=YYYY-MM-DD`
  - `{package}` must be `jwo` or `material`.
  - `jwo` exports Job Work Out Orders only.
  - `material` exports Material Out and Material In together.

Primary routes:

- `/` — Home
- `/masters/ledger-groups`
- `/masters/ledgers`
- `/masters/job-workers`
- `/masters/uqc`
- `/masters/stock-groups`
- `/masters/stock-categories`
- `/masters/stock-items`
- `/masters/godowns`
- `/masters/colours`
- `/masters/sizes`
- `/masters/processes`
- `/masters/tax-classifications`
- `/masters/voucher-types`
- `/vouchers/master-job-order`
- `/vouchers/job-work-out-order`
- `/vouchers/material-out`
- `/vouchers/material-in`
- `/reports/job-work/vouchers/{Kind}`
- `/reports/job-work/pending-material-issue`
- `/reports/job-worker-control`
- `/reports/inventory/closing-stock`
- `/reports/inventory/stock-register`
- `/settings/tally-xml`

Placeholder routes:

- `/vouchers/contra`, `/payment`, `/receipt`, `/journal`, `/sales`, `/purchase`, `/stock-journal`

Legacy report routes remain available in `ProductionReports.razor`.

## 12. Critical business invariants

### Company and identity

- All business data is company-scoped.
- Voucher uniqueness is company + financial year + Voucher Type + normalized Voucher Number.
- Exact Voucher Number is the cross-system identity expected between TexTrack and Tally.
- Order/reference number is independent and may differ from Voucher Number.

### Stock Items and UQC

- Structural `StockItemColour`, `StockItemSize`, and `StockItemVariant` rows alone do not lock UQC.
- Real use includes JWO finished goods, JWO components, JWO size allocations through variants, Material Out lines, and Stock Movements.
- A used Stock Item may save permitted changes when UQC is unchanged.
- A changed UQC on a used item must fail atomically with:
  `Cannot change UQC because this Stock Item has already been used or linked.`

### JWO

- One serial number represents one finished-good design row; additional colours under that design do not create a new serial number.
- Size quantities roll into colour quantity and voucher total.
- Component allocation/Godown may be shared; a different allocation requires a separate finished-good row.
- MJO is only a higher-level grouping/link and is not required on the JWO itself.

### Material Out

- Only components belonging to the referenced JWO may be issued.
- Source and destination Godowns are distinct.
- Stock is posted atomically as source outward and job-worker/destination inward.
- Alter/delete/cancel must respect downstream Material In allocations.

### Material In

- Material In is the terminal production voucher and has a delete option.
- Finished goods are received into the receiving Godown.
- Raw material consumption is allocated only from unconsumed Material Out quantities, FIFO.
- Process charge is mandatory.
- Total charge divided by total FG quantity received determines per-unit process charge when total-charge entry is used.
- Save/update/cancel/delete must reverse/rebuild all related stock movements and allocations atomically.

### Reporting cost logic

- Material cost means the cost of raw material actually consumed, not merely issued.
- Total process cost is the processing charge incurred on finished goods received.
- Cost per unit = `(material consumed cost + total process charges incurred) / total FG quantity received`.
- Pending reports include partially pending as well as fully pending orders when applicable.
- Cancelled/deleted vouchers must not contribute to active production totals.
- Quantity totals must always be grouped by UQC; never mix PCS and MTS.

## 13. Global keyboard contract

Owned by `wwwroot/js/textrack-alt-delete.js`, `KeyboardContextScope.razor`, and the active page:

- Alt+M: Masters menu
- Alt+T and Alt+E: Entries menu aliases
- Alt+R: Reports menu
- Settings deliberately has no Alt+S shell shortcut because Alt+S is used in report flows and historically conflicted with save behavior.
- Alt+A: accept/save/update active voucher or confirm active modal
- Alt+D: active-page delete only; browser Alt+D is intercepted in capture phase
- Alt+X: cancel eligible active voucher
- F2: edit current voucher date
- Alt+F2: change active period
- Esc: Tally-style quit/back; dirty voucher asks through TexTrack modal, not browser dialogs
- Backspace: edit character when appropriate; otherwise move to the previous logical field without clearing values
- Enter: accept lookup/advance field/activate focused command

Never add page-specific global listeners. The capture listener must remain registered exactly once.

## 14. Styling contract

- Theme: TexTrack navy blue and white, black primary text, pale yellow active fields, pale blue table headers, green success, red destructive/error.
- Voucher pages should preserve the approved compact, full-width JWO layout.
- Shared field alignment belongs in reusable voucher components/CSS where practical.
- Page-specific visual changes belong in the matching `.razor.css` file.
- Do not redesign positions or business controls during a behavior-only fix.
- Remove browser autofill/save-information overlays using proper autocomplete semantics; do not replace them with custom browser dialogs.

Static web assets:

- `wwwroot/css/app.css`: global application theme, master/report/voucher base styles, shell layout, modals, tables, lookup lists, and responsive behavior.
- `wwwroot/js/textrack-alt-delete.js`: the one capture-phase command router, focus utilities, context registry, flexible date helpers, dirty-form detection, menu return helpers, and browser-shortcut interception.

## 15. Test file map

### .NET/PostgreSQL integration tests

- `Tests/TexTrack.Web.IntegrationTests/TexTrack.Web.IntegrationTests.csproj`: xUnit .NET 10 project referencing the web project and appsettings.
- `AssemblyInfo.cs`: test assembly behavior/configuration.
- `MigrationChainTests.cs`: migration discovery/order and clean-chain verification.
- `GodownMasterStabilizationTests.cs`: Godown CRUD, dependencies, audit, and master stability.
- `JobWorkerMasterStabilizationTests.cs`: Job Worker identity/default Godowns/role/dependency behavior.
- `VoucherTypeMasterStabilizationTests.cs`: Voucher Type identity, numbering, hierarchy, historical use, and deletion protection.
- `StockItemUqcImmutabilityTests.cs`: all required UQC lock/unlock, manipulation, unchanged-UQC, and rollback scenarios; also provides the shared PostgreSQL test environment used by later fixtures.
- `CurrentPeriodContextTests.cs`: valid/invalid active-period behavior.
- `KeyboardContextComponentTests.cs`: structural shared keyboard ownership contracts.
- `ProductionReportingTests.cs`: JWO/MO/MI posting/report math, UQC totals, pending states, cost calculations, Closing Stock, Stock Register, and report exclusions.
- `TallyXmlParserTests.cs`: parser tests for voucher/order identity, control-character sanitation, material directions, masters, cancellation, nested JWO components, and independent destination Godown.
- `TallyXmlExchangeIntegrationTests.cs`: UQC reuse/conflict, nested JWO persistence, downstream Material Out linkage, and exporter hierarchy/sign/balance tests.

### JavaScript structural/unit tests

- `Tests/JavaScript/textrack-alt-delete.test.cjs`: global capture listener, context precedence, shortcuts, Esc, Backspace, date parsing, menu routing, and voucher contracts.
- `production-reports.test.cjs`: report menu, JWC structure, Pending Material Issue, totals/UQC, routes, Esc, and drill-down structural assertions.
- `voucher-history-workflow.test.cjs`: month-first histories, return URLs, lifecycle actions, MJO/JWO multi-colour behavior, voucher shells, lookup presentation, Enter save, and hidden empty dropdowns.
- `job-worker-master.test.cjs`: Job Worker and Godown precedence/role structure.

### Optional browser scripts

- `godown-master.browser.cjs`
- `job-worker-master.browser.cjs`
- `keyboard-context.browser.cjs`
- `production-reports.browser.cjs`
- `reporting-voucher-corrections.browser.cjs`
- `voucher-types.browser.cjs`

These require a running app/browser environment and are not included in the 69 Node unit-test count unless explicitly executed.

## 16. Historical AI maintenance pack

`TexTrack_ERP_AI_MAINTENANCE_PACK_Build2_9/` is an older Build 2.9 guidance pack. It is useful for regression boundaries but is not the current baseline.

- `README_FIRST.md`: old reading order.
- `CURRENT_BASELINE.md`: Build 2.9 baseline, now superseded.
- `BUILD_2_9_SOURCE_FACTS.md`: facts from the old Build 2.9 ZIP.
- `TEXTRACK_CODE_MAP.md`: old targeted code map.
- `FROZEN_MODULES.md`: protected-module rules that remain broadly sensible.
- `KNOWN_ISSUES.md`: old report/navigation issues; many are now covered by current tests.
- `AI_CHANGE_PROTOCOL.md`: narrow-review/change-level protocol; continue using it.
- `AI_TASK_TEMPLATE.md`: template for future work requests.
- `BUILD_CHANGELOG.md`: Build 2.9 chronology.
- `TEXTRACK_MODULE_MANIFEST.json`: machine-readable old module map; paths are useful but issue/status data is stale.

## 17. Recommended next task: real TallyPrime round-trip acceptance

1. Start the updated Desktop project and export a fresh JWO containing at least one finished good and one component.
2. Import it into TallyPrime 7.1.
3. Confirm the finished good appears once, components are nested, destination Godown is correct, and the voucher balances.
4. Export/import the linked Material Out and confirm it resolves the JWO component rather than reporting it missing.
5. Re-export the accepted Tally voucher and compare its semantic hierarchy with the TexTrack export.
6. Only after those checks, mark the Tally round trip accepted. If Tally reports a defect, preserve the exact generated XML and Tally re-export before changing code.

Keep the same narrow XML-only production boundary unless a captured real-Tally file proves another dependency is required.

## 18. Handover checklist for every future AI

Before editing:

- Confirm the authoritative folder and last modified dates.
- Read this file and the exact target files only.
- State task level, permitted files, protected files, and tests.
- Reproduce the defect or establish exact code evidence.

Before delivery:

- Report root cause.
- List exact changed files using absolute paths.
- Explain validation and transaction behavior.
- Run restore/build/focused tests/full accepted tests.
- Separate automated verification from manual browser/Tally verification.
- State remaining limitations honestly.
- Produce a Desktop folder/ZIP only when requested and provide its SHA-256.
