# A09 Inventory Period Control — Implementation Slice 1

**Date:** 10 September 2026  
**Branch:** `codex/inventory-period-control-2026-09-10`  
**Policy source:** `A09_INVENTORY_PERIOD_AND_CHRONOLOGY_POLICY_2026-09-10.md`

## Delivered scope

This slice establishes the first enforceable A09 backend boundary without changing the Razor UI:

1. company-owned inclusive `Stock Frozen Through` date;
2. one shared server-side period-control service;
3. enforcement on native Material Out and Material In create, alter, cancel and delete paths;
4. one reusable, company-scoped exact stock-position query service;
5. Migration 029 and behavioral regression coverage.

It deliberately does **not** implement FIFO layers, negative-stock blocking, dated cancellation reversals, cross-financial-year JWO posting changes, import enforcement, or a Settings screen. Those remain separate controlled slices under the approved A09 policy.

## Migration 029

File: `Data/Migrations/029_inventory_period_control.sql`

Database change:

```sql
ALTER TABLE companies
    ADD COLUMN stock_frozen_through date;
```

The column is nullable. Existing companies and all historical vouchers therefore preserve their pre-migration behavior because `NULL` means that no stock period has been frozen. The migration does not update, delete, re-key or recalculate any voucher, movement, allocation, audit or master row. It adds only column metadata and a database comment.

This is safe for an existing production database containing historical vouchers, subject to the normal mandatory backup and tested migration procedure. PostgreSQL may briefly acquire an `ACCESS EXCLUSIVE` table lock while altering `companies`; the table is small master data, and no historical transaction rewrite is performed.

## Inclusive boundary semantics

If a company has `stock_frozen_through = 2026-06-30`:

- any protected posting dated on or before 30-Jun-2026 is rejected;
- 01-Jul-2026 is the first open posting date;
- moving a voucher originally dated in the frozen period to an open date is rejected because both the original and proposed dates are checked;
- Developer does not silently bypass this integrity boundary.

Until dated reversal posting is implemented, cancellation and deletion of an MO/MI whose original posting date is frozen are rejected. This prevents the current cancellation mechanism from retrospectively changing closed stock history.

## Enforced native mutation paths

`MaterialOutRepository`:

- `SaveAsync`
- `UpdateAsync`
- `CancelAsync`
- `DeleteAsync`

`MaterialInRepository`:

- `SaveAsync` for creation and alteration
- `CancelAsync`
- `DeleteAsync`

The check executes after authorization and inside the same serializable transaction used by the voucher mutation. The authoritative company value is read from PostgreSQL; no browser-supplied freeze value is trusted.

## Exact stock-position query

`StockPositionService` accepts exactly:

- stock item;
- nullable variant (where `NULL` is distinct from a concrete variant);
- UQC;
- godown;
- as-on date.

It derives quantity and value only from persisted effective `stock_movements` for the authenticated company and excludes cancelled vouchers under the current cancellation model. It does not open, scrape or imitate a report. It validates that every supplied master belongs to the authenticated company, including inactive historical masters, so a caller cannot cross the tenant boundary by submitting foreign IDs.

The returned analytical average is `value / quantity`, or zero when quantity is zero. This service is a read foundation for Stock in Hand assistance, shortage analysis and reconciliation. It does not allocate FIFO layers. Negative-stock decisions were subsequently implemented at the shared posting boundary; see `NEGATIVE_STOCK_POLICY_IMPLEMENTATION_2026-09-11.md`.

## Verification

Behavioral tests cover:

- inclusive freeze boundary and first open day;
- null freeze backward compatibility;
- original-date protection during amendment;
- native MO/MI creation enforcement;
- native MO/MI alteration, cancellation and deletion enforcement;
- exact item/variant/UQC/godown/as-on aggregation;
- base variant isolation from concrete variants;
- company isolation;
- Migration 029 sequence, schema nullability and startup compatibility behavior.

Verification result on 10 September 2026:

- .NET integration suite: **223 passed, 0 failed**;
- JavaScript regression suite: **73 passed, 0 failed**;
- application build: **0 warnings, 0 errors**.

## Operational activation

Migration 029 is inert after deployment because existing values are `NULL`. Do not set `stock_frozen_through` in production until the authorised Settings workflow and reopen audit event are implemented in the next controlled slice. Direct database updates are not the intended production operating procedure.
