# TexTrack Native Inventory Inward Foundation

Date: 10 September 2026
Branch: `codex/native-inventory-inward-2026-09-10`

## Outcome

TexTrack now has a native, auditable backend posting contract for two legitimate
sources of inward inventory:

- Purchase: accepted supplier quantity and transaction cost enter stock.
- Opening Stock: dated opening quantity and opening value enter stock.

This foundation intentionally precedes default negative-stock blocking. It gives a
new item a valid native path into inventory instead of requiring a Tally import or a
direct database movement.

## Deliberate boundary

Purchase currently posts **inventory quantity and inventory value only**. It does
not yet create supplier payable, purchase account, GST/tax or other accounting
ledger entries. Those accounting features remain deferred and must not be inferred
from an inventory-inward voucher.

No Purchase or Opening Stock Razor UI was changed in this slice. The service is the
backend contract for later UI wiring, allowing the separately redesigned UI to be
preserved.

Negative-stock enforcement is also not enabled by this change. The agreed
company-level Allow/Block policy remains a later step after all inward paths and
imports use the shared domain contract.

## Migration 031

File: `Data/Migrations/031_native_inventory_inward_foundation.sql`

The migration performs these changes:

1. Adds the permanent `OPENING_STOCK` Voucher Type to every existing company.
2. Changes the permanent `PURCHASE` Voucher Type posting mode from the old
   placeholder `Enabled later` value to `Inventory Inward`.
3. Creates `inventory_inward_lines`, containing:
   - voucher and line number;
   - Stock Item and explicit Stock Item Variant, including the BASE variant;
   - UQC and Godown;
   - positive quantity;
   - accepted rate;
   - server-calculated amount rounded to four decimal places.
4. Adds nullable `stock_movements.inventory_inward_line_id` so every posting is
   traceable to its source voucher line.
5. Adds lookup and traceability indexes.
6. Adds database triggers that reject:
   - a non-Purchase/non-Opening-Stock source voucher;
   - cross-company item, variant, UQC or Godown references;
   - a variant that does not belong to the selected item;
   - a UQC that differs from the Stock Item UQC;
   - a stock movement whose identity, quantity, rate, value, kind or voucher does
     not match its source inward line.

The migration is additive for historical vouchers and does not backfill or rewrite
existing stock movements. It deliberately stops if a company already has a
different Voucher Type named `Opening Stock`, because silently converting a custom
type could corrupt its meaning. Resolve that name conflict before retrying.

## Application contract

`InventoryInwardRepository` uses the existing production controls:

- authorization through `OperateVouchers` or `CancelOrDeleteVouchers`;
- company and Financial Year scope on every operation;
- inclusive stock-period freeze checks on create, alter, cancel and delete;
- serializable database transactions;
- centralized atomic voucher-number allocation;
- optimistic concurrency tokens on alteration;
- active-master validation;
- a required active Sundry Creditors supplier for Purchase;
- no supplier ledger on Opening Stock;
- exact item + variant + UQC + Godown identity;
- server-side amount calculation (`quantity × accepted rate`);
- duplicate exact-position rejection inside one voucher;
- create/update/cancel/delete operational audit logs;
- immutable, hash-chained full voucher snapshots, including inward lines and stock
  movements;
- downstream-link protection before cancellation or deletion.

Cancellation creates an equal and opposite movement against the same source line.
Deletion removes the live voucher and its live stock effect only after recording a
final immutable audit snapshot. Audit evidence has no foreign key to the live
voucher and therefore survives deletion.

## Database-maintenance behavior

`ClearAllBusinessDataAsync` preserves schema migration history while clearing
business rows. Migration 031 therefore exposes a bounded reset-seed section. The
maintenance service replays that section so that:

- permanent Opening Stock is available after a reset; and
- Purchase returns with `Inventory Inward` posting mode.

This is covered by a behavioral integration test.

## Valuation behavior at this stage

Each accepted inward line writes its transaction rate and value into the stock
movement ledger. Existing Closing Stock and exact stock-position queries can see
that quantity and value automatically because they consume `stock_movements`.

This is not yet the proposed perpetual FIFO cost-layer engine. Migration 031 gives
future FIFO layers an auditable source transaction and exact stock identity; it does
not claim to implement layer allocation, revaluation or alternative valuation
methods.

## Verification

Focused PostgreSQL integration coverage proves:

- Purchase exact-variant, Godown, quantity, rate and value posting;
- Opening Stock without a party ledger and visibility in exact stock position;
- alteration replaces old postings without duplicating stock and creates a second
  audit revision;
- cancellation nets quantity and value to zero while preserving traceability;
- deletion removes live stock but preserves its final audit revision;
- wrong variant and wrong UQC inputs are rejected atomically;
- the shared stock freeze blocks inward creation;
- Migration 031 participates in forward-only bootstrap, checksum, gap, rollback,
  concurrent-startup and newer-database protection tests;
- developer maintenance reseeds the permanent inward voucher foundation.

## Next safe step

Migration 032 supersedes the earlier Opening Stock UI assumption: opening inventory
is now entered through the Stock Item master and posts through this audited backend
contract. See `STOCK_ITEM_OPENING_INVENTORY_2026-09-10.md`. Purchase UI wiring,
import routing, perpetual FIFO layers and the company-level negative-stock
Allow/Block policy remain separate later steps.
