# TexTrack ERP — FIFO Phase 1 Design and Migration Contract

**Date:** 11 September 2026  
**Baseline:** `3e47cda13d903b28cb05f19d9ba163f28280c679`  
**Status:** Design contract and migration plan only. No schema or posting behavior is changed by this document.  
**Preceded by:** `INVENTORY_VALUATION_CURRENT_STATE_AUDIT_2026-09-09.md`, `A09_INVENTORY_PERIOD_AND_CHRONOLOGY_POLICY_2026-09-10.md`, `NEGATIVE_STOCK_POLICY_IMPLEMENTATION_2026-09-11.md`, and `WEBVELLA_PRE_FIFO_ARCHITECTURE_AUDIT_2026-09-11.md`.

## Executive decision

TexTrack will implement **perpetual FIFO from immutable inward cost layers**. Quantity custody and book valuation will remain connected but distinct:

- `stock_movements` records where and when quantity moved;
- cost layers record the value carried by inward quantity at an exact stock position;
- cost allocations record which layers funded each outward quantity;
- transfers preserve each source cost tranche instead of replacing it with an average rate; and
- manufactured output receives the actual allocated material value plus actual process/conversion charges.

Phase 1 is deliberately staged. The first migration will be additive and dormant. It will not reinterpret historical vouchers, silently activate FIFO, or change any report. A company may enter FIFO mode only after reconciliation proves that its opening layers match its physical stock and approved value.

## Goals

1. Preserve the original accepted cost of Purchase and Opening Stock quantities.
2. Deduct the oldest available cost layers first for every outward movement.
3. Preserve layer lineage through internal-to-jobber and jobber-to-internal transfers.
4. Carry actual material cost through child-BOM, WIP and final production stages.
5. Keep layers continuous across financial years.
6. Make as-on valuation deterministic, reproducible and auditable.
7. Keep editable/reference voucher rates separate from authoritative inventory cost.
8. Provide a safe, explicit conversion route for an existing production database.
9. Prevent UI, imports, plugins or retry paths from bypassing the same posting rules.

## Explicit non-goals for Phase 1

- provisional costing of negative stock;
- arbitrary backdated replay after later affected movements exist;
- weighted-average or other alternative book methods;
- sales/COGS accounting, general ledger posting, GST or landed-cost allocation;
- scrap, wastage, by-product, rejection or rework valuation;
- Tally import/export redesign;
- a general distributed background-job platform; and
- UI redesign beyond controls needed to explain valuation state.

These are not rejected features. They are excluded so the production module can be frozen around a trustworthy FIFO core.

## Terms

### Exact stock position

The smallest identity against which physical availability and FIFO eligibility are evaluated:

`Company + Stock Item + Variant/Base + UQC + Godown`

`StockItemVariantId = NULL` is a valid base-item identity and must never mix with a non-null variant. Positive stock in another godown, UQC or variant cannot fund an outward movement.

### Effective date and valuation order

The business date on a voucher is the effective stock date. Deterministic order is:

1. `EffectiveDate`;
2. optional `EffectiveTime` supplied by an approved integration;
3. immutable company-wide `PostingOrder`; and
4. `MovementLineOrder` and `LayerSequence`.

Voucher type sequence and displayed voucher number are not valuation order. Renumbering or changing a reference number cannot alter FIFO order.

### Origin and manufactured layers

An origin layer is created when quantity enters an exact position without being a custody transfer from another layer. Phase 1 origins are:

- accepted Purchase inward (external root);
- approved Stock Item Opening Inventory (external root); and
- manufactured output whose value is derived from consumed component layers plus process charge.

Manufactured output is not an external root cost. It is a new derived layer and retains component-allocation lineage through its production voucher.

### Transfer child layer

A layer created at a destination godown from one exact source-layer allocation. It keeps the source tranche's quantity and value. One aggregate Material Out destination movement may therefore own several child layers at different unit costs.

### Allocation

An immutable quantity/value relationship between one outward stock movement and one eligible source layer. Its value is derived by the engine, not accepted from the browser.

## Current model versus target authority

| Concern | Current authority | FIFO target authority |
|---|---|---|
| Physical quantity | `stock_movements.quantity_change` | Unchanged: stock movements remain the quantity ledger. |
| Purchase/opening value | Accepted line rate/amount copied to movement | Root layer quantity/value created from the accepted transaction. |
| Ordinary MO value | Editable/reference MO rate | Sum of FIFO allocations from source-godown layers. |
| Jobber WIP value | One aggregate MO destination rate/value | Transfer child layers preserving every consumed source tranche. |
| MI material consumption | Oldest eligible MO line snapshot | FIFO allocations against layers actually present in the consumption godown, cross-checked with MO/JWO lineage. |
| Manufactured output | Allocated MO value plus process charge | FIFO material allocations plus actual process charge, distributed exactly to output variants. |
| Purchase Return value | Entered commercial rate/amount | FIFO book cost from source layers; entered amount remains commercial/reference data. |
| Closing Stock value | Sum of persisted movement values | Sum of remaining current FIFO layer values as on the requested date. |

During transition, current movement values remain the active book presentation. FIFO becomes authoritative only for a company whose valuation state is `Ready` and method is explicitly activated.

## Required posting model

### Inventory posting header

Every stock-affecting operation needs one durable header, logically named `inventory_postings`:

| Column | Contract |
|---|---|
| `id` | Immutable primary key. |
| `operation_id` | UUID, unique; the idempotency/correlation identity for the logical save. |
| `company_id` | Required and immutable. |
| `financial_year_id` | FY of the posting voucher; reporting/numbering only, not a layer boundary. |
| `voucher_id` | Required for native postings; restricted from deletion after a layered posting exists. |
| `effective_date` | Required business date. |
| `effective_time` | Nullable integration-provided business time. Manual entry leaves it null. |
| `posting_order` | Required monotonic number unique within company, reserved inside the transaction. |
| `event_kind` | `Original`, `Reversal`, `Cutover` or future reviewed kind. |
| `reversal_of_posting_id` | Required only for a reversal; unique so one posting cannot be reversed twice. |
| `algorithm_version` | Valuation algorithm that produced allocations. |
| `created_at_utc`, `created_by` | Immutable audit metadata. |

The company posting sequence must use a database row lock/upsert inside the posting transaction. It must not use voucher numbers or application-memory counters.

Each layered `stock_movement` receives a required `inventory_posting_id` and positive `movement_line_order`. The existing movement ID remains its physical row identity; it is not used as the business tie-breaker for new postings.

### FIFO cost layer

The logical `inventory_cost_layers` table requires:

| Column | Contract |
|---|---|
| `id` | Immutable primary key. |
| `company_id` | Required; must equal posting/movement company. |
| `receipt_movement_id` | Positive/inward stock movement that created this layer. |
| `layer_sequence` | Positive order within the receipt movement. |
| `stock_item_id`, `stock_item_variant_id`, `uqc_id`, `godown_id` | Exact position copied from the receipt movement. |
| `origin_kind` | `Purchase`, `Opening`, `Manufactured`, `Transfer`, `Reversal` or `CutoverSynthetic`. |
| `source_allocation_id` | Nullable and unique. Set for a transfer/reversal child layer created from an earlier allocation. |
| `original_quantity` | Positive `numeric(19,4)`. |
| `original_value` | Non-negative `numeric(19,4)`. |
| `unit_cost` | Non-negative `numeric(28,8)` used for partial-allocation calculation. |
| `remaining_quantity` | Mutable transactionally; between zero and original quantity. |
| `remaining_value` | Mutable transactionally; between zero and original value. |
| `effective_date`, `posting_order`, `movement_line_order` | Denormalized immutable FIFO sort key. |
| `algorithm_version` | Required. |
| `created_at_utc`, `created_by` | Immutable audit metadata. |

Multiple cost layers may reference one receipt movement. This is necessary when one Material Out transfer consumes several differently priced source layers and produces a single aggregate destination movement.

Required uniqueness: `(receipt_movement_id, layer_sequence)`. FIFO lookup index: exact position followed by `effective_date, posting_order, movement_line_order, layer_sequence, id`, filtered to `remaining_quantity > 0`.

### FIFO allocation

The logical `inventory_cost_allocations` table requires:

| Column | Contract |
|---|---|
| `id` | Immutable primary key. |
| `operation_id` | Posting/replay correlation UUID. |
| `company_id` | Required; must match movement and layer. |
| `outward_movement_id` | Negative/outward stock movement being valued. |
| `source_layer_id` | Layer consumed at the exact same position. |
| `allocation_sequence` | Positive order within the outward movement. |
| `allocated_quantity` | Positive `numeric(19,4)`. |
| `allocated_value` | Non-negative `numeric(19,4)`. |
| `unit_cost_snapshot` | `numeric(28,8)` copied from source layer. |
| `reversal_of_allocation_id` | Nullable; identifies exact-cost restoration/reversal lineage. |
| `created_at_utc`, `created_by`, `algorithm_version` | Immutable evidence. |

Required uniqueness: `(outward_movement_id, allocation_sequence)` and `(outward_movement_id, source_layer_id)` for one algorithm run. A source layer can fund many different outward movements, but only up to its original quantity/value.

The outward movement's absolute quantity and value must equal the sums of its active allocations. A transfer child layer's quantity/value must equal its source allocation.

### Valuation position state

The logical `inventory_valuation_positions` table stores durable readiness rather than stock quantity:

- exact stock-position key;
- state: `Ready`, `PendingReplay`, `Failed` or `Legacy`;
- earliest dirty effective date/order;
- current successful run ID;
- last error summary safe for display; and
- concurrency token/update metadata.

Uniqueness must normalize nullable variant identity using a reviewed expression/index so NULL base positions cannot duplicate.

### Company valuation setting

The logical `inventory_valuation_settings` table is one row per company and contains:

- book method: `LegacyPersistedValue` or `FIFO`;
- lifecycle state: `NotInitialized`, `Building`, `Ready`, `PendingReplay` or `Failed`;
- cutover date and cutover run ID;
- active algorithm version;
- activation timestamp, actor and reason;
- last successful reconciliation run ID; and
- concurrency token/update metadata.

Migration seeds every existing and new company as `LegacyPersistedValue / NotInitialized` until the FIFO posting implementation is ready. No UI setting can mark a company `FIFO / Ready` without the server-side activation preflight.

### Valuation run envelope

The logical `inventory_valuation_runs` table requires:

- immutable run UUID and unique idempotency key;
- company and run kind (`InitialBuild`, `Cutover`, `Replay`, `Reconciliation`);
- algorithm/schema version;
- requested scope and cutoff as structured JSON with a schema version;
- `Pending`, `Running`, `Succeeded`, `Failed` or `Cancelled` state;
- requested/started/finished timestamps and actors;
- attempt count;
- optional worker lease owner, lease expiry and heartbeat;
- earliest affected valuation order;
- affected position/movement/layer counts;
- quantity and value reconciliation totals before/after;
- safe failure summary and detailed diagnostic reference; and
- audit correlation UUID.

Phase 1 may execute synchronously, but still records this envelope. Leases and database-safe claiming become mandatory before a run can execute outside the initiating process.

## Voucher-by-voucher valuation contract

### Purchase

1. Accepted Purchase quantity creates a positive stock movement.
2. That movement creates one root cost layer per exact line/variant/godown position.
3. Layer value is the accepted inventory transaction amount. Phase 1 continues the current `quantity × accepted rate`, rounded to four decimals.
4. Last Purchase Rate remains a UI prefill only; the final accepted rate becomes the inward layer cost.
5. Future taxes, freight and landed cost are excluded until an approved allocation design exists.
6. A Purchase Order remains planning/reference only and creates no layer.

### Stock Item Opening Inventory

1. Opening quantity/value is entered through the Stock Item master workflow already adopted by TexTrack.
2. Each exact position creates a root layer with the approved opening quantity and rate/value.
3. Opening must precede later outward movement for that position or be blocked by the temporary chronology rule.
4. Changing used opening inventory after downstream allocation is prohibited. Correction uses controlled reversal/re-entry in an open period.

### Material Out to jobber

For every MO component line:

1. The source negative movement consumes the oldest eligible source-godown layers.
2. The entered/JWO/MO rate remains a reference snapshot and cannot determine book cost.
3. Allocation values determine the source movement's negative value.
4. The paired destination positive movement creates one transfer child layer for every source allocation, preserving quantity, value and unit-cost snapshot at the Jobber Destination Godown.
5. Total source value must equal total destination value exactly; MO cannot create or destroy company inventory value.
6. Source and destination may not be the same exact godown position.

Example: 60 MTS at ₹100 and 40 MTS at ₹120 are issued together. The MO can display 100 MTS and ₹10,800 total, but the jobber godown receives two layers—60/₹6,000 and 40/₹4,800—not one 100/₹108 average layer.

### Material In consumption

1. Consumption allocates FIFO from layers physically present in the selected Consumption Godown.
2. JWO component, stage assignment and existing MO-line allocation rules remain quantity/dependency controls.
3. Cost-layer allocations become the authority for value.
4. The sum of `material_in_mo_allocations` quantity must equal the cost allocation quantity for the consumption line, even though one MO line can contain multiple cost layers.
5. Material physically held at another jobber/internal godown cannot be consumed.

### Intermediate/final output

For each received stage output:

`Finished Output Value = Actual Allocated Material Value + Actual Process Charge`

1. Expected process rate remains comparison metadata.
2. Actual per-unit/total process-charge fields remain mutually calculated after receipt quantity is entered.
3. Each output variant receives quantity and value proportionally by received quantity unless a future approved variant-specific cost driver exists.
4. Four-decimal residual value is assigned to the final non-zero variant in stable variant order so the variant-layer values equal the line total exactly.
5. Each variant produces its own derived manufactured layer at the actual receiving godown.
6. Child-stage output layers can later be transferred onward by ordinary FIFO allocation; the current pooled residual produced-component average is retired only after this path is proven.

### Purchase Return

1. Purchase Return is an outward movement and consumes FIFO layers at its selected godown.
2. Because TexTrack intentionally permits a standalone return without linking one original Purchase, it consumes the oldest available layers, not the supplier invoice named in free text.
3. Entered return rate/amount remains the commercial/reference amount.
4. Inventory book value is the cost of the FIFO layers returned and may differ from the commercial credit amount.
5. That variance must remain visible for future accounting integration; it must not be hidden by overwriting either amount.

### Future Sales and Stock Journal

- Sales outward will consume FIFO layers and expose COGS separately from sale price.
- Stock transfer will behave like MO custody transfer without production lineage.
- Positive adjustment creates an explicitly approved root layer; negative adjustment consumes FIFO.
- UQC conversion, repacking and manufacturing journals require separate reviewed transformations and are not implied by this contract.

## Cross-financial-year contract

Cost layers are company inventory history and do not reset on 31 March.

- The source voucher and movement retain their own FY.
- An open prior-year layer can be consumed by a current-year voucher.
- JWO/MO/MI lookup must eventually allow a valid open production chain to continue across FY inside the same company.
- A closed prior year blocks mutation of its posting; it does not block a current-year receipt, issue or reversal.
- Reports can group by FY, but FIFO eligibility never partitions layers by FY.

No year-end layer cloning, zeroing or artificial transfer is permitted.

## Cancellation, alteration and deletion

The target model is event-based:

1. A layered posted voucher is never physically deleted from economic history.
2. Cancellation creates a new linked posting on the actual cancellation date in an open period.
3. Reversal cost follows the original allocation lineage; it does not take whatever FIFO layer happens to be oldest on the cancellation date.
4. Reversing an outward movement creates inward restoration layers at the reversal date with the original allocated tranche values.
5. Reversing an inward movement consumes its remaining descendant layer quantities at exact cost and is blocked when later active allocations depend on them.
6. A transfer reversal consumes the destination descendants first and restores corresponding source-godown layers.
7. Alteration of a posted layered voucher is implemented as reversal plus replacement, each with its own posting/order/audit identity.
8. Physical deletion remains limited to an unposted draft that never created a posting, movement, layer or allocation.

This intentionally replaces the current transitional behavior that removes/rebuilds movements during alteration and can treat cancellation as void from inception in reports. That behavior remains unchanged until the dated-reversal phase is implemented and tested.

## Backdating and replay

Phase 1 retains the safe temporary A09 rule:

> A backdated stock operation is allowed only when no later affected movement exists for any touched position or dependent production output.

If later effects exist, save is rejected with the earliest conflicting voucher/date. Phase 1 does not silently rebuild history.

Future replay must:

- run under one durable valuation-run identity;
- lock affected positions in deterministic order;
- rebuild from the earliest affected order through every dependent transfer/production output;
- retain before/after reconciliation and audit evidence;
- never change a closed period without a controlled reopen; and
- mark affected positions `PendingReplay` so reports cannot claim stale values are final.

## Negative-stock policy interaction

The existing company switch remains valid, but its meaning must stay honest:

- `Block Negative Stock` is the required mode for initial FIFO activation.
- The company must have no historical negative position at the cutover boundary.
- `Allow Negative Stock` may continue under legacy persisted-value mode.
- Final FIFO cannot value an outward quantity for which no layer exists.

Provisional negative-layer costing and later recosting are explicitly Phase 2. Until that engine exists, TexTrack must not activate FIFO book mode for a company that requires new negative postings. It may retain operational legacy mode, or the administrator can reconcile stock and enable blocking before FIFO cutover.

This restriction does not remove the user's negative-stock option. It prevents a compatibility switch from being presented as a complete FIFO valuation method.

## Precision and rounding

1. Quantities and posted values remain `numeric(19,4)` to match the current ledger.
2. Internal unit cost uses `numeric(28,8)`.
3. Partial allocation value is `round(quantity × unit_cost, 4)`.
4. The final allocation exhausting a layer receives the layer's exact remaining value, eliminating accumulated rounding residue.
5. .NET uses `MidpointRounding.AwayFromZero`; PostgreSQL numeric rounding and tests must be verified to produce the same stored result.
6. No UQC conversion is implicit. A layer is eligible only for the exact UQC.
7. Every transfer and production distribution has an exact value-conservation assertion after rounding.

## Database-enforced invariants

Application validation is necessary but not sufficient. The implementation migration must add constraints/triggers that reject:

1. cross-company posting/movement/layer/allocation references;
2. mismatched item, variant/base, UQC or godown between allocation and source layer;
3. an allocation attached to a non-outward movement;
4. a layer attached to a non-inward movement;
5. non-positive quantities or negative costs/values;
6. remaining quantity/value outside original bounds;
7. allocation beyond a layer's remaining quantity/value;
8. transfer child layer quantity/value different from its source allocation;
9. duplicate reversal of one posting/allocation;
10. layered posting deletion or mutation of immutable order/identity fields;
11. movement value sign inconsistent with quantity direction; and
12. activated-company movement lacking its required posting/layer/allocation state.

Complex aggregate invariants are checked by the transaction service and reconciliation procedure as well as targeted database triggers. A trigger must not perform unlocked FIFO selection; layer selection belongs in the posting service under deterministic locks.

## Service boundaries

Recommended server-side interfaces:

- `IInventoryPostingCoordinator`: owns operation identity, transaction, posting order, audit correlation and commit.
- `IFifoLayerAllocator`: selects/locks layers and returns exact allocations.
- `ICostLayerFactory`: creates root, transfer and restoration layers.
- `IManufacturedCostService`: combines stage material allocations and process charge, then distributes exact output value.
- `IInventoryChronologyGuard`: period, later-movement and dependency checks.
- `IInventoryValuationReconciler`: proves movements, layers, allocations and reports agree.
- `IInventoryValuationRunService`: durable build/cutover/replay envelope.

Only the coordinator may make valuation-authoritative writes. Razor pages, XML/Excel/CSV imports, APIs and future plugins submit typed commands to it. `StockPostingService` remains the physical-position enforcement boundary and is evolved/composed rather than bypassed.

## Safe migration plan for an existing database

Migration filenames are **not reserved in this document**. Implementation must use the next available number after all concurrent Purchase/UI work is integrated.

### Migration A — additive dormant foundation

- Create posting sequence/header, valuation settings, layers, allocations, position states and run-envelope tables.
- Add nullable posting/order references to existing movement/audit structures.
- Add indexes and constraints that apply only to layered rows.
- Seed every existing company as `LegacyPersistedValue / NotInitialized`.
- Do not backfill layers, update movement values or change report queries.
- Record the migration choice in `audit_logs`.

This migration is safe for historical vouchers because it is additive and FIFO remains inactive.

### Migration B — deterministic legacy inventory census

A read-only readiness service calculates, per exact position and as-on cutoff:

- movement quantity/value;
- earliest/latest movement;
- negative-history intervals;
- missing/mismatched variant or UQC identity;
- cancellation/void behavior affecting history;
- unlinked/imported movements;
- existing MO/MI lineage mismatches; and
- candidate opening quantity/value.

It writes a versioned reconciliation result, not layers. Any mismatch blocks activation.

### Migration/operation C — explicit company conversion

An administrator chooses one reviewed path:

1. **Full replay:** only where complete inward history and chronology can reconstruct every layer with no unresolved negative interval.
2. **Cutover opening:** freeze through an approved date and supply/approve opening layer details for each exact position.

If detailed legacy lot costs are unavailable, TexTrack may create one clearly marked synthetic cutover layer per exact position using an approved quantity and total book value. That provides correct prospective FIFO from the cutoff but must be labelled `CutoverSynthetic`; the system must not claim historical pre-cutoff FIFO accuracy.

Conversion runs in a serializable transaction or a controlled maintenance window, reconciles totals, records the valuation run and produces an immutable audit summary.

### Phase D — shadow/dual calculation

Before cutover, eligible new postings write layers in shadow mode while existing reports continue using current movement values. For every voucher, tests/reconciliation compare:

- physical movement totals;
- layer/allocation quantities;
- value conservation;
- legacy persisted value versus proposed FIFO value; and
- downstream WIP/manufactured output values.

Differences are expected where editable reference rates diverge from real cost, but every difference must be explainable by layer evidence.

### Phase E — per-company activation

Activation requires:

- inventory period frozen through the approved cutoff;
- Block Negative Stock mode;
- zero negative positions at cutoff;
- no failed/pending valuation position;
- complete reconciliation with signed-off quantity/value totals;
- tested backup and restore point;
- no unsupported import path enabled; and
- authorized actor, reason, algorithm version and audit event.

Activation changes one company at a time. It does not globally switch every database company.

### Phase F — enforcement

After activation:

- all authoritative postings require layer/allocation parity;
- reports use FIFO layer values;
- direct legacy writes are rejected;
- imports without the domain coordinator are rejected;
- posted layered vouchers cannot use physical-delete/rebuild behavior; and
- downgrade to legacy mode is prohibited except through a separately designed recovery rollback using a restored backup.

## Reconciliation equations

For each exact position and cutoff:

`Movement Quantity = Sum(Inward Quantity) − Sum(Outward Quantity)`

`Layer Quantity = Sum(Remaining Layer Quantity)`

`Movement Quantity = Layer Quantity`

For every inward movement:

`Positive Movement Quantity = Sum(Original Quantity of owned layers)`

`Positive Movement Value = Sum(Original Value of owned layers)`

For every outward movement:

`Absolute Movement Quantity = Sum(Allocated Quantity)`

`Absolute Movement Value = Sum(Allocated Value)`

For every layer:

`Original Quantity = Remaining Quantity + Sum(Allocated Quantity)`

`Original Value = Remaining Value + Sum(Allocated Value)`

For every MO transfer pair:

`Source Absolute Quantity/Value = Destination Quantity/Value = Transfer Child Layer Quantity/Value`

For every MI output line:

`Output Layer Value = Allocated Material Value + Actual Process Charge`

All equations must hold by company, exact position, voucher and full database. A report is not final while its affected position is not `Ready`.

## Acceptance test contract

### Layer creation and FIFO

1. Purchase 100 at ₹10 and 50 at ₹12; issue 120 consumes 100/₹1,000 plus 20/₹240.
2. The remaining layer is 30/₹360.
3. Same-day receipts are ordered by immutable posting order, not voucher type/number.
4. Different variant, UQC, godown or company is never selected.
5. A final partial allocation absorbs exact four-decimal residual value.

### Transfers and production

6. One MO consuming two costs creates two jobber child layers with unchanged tranche costs.
7. Company total value is unchanged by MO transfer.
8. MI consumes only layers at its actual Consumption Godown.
9. MO-line quantity lineage and cost-layer quantity agree.
10. Child-stage receipt creates manufactured layers from actual material plus actual process charge.
11. Onward child-component issue consumes those manufactured layers FIFO, not a pooled editable rate.
12. Variant output values add exactly to the finished line value.

### Purchase Return and reference rates

13. Purchase Return book cost comes from FIFO even when entered supplier-credit rate differs.
14. Changing an MO reference rate does not change FIFO movement or manufactured cost.
15. Last Purchase Rate prefill has no effect until the accepted Purchase is saved.

### Period, cross-year and history

16. Prior-year open layer is consumable by a current-year voucher in the same company.
17. Closed-period layer remains immutable.
18. Backdating with later affected movement is blocked before replay exists.
19. Displayed voucher renumbering cannot change valuation order.
20. As-on report before a dated reversal includes the original; after reversal it includes both net effects.

### Concurrency and idempotency

21. Two simultaneous issues cannot allocate the same remaining layer quantity.
22. Locks are taken in deterministic position/layer order to avoid deadlocks.
23. Retrying the same operation UUID returns the original result and creates no duplicate movement/layer/allocation.
24. A different operation UUID for a duplicate logical voucher is stopped by voucher/business uniqueness rules.
25. Failed transaction leaves no posting, movement, allocation, layer mutation or audit success event.

### Migration/cutover

26. Additive migration changes no pre-existing voucher/movement values.
27. Every existing company remains legacy until explicit activation.
28. Reconciliation mismatch blocks conversion.
29. Synthetic cutover layers are labelled and pre-cutoff FIFO is reported unavailable.
30. Backup/restore preserves posting order, layers, allocations, sequence state and run evidence.

### Authorization/import parity

31. Company isolation applies to every layer query and lock.
32. Unauthorized user cannot build, activate, replay or inspect another company's valuation.
33. Native entry, Tally/XML, Excel/CSV and API routes cannot bypass the coordinator.
34. Activated company rejects any stock movement without complete valuation evidence.

## Implementation sequence after approval

1. Add failing schema/invariant tests for the dormant foundation.
2. Implement the additive migration and entity mappings only.
3. Implement posting-order allocation and idempotent posting header.
4. Implement layer allocator/factory and reconciliation service.
5. Integrate Purchase and Opening Stock in shadow mode.
6. Integrate MO transfer and MI consumption/output in shadow mode.
7. Integrate Purchase Return book-cost allocation.
8. Add company readiness/cutover service and administrator evidence screen.
9. Compare shadow results against current values on clean and restored databases.
10. Implement dated reversal and remove physical rewrite behavior for layered vouchers.
11. Enable FIFO reports and per-company activation only after all acceptance tests pass.
12. Add import parity, CI, recovery and concurrency/load evidence before beta deployment.

## Decisions frozen by this contract

- Perpetual FIFO is the authoritative target book method.
- Layers are exact-position and cross-FY.
- Transfers preserve cost tranches by child layers.
- Editable/reference rates do not determine FIFO book cost.
- Manufactured cost is actual allocated material plus actual process charge.
- Purchase Return commercial amount and inventory cost may differ.
- Valuation order is immutable and independent of voucher numbering.
- Existing companies are not silently converted.
- Backdating remains blocked when later affected movement exists until replay is proven.
- FIFO activation initially requires negative-stock blocking and clean reconciliation.
- Every posting route uses one server-side coordinator and idempotent operation identity.

## Deferred decisions requiring their own design

- landed-cost components and allocation basis;
- sales/COGS and accounting-ledger integration;
- provisional negative-stock valuation and retrospective recosting;
- scrap, wastage, by-product and rework cost splitting;
- alternate book methods and change-of-method accounting;
- UQC conversion/repacking;
- distributed replay workers; and
- historical Tally import mapping into authoritative opening layers.

This contract gives TexTrack a safe FIFO foundation without pretending that current persisted rates are historical cost layers and without forcing an irreversible reinterpretation of existing vouchers.
