# Negative Stock Policy — Implementation Record

**Date:** 11 September 2026

**Branch:** `codex/negative-stock-policy-2026-09-11`

**Policy source:** `A09_INVENTORY_PERIOD_AND_CHRONOLOGY_POLICY_2026-09-10.md`

## Delivered behavior

TexTrack now has a company-owned inventory policy with two modes:

- **Block Negative Stock** (the default for new or empty companies); and
- **Allow Negative Stock** (an explicit administrative override).

When blocking is active, every stock mutation is validated before its database transaction commits. The validation key is the exact:

- company;
- Stock Item;
- nullable variant/base identity;
- UQC; and
- godown.

For every touched position, movement history is accumulated by effective voucher date. A later inward voucher cannot satisfy an earlier outward voucher, and stock in another godown or variant cannot satisfy the position being issued. Movements on the same effective date are evaluated as one daily net position because the present stock ledger has date—not time-of-day—as its accounting chronology.

## Shared posting boundary

The check is part of `StockPostingService`, not a browser-only validation. All current production stock writers use that service and invoke the policy before commit:

- Material Out create, alter, cancel and delete;
- Material In create, alter, cancel and delete;
- Opening Stock and Purchase create, alter, cancel and delete through `InventoryInwardRepository`;
- Stock Item opening-inventory maintenance;
- Purchase Return create, alter, cancel and delete; and
- Tally XML Material Out/Material In import, update and cancellation.

The final transaction state is checked, rather than each line independently. This covers multi-line vouchers and alterations that remove old movements and add replacements in the same transaction.

## Concurrency rule

Each exact stock position receives a deterministic PostgreSQL transaction-level advisory lock before validation. Competing issues of the same position therefore serialize: after the first transaction commits, the second rechecks the persisted balance and is rejected if the remaining quantity is insufficient. Repository transactions retain their existing PostgreSQL isolation level as an additional concurrency safeguard.

## Migration 041 and existing-data safety

Migration `041_negative_stock_policy.sql` adds:

```sql
companies.allow_negative_stock boolean NOT NULL DEFAULT false
```

It does not rewrite vouchers, stock movements, quantities, rates, values or links. To avoid making a previously permissive live company unusable immediately after deployment:

- an existing company with any stock-movement history is migrated to **Allow Negative Stock**;
- an existing company without stock history remains in the default blocking mode; and
- every migration decision is written to `audit_logs`.

This preserves operational compatibility while requiring an administrator to reconcile historical exceptions before strengthening an established company's policy.

## Administration and audit

The Settings → Inventory Policy page requires `AdminSettings` authorization. It shows the active mode and up to 20 historical negative positions.

Changing from blocking to allowing requires an explicit risk acknowledgement. Changing from allowing to blocking performs a full reconciliation inside a serializable transaction and is refused if any negative historical position remains. Both successful changes and rejected activation attempts are audited. There is no Developer-role bypass in the stock-posting check.

## Known boundary

Allow mode permits operational negative quantities but does not pretend that final FIFO valuation exists. Until the future FIFO/provisional-cost replay engine is implemented, reports and users must treat valuation of negative positions as provisional. The setting is therefore a controlled compatibility option, not the recommended default.

The broader A09 dated-reversal and closed-period chronology work remains separate. The current check follows the stock ledger's presently persisted active-movement model and does not itself implement dated cancellation reversals.

## Verification

Behavioral coverage includes:

- exact-godown isolation and rejection of a future inward used against an earlier outward;
- combined final-state validation for multiple outward lines;
- explicit Allow mode compatibility;
- refusal and audit when historical negatives prevent activation;
- successful and audited activation for a clean company;
- two concurrent issues competing for the same remaining stock;
- native Purchase Return rollback when it would create negative stock; and
- Tally Material Out rollback, proving imports use the same policy as native entry.

The final verification result is recorded in the implementing commit and its test run output.
