const { chromium } = require('playwright');
const assert = require('node:assert/strict');

const baseUrl = process.env.TEXTRACK_BROWSER_BASE_URL || 'http://127.0.0.1:5075';
const executablePath = process.env.TEXTRACK_BROWSER_EXECUTABLE || undefined;
const usedName = process.env.TEXTRACK_USED_VOUCHER_TYPE_NAME || 'Material Out';
const unique = `Browser Voucher Type ${Date.now()}`;

(async () => {
    const browser = await chromium.launch({ headless: true, executablePath });
    const page = await browser.newPage();
    const results = [];

    await page.goto(baseUrl + '/masters/voucher-types', { waitUntil: 'domcontentloaded' });
    await page.waitForFunction(() => window.texTrackKeyboardContexts?.resolveActive()?.contextId === 'voucher-types-list');
    await page.waitForSelector('#voucher-types-grid tbody tr[id]');
    await page.evaluate(() => {
        window.__voucherTypeProbe = 0;
        document.addEventListener('keydown', event => {
            if (event.key === 'F8') window.__voucherTypeProbe += 1;
        }, true);
    });

    async function expectFocus(id, label) {
        await page.waitForFunction(expected => document.activeElement?.id === expected, id);
        assert.equal(await page.evaluate(() => document.hasFocus()), true, `${label}: application lost focus`);
    }

    async function waitDialog() {
        await page.waitForSelector('[role="dialog"][aria-modal="true"]');
        await page.waitForFunction(() => document.activeElement?.closest('[role="dialog"][aria-modal="true"]'));
    }

    async function search(value, expected = value) {
        await page.keyboard.press('F4');
        await expectFocus('voucher-type-search', `search ${value}`);
        await page.keyboard.press('Control+a');
        await page.keyboard.type(value);
        await page.keyboard.press('Enter');
        await expectFocus('voucher-types-page-root', `search results `);
        await page.waitForFunction(text => [...document.querySelectorAll('#voucher-types-grid tbody tr[id]')].some(row => row.textContent.includes(text)), expected);
    }

    async function confirmYes() {
        await waitDialog();
        await page.keyboard.press('ArrowLeft');
        await page.keyboard.press('Enter');
        await page.waitForSelector('[role="dialog"][aria-modal="true"]', { state: 'detached' });
    }

    await search('Sales', 'Sales');
    await page.keyboard.press('Enter');
    await page.waitForSelector('#voucher-types-form-root');
    await page.waitForFunction(() => window.texTrackKeyboardContexts.resolveActive()?.mode === 'Alteration');
    await expectFocus('voucher-type-tally-name', 'system alteration focus');
    assert.equal(await page.locator('#voucher-type-name').inputValue(), 'Sales');
    await page.keyboard.press('Alt+d');
    await waitDialog();
    await page.keyboard.press('Alt+d');
    assert.equal(await page.locator('[role="dialog"][aria-modal="true"]').count(), 1, 'system modal re-entry');
    await page.keyboard.press('Escape');
    await page.waitForSelector('[role="dialog"][aria-modal="true"]', { state: 'detached' });
    await expectFocus('voucher-type-tally-name', 'system dialog Esc restoration');
    await page.keyboard.press('Alt+d');
    await confirmYes();
    await page.waitForFunction(() => document.querySelector('.form-message.error')?.textContent.includes('Permanent system Voucher Types cannot be deleted'));
    assert.equal(await page.locator('#voucher-type-name').inputValue(), 'Sales');
    results.push('permanent delete blocked|PASS');
    await page.keyboard.press('Escape');
    await page.waitForSelector('#voucher-types-form-root', { state: 'detached' });

    await page.keyboard.press('Alt+c');
    await page.waitForSelector('#voucher-types-form-root');
    await page.waitForFunction(() => window.texTrackKeyboardContexts.resolveActive()?.mode === 'Create');
    await expectFocus('voucher-type-name', 'custom create focus');
    const beforeProbe = await page.evaluate(() => window.__voucherTypeProbe);
    await page.keyboard.press('Alt+d');
    await page.waitForTimeout(80);
    assert.equal(await page.locator('[role="dialog"][aria-modal="true"]').count(), 0, 'create Alt+D opened dialog');
    await page.keyboard.press('F8');
    await page.waitForFunction(value => window.__voucherTypeProbe > value, beforeProbe);
    assert.equal(await page.evaluate(() => document.hasFocus()), true, 'create Alt+D moved focus to browser chrome');
    await page.locator('#voucher-type-name').fill(unique);
    await page.locator('#voucher-type-parent').selectOption({ label: 'Job Work Out Order' });
    await page.getByLabel('Abbreviation').fill('BVT');
    await page.locator('#voucher-type-tally-name').fill('Browser Tally Type');
    await page.keyboard.press('Alt+s');
    await page.waitForSelector('#voucher-types-form-root', { state: 'detached' });
    await page.waitForFunction(name => [...document.querySelectorAll('#voucher-types-grid tbody tr[id]')].some(row => row.textContent.includes(name)), unique);
    results.push('custom create|PASS');

    await search(unique);
    const originalRowId = await page.locator('#voucher-types-grid tbody tr[id]').first().getAttribute('id');
    await page.keyboard.press('Enter');
    await page.waitForSelector('#voucher-types-form-root');
    await expectFocus('voucher-type-name', 'custom alteration focus');
    await page.locator('#voucher-type-name').fill(unique + ' Renamed');
    await page.locator('#voucher-type-tally-name').fill('Browser Tally Renamed');
    await page.keyboard.press('Alt+s');
    await page.waitForSelector('#voucher-types-form-root', { state: 'detached' });
    await page.waitForFunction(rowId => document.getElementById(rowId)?.textContent.includes('Browser Tally Renamed'), originalRowId);
    assert.equal(await page.locator('#voucher-types-grid tbody tr.selected-row').getAttribute('id'), originalRowId, 'rename changed record ID');
    results.push('custom rename same ID|PASS');

    await page.keyboard.press('Enter');
    await page.waitForSelector('#voucher-types-form-root');
    await expectFocus('voucher-type-name', 'custom delete focus');
    await page.keyboard.press('Alt+d');
    await waitDialog();
    await page.keyboard.press('Escape');
    await page.waitForSelector('[role="dialog"][aria-modal="true"]', { state: 'detached' });
    await expectFocus('voucher-type-name', 'custom dialog Esc restoration');
    await page.keyboard.press('Alt+d');
    await confirmYes();
    await page.waitForSelector('#voucher-types-form-root', { state: 'detached' });
    await page.waitForFunction(name => ![...document.querySelectorAll('#voucher-types-grid tbody tr[id]')].some(row => row.textContent.includes(name)), unique);
    results.push('unused custom delete|PASS');

    await search(usedName);
    await page.keyboard.press('Enter');
    await page.waitForSelector('#voucher-types-form-root');
    await expectFocus(usedName === 'Material Out' ? 'voucher-type-tally-name' : 'voucher-type-name', 'used/permanent focus');
    assert.equal(await page.locator('#voucher-type-name').inputValue(), usedName);
    await page.keyboard.press('Alt+d');
    await confirmYes();
    await page.waitForFunction(() => /used in vouchers|Permanent system Voucher Types cannot be deleted/i.test(document.querySelector('.form-message.error')?.textContent || ''));
    assert.equal(await page.locator('#voucher-type-name').inputValue(), usedName, 'used custom record changed');
    results.push('used custom delete blocked|PASS');
    await page.keyboard.press('Escape');
    await page.waitForSelector('#voucher-types-form-root', { state: 'detached' });

    for (let cycle = 0; cycle < 3; cycle += 1) {
        await page.goto(baseUrl + '/', { waitUntil: 'domcontentloaded' });
        await page.goto(baseUrl + '/masters/voucher-types', { waitUntil: 'domcontentloaded' });
        await page.waitForFunction(() => window.texTrackKeyboardContexts?.resolveActive()?.contextId === 'voucher-types-list');
        const state = await page.evaluate(() => ({
            contexts: window.texTrackKeyboardContexts.contexts.size,
            listeners: window.texTrackKeyboardContexts.registrationCount
        }));
        assert.equal(state.contexts, 1, `cycle ${cycle}: stale context`);
        assert.equal(state.listeners, 1, `cycle ${cycle}: duplicate listener`);
    }
    results.push('focus and navigation cleanup|PASS');

    console.log(results.join('\n'));
    await browser.close();
})().catch(error => {
    console.error(error);
    process.exit(1);
});
