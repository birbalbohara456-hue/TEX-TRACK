const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '../..');
const read = relative => fs.readFileSync(path.join(root, relative), 'utf8');

test('Entries open voucher creation directly while Job Work Reports owns history routes', () => {
    const layout = read('Components/Layout/MainLayout.razor');
    assert.match(layout, /"Job Work Reports"/);
    assert.match(layout, /\/reports\/job-work\/vouchers\/master-job-orders/);
    assert.match(layout, /\/reports\/job-work\/vouchers\/job-out-orders/);
    assert.match(layout, /\/reports\/job-work\/vouchers\/material-out/);
    assert.match(layout, /\/reports\/job-work\/vouchers\/material-in/);

    const expectations = [
        ['Components/Pages/Vouchers/JobWorkOutOrder.razor', /else await OpenCreate\(\)/],
        ['Components/Pages/Vouchers/MaterialOut.razor', /else await OpenCreate\(\)/],
        ['Components/Pages/Vouchers/MaterialIn.razor', /else OpenCreate\(\)/]
    ];
    for (const [file, pattern] of expectations) assert.match(read(file), pattern, file);
});

test('shared voucher history opens straight into the register and is Alt+F2 period-aware', () => {
    const report = read('Components/Pages/Reports/VoucherHistory.razor');
    assert.doesNotMatch(report, /month-folder-grid/);
    assert.doesNotMatch(report, /History\.GetMonthsAsync\(Kind\)/);
    assert.match(report, /History\.GetPageAsync\(Kind, from, to, searchText, pageIndex, PageSize\)/);
    assert.match(report, /CurrentPeriod\.Changed \+= HandlePeriodChanged/);
    assert.match(report, /customFrom = CurrentPeriod\.From/);
    assert.match(report, /texTrackLeaveCurrentPage", "reports", HistoryReportFocusId/);
    assert.match(report, /"material-out" => "reports-child-0-2"/);
    assert.match(report, /Alt\+F2 Period/);
    assert.match(report, /PageSize = 5000/);
});

test('report row navigation stops at the first and last records', () => {
    const report = read('Components/Pages/Reports/VoucherHistory.razor');
    const closing = read('Components/Pages/Reports/InventoryClosingStock.razor');
    const register = read('Components/Pages/Reports/StockRegister.razor');
    assert.doesNotMatch(report, /selectedIndex = \(selectedIndex \+ 1\) %/);
    assert.match(report, /Math\.Min\(selectedIndex \+ 1/);
    assert.match(closing, /Math\.Clamp\(i\+delta/);
    assert.match(register, /Math\.Clamp\(i\+d/);
});

test('inventory report Escape reopens Reports at the exact originating item', () => {
    const closing = read('Components/Pages/Reports/InventoryClosingStock.razor');
    const register = read('Components/Pages/Reports/StockRegister.razor');
    const keyboard = read('wwwroot/js/textrack-alt-delete.js');
    assert.match(closing, /texTrackLeaveCurrentPage","reports","reports-child-2-0"/);
    assert.match(register, /texTrackLeaveCurrentPage","reports","reports-child-2-1"/);
    assert.match(keyboard, /document\.getElementById\('dashboard-page-root'\)/);
    assert.match(keyboard, /tt-\$\{returnMenu\}-menu-command/);
    assert.match(keyboard, /returnFocusId/);
});

test('report alteration return URL preserves the active period and selected voucher', () => {
    const report = read('Components/Pages/Reports/VoucherHistory.razor');
    assert.match(report, /BuildReturnUrl\(row\.Id\)/);
    assert.match(report, /from=\{customFrom:yyyy-MM-dd\}/);
    assert.match(report, /selectedId=\{selectedId\}/);
    for (const file of [
        'Components/Pages/Vouchers/JobWorkOutOrder.razor',
        'Components/Pages/Vouchers/MaterialOut.razor',
        'Components/Pages/Vouchers/MaterialIn.razor'
    ]) assert.match(read(file), /Navigation\.NavigateTo\(ReturnUrl\)/, file);
});

test('history actions preserve repository safety and Material In supports terminal deletion', () => {
    const report = read('Components/Pages/Reports/VoucherHistory.razor');
    assert.match(report, /JobWorkOrders\.DeleteAsync/);
    assert.match(report, /MasterJobOrders\.DeleteAsync/);
    assert.match(report, /MaterialOuts\.DeleteAsync/);
    assert.match(report, /MaterialIns\.DeleteAsync/);
    assert.match(report, /MaterialIns\.CancelAsync/);
    assert.match(report, /CanDeleteSelected.*\(Kind is "master-job-orders" or "job-out-orders" or "material-out" or "material-in"\)/s);
    assert.match(report, /"material-in" => MaterialIns\.DeleteAsync\(id\)/);
});

test('Master Job Order links saved JWOs and JWO colour lines share one allocation group', () => {
    const mjo = read('Components/Pages/Vouchers/MasterJobOrder.razor');
    const jwo = read('Components/Pages/Vouchers/JobWorkOutOrder.razor');
    const repository = read('Services/JobWorkOrderRepository.cs');
    const migration = read('Data/Migrations/017_master_job_order_multicolour_linking.sql');

    for (const required of ['Party A/c Name', 'Link Existing Job Orders', 'NAME OF FINISHED GOOD', 'Colour', 'Size']) assert.ok(mjo.includes(required));
    assert.doesNotMatch(mjo, /Component Allocation|Nature of Process/);
    assert.match(mjo, /LinkedJobOrderIds/);
    assert.match(mjo, /RebuildLinkedPlan/);
    assert.match(jwo, /GetGroupOwner/);
    assert.match(jwo, /AddColourLine/);
    assert.doesNotMatch(jwo, /Sub Total/);
    assert.doesNotMatch(jwo, /jwo-master-order-input/);
    // The in-grid total row was removed as a duplicate of the totals already
    // shown in the summary panel and the bottom status bar.
    assert.doesNotMatch(jwo, /jwo-voucher-total-row/);
    assert.match(jwo, /jwo-distinct-value/);
    assert.match(repository, /DesignGroupKey/);
    assert.doesNotMatch(repository, /MasterJobOrderId/);
    assert.match(migration, /master_job_order_finished_goods/);
    assert.match(migration, /master_job_order_allocations/);
});

test('JWO uses one editable header date and a full-width form without the command rail', () => {
    const page = read('Components/Pages/Vouchers/JobWorkOutOrder.razor');
    const css = read('Components/Pages/Vouchers/JobWorkOutOrder.razor.css');

    assert.equal((page.match(/Id="jwo-date-input"/g) || []).length, 1);
    assert.match(page, /<h1 class="jwo-screen-title">Job Work Out Order<\/h1>/);
    assert.match(page, /jwo-field-batch[\s\S]*jwo-field-reference[\s\S]*jwo-field-date[\s\S]*<VoucherDateInput Id="jwo-date-input"/);
    assert.doesNotMatch(page, /jwo-field-job-worker/);
    assert.match(page, /class="jwo-stage-job-worker" data-erp-lookup="true"[\s\S]*value="@stage\.AssignedJobWorkerText"/);
    assert.match(css, /\.jwo-field-date ::deep \.jwo-date-field-input \{[\s\S]*border-bottom: 1px solid #aab6c4/);
    assert.doesNotMatch(page, /class="tally-field jwo-field-date"/);
    assert.doesNotMatch(page, /Order Voucher Creation|CREATE MODE|ALTERATION MODE/);
    assert.doesNotMatch(page, /<VoucherCommandRail/);
    assert.doesNotMatch(page, /Alt\+C: Add finished good|Label="Add Finished Good"/);
    assert.doesNotMatch(page, /else if \(e\.AltKey && e\.Key\.Equals\("c"[\s\S]{0,100}AddFinishedGood/);
    assert.doesNotMatch(page, /tt-voucher-entry has-command-rail/);
    assert.match(css, /\.jwo-entry-form \{[\s\S]*padding-right: 0 !important;/);
});

test('focused voucher save buttons are activated by Enter and browser autofill is suppressed', () => {
    const app = read('Components/App.razor');
    assert.match(app, /current\.matches\('\[data-enter-save="true"\]'/);
    assert.match(app, /current\.click\(\)/);
    assert.match(app, /texTrackAutofillSuppressionInstalled/);
    assert.match(app, /data-lpignore/);
    assert.match(app, /MutationObserver/);
});

test('Master Job Order lookup stays open while filtering and lifecycle actions are unrestricted', () => {
    const page = read('Components/Pages/Vouchers/MasterJobOrder.razor');
    const repository = read('Services/MasterJobOrderRepository.cs');
    assert.match(page, /lookupKey == "job-order"/);
    assert.match(page, /JobOrderSearchChanged[\s\S]*lookupKey\s*=\s*"job-order"/);
    assert.match(repository, /MasterJobOrderId = null/);
    assert.doesNotMatch(repository, /Cancel linked active Job Work Out Orders first/);
    assert.doesNotMatch(repository, /cannot be deleted\./);
    assert.doesNotMatch(repository, /A cancelled Master Job Order cannot be altered/);
});

test('Material Out and Material In use full-width voucher shells and Material In lookups are typeable', () => {
    const materialOut = read('Components/Pages/Vouchers/MaterialOut.razor');
    const materialIn = read('Components/Pages/Vouchers/MaterialIn.razor');
    const frame = read('Components/Vouchers/Shared/VoucherEntryFrame.razor');
    const materialOutCss = read('Components/Pages/Vouchers/MaterialOut.razor.css');
    const materialInCss = read('Components/Pages/Vouchers/MaterialIn.razor.css');
    assert.doesNotMatch(materialOut, /has-command-rail|<VoucherCommandRail/);
    assert.equal((materialOut.match(/Id="mo-date"/g) || []).length, 1);
    assert.match(materialOut, /mo-context-date/);
    assert.doesNotMatch(materialIn, /has-command-rail|<VoucherCommandRail/);
    assert.match(materialIn, /RootElementId="mi-form-root"/);
    assert.match(materialIn, /VoucherEntryFrame Id="mi-form-root"/);
    assert.match(frame, /data-enter-navigation="true"/);
    assert.match(frame, /tabindex="0"/);
    assert.match(materialIn, /id="mi-save"[\s\S]*data-enter-save="true"/);
    for (const id of ['mi-job-worker', 'mi-order', 'mi-consumption-godown', 'mi-receiving-godown']) {
        assert.match(materialIn, new RegExp(`id="${id}" data-erp-lookup="true"`), id);
    }
    for (const css of [materialOutCss, materialInCss]) {
        assert.match(css, /inset: 0 0 0 208px/);
        assert.match(css, /height: 50px/);
        assert.match(css, /#dde6f0/);
        assert.match(css, /#fff0b7/);
    }
    assert.match(materialIn, /class="mi-narration-section"/);
    assert.match(materialIn, /class="mi-save-actions"/);
    assert.match(materialIn, /class="mi-gross-total"/);
    assert.doesNotMatch(materialIn, /QC PASSED/);
    assert.match(materialIn, /<div class="tt-dialog-overlay" role="presentation">[\s\S]*?<section class="tt-dialog"[^>]*aria-labelledby="mi-quit-title"/);
    assert.match(materialOut, /<div class="tt-dialog-overlay" role="presentation">[\s\S]*?<section class="tt-dialog"[^>]*aria-labelledby="mo-exit-title"/);
    assert.match(materialInCss, /\.mi-variant-cell \{[\s\S]*grid-template-columns: auto auto 64px/);
    assert.match(materialInCss, /\.mi-gross-total \{[\s\S]*justify-content: flex-end/);
    assert.match(materialOutCss, /#mo-page-root ::deep \.tt-dialog-actions button\.dialog-selected/);
});

test('voucher party lookups keep selected names while JWO assigns workers per production stage', () => {
    const jwo = read('Components/Pages/Vouchers/JobWorkOutOrder.razor');
    const materialOut = read('Components/Pages/Vouchers/MaterialOut.razor');
    const materialIn = read('Components/Pages/Vouchers/MaterialIn.razor');
    const masterJobOrder = read('Components/Pages/Vouchers/MasterJobOrder.razor');

    for (const page of [materialOut, materialIn]) {
        assert.match(page, /<strong>@worker\.Name<\/strong><span>@worker\.GroupName<\/span>/);
    }
    assert.doesNotMatch(jwo, /jwo-field-job-worker/);
    assert.match(jwo, /class="jwo-stage-job-worker" data-erp-lookup="true"[\s\S]*value="@stage\.AssignedJobWorkerText"/);
    assert.match(materialIn, /private static string JobWorkerFieldText\(JobWorkerLookupItem\? worker\) => worker\?\.Name \?\? string\.Empty;/);
    assert.match(materialOut, /jobWorkerText = worker\.Name;/);
    assert.match(masterJobOrder, /model\.PartyText=row\.Name;/);
});

test('empty lookup panels are hidden instead of leaving a thin visual strip', () => {
    const css = read('wwwroot/css/app.css');
    assert.match(css, /\.erp-lookup-panel:not\(:has\(button\)\)/);
});
