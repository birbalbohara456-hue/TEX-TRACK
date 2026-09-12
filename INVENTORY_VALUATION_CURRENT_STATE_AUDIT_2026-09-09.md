# TexTrack ERP — Inventory Valuation Current-State Audit

**Date:** 9 September 2026

**Baseline:** `f41e7e3` (`codex/github-prep-2026-09-09`)

**Audit branch:** `codex/inventory-valuation-audit-2026-09-09`

**Scope:** Documentation and source inspection only. No application code, schema, migration, or production behavior was changed.

## Executive conclusion

TexTrack currently has a sound **production stock-movement ledger** and useful **MO-to-MI cost lineage**, but it does not yet have a complete general-purpose inventory valuation engine.

The implemented accounting model is best described as:

1. Material Out transfers quantity and the operator/JWO-supplied rate from an internal source godown to a jobber destination godown.
2. Material In consumes the oldest eligible Material Out lines first and preserves their rate/value snapshots.
3. A received intermediate or finished output is valued as actual allocated material value plus the entered process charge.
4. Closing Stock reports sum persisted movement values and display `net value / net quantity` as a weighted-average presentation rate.

This is internally coherent for the currently supported JWO → MO → MI production chain. It is **not perpetual FIFO of original purchase layers**, because purchase, opening-stock, sales, and stock-journal posting are not implemented and raw-material MO value is derived from an editable reference rate rather than an immutable inward cost layer.

The strongest current feature is manufactured-output lineage: ordinary MI entry allocates consumption to actual MO lines in oldest-MO-first order and carries consumed value plus conversion cost into the output. The largest valuation risk is earlier in the chain: an editable raw-material MO rate becomes the actual value transferred into WIP and subsequently becomes the material portion of manufactured cost.

No FIFO redesign should begin until the business decisions listed under **Decision gates** are approved.

## Scope inspected

- `Services/StockPostingService.cs`
- `Services/StockMovementSemantics.cs`
- `Services/MaterialOutRepository.cs`
- `Services/MaterialInRepository.cs`
- `Services/OperationalReportingService.cs`
- `Services/TallyXmlExchangeService.cs`
- `Models/MaterialOutModels.cs`
- `Models/MaterialInModels.cs`
- `Models/OperationalReportingModels.cs`
- `Models/ProcessChargeCalculation.cs`
- `Domain/Entities.cs`
- `Data/TexTrackDbContext.cs`
- migrations 009, 013–015, 019, 023, 026 and 027
- existing valuation, lifecycle, BOM, godown and UQC tests
- Purchase, Sales and Stock Journal routes/pages

## What the system does today

### 1. Stock ledger

`stock_movements` is the reporting source of truth for implemented stock quantity and value. Each movement records company, financial year, voucher, date, item, UQC, godown, quantity change, rate, value change, movement kind, optional source-line identity, and optional variant identity.

The posting service validates identity, a non-zero quantity, a non-negative rate and a recognized-length movement-kind string. It does not itself validate that:

- value sign agrees with quantity sign;
- `value = quantity × rate`;
- sufficient stock exists in the source godown;
- a corresponding cost layer exists;
- a retry has not already posted the same logical movement.

Those guarantees currently depend on the calling repository and database relationships.

### 2. Job Work Order

JWO is a production requirement/reference document. It does not post stock. Its component `XmlRate` is a reference snapshot used to prefill ordinary Material Out component rates.

JWO stage assignment identity and expected process-rate history are preserved separately from stock value. This is a correct separation for rate-variance reporting.

### 3. Material Out — ordinary/raw component

For a normal component:

- the pending MO line is prefilled from the JWO component `XmlRate`;
- the operator can edit the rate in the MO form;
- the server calculates `Amount = Issued Quantity × Rate`;
- the source godown receives a negative quantity and negative value;
- the jobber destination godown receives equal positive quantity and positive value.

Therefore MO is value-neutral across all godowns, but the entered MO rate is the actual value carried to the jobber and later into MI consumption. There is no query for an original purchase/opening layer before this posting.

### 4. Material Out — produced child-BOM component

For a component produced by an earlier stage, the server ignores the client-supplied rate and calculates:

`Remaining rate = (total earlier-stage receipt value − total prior onward-issue value) / (total received quantity − total prior onward-issue quantity)`

This is a pooled residual weighted-average calculation at child-stage/component level. It correctly prevents an operator from overwriting system-derived manufactured value, and the existing integration test proves this behavior. It is not FIFO by individual produced receipt layer.

### 5. Material In consumption

Ordinary MI:

- requires actual MO quantity to be available for the same JWO component and stage-assignment version;
- requires the consumption godown to equal the linked jobber destination godown;
- orders eligible MO lines by MO voucher date, sequence number and line number;
- consumes the oldest eligible MO quantities first;
- stores quantity, rate and value snapshots in `material_in_mo_allocations`;
- posts negative quantity/value from the consumption godown.

This is **oldest-MO-first allocation**, not purchase-layer FIFO. Its input cost is whatever value was recorded on the MO line.

### 6. Material In manufactured output

For each received stage output:

`Material Value = sum of current MI consumption value assigned to that stage/assignment`

`Finished Output Value = Material Value + Total Process Charge`

`Output Rate = Finished Output Value / Received Quantity`

The output is then posted into the selected receiving godown. This correctly capitalizes the currently captured direct material and job-work/conversion charge into intermediate or final output.

Expected process rate, actual process rate and charge-entry mode are separately snapshotted for variance analysis. They do not replace manufactured stock value.

### 7. Process-charge rate/total calculation

The calculation supports either:

- Per Unit: preserve entered unit rate and calculate total; or
- Total: preserve entered total and calculate unit rate.

The repository recalculates server-side before saving, which prevents trusting browser-only arithmetic. Negative inputs and unsupported modes are rejected.

### 8. Closing Stock and Stock Register

Closing Stock does not run an independent FIFO or weighted-average algorithm. It:

- selects valid stock movements up to the as-on date;
- sums quantity changes;
- sums stored value changes;
- displays `Value / Quantity` as “Weighted Average.”

This is an average presentation of persisted net values, not a configurable valuation method.

WIP is a reporting classification based on canonical movement kinds:

- MO destination and its cancellation;
- MI consumption and its cancellation.

This allows material physically held at jobber godowns to be reported as WIP without changing the stock item’s permanent stock group. Native MO posts equal source/destination values, so a transfer does not change total company inventory value.

### 9. Cancellation, deletion and alteration

- Native MO alteration is blocked when active downstream links exist.
- Native MO cancellation/deletion is blocked when an active MI consumes its lines.
- Cancellation creates opposite stock movements atomically and marks the voucher cancelled.
- Reports exclude every movement belonging to a currently cancelled voucher.
- MI alteration removes and rebuilds its postings and MO allocations inside a serializable transaction.
- Physical deletion removes the voucher and its dependent stock rows, while the separate full-audit snapshot mechanism is intended to preserve audit history.

There is no closed-period or valuation-period lock yet.

### 10. Financial years

Closing Stock queries company movements across financial-year IDs and applies an as-on date, so movement totals can carry across financial years.

However, native MO and MI require the selected JWO and new voucher to belong to the currently selected financial year. An unfinished JWO cannot currently continue naturally into the next financial year through the normal repositories. This conflicts with perpetual cross-year layer continuity and must be resolved before FIFO implementation.

### 11. Purchase, opening stock, sales and stock journal

The Purchase, Sales and Stock Journal pages are currently placeholders. No native posting service creates inward purchase layers, opening layers, sales issues, or general stock-journal adjustments.

Consequently, the current stock ledger is a production-chain ledger, not yet a complete inventory subledger. Test/demo raw material may become negative because MO can post without a prior native inward movement.

### 12. Tally XML import

The current Tally importer writes imported rate and amount directly into MO/MI rows and stock movements. Imported MI does not create the same MO-allocation lineage as the native Material In repository.

That behavior is acceptable only as a deferred/legacy adapter limitation. It does not yet satisfy the proposed “Preview → Validate → Commit with domain-validation parity” import architecture and must not be used as evidence that the native valuation engine supports imported historical layers.

## Confirmed strengths

1. Native MO and MI save inside serializable database transactions.
2. MO creates symmetric source and destination movement values.
3. Native MI consumption is tied to actual MO lines and snapshots rate/value.
4. Native MI rejects consumption beyond actual unconsumed MO quantity.
5. Native MI requires the actual jobber destination/consumption godown.
6. Manufactured output value includes actual allocated material value plus process charge.
7. Produced child components ignore an editable client rate on onward issue.
8. UQC is stored on every movement and report totals do not mix UQCs.
9. Variant quantities are linked to final-output movement rows.
10. Cancellation and alteration are transactional and downstream dependencies block unsafe native MO changes.
11. WIP movement vocabulary is centralized in `StockMovementSemantics`.
12. Closing reports apply company isolation and an as-on movement-date filter.
13. Expected and actual process rates are kept separate from output stock value.

## Findings and risks

### VAL-01 — High: no perpetual inward cost-layer model

There is no purchase/opening cost-layer table, layer-consumption table, company valuation-method setting, or layer rebuild service. The current schema cannot represent “100 units received at 10, then 50 at 12, issue the oldest 120” independently of an editable MO rate.

**Consequence:** Perpetual FIFO is not currently implemented and cannot be added safely as a report-only formula.

### VAL-02 — High: editable MO reference rate becomes book stock value

For ordinary raw components, the JWO XML rate or operator-edited MO rate becomes the source deduction, jobber WIP value and subsequent MI material cost.

**Consequence:** Changing a reference/challan rate can change manufactured inventory value even when actual purchase cost is different.

### VAL-03 — High: native inventory flow is incomplete

Purchase, opening stock, sales and stock journal are placeholders. Only MO, MI and the Tally adapter currently post movements.

**Consequence:** Closing Stock cannot yet be considered a complete company inventory valuation, and a clean database has no native way to establish purchased/opening raw-material cost.

### VAL-04 — High: negative source stock is permitted without a defined cost policy

Native MO validates the JWO pending quantity but does not validate source-godown on-hand quantity or value.

**Consequence:** Quantity and value can become negative. Later inward receipts cannot automatically repair historical issue cost because no negative-stock recosting policy exists.

### VAL-05 — High: backdated insertion does not rebuild chronological allocations

Native MI preserves actual MO allocations. A newly inserted backdated MO can be chronologically older than MO lines already consumed by an existing MI, but existing allocations are not recalculated. MI alteration also considers allocations persisted by vouchers regardless of later reporting dates.

**Consequence:** Oldest-MO-first results can depend on entry order instead of final voucher chronology. A09 closed-period/backdating rules are a prerequisite for stable FIFO.

### VAL-06 — High: cross-financial-year production continuity is inconsistent

Reports aggregate company movements across years, while native MO/MI require the JWO to be in the active financial year.

**Consequence:** outstanding WIP may report across years but cannot be completed normally in the following year. Perpetual FIFO and WIP carry-forward require an explicit cross-year workflow.

### VAL-07 — High for imported data: Tally import bypasses native valuation lineage

The adapter trusts imported amounts/rates and imported MI does not create native MO-allocation snapshots.

**Consequence:** imported and manually entered vouchers can produce different traceability and validation results. Keep the adapter deferred until it uses the same domain services or an equivalent validated posting contract.

### VAL-08 — Medium: produced-component costing is pooled residual average, not FIFO

Child-stage receipts and onward issues are aggregated by stage/component. The rate is recomputed from total remaining quantity/value.

**Consequence:** total value is preserved, but receipt-lot identity and FIFO age/cost are not. This must be replaced or deliberately retained as the manufactured-item policy.

### VAL-09 — Medium: variant movement values can accumulate rounding drift

For variant outputs, the output rate is rounded to four decimals and each variant value is then separately calculated as variant quantity × rounded rate. The sum can differ slightly from the exact finished-good line value. Non-variant output posts the exact line value.

**Consequence:** `material_in_finished_goods.finished_goods_value` may differ from the sum of its variant stock movements by small amounts.

### VAL-10 — Medium: centralized stock-posting invariants are incomplete

`StockPostingService` does not enforce value-sign consistency, rate/value arithmetic, movement-kind-specific links, or logical idempotency.

**Consequence:** a future caller can create structurally valid but financially inconsistent movement rows even if current native repositories behave correctly.

### VAL-11 — Policy decision: cancellation changes historical as-on results retroactively

Reports exclude all movements of a voucher whose current status is Cancelled. A voucher entered on 1 August and cancelled on 1 September is absent even from a report run “as on 15 August.” Reversal rows use the original movement date and are also attached to the cancelled voucher.

**Consequence:** this implements “void from inception,” not “reverse on cancellation date.” Either policy can be intentional, but it must be explicit before historical valuation is certified.

### VAL-12 — Medium: “Weighted Average” label can overstate the implemented method

The report calculates net stored value divided by net quantity. It does not maintain moving-weighted-average layers or recost outward movements using weighted average.

**Consequence:** users may assume an accounting valuation method that the engine does not currently implement. Until the method exists, the label should be understood as “Average of Persisted Net Value.”

### VAL-13 — Medium: godown classification/reconciliation layer is not implemented

Godown currently has no Internal/Third Party classification. WIP is inferred from movement kind rather than custody type.

**Consequence:** jobber custody is visible, but a formal reconciliation between third-party godown balance and Material With Job Workers cannot yet be guaranteed.

## Existing test coverage

Current tests prove:

- symmetric movement reversal and cancellation audit behavior;
- rejection of a negative movement rate;
- produced-component residual-value carry-forward and client-rate override;
- process charge total/per-unit calculation;
- BOM hierarchy and immutable stage-assignment history;
- UQC immutability after transactional use;
- godown-reference preservation and delete restrictions.

### Characterization progress — 10 September 2026

The first executable characterization tranche now proves:

- mixed-rate Material Out lots are allocated oldest-first by MO date during Material In;
- allocated material value plus process charge becomes manufactured output value;
- adding a backdated MO does not rebalance an already persisted MI allocation;
- a currently cancelled voucher is excluded even from an as-on report dated before cancellation;
- Closing Stock aggregates movements across financial-year IDs; and
- the central posting service permits an outward movement without prior stock.

These tests preserve the current behavior; they do not approve it as the target FIFO,
backdating, cancellation, cross-year, or negative-stock policy.

## Required characterization tests before redesign

1. Two inward cost layers with one partial issue.
2. Mixed rates across multiple MO issues and multiple partial MI consumptions.
3. Backdated MO inserted before already allocated MI.
4. Backdated MI inserted before a later MI.
5. Cancellation before and after an as-on date.
6. MO with insufficient source-godown quantity.
7. Negative stock followed by a later inward receipt.
8. JWO open at financial-year end and completed in the next year.
9. Produced child component with multiple receipts, rates and assignment versions.
10. Variant output where rate rounding creates a residual value.
11. Alteration/reversal after downstream value has been capitalized.
12. Native-entry versus Tally-import parity for the same voucher chain.
13. Jobber-godown balance versus Material With Job Workers reconciliation.
14. Company isolation for every new cost-layer and analytical query.

These should initially document current behavior. Expected behavior should change only after the policy decision is approved.

## Decision gates before perpetual FIFO

Management/domain approval is required for:

1. Official book valuation method per company.
2. Treatment of negative stock: block, provisional cost, or retrospective recosting.
3. Cost-layer identity dimensions: company, item, variant, UQC, godown, ownership and inward source.
4. Whether godown transfers preserve the original layer identity.
5. Whether jobber WIP remains the same material layer in a third-party godown.
6. Manufactured output allocation when one MI produces multiple outputs.
7. Rounding-residual handling.
8. Backdated transaction and closed-period policy.
9. Cancellation policy: void from inception or dated reversal.
10. Cross-financial-year JWO/WIP continuity.
11. Historical migration start date and opening-layer creation.
12. Whether analytical report methods are available only after a reliable layer-history cutoff.

## Recommended next sequence

1. Review and accept this current-state description.
2. Add behavior-preserving characterization tests for the current engine.
3. Decide A09 backdating, cancellation and closed-period rules.
4. Decide negative-stock and cross-FY policies.
5. Define the cost-layer contract and migration/backfill strategy.
6. Implement a focused stock-position query service.
7. Add Internal/Third Party godown classification and reconciliation.
8. Implement the approved book valuation method.
9. Move MO rate to a clearly labelled operational/reference role while stock value comes from layers.
10. Add analytical report valuation views without rewriting transactions.
11. Complete A11 retry/idempotency before enabling bulk imports.

## Final assessment

TexTrack's present production-cost flow is more advanced than a simple quantity tracker: it preserves actual MO-to-MI issue allocation, manufactured material value, process charge and onward produced-component value. Those foundations should be retained.

The system should not yet advertise perpetual FIFO, general weighted-average valuation, complete closing-stock valuation, or purchase-layer costing. The correct next step is characterization and policy closure—not immediate schema redesign.
