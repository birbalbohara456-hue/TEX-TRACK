# TexTrack Pre-Beta Fix Handoff — 2026-09-06

This note is an audit aid. It records the narrow fixes made after reviewing
`TexTrack_Pre_Beta_Calculation_Audit.md`. It does not certify the application
and should not replace independent source and behavioral verification.

## 1. Multi-stage produced-component value lineage

### Defect

Quantity lineage already prevented a component from being issued onward before
its child production stage was received. Value lineage did not: Material Out
accepted the client/UI rate, while the JWO snapshot rate for a produced
component could be zero. This could silently remove child-stage material and
process cost from the final product.

### Implemented rule

For a JWO component linked by `ChildBomStageId`, Material Out now derives its
rate from persisted, non-cancelled child-stage receipts and prior non-cancelled
onward issues:

- received quantity = sum of child-stage `MaterialInFinishedGood.ReceivedQuantity`
- received value = sum of child-stage `MaterialInFinishedGood.FinishedGoodsValue`
- remaining quantity = received quantity - prior onward MO quantity
- remaining value = received value - prior onward MO amount
- onward rate = remaining value / remaining quantity, rounded to four decimals

The repository overwrites the submitted UI rate for produced components. Raw
material component rates remain user-editable. A negative residual value is
rejected as an inconsistent chain. A legitimate zero-value receipt remains
zero-valued rather than inventing a cost.

On Material Out alteration, the current voucher is excluded from prior issues
before deriving its replacement rate.

### UI behavior

The Material Out rate field is read-only for produced components and explains
that it is derived from the earlier production stage. Enter from its quantity
skips the read-only rate. This is convenience only; the repository remains the
authoritative enforcement boundary.

### Behavioral regression

`ProducedComponentValuationTests` proves:

1. a receipt of 50 units valued at 5,000 produces an onward rate of 100 even
   when the client submits rate zero;
2. issuing 20 units carries 2,000 of value;
3. receiving another 50 units valued at 7,500 leaves 80 units valued at 10,500,
   producing a residual rate of 131.25; and
4. total onward MO value is 12,500, exactly matching total child-stage receipts.

## 2. Material In quantity-before-charge workflow

After selecting a JWO, focus now starts on the first direct or variant receipt
quantity rather than Total Charges. Process Charge / Unit and Total Charges are
disabled until that stage has a positive received quantity. Enter advances
through variant quantities and then to Process Charge / Unit. Validation focus
IDs now use the actual stage/assignment identity used by the rendered inputs.

The existing reciprocal calculator remains unchanged: the user may enter either
rate per unit or total charge after quantity, and the other field is calculated.

## 3. Reporting corrections

- Job Worker Control footer totals now use every order in the filtered report,
  not only the batches on the visible client-side page.
- The older server-paged Production Reports still calculate their footer from
  the loaded page. Their labels now explicitly say `Page Total`, removing the
  false implication that they are grand totals. A future server-side grouped
  aggregate can add a separate filtered grand-total row without loading all
  detail records into memory.

## 4. WIP robustness

Closing-stock godown detail now resolves the Work In Progress stock group with
`SingleOrDefaultAsync`. A company missing that seeded group no longer throws
from this lookup path.

## 5. Verification performed

- `dotnet build TexTrack.sln --no-restore`: passed, zero warnings/errors.
- full .NET integration suite after the core valuation/report fixes: 227/227
  passed.
- focused valuation and charge-calculation suite after the Material In focus
  change: 5/5 passed.

The JavaScript source-contract suite currently reports 84/89 passing. Its five
failures pre-date and are unrelated to this backend fix; they assert older UI
markup/version strings that no longer match the ongoing Claude UI branch:

- scoped visual CSS/version assertion;
- JWO destination-godown markup assertion;
- JWO single-date/full-width markup assertion;
- Material Out/Material In shell markup assertion; and
- voucher party/stage-jobber markup assertion.

These tests must be independently classified as either stale assertions or real
UI regressions before beta; they must not simply be changed to make the suite
green.

## 6. Deliberately not changed

- Tally import sequence handling and two-decimal export formatting: deferred
  with the embedded Tally exchange engine, which is planned as a separate
  converter and must be validated against real Tally mappings.
- Negative-stock behavior: remains an explicit product-policy decision. The
  current engine permits negative stock.
- Legacy manual-process `ExpectedRate` / `RateBasis`: remains a visible policy
  issue. The authoritative rate-variance path is the per-stage,
  per-assignment `ExpectedProcessRate`. Do not treat the legacy field as a
  costing control until it is either wired to a defined rule or removed from
  the UI.
- Placeholder accounting vouchers are outside the frozen production-engine
  scope and must not be represented as implemented accounting modules.

## 7. Files directly involved in this fix

- `Models/MaterialOutModels.cs`
- `Services/MaterialOutRepository.cs`
- `Components/Pages/Vouchers/MaterialOut.razor`
- `Components/Pages/Vouchers/MaterialIn.razor`
- `Services/OperationalReportingService.cs`
- `Components/Pages/Reports/JobWorkerControl.razor`
- `Components/Pages/Reports/ProductionReports.razor`
- `Tests/TexTrack.Web.IntegrationTests/ProducedComponentValuationTests.cs`

