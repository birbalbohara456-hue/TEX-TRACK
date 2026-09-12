const { chromium } = require('playwright');
const assert = require('node:assert/strict');

const baseUrl = process.env.TEXTRACK_BROWSER_BASE_URL || 'http://127.0.0.1:5075';
const executablePath = process.env.TEXTRACK_BROWSER_EXECUTABLE || undefined;
const unique = `Browser Godown ${Date.now()}`;

(async () => {
    const browser = await chromium.launch({ headless: true, executablePath });
    const page = await browser.newPage();
    const results = [];

    await page.goto(baseUrl + '/masters/godowns', { waitUntil: 'domcontentloaded' });
    await page.waitForFunction(() => window.texTrackKeyboardContexts?.resolveActive()?.contextId === 'godowns-list');
    await page.waitForSelector('tbody tr[id]');
    await page.evaluate(() => {
        window.__godownProbe = 0;
        document.addEventListener('keydown', event => {
            if (event.key === 'F8') window.__godownProbe += 1;
        }, true);
    });

    async function expectFocus(id, label) {
        await page.waitForFunction(expected => document.activeElement?.id === expected, id);
        assert.equal(await page.evaluate(() => document.hasFocus()), true, `${label}: application lost focus`);
    }

    async function assertAltDConsumedWithoutDialog(label) {
        const before = await page.evaluate(() => window.__godownProbe);
        const url = page.url();
        await page.keyboard.press('Alt+d');
        await page.waitForTimeout(80);
        assert.equal(await page.locator('[role="dialog"][aria-modal="true"]').count(), 0, `${label}: unexpected dialog`);
        assert.equal(page.url(), url, `${label}: URL changed`);
        await page.keyboard.press('F8');
        await page.waitForFunction(value => window.__godownProbe > value, before);
        assert.equal(await page.evaluate(() => document.hasFocus()), true, `${label}: browser chrome took focus`);
    }

    async function search(value) {
        await page.keyboard.press('F4');
        await expectFocus('godown-search', `search ${value}`);
        await page.keyboard.press('Control+a');
        await page.keyboard.type(value);
        await page.keyboard.press('Enter');
        await page.waitForFunction(expected => {
            const rows = [...document.querySelectorAll('#godown-grid tbody tr[id]')];
            return rows.length === 1 && rows[0].textContent.toLowerCase().includes(expected.toLowerCase());
        }, value);
    }

    await page.keyboard.press('Alt+c');
    await page.waitForSelector('#godown-form-root');
    await page.waitForFunction(() => window.texTrackKeyboardContexts.resolveActive()?.mode === 'Create');
    await expectFocus('godown-name-input', 'create focus');
    await assertAltDConsumedWithoutDialog('create Alt+D');
    await page.locator('#godown-name-input').fill(unique);
    await page.getByLabel('Alias').fill('BG-A');
    await page.getByLabel('Address Line 1').fill('Keyboard Industrial Estate');
    await page.getByLabel('City').fill('Surat');
    await page.getByLabel('State').fill('Gujarat');
    await page.keyboard.press('Alt+s');
    await page.waitForSelector('#godown-form-root', { state: 'detached' });
    await page.waitForFunction(expected => [...document.querySelectorAll('#godown-grid tbody tr[id]')].some(row => row.textContent.includes(expected)), unique);
    results.push('create|PASS');

    await search(unique);
    await page.keyboard.press('Enter');
    await page.waitForSelector('#godown-form-root');
    await page.waitForFunction(() => window.texTrackKeyboardContexts.resolveActive()?.mode === 'Alteration');
    await expectFocus('godown-name-input', 'alteration focus');
    await page.getByLabel('Alias').fill('BG-ALTERED');
    await page.getByLabel('City').fill('Ahmedabad');
    await page.keyboard.press('Alt+s');
    await page.waitForSelector('#godown-form-root', { state: 'detached' });
    await page.waitForFunction(() => document.querySelector('#godown-grid tbody tr[id]')?.textContent.includes('BG-ALTERED'));
    results.push('alter same record|PASS');

    await page.keyboard.press('Enter');
    await page.waitForSelector('#godown-form-root');
    await expectFocus('godown-name-input', 'delete focus');
    await page.keyboard.press('Alt+d');
    await page.waitForSelector('[role="dialog"][aria-modal="true"]');
    await page.waitForFunction(() => document.activeElement?.closest('[role="dialog"][aria-modal="true"]'));
    await page.keyboard.press('Alt+d');
    assert.equal(await page.locator('[role="dialog"][aria-modal="true"]').count(), 1, 'duplicate delete modal opened');
    await page.keyboard.press('Escape');
    await page.waitForSelector('[role="dialog"][aria-modal="true"]', { state: 'detached' });
    await expectFocus('godown-name-input', 'delete Esc restoration');
    await page.keyboard.press('Alt+d');
    await page.waitForSelector('[role="dialog"][aria-modal="true"]');
    await page.waitForFunction(() => document.activeElement?.closest('[role="dialog"][aria-modal="true"]'));
    await page.keyboard.press('ArrowLeft');
    await page.keyboard.press('Enter');
    await page.waitForSelector('[role="dialog"][aria-modal="true"]', { state: 'detached' });
    await page.waitForSelector('#godown-form-root', { state: 'detached' });
    await page.waitForFunction(expected => ![...document.querySelectorAll('#godown-grid tbody tr[id]')].some(row => row.textContent.includes(expected)), unique);
    results.push('unused delete and modal re-entry|PASS');

    await search('Jobber');
    await page.keyboard.press('Enter');
    await page.waitForSelector('#godown-form-root');
    await expectFocus('godown-name-input', 'linked alteration focus');
    await page.keyboard.press('Alt+d');
    await page.waitForSelector('[role="dialog"][aria-modal="true"]');
    await page.waitForFunction(() => !!document.activeElement?.closest('[role="dialog"][aria-modal="true"]'));
    await page.keyboard.press('ArrowLeft');
    await page.keyboard.press('Enter');
    await page.waitForSelector('[role="dialog"][aria-modal="true"]', { state: 'detached' });
    await page.waitForFunction(() => document.querySelector('.notice-error')?.textContent.includes('Job Work Out Orders'));
    assert.equal(await page.locator('#godown-form-root').count(), 1, 'linked Godown form unexpectedly closed');
    assert.equal((await page.locator('#godown-name-input').inputValue()).toUpperCase(), 'JOBBER1', 'linked Godown changed');
    results.push('linked delete blocked accurately|PASS');

    await page.keyboard.press('Escape');
    await page.waitForSelector('#godown-form-root', { state: 'detached' });
    await page.waitForFunction(() => window.texTrackKeyboardContexts.resolveActive()?.contextId === 'godowns-list');
    for (let cycle = 0; cycle < 3; cycle += 1) {
        await page.goto(baseUrl + '/', { waitUntil: 'domcontentloaded' });
        await page.goto(baseUrl + '/masters/godowns', { waitUntil: 'domcontentloaded' });
        await page.waitForFunction(() => window.texTrackKeyboardContexts?.resolveActive()?.contextId === 'godowns-list');
        const state = await page.evaluate(() => ({
            contexts: window.texTrackKeyboardContexts.contexts.size,
            listeners: window.texTrackKeyboardContexts.registrationCount
        }));
        assert.equal(state.contexts, 1, `cycle ${cycle}: stale context`);
        assert.equal(state.listeners, 1, `cycle ${cycle}: duplicate listener`);
    }
    results.push('repeated navigation cleanup|PASS');

    console.log(results.join('\n'));
    await browser.close();
})().catch(error => {
    console.error(error);
    process.exit(1);
});
