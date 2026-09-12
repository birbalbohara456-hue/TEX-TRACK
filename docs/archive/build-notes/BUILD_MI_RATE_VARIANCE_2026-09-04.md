# Material In charge entry and Job Worker Control rate variance

## Delivered scope

- Material In accepts either process rate per output unit or total process charges. The last edited field is authoritative. Quantity changes preserve that choice.
- `Models/ProcessChargeCalculation.cs` is the common UI/server calculator. Rates and totals use the existing four-decimal persisted precision, with midpoint rounding away from zero. Total-entry mode retains the entered total rather than multiplying the rounded derived rate back into a different amount.
- A zero quantity never divides. Saving a charged UI row without a receipt quantity is blocked. Existing positive process-charge requirements remain in place.
- Optional expected process rate per output unit is entered on each JWO stage/jobber assignment. This is distinct from a component material rate and from finished-stock valuation.
- Rate-only amendments append an assignment version, preserve permanent stage identity, and recalculate JWO status from production activity instead of resetting it to Open.
- MI persists expected rate, actual rate and charge-entry mode. The server selects the expectation from the referenced assignment, never from a client-supplied expectation. Alteration preserves the original receipt's expected snapshot, including NULL for old records. Audit entries record mode, quantity, expected/actual rate and total charge.
- The Rate Variance tab is inside Job Worker Control (`?tab=rates`), not a new Reports menu entry. Rows represent individual MI stage receipts, including intermediate outputs, with JWO/product/batch/jobber/process/assignment/MI references and quantity/UQC. It shows expected rate, actual rate and signed actual-minus-expected difference. No arithmetic averaging or mixed-UQC rate total is presented.
- The report respects current company, financial year, as-on date and cancellation status. It reuses header filters and pagination and links to JWO/MI vouchers. The wide table scrolls horizontally.

## Historical-data rules

Migration `027_process_charge_rate_snapshots.sql` adds nullable rate snapshots and a Total-mode default for historical receipts. It does not rewrite receipt quantities, charges, valuations, or expected rates. Actual historical rate can be derived from its saved process-charge total and receipt quantity; unknown expected rates display `Not recorded` and have no variance.

The older finished-good process-list expected rate is not automatically mapped to a multi-jobber assignment. Such a mapping could attribute a different stage/jobber's cost to historical work. Manual/non-BOM receipts therefore remain without a stage expectation unless a future explicit mapping workflow supplies one.

Work already dispatched under an older assignment keeps that assignment's expected rate. Changing the current JWO rate does not reprice earlier work or historical MI comparisons.

## Validation

- Full .NET/PostgreSQL integration suite: 209 passed, 0 failed, 0 skipped.
- Five JavaScript test files passed (including bidirectional-entry wiring and the embedded Rate Variance tab).
- Tests cover both modes, authoritative-field recalculation, zero quantity, rounding, invalid values/modes, saved rate snapshots, alteration, cancellation, legacy NULL expectations, JWO assignment rate history and rate-only amendments.
- No migration was applied to the running/live database. No live browser visual acceptance was performed against migration 027.

## Before deployment

Rehearse pending migrations, including 026 and 027, on a restored backup, verify historical totals, and take a recoverable backup before restarting the app with this code. The bootstrapper applies pending migrations on startup; restarting the server is therefore a deployment action, not just a browser refresh. After approved deployment, hard-refresh the browser to load updated scoped CSS.

UI acceptance: enter 50 units at rate 12 (total 600); change total to 650 (rate 13); increase quantity to 100 in Total mode (total stays 650); edit rate to 14 (total 1400); reopen and verify mode; check the Rate Variance tab; cancel the MI and confirm it disappears.

## Still outside this build

MO material-rate policy, yield/scrap variance authorization, component colour/size tracking decisions, average jobber delivery-time methodology, and production hosting/backup operations remain separate work. Rate variance is informational and does not block an MI because its rate differs from expectation.

Scope correction (2026-09-04): MJO groups existing JWOs; it is not a customer order or production authorization. The previously listed MJO allocation ceiling is not an applicable defect and must not be implemented as a blanket restriction.
