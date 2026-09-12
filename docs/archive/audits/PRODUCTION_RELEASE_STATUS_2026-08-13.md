# TexTrack ERP Production Release Status

Date: 13 August 2026

## Verification

- .NET SDK: 10.0.302
- Restore: successful; dependencies are current
- Full solution build: successful with 0 warnings and 0 errors
- PostgreSQL integration tests: 102 passed, 0 failed, 0 skipped
- Migration integration test initializes a clean schema through migration 019

## Included in this release candidate

- Optional multi-level Bill of Materials with parent/child stages, quantities, UQC and cycle prevention.
- One Job Work Out Order can coordinate multiple BOM stages, job workers and processes.
- BOM hierarchy is snapshotted into the JWO so later BOM edits do not rewrite historical orders.
- Material Out selects the components assigned to the active stage/job worker and validates the selection again on the server.
- WIP is transaction-derived; permanent Stock Item groups are not rewritten.
- Manual creation or movement of a Stock Item into the protected WIP master group is rejected.
- Stock Item UQC immutability after transactional use, including server-side bypass protection and atomic rollback.
- Closing Stock shows colour summaries alongside size quantities.
- Database-side paging for Ledgers, Job Workers and Stock Items (100 rows per page).
- Existing high-volume report optimizations, report pagination, flexible dates, voucher registers, production reports and Tally XML exchange remain included.
- Selected menu text is white for readable keyboard focus.

## Principal implementation files

- `Data/Migrations/019_multilevel_bom_wip.sql` — BOM/stage schema and database cycle guard.
- `Domain/Entities.cs` and `Data/TexTrackDbContext.cs` — BOM and voucher-stage persistence mapping.
- `Models/BillOfMaterialModels.cs` — BOM edit, lookup and list contracts.
- `Services/BillOfMaterialRepository.cs` — validation, hierarchy loading, saving and cycle detection.
- `Components/Pages/Masters/BillOfMaterials.razor` — optional BOM master interface.
- `Services/JobWorkOrderRepository.cs` — BOM snapshot and stage assignment during JWO creation/alteration.
- `Components/Pages/Vouchers/JobWorkOutOrder.razor` — BOM and stage assignment workflow.
- `Services/MaterialOutRepository.cs` — stage-aware pending orders and component validation.
- `Services/StockItemRepository.cs` — WIP protection, UQC immutability and paged Stock Item lists.
- `Services/LedgerRepository.cs` — paged Ledger lists.
- `Services/JobWorkerRepository.cs` — paged Job Worker lists.
- `Components/Pages/Masters/Ledgers.razor`, `StockItems.razor`, `JobWorkers.razor` — paged keyboard-first master registers.
- `Components/Pages/Reports/InventoryClosingStock.razor` — colour/size-aware closing stock presentation.
- `Tests/TexTrack.Web.IntegrationTests/MultiLevelBomTests.cs` — nested BOM and atomic cycle tests.
- `Tests/TexTrack.Web.IntegrationTests/StockItemUqcImmutabilityTests.cs` — UQC lock and migration-chain coverage.

## Remaining release limitations

These items are not represented as completed by this package:

- Context-sensitive F3 creation and automatic selection of Ledger, Godown, Size and Colour masters from every voucher field still needs one shared implementation.
- A truly global busy overlay that blocks navigation and every competing action during long import/export/report operations still needs cross-page wiring.
- Material In does not yet expose full BOM-stage completion selection for every nested production stage.
- WIP storage is transaction-derived, but every inventory report does not yet present a separate virtual WIP group/balance split.
- Some lower-volume master and report screens still use client-side lists; the largest master lists are database-paged in this candidate.

This package is therefore a verified release candidate, not a claim that the remaining limitations are finished.
