# Frozen and Protected Modules — Build 2.9

The following areas must not be modified for ordinary visual, keyboard or report-navigation fixes.

## Fully protected business foundations

- Ledger Groups
- Ledgers
- UQC
- Stock Groups
- Stock Categories
- Stock Items and hidden variant integrity
- Godowns
- Voucher Types
- Job Worker master
- universal master deletion rules
- voucher numbering and sequence identity
- PostgreSQL transaction/rollback framework
- existing foreign keys and dependency protection

## Protected production engines

- `Services/JobWorkOrderRepository.cs`
- `Services/MaterialOutRepository.cs`
- Material In save/posting sections of `Services/MaterialInRepository.cs`
- stock movement posting
- automatic Material In component consumption
- previous-MI-consumption deduction
- MO availability cap
- FIFO MO allocation
- atomic RM consumption and FG receipt

## Immutable historical migrations

- `Data/Migrations/001_initial_foundation.sql`
- `Data/Migrations/002_ledger_master.sql`
- `Data/Migrations/003_inventory_master_engine.sql`
- `Data/Migrations/004_corrected_stock_item_engine.sql`
- `Data/Migrations/005_voucher_core_jwo.sql`
- `Data/Migrations/006_jwo_tally_alignment.sql`
- `Data/Migrations/007_universal_voucher_numbering_material_out_foundation.sql`
- `Data/Migrations/008_universal_voucher_cancellation.sql`
- `Data/Migrations/009_material_out_posting.sql`
- `Data/Migrations/010_material_out_alter_cancel.sql`
- `Data/Migrations/011_stock_movement_kind_length.sql`
- `Data/Migrations/012_job_worker_master.sql`
- `Data/Migrations/013_material_in_foundation.sql`

## Shared keyboard framework

Protected unless a defect reproduces across multiple unrelated pages:

- `Components/Keyboard/KeyboardContextHost.razor`
- `Components/Keyboard/KeyboardContextScope.razor`
- `Components/Keyboard/KeyboardContextMode.cs`
- `Components/Keyboard/KeyboardContextType.cs`
- `wwwroot/js/textrack-alt-delete.js`

A page-local Esc/focus issue must first be fixed in the page that owns the state.

## Acceptable changes without broad review

### Visual-only

- specific `.razor` markup
- matching page-scoped `.razor.css`
- focused browser test

### Local keyboard/navigation

- specific report/voucher `.razor`
- `MainLayout.razor` only when menu return is involved
- focused browser test

### Report calculation

- exact method in `OperationalReportingService.cs`
- exact DTO in `OperationalReportingModels.cs`
- report page and focused integration test

Do not broaden scope merely “to be safe.” Broad review itself creates regression risk.
