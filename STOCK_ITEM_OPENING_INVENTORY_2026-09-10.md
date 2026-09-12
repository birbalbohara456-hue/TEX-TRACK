# TexTrack Stock Item Opening Inventory

Date: 10 September 2026
Branch: `codex/stock-item-opening-inventory-2026-09-10`

## Outcome

Opening inventory is entered and altered inside the Stock Item master, following
the familiar Tally-style workflow. TexTrack does not expose Opening Stock as a
normal day-to-day voucher screen.

The visible master fields are backed by a real internal Opening Stock voucher,
inventory-inward lines and stock movements. This preserves the existing inventory,
valuation, period-control and audit architecture instead of storing an unaudited
quantity directly on the Stock Item row.

## User workflow

For the current financial year, the Stock Item form shows an Opening Inventory
section dated at the Books Beginning Date. A user can:

- allocate the item across one or more Godowns;
- allocate each selected Colour/Size variant separately;
- enter Opening Quantity and either Rate or Opening Value;
- add more Godown/variant allocation rows;
- alter or remove the opening allocation later, subject to period control.

If Rate is entered, Value is calculated as `Quantity × Rate`. If Value is entered,
Rate is calculated as `Value ÷ Quantity`. The accepted Value is retained exactly;
this avoids inventing a rounding difference when the derived four-decimal Rate
cannot reproduce the exact value.

The section uses the Stock Item's single UQC. Duplicate rows for the same
Colour/Size variant and Godown are rejected. Zero-only placeholder rows are not
posted.

## Internal posting design

Each Stock Item may own at most one Opening Stock voucher per company and financial
year. That voucher:

- is dated exactly on the financial year's Start Date;
- has no party ledger;
- contains only lines for its owning Stock Item;
- identifies the exact hidden Stock Item Variant and Godown on every line;
- posts `OpeningStockInward` stock movements with the accepted quantity and value;
- uses the shared voucher sequence allocator and period-control service;
- records operational audit logs and immutable full voucher revisions.

An unchanged master save does not rewrite the opening posting or add a false
voucher-history revision. An actual alteration replaces the old live lines and
movements in one serializable transaction and adds an Update revision. Removing
all opening allocations removes the live voucher and stock effect only after a
final Delete snapshot is recorded; the audit history survives.

## Migration 032

File: `Data/Migrations/032_stock_item_opening_inventory.sql`

The migration:

1. adds nullable `vouchers.opening_stock_item_id` with a restrictive Stock Item
   foreign key;
2. adds a filtered unique index on company, financial year and opening Stock Item;
3. adds a database trigger enforcing the Opening Stock voucher type, same-company
   ownership, Books Beginning Date and absence of a party ledger;
4. strengthens inventory-inward line validation so an Opening Stock line must
   match the voucher's owning Stock Item, while Purchase cannot carry this link;
5. removes the old `amount = round(quantity × rate, 4)` check so an exact
   user-entered Value remains authoritative when its derived Rate is rounded.

### Existing-production safety

The new foreign key column is nullable and historical vouchers receive `NULL`.
The filtered unique index therefore does not collide with historical data. The
new voucher-origin trigger only applies when `opening_stock_item_id` is populated,
so existing Purchase, JWO, MO and MI vouchers are not rewritten or reclassified.

Migration 031 introduced `inventory_inward_lines`; older historical vouchers do
not use that table. Migration 032 performs no quantity/value backfill and no stock
movement rewrite. It is forward-only and is covered by the bootstrap checksum,
gap, rollback, concurrent-startup and newer-database compatibility tests.

## Integrity boundaries

The Razor page performs only interaction and immediate Rate/Value assistance.
Authoritative checks remain in `StockItemRepository`, and critical identity rules
are repeated in PostgreSQL triggers. The save operates inside one serializable
transaction, so the master, variants, inward lines, stock movements and audit
records commit together or all roll back.

Opening-created stock also locks any used Colour/Size variant from removal, just
as other voucher and stock history does. Company, financial-year, active-master,
variant, UQC and Godown ownership checks are server-side and cannot be bypassed by
editing browser values.

## Verification baseline

- Application build: zero warnings and zero errors.
- .NET/PostgreSQL integration suite: 240 passed, zero failed.
- JavaScript keyboard/UI suite: 73 passed, zero failed.
- Focused coverage verifies exact Value preservation and Rate derivation, Books
  Beginning dating, one voucher per item/year, alteration without duplicate stock,
  no-op save behavior, removal with retained audit, duplicate allocation rollback,
  and database-trigger rejection of an invalid opening date.

## Deliberate boundary

This change supplies beginning inventory. It does not make Opening Stock a
mid-period adjustment mechanism. Stock introduced after the Books Beginning Date
must come from a legitimate transaction such as Purchase or another future inward
document. Perpetual FIFO cost layers and default negative-stock blocking remain
separate subsequent phases.
