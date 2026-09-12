const { chromium } = require("playwright");
const assert = require("node:assert/strict");

const baseUrl = process.env.TEXTRACK_BROWSER_BASE_URL || "http://127.0.0.1:5076";
const executablePath = process.env.TEXTRACK_BROWSER_EXECUTABLE;
const unique = "Browser Worker " + Date.now();
const renamed = unique + " Renamed";
const mainGodown = "1MAIN LOCATION";
const alternateGodown = process.env.TEXTRACK_ALTERNATE_GODOWN || "JOBBER1";
let usedWorkerDisplayName = "Jobber1";

(async () => {
  const browser = await chromium.launch({ headless: true, executablePath });
  const page = await browser.newPage();
  const results = [];

  async function waitContext(id, mode) {
    await page.waitForFunction(([expectedId, expectedMode]) => {
      const context = window.texTrackKeyboardContexts?.resolveActive();
      return context?.contextId === expectedId && context?.mode === expectedMode && context.element?.isConnected;
    }, [id, mode]);
    const state = await page.evaluate(() => ({
      count: window.texTrackKeyboardContexts.contexts.size,
      listeners: window.texTrackKeyboardContexts.registrationCount
    }));
    assert.equal(state.listeners, 1, id + ": duplicate global listener");
  }

  async function expectFocus(id, label) {
    await page.waitForFunction(expected => document.activeElement?.id === expected, id);
    assert.equal(await page.evaluate(() => document.hasFocus()), true, label + ": application focus lost");
  }

  async function tabTo(id, max = 40) {
    for (let i = 0; i < max; i += 1) {
      if (await page.evaluate(expected => document.activeElement?.id === expected, id)) return;
      await page.keyboard.press("Tab");
    }
    throw new Error("Keyboard Tab could not reach #" + id + "; active=" + await page.evaluate(() => document.activeElement?.id));
  }

  async function installFocusProbe() {
    await page.evaluate(() => {
      window.__jobWorkerProbe = 0;
      document.addEventListener("keydown", event => {
        if (event.key === "F8") window.__jobWorkerProbe += 1;
      }, true);
    });
  }

  async function assertAltDConsumedNoDialog(label) {
    const before = await page.evaluate(() => window.__jobWorkerProbe);
    const url = page.url();
    await page.keyboard.press("Alt+d");
    await page.waitForTimeout(80);
    assert.equal(await page.locator('[role="dialog"][aria-modal="true"]').count(), 0, label + ": unexpected dialog");
    assert.equal(page.url(), url, label + ": URL changed");
    await page.keyboard.press("F8");
    await page.waitForFunction(value => window.__jobWorkerProbe > value, before);
    assert.equal(await page.evaluate(() => document.hasFocus()), true, label + ": Chrome address bar took focus");
  }

  async function searchWorker(value) {
    await page.waitForFunction(() => document.activeElement?.closest("#job-worker-page-root"));
    await page.keyboard.press("F4");
    await expectFocus("job-worker-search", "search " + value);
    await page.keyboard.press("Control+a");
    await page.keyboard.type(value);
    await page.keyboard.press("Enter");
    await page.waitForFunction(expected => {
      const row = [...document.querySelectorAll("#job-worker-grid tbody tr[id]")]
        .find(candidate => candidate.textContent.includes(expected));
      return row && document.activeElement === row;
    }, value);
  }

  async function openJobWorkers() {
    await page.goto(baseUrl + "/masters/job-workers", { waitUntil: "domcontentloaded" });
    await waitContext("job-workers-list", "List");
    await installFocusProbe();
  }

  async function tabToInputWithLabel(labelText) {
    for (let i = 0; i < 40; i += 1) {
      const matches = await page.evaluate(label => {
        const active = document.activeElement;
        return active instanceof HTMLInputElement && active.closest("label")?.textContent.includes(label);
      }, labelText);
      if (matches) return;
      await page.keyboard.press("Tab");
    }
    throw new Error("Keyboard Tab could not reach " + labelText);
  }

  async function saveJobWorkerForm() {
    for (let attempt = 0; attempt < 3; attempt += 1) {
      await page.keyboard.press("Alt+s");
      const closed = await page.waitForSelector("#job-worker-form-root", { state: "detached", timeout: 1500 })
        .then(() => true, () => false);
      if (closed) return;
    }
    throw new Error("Job Worker form did not close after keyboard save attempts: " +
      (await page.locator(".notice-error").textContent().catch(() => "")));
  }
  async function setWorkerDefault(godownName) {
    await openJobWorkers();
    await searchWorker("Jobber1");
    await page.keyboard.press("Enter");
    await waitContext("job-workers-form", "Alteration");
    await expectFocus("job-worker-name-input", "Jobber1 alteration");
    await tabTo("job-worker-mo-godown");
    await page.keyboard.press("Control+a");
    await page.keyboard.type(godownName);
    await page.waitForFunction(name => {
      const input = document.getElementById("job-worker-mo-godown");
      const option = [...document.querySelectorAll("#job-worker-godowns option")]
        .find(item => item.value.toLocaleLowerCase() === name.toLocaleLowerCase());
      return input?.value === name && option && input.dataset.selectedId === option.dataset.id;
    }, godownName);
    await page.keyboard.press("Tab");
    await tabTo("job-worker-mi-godown");
    await page.keyboard.press("Tab");
    await saveJobWorkerForm();
  }

  await openJobWorkers();
  await page.keyboard.press("Alt+c");
  await waitContext("job-workers-form", "Create");
  await expectFocus("job-worker-name-input", "create initial focus");
  await assertAltDConsumedNoDialog("create Alt+D");
  await page.keyboard.press("Enter");
  assert.notEqual(await page.evaluate(() => document.activeElement?.id), "job-worker-name-input", "Enter navigation did not start");
  await page.keyboard.press("Shift+Tab");
  await expectFocus("job-worker-name-input", "return to name");
  await page.keyboard.type(unique);
  await tabTo("job-worker-mo-godown");
  await page.keyboard.type(mainGodown);
  await page.keyboard.press("Tab");
  await page.keyboard.type(mainGodown);
  await page.keyboard.press("Tab");
  await saveJobWorkerForm();
  await page.waitForFunction(expected =>
    [...document.querySelectorAll("#job-worker-grid tbody tr[id]")].some(row => row.textContent.includes(expected)), unique);
  const createdRowId = await page.evaluate(expected =>
    [...document.querySelectorAll("#job-worker-grid tbody tr[id]")].find(row => row.textContent.includes(expected))?.id, unique);
  assert.ok(createdRowId, "created Job Worker row not found");
  results.push("create keyboard-only and defaults|PASS");

  await searchWorker(unique);
  await page.keyboard.press("Enter");
  await waitContext("job-workers-form", "Alteration");
  await expectFocus("job-worker-name-input", "alteration initial focus");
  await page.keyboard.press("Control+a");
  await page.keyboard.type(renamed);
  await page.keyboard.press("Tab");
  await saveJobWorkerForm();
  const alteredRowId = await page.evaluate(expected =>
    [...document.querySelectorAll("#job-worker-grid tbody tr[id]")].find(row => row.textContent.includes(expected))?.id, renamed);
  assert.equal(alteredRowId, createdRowId, "alteration changed stable Ledger ID");
  results.push("alter stable ID|PASS");
  await expectFocus(createdRowId, "alter row restoration");

  await page.keyboard.press("Enter");
  await waitContext("job-workers-form", "Alteration");
  await page.keyboard.press("Alt+d");
  await page.waitForSelector('[role="dialog"][aria-modal="true"]');
  await page.keyboard.press("Alt+d");
  assert.equal(await page.locator('[role="dialog"][aria-modal="true"]').count(), 1, "duplicate Job Worker delete modal");
  await page.keyboard.press("Escape");
  await page.waitForSelector('[role="dialog"][aria-modal="true"]', { state: "detached" });
  await expectFocus("job-worker-name-input", "delete Esc restore");
  await page.keyboard.press("Alt+d");
  await page.waitForSelector('[role="dialog"][aria-modal="true"]');
  await page.waitForFunction(() => document.activeElement?.closest('[role="dialog"][aria-modal="true"]'));
  await page.keyboard.press("ArrowLeft");
  await page.waitForFunction(() => document.activeElement instanceof HTMLButtonElement && document.activeElement.textContent === "Yes");
  await page.keyboard.press("Enter");
  await page.waitForSelector("#job-worker-form-root", { state: "detached" });
  await page.waitForFunction(expected =>
    ![...document.querySelectorAll("#job-worker-grid tbody tr[id]")].some(row => row.textContent.includes(expected)), renamed);
  results.push("unused delete modal re-entry and Esc|PASS");

  await searchWorker("Jobber1");
  await page.keyboard.press("Enter");
  await waitContext("job-workers-form", "Alteration");
  await expectFocus("job-worker-name-input", "used alteration initial focus");
  usedWorkerDisplayName = await page.locator("#job-worker-name-input").inputValue();
  await page.keyboard.press("Alt+d");
  await page.waitForSelector('[role="dialog"][aria-modal="true"]');
  await page.waitForFunction(() => document.activeElement?.closest('[role="dialog"][aria-modal="true"]'));
  await page.keyboard.press("ArrowLeft");
  await page.waitForFunction(() => document.activeElement instanceof HTMLButtonElement && document.activeElement.textContent === "Yes");
  await page.keyboard.press("Enter");
  await page.waitForSelector('[role="dialog"][aria-modal="true"]', { state: "detached" });
  await page.waitForSelector(".notice-error");
  const blockedDeleteText = await page.locator(".notice-error").textContent();
  assert.match(blockedDeleteText || "", /linked|cannot be deleted/i);
  assert.equal(await page.locator("#job-worker-form-root").count(), 1, "used Job Worker form closed after blocked delete");
  results.push("used JWO dependency delete blocked|PASS");
  await page.keyboard.press("Escape");
  await waitContext("job-workers-list", "List");

  await setWorkerDefault(mainGodown);

  await page.goto(baseUrl + "/vouchers/material-out", { waitUntil: "domcontentloaded" });
  await waitContext("material-out-list", "List");
  await installFocusProbe();
  await page.keyboard.press("Alt+c");
  await waitContext("material-out-form", "Create");
  await tabTo("mo-job-worker");
  await page.keyboard.type(usedWorkerDisplayName);
  await page.waitForSelector(".mo-field-lookup-panel button");
  await page.keyboard.press("Tab");
  await page.keyboard.press("Enter");
  await page.waitForFunction(expected => document.querySelector("#mo-destination")?.value === expected, mainGodown);
  assert.equal(await page.locator("#mo-destination").inputValue(), mainGodown, "Job Worker default was not applied");

  const pendingOrderCount = await page.locator(".mo-order-option").count();
  if (pendingOrderCount > 0) {
    await tabTo("mo-destination");
  await page.keyboard.press("Control+a");
  await page.keyboard.type(alternateGodown);
  await page.waitForFunction(expected => {
    const input = document.getElementById("mo-destination");
    const buttons = [...document.querySelectorAll(".erp-lookup-panel button")];
    return input?.value === expected && buttons.some(button => button.textContent.trim() === expected);
  }, alternateGodown);
  await page.keyboard.press("Tab");
  await page.keyboard.press("Enter");
  await expectFocus("mo-order-number", "destination selection returns to order");
  assert.equal(await page.locator("#mo-destination").inputValue(), alternateGodown, "keyboard destination lookup did not select the override");
    await page.keyboard.press("Tab");
    await page.keyboard.press("Enter");
    await page.waitForFunction(expected => document.querySelector("#mo-destination")?.value === expected, alternateGodown);
    assert.equal(await page.locator("#mo-destination").inputValue(), alternateGodown, "JWO selection overwrote user destination");
    await page.keyboard.press("Alt+s");
    await page.waitForSelector("#mo-form-root", { state: "detached" });
    await page.waitForFunction(() => document.querySelector(".mo-message.success")?.textContent.includes("saved"));
    results.push("Material Out default, override and save|PASS");

    await setWorkerDefault(alternateGodown);
    await setWorkerDefault(mainGodown);

    await page.goto(baseUrl + "/vouchers/material-out", { waitUntil: "domcontentloaded" });
    await waitContext("material-out-list", "List");
    await page.waitForSelector("#mo-grid tbody tr[id]");
    await page.keyboard.press("Enter");
    await waitContext("material-out-form", "Alteration");
    assert.equal(await page.locator("#mo-destination").inputValue(), alternateGodown,
      "changing Job Worker default rewrote saved Material Out destination");
    results.push("saved Material Out historical Godown preserved|PASS");

    if (process.env.TEXTRACK_RETAIN_BROWSER_MO !== "1") {
      await page.keyboard.press("Alt+d");
      await page.waitForSelector('[role="dialog"][aria-modal="true"]');
      await page.waitForFunction(() => !!document.activeElement?.closest('[role="dialog"][aria-modal="true"]'));
      await page.keyboard.press("ArrowLeft");
      await page.waitForFunction(() => document.activeElement instanceof HTMLButtonElement && document.activeElement.textContent === "Yes");
      await page.keyboard.press("Enter");
    }
  } else {
    results.push("Material Out default prefill|PASS");
    results.push("Material Out save/history fixture (no pending JWO)|SKIP");
    await setWorkerDefault(mainGodown);
  }
  for (let cycle = 0; cycle < 3; cycle += 1) {
    await page.goto(baseUrl + "/", { waitUntil: "domcontentloaded" });
    await page.goto(baseUrl + "/masters/job-workers", { waitUntil: "domcontentloaded" });
    await waitContext("job-workers-list", "List");
    const state = await page.evaluate(() => ({
      contexts: window.texTrackKeyboardContexts.contexts.size,
      listeners: window.texTrackKeyboardContexts.registrationCount
    }));
    assert.equal(state.contexts, 1, "cycle " + cycle + ": stale context");
    assert.equal(state.listeners, 1, "cycle " + cycle + ": duplicate listener");
  }
  results.push("navigation cleanup and one listener|PASS");

  console.log(results.join("\n"));
  await browser.close();
})().catch(error => {
  process.stderr.write("BROWSER_ERROR=" + String(error?.stack || error) + "\n");
  setTimeout(() => process.exit(1), 100);
});
