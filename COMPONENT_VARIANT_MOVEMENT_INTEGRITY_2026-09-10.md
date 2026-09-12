# Component-Variant Stock-Movement Integrity — Implementation Slice 2

**Date:** 10 September 2026  
**Branch:** `codex/component-variant-movement-integrity-2026-09-10`  
**Depends on:** `codex/inventory-period-control-2026-09-10`

## Purpose

TexTrack already stores the selected component colour/size identity on
`job_work_order_components.component_variant_id`. Before this slice, Material Out source and
destination movements and Material In consumption movements did not copy that identity into
`stock_movements.stock_item_variant_id`. Consequently, movement-based stock queries could merge
different variants of the same Stock Item.

This slice repairs that identity chain before exact-position negative-stock enforcement is built.
It does not enable negative-stock blocking and does not change any UI.

## Future posting behavior

The following movement types now copy the immutable JWO component snapshot variant:

- native Material Out source;
- native Material Out destination;
- native Material Out alteration/reposting;
- native Material In consumption;
- Tally XML Material Out source and destination;
- Tally XML Material In consumption.

Material In finished-good movements already carried the received allocation variant and remain
unchanged. Cancellation reversals already clone the original movement variant through the shared
`StockPostingService`.

For components whose snapshot legitimately has no variant, the movement remains null. The code
does not guess a colour or size at posting time.

## Migration 030

File: `Data/Migrations/030_component_variant_movement_integrity.sql`

Migration 030 performs four bounded operations:

1. For a historical JWO component with a null variant, it assigns a variant only when that Stock
   Item has exactly one variant in PostgreSQL. A component belonging to a multi-variant Stock Item
   remains null because its historical identity is ambiguous.
2. It validates linked Material Out and Material In consumption rows. If an existing movement,
   line, component and variant disagree about the Stock Item or variant, the migration raises an
   exception and rolls back instead of overwriting the conflict.
3. It fills a null movement variant from the now-deterministic linked JWO component for all linked
   Material Out movements and Material In consumption movements, including historical reversals
   that retain those line links.
4. It adds `ix_stock_movements_exact_position` over company, item, nullable variant, UQC, godown,
   movement date and movement ID for exact as-on stock-position queries.

The migration does **not** change quantities, rates, values, movement dates, voucher dates,
godowns, voucher status, allocation quantities or audit snapshots. Ambiguous history is preserved
rather than guessed.

### Existing-database safety

The data updates are deterministic and execute in the bootstrapper's migration transaction. Any
raised inconsistency aborts Migration 030 and prevents its schema-version record from being
written. The existing database is therefore not left half-migrated.

Normal production precautions still apply: take and verify a backup, run the migration during a
controlled maintenance window, and review any blocking inconsistency before retrying. The updates
scan component and linked movement tables and index creation takes a table lock, so migration time
depends on historical volume.

## Tally adapter boundary

An imported Tally JWO now stores the component's base variant when that import creates/resolves the
base representation. Its imported MO and MI movements reuse the same saved component variant.
This keeps the existing adapter path dimensionally consistent; it does not turn Tally import into
an opening-stock bypass. Tally stock imports were subsequently routed through the shared
negative-stock policy; see `NEGATIVE_STOCK_POLICY_IMPLEMENTATION_2026-09-11.md`.

## Verification

Behavioral tests cover:

- native Material Out creation and alteration preserving the component variant;
- native Material In consumption preserving the same variant;
- Tally JWO/MO/MI import preserving the same variant identity;
- deterministic historical backfill for a sole variant;
- preservation of null for an ambiguous multi-variant component;
- migration rollback on a conflicting existing variant;
- the complete 30-file migration sequence and bootstrapper compatibility behavior.

Verification result on 10 September 2026:

- focused .NET integration slice: **24 passed, 0 failed**;
- complete .NET integration suite: **226 passed, 0 failed**;
- shared JavaScript regression suite: **73 passed, 0 failed**.

## Negative-stock prerequisite closure

The prerequisite sequence above was completed on 11 September 2026. Native Opening Stock and
Purchase inward posting now establish legitimate stock, current native and Tally stock writers use
the shared posting policy, and the company option is administrative, reconciled and audited without
a Developer bypass. The implemented validation key is company + Stock Item + nullable variant +
UQC + godown + voucher date. See `NEGATIVE_STOCK_POLICY_IMPLEMENTATION_2026-09-11.md` for the
current behavior and migration-safety record.
