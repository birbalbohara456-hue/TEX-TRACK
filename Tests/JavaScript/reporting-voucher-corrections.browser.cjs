const assert = require('node:assert/strict');
const { chromium } = require('playwright');

(async () => {
    const browser = await chromium.launch({ executablePath: 'C:/Program Files/Google/Chrome/Application/chrome.exe', headless: true });
    const page = await browser.newPage({ viewport: { width: 1440, height: 1000 } });
    const base = process.env.TEXTRACK_BROWSER_BASE_URL || 'http://127.0.0.1:5075';
    const context = () => page.evaluate(() => window.texTrackKeyboardContexts?.resolveActive()?.contextId);
    try {
        await page.goto(base + '/vouchers/material-in', { waitUntil: 'networkidle' });
        await page.waitForFunction(() => window.texTrackKeyboardContexts?.resolveActive()?.contextId === 'material-in-list');
        assert.equal(await context(), 'material-in-list');
        await page.locator('#mi-page-root').focus();
        await page.keyboard.press('Alt+d');
        assert.equal(await page.evaluate(() => document.hasFocus()), true);
        await page.keyboard.press('Alt+c');
        await page.waitForFunction(() => window.texTrackKeyboardContexts?.resolveActive()?.contextId === 'material-in-create');
        await page.waitForFunction(() => ['mi-voucher-number', 'mi-date'].includes(document.activeElement?.id));
        await page.keyboard.press('Alt+d');
        assert.equal(await page.evaluate(() => document.hasFocus()), true);
        await page.keyboard.press('Escape');
        await page.waitForFunction(() => window.texTrackKeyboardContexts?.resolveActive()?.contextId === 'material-in-list');
        const miRows = page.locator('#mi-grid tbody tr[tabindex="-1"]');
        if (await miRows.count()) {
            await page.locator('#mi-page-root').focus();
            await page.keyboard.press('Enter');
            await page.waitForFunction(() => window.texTrackKeyboardContexts?.resolveActive()?.contextId === 'material-in-view');
            assert.equal(await page.locator('label:has-text("Original JWO") input').inputValue() !== '', true);
            await page.keyboard.press('Escape');
            await page.waitForFunction(() => window.texTrackKeyboardContexts?.resolveActive()?.contextId === 'material-in-list');
        }

        await page.goto(base + '/vouchers/job-work-out-order', { waitUntil: 'networkidle' });
        await page.waitForFunction(() => window.texTrackKeyboardContexts?.resolveActive()?.contextId === 'jwo-list');
        await page.locator('#jwo-page-root').focus();
        await page.keyboard.press('Alt+c');
        await page.waitForFunction(() => window.texTrackKeyboardContexts?.resolveActive()?.contextId === 'jwo-form');
        await page.waitForFunction(() => document.activeElement?.id === 'jwo-voucher-type-input');
        await page.keyboard.press('Enter');
        await page.waitForFunction(() => ['jwo-voucher-number', 'jwo-date-input'].includes(document.activeElement?.id));
        const jwoFocus = await page.evaluate(() => document.activeElement?.id);
        const manual = await page.locator('#jwo-voucher-number').evaluate(el => !el.readOnly);
        assert.equal(jwoFocus, manual ? 'jwo-voucher-number' : 'jwo-date-input');
        assert.equal(await page.evaluate(() => document.hasFocus()), true);
        await page.keyboard.press('Enter');
        await page.waitForFunction(expected => document.activeElement?.id === expected, manual ? 'jwo-date-input' : 'jwo-job-worker-input');
        assert.equal(await page.evaluate(() => document.activeElement?.id), manual ? 'jwo-date-input' : 'jwo-job-worker-input');

        console.log('PASS real Chrome: Material In list/create/view keyboard lifecycle, Alt+D containment, original JWO view, and JWO second-field Enter navigation.');
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
