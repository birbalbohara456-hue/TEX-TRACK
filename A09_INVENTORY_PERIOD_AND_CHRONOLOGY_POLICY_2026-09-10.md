# TexTrack ERP — A09 Inventory Period and Chronology Policy

**Date:** 10 September 2026

**Status:** Recommended implementation contract; no schema or production behavior changed by this document.

**Depends on:** `INVENTORY_VALUATION_CURRENT_STATE_AUDIT_2026-09-09.md` and its executable characterization tests.

## Decision summary

TexTrack should adopt the following policies before perpetual FIFO is implemented:

1. **Closed periods:** company-level stock freeze through an inclusive date. No stock- or value-affecting create, alter, delete, import, or backdated cancellation may affect that date or an earlier date.
2. **Backdating:** before a replay engine exists, reject a backdated change when later affected stock movements already exist. After replay exists, allow it only inside an open period and revalue every later affected movement deterministically.
3. **Cancellation:** never erase a posted stock voucher from economic history. Preserve the original and post a linked reversal on the actual cancellation date in an open period.
4. **Negative stock:** block it by default at the exact company + item + variant + UQC + godown position. Do not invent provisional FIFO cost.
5. **Cross-financial-year production:** a JWO may remain open across years. Each MO/MI belongs to the financial year of its own voucher date while retaining the immutable link to the original JWO.
6. **As-on reports:** derive history from effective-dated movements and reversals, not from the voucher's current status alone.

These choices prioritize historical truth, deterministic valuation, and auditability over convenience. They do not require redesigning JWO as one order for multiple jobbers.

## Why this policy fits TexTrack

TexTrack is an inventory-focused production system whose strongest integrity property is explicit JWO → MO → MI lineage. Perpetual FIFO adds a second chronological dependency: the value of a later outward movement can depend on every earlier inward layer.

Consequently, allowing unrestricted backdating, retroactive cancellation, or negative stock would make an apparently local edit change later WIP, intermediate-component cost, finished-goods cost, Closing Stock, and jobber reconciliation. The policy must therefore be decided before cost-layer tables or algorithms are designed.

Large ERP systems use the same broad controls:

- Odoo documents lock dates that prevent posting or modifying entries on or before the lock date, with separately logged exceptions and an irreversible hard-lock option: <https://www.odoo.com/documentation/19.0/applications/finance/accounting/reporting/year_end.html>
- ERPNext provides `Stock Frozen Upto`, an allowed override role, negative-stock configuration, and restrictions for serial/batch stock: <https://docs.frappe.io/erpnext/stock-settings>
- ERPNext's immutable-ledger guidance describes controlled backdated valuation reposting rather than silently leaving later valuation unchanged: <https://docs.frappe.io/erpnext/immutable-ledger-in-erpnext>
- Odoo reverses an inventory adjustment by adding a new movement instead of removing the historical movement: <https://www.odoo.com/documentation/19.0/applications/inventory_and_mrp/inventory/warehouses_storage/inventory_management/count_products.html>

TexTrack should use these as control patterns, not copy their user interface or accounting model blindly.

## Definitions

### Business/effective date

The date on which a voucher economically affects stock and valuation. For current vouchers this is `VoucherDate`/`MovementDate`.

### Recorded timestamp

The immutable UTC timestamp showing when TexTrack actually accepted the operation. This is not a substitute for the business date.

### Open period

A date later than the company's inclusive stock-freeze date and inside a configured financial year.

### Closed period

Any date on or before the stock-freeze date. Reports for a closed period must remain reproducible.

### Later affected movement

A valid movement after the proposed effective date whose quantity, source availability, FIFO layer allocation, WIP value, manufactured output value, or downstream consumption could change if the proposed operation were inserted, altered, reversed, or removed.

### Valuation position

The smallest stock identity against which availability and layers are checked:

`Company + Stock Item + Variant (or base) + UQC + Godown + ownership/custody dimension`

The ownership/custody dimension is reserved for the approved Internal/Third Party godown architecture.

## Policy A09-1 — Closed inventory periods

### Rule

Maintain one company-level **Stock Frozen Through** date. The boundary is inclusive.

If it is 31-Aug-2026:

- 31-Aug-2026 and earlier are closed;
- 01-Sep-2026 and later are potentially open;
- a user may not create, alter, delete, cancel effective-dated, or import a stock-affecting voucher into August;
- reports as on 31-Aug-2026 must not change unless the period is formally reopened.

### Covered operations

The rule applies server-side to:

- Material Out and Material In;
- future Purchase, Purchase Return, Sales, Sales Return and Stock Journal;
- opening/migration stock;
- inventory adjustment, scrap and future by-product movements;
- Tally/XML/Excel/CSV/API imports;
- cancellation and reversal effective dates;
- batch operations and retry/recovery handlers.

JWO itself does not post stock. A JWO may remain open, but a linked field that would rewrite an already posted or closed-period stock dependency must still be protected by the dependency rules.

### Reopening

Do not implement a permanent routine bypass. A controlled reopen must require:

1. a dedicated high-privilege policy;
2. an explicit start/end time;
3. the earliest date being reopened;
4. a mandatory reason;
5. authenticated actor and UTC timestamps;
6. an immutable audit event before and after the exception;
7. automatic expiry; and
8. revalidation/revaluation completion before the period is locked again.

The Developer role must not silently bypass a production company's lock. Developer diagnostics should operate on a restored copy or through the same audited exception.

### User experience

TexTrack must reject the operation and state:

- the proposed voucher date;
- the freeze-through date;
- the earliest allowed date; and
- that an authorized period-reopen workflow is required.

It must **not silently replace the user's date** with the next open date.

## Policy A09-2 — Backdated stock operations

### Launch rule before valuation replay exists

Allow a backdated voucher in an open period only when it has no later affected movement.

If later affected movements exist, reject the save and list the earliest conflicting voucher/date. The correction choices are:

- enter the missing document before later movements are posted;
- cancel/reverse dependent vouchers in reverse dependency order, correct the chronology, then repost; or
- post an approved current-date adjustment when the original period must remain unchanged.

This strict rule is temporary but safe. It prevents the current persisted MI allocations from becoming entry-order dependent.

### Target rule after controlled replay exists

A permitted backdated operation must:

1. validate that the effective date is in an open period;
2. determine every affected valuation position and downstream production output;
3. obtain database locks that prevent concurrent postings from changing those positions;
4. replay quantity and valuation chronologically from the earliest affected point;
5. update derived allocations through WIP, intermediate outputs and final goods;
6. preserve the original and revised audit snapshots;
7. finish atomically, or mark the affected positions as revaluation-pending and prevent unreliable reports/postings until completion;
8. produce a revaluation result showing changed vouchers and old/new values; and
9. leave already closed periods unchanged.

### Deterministic same-day ordering

Voucher type sequence numbers are not a sufficient global order because each voucher type has its own sequence.

FIFO requires an immutable valuation order composed of:

1. effective business date;
2. optional effective business time when supplied by an integration;
3. an immutable company-wide posting order/tie-breaker; and
4. movement-line order.

Changing a voucher number must never change valuation order.

### Existing behavior to replace deliberately

Current MI allocations are not retroactively rebalanced after a backdated MO is inserted. The characterization test now freezes that behavior so the future replay change must be explicit and reviewed.

## Policy A09-3 — Cancellation and correction

### Rule

A posted stock voucher is never economically removed after it has become part of history.

Cancellation must:

- retain the original voucher and movements;
- record who cancelled it, when, and why;
- create a linked reversal document/movement set;
- use the actual cancellation business date in an open period;
- reverse quantities, layer consumption and values exactly; and
- retain references between original, reversal and any replacement voucher.

### As-on result

Example:

- MO posted: 10-Aug-2026;
- cancelled/reversed: 05-Sep-2026.

Expected reports:

- as on 31-Aug-2026: the MO is present;
- as on 04-Sep-2026: the MO is present;
- as on 05-Sep-2026 after reversal: the net effect is zero;
- audit history: both the original and reversal remain visible.

This replaces today's “void from inception” reporting behavior, where current `Cancelled` status removes the voucher even from an earlier as-on report.

### Closed-period original

If the original voucher is in a closed period, do not reopen or mutate it merely to cancel it. Post the reversal in the current open period. If law or audited financial statements require reopening, use the formal exception process.

### Dependencies

An upstream MO cannot be reversed while an active MI consumes it. The operator must reverse downstream effects first:

`Final/Intermediate MI → onward MO → earlier-stage MI → supplying MO`

TexTrack should display this dependency path rather than only a generic block message.

### Physical deletion

Physical deletion is limited to unposted drafts that never created stock movements. Posted vouchers are cancelled/reversed, not deleted. The immutable voucher audit history remains available in either case.

## Policy A09-4 — Negative stock

### Default rule

Block an outward stock posting when available physical quantity at its exact source valuation position is insufficient at the proposed effective date.

The check must include:

- company;
- item;
- exact variant/base identity;
- UQC;
- source godown;
- effective date and valuation order;
- active movements and dated reversals; and
- concurrent uncommitted posting protection.

Do not allow positive stock in another godown or another variant to satisfy the check.

### Why TexTrack should block rather than provisionally value

Permitting negative stock under FIFO requires a provisional-cost and later recosting engine. That recosting can cascade through MO, WIP, MI, child BOM output and finished goods. TexTrack does not yet have that engine.

Until it exists, blocking is more honest and safer than inventing a cost that later reports treat as final.

### Operational correction

When staff recorded the outward voucher before a missing inward voucher:

- if the period is open and chronology can be safely replayed, enter the real inward voucher on its true date first;
- if later movements exist before replay capability, correct/repost in dependency order;
- if the period is closed, use an authorized current-period stock adjustment with a reason rather than changing signed-off history.

### JWO reservations are separate

JWO is a requirement, not a stock movement. Future material-shortage functionality should calculate:

`Available to Promise = Physical On Hand − reservations for earlier approved open production demand`

That warning/reservation engine must not be confused with the final physical-stock check when MO is posted. A user may have physical stock but be consuming stock reserved for another JWO; TexTrack should warn and require an explicit reallocation decision without fabricating negative physical stock.

## Policy A09-5 — Cross-financial-year JWO and WIP

### Rule

Financial year is a reporting, control and numbering dimension for posted vouchers—not a boundary that terminates a production order or cost layer.

A JWO created in FY 2026-27 may remain open and receive linked MO/MI vouchers in FY 2027-28.

### Required behavior

- The JWO retains its original FY, voucher number, audit identity and stage/jobber assignments.
- Each MO/MI uses the FY containing its own voucher date.
- MO/MI lookup may cross FY boundaries inside the same company for a valid open JWO.
- An MO/MI date cannot precede the JWO or its supplying dependency.
- Voucher-number reset rules continue to use the current posting FY where applicable.
- WIP quantity, custody, FIFO layer identity and value carry forward without an artificial year-end transfer.
- Closing Stock as on a new-year date includes all earlier valid movements.
- Production-aging reports distinguish total JWO age, jobber material age and current-period activity.
- A closed prior year does not prevent current-year receipt/cancellation; it prevents changing the prior-year movements.

Do not clone the JWO merely because the financial year changed. Cloning would split production lineage, jobber accountability, component reservations and audit history.

## Policy A09-6 — Reporting contract

As-on inventory reports must use effective-dated movement history.

They must not remove an original movement solely because its voucher is currently cancelled. Instead:

- include the original when its effective date is on/before the report date;
- include the reversal only when the reversal effective date is on/before the report date;
- calculate current balances from their net effect; and
- expose cancelled/reversed status as presentation metadata.

The same rule applies to Closing Stock, Stock Register, WIP, jobber custody, reconciliation and future valuation-method views.

Reports must state when valuation replay is pending. They must never present stale values as final without warning.

## Policy A09-7 — Imports, retries and concurrency

Imports are not exempt. Preview → Validate → Commit must call the same period, chronology, negative-stock, dependency and valuation services as manual entry.

Retry/idempotency must ensure the same logical request cannot post duplicate movements or layers.

Concurrent outward vouchers for the same valuation position must serialize the availability check and posting. Two users must not both consume the same last 10 units after independently reading 10 available.

## Anticipated implementation boundaries

No migration is authorized by this policy document. The future design will probably require:

- company inventory-control settings, including stock-freeze date;
- audited, expiring period-reopen records;
- reversal document/link identity and reversal effective date;
- immutable valuation ordering;
- cost layers and layer allocations;
- revaluation/replay runs and affected-position status;
- exact stock-position query/locking service; and
- cross-FY repository lookup rules.

Business rules belong in shared server-side services. Razor pages, WebView2, import adapters and reports should consume those services rather than reproduce the rules.

## Acceptance tests required before release

### Period control

1. The freeze boundary is inclusive.
2. Manual, imported and API entry are blocked equally.
3. A privileged reopen requires reason, expiry and audit.
4. An expired exception cannot be reused.
5. A failed attempt is auditable without exposing secret data.

### Backdating

6. A backdated movement with no later dependency is accepted in an open period.
7. Before replay exists, a later affected movement causes a clear rejection.
8. After replay exists, mixed FIFO layers and downstream manufactured values are recalculated deterministically.
9. Same-day, cross-voucher-type ordering is stable.
10. Concurrent posting during replay cannot corrupt allocations.

### Cancellation

11. A report before cancellation shows the original voucher.
12. A report after reversal shows the net reversal.
13. A closed-period original is unchanged and reversed in the open period.
14. Downstream dependency reversal order is enforced.
15. Original, reversal and replacement snapshots remain linked.

### Negative stock

16. Insufficient item/variant/UQC/godown quantity is blocked.
17. Stock in another godown or variant does not satisfy availability.
18. Two simultaneous issues cannot over-consume the same balance.
19. Future-dated inward stock does not satisfy an earlier outward posting.
20. JWO reservation warning is separate from physical-stock validation.

### Cross financial year

21. Prior-year JWO appears for a current-year MO/MI in the same company.
22. Current-year vouchers retain their own FY and numbering sequence.
23. WIP and cost layers remain continuous across year end.
24. Prior-year closed data cannot be altered through a current-year workflow.
25. Company isolation applies to every cross-year lookup and replay.

## Rollout sequence

1. Review and explicitly accept or amend this policy.
2. Add policy-focused tests that initially fail against current behavior.
3. Implement period control and the exact stock-position service.
4. Enforce default negative-stock blocking with concurrency protection.
5. Implement dated reversal and correct as-on reporting.
6. Permit cross-FY JWO continuation without changing prior-year vouchers.
7. Define and migrate FIFO cost layers.
8. Implement controlled backdated replay.
9. Route imports through the same domain contract.
10. Run reconciliation, restored-backup rehearsal and production-mode verification before enabling the policy in a live database.

## Explicitly deferred

- provisional negative-stock costing and retrospective recosting;
- scrap, wastage and by-product valuation;
- Tally import/export mapping redesign;
- configurable non-FIFO book methods;
- UI overhaul beyond the minimum controls/messages required by this policy.

These remain broader ERP work and must not delay freezing the current garment-production module.

## Recommended approval

Approve the six decision-summary policies as TexTrack's target contract, with one staged limitation:

> Until controlled valuation replay is implemented and verified, TexTrack must block a backdated stock operation whenever later affected stock or production movements exist.

This gives TexTrack a safe route to perpetual FIFO without pretending the current engine can recost arbitrary history.
