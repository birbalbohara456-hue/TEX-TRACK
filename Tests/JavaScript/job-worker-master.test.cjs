const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");

const root = path.resolve(__dirname, "..", "..");
const read = relative => fs.readFileSync(path.join(root, relative), "utf8");

test("Job Worker page uses only shared list/form keyboard contexts", () => {
  const page = read("Components/Pages/Masters/JobWorkers.razor");
  assert.match(page, /ContextId="job-workers-list"/);
  assert.match(page, /ContextId="job-workers-form"/);
  assert.match(page, /KeyboardContextMode\.Create/);
  assert.match(page, /KeyboardContextMode\.Alteration/);
  assert.match(page, /InitialFocusTarget="job-worker-name-input"/);
  assert.match(page, /e\.Key == "F4"/);
  assert.match(page, /e\.AltKey && e\.Key\.Equals\("s"/);
  assert.doesNotMatch(page, /<script|\.js"/i);
});

test("Material Out precedence preserves saved and user-entered destination", () => {
  const page = read("Components/Pages/Vouchers/MaterialOut.razor");
  // SelectJobWorker must return before touching selectedJobWorkerId/destinationGodownId
  // at all during alteration - Party A/c Name (and the JWO/destination it drives) is
  // immutable once a voucher is open for alteration, so this is a true no-op rather
  // than applying the new worker and only suppressing the pending-order reload.
  assert.match(page, /if \(editingVoucherId is not null\)\s*\{[\s\S]{0,220}?return;\s*\}\s*\n\s*selectedJobWorkerId = worker\.Id;/);
  assert.match(page, /destinationGodownId is null && worker\.DefaultMaterialOutDestinationGodownId is not null/);
  assert.match(page, /if \(destinationGodownId is null\)\s*\{\s*destinationGodownText = selectedOrder/s);
  assert.match(page, /destinationGodownId = data\.DestinationGodownId/);
});

test("Party A/c Name and JWO / Order No. are readonly and keyboard-immutable during Material Out alteration", () => {
  const page = read("Components/Pages/Vouchers/MaterialOut.razor");
  // The fields themselves must be readonly while editingVoucherId is set...
  assert.match(page, /id="mo-job-worker"[\s\S]{0,320}readonly="@\(editingVoucherId is not null\)"/);
  assert.match(page, /id="mo-order-number"[\s\S]{0,320}readonly="@\(editingVoucherId is not null\)"/);
  // ...and every handler that can still fire on a readonly field (focus, input,
  // and every Arrow/Enter key) must guard on editingVoucherId too - readonly only
  // stops typing, not keydown handlers, so Arrow Up/Down could otherwise still
  // reopen the lookup on a field the user can no longer actually change.
  assert.match(page, /private void OpenJobWorkerLookup\(\)\s*\{\s*(?:\/\/[^\n]*\n\s*)*if \(editingVoucherId is not null\) return;/);
  assert.match(page, /private void JobWorkerTextChanged\(ChangeEventArgs e\)\s*\{\s*if \(editingVoucherId is not null\) return;/);
  assert.match(page, /private async Task HandlePartyKeyDown\(KeyboardEventArgs e\)\s*\{\s*(?:\/\/[^\n]*\n\s*)*if \(editingVoucherId is not null\) return;/);
  assert.match(page, /private void OpenOrderLookup\(\)\s*\{\s*(?:\/\/[^\n]*\n\s*)*if \(editingVoucherId is not null\) return;/);
  assert.match(page, /private void OrderTextChanged\(ChangeEventArgs e\)\s*\{\s*if \(editingVoucherId is not null\) return;/);
  assert.match(page, /private async Task HandleOrderKeyDown\(KeyboardEventArgs e\)\s*\{\s*(?:\/\/[^\n]*\n\s*)*if \(editingVoucherId is not null\) return;/);
  assert.match(page, /private async Task SelectOrder\(int index\)\s*\{\s*(?:\/\/[^\n]*\n\s*)*if \(editingVoucherId is not null\) return;/);
});

test("JWO and Material Out accept only Job Worker Ledger roles", () => {
  const jwo = read("Services/JobWorkOrderRepository.cs");
  const materialOut = read("Services/MaterialOutRepository.cs");
  assert.match(jwo, /x\.IsActive && x\.IsJobWorker/);
  assert.match(materialOut, /x\.IsActive && x\.IsJobWorker/);
  assert.doesNotMatch(jwo, /JobWorkers = await[\s\S]{0,300}!x\.IsSystem/);
});

test("Migration backfills used identities and restricts both Godown foreign keys", () => {
  const sql = read("Data/Migrations/012_job_worker_master.sql");
  assert.match(sql, /party_ledger_id = l\.id/);
  assert.match(sql, /JOB_WORK_OUT_ORDER/);
  assert.match(sql, /MATERIAL_OUT/);
  assert.equal((sql.match(/ON DELETE RESTRICT/g) || []).length, 2);
  assert.match(sql, /default_material_out_destination_godown_id/);
  assert.match(sql, /default_material_in_consumption_godown_id/);
});