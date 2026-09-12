const { chromium } = require('playwright');
const assert = require('node:assert/strict');

const baseUrl = process.env.TEXTRACK_BROWSER_BASE_URL || 'http://127.0.0.1:5075';
const screens = [
    { label: 'Ledger Groups', path: '/masters/ledger-groups', list: 'master-ledgergroup-list', form: 'master-ledgergroup-form', root: 'master-ledgergroup-page-root', formRoot: 'master-ledgergroup-form-root', first: 'master-name-input' },
    { label: 'Ledgers', path: '/masters/ledgers', list: 'ledgers-list', form: 'ledgers-form', root: 'ledger-page-root', formRoot: 'ledger-form-root', first: 'ledger-name-input' },
    { label: 'UQC', path: '/masters/uqc', list: 'master-uqc-list', form: 'master-uqc-form', root: 'master-uqc-page-root', formRoot: 'master-uqc-form-root', first: 'master-name-input' },
    { label: 'Stock Groups', path: '/masters/stock-groups', list: 'master-stockgroup-list', form: 'master-stockgroup-form', root: 'master-stockgroup-page-root', formRoot: 'master-stockgroup-form-root', createFirst: 'master-name-input', first: 'master-stockgroup-form-root' },
    { label: 'Stock Categories', path: '/masters/stock-categories', list: 'master-stockcategory-list', form: 'master-stockcategory-form', root: 'master-stockcategory-page-root', formRoot: 'master-stockcategory-form-root', first: 'master-name-input' },
    { label: 'Stock Items', path: '/masters/stock-items', list: 'stock-items-list', form: 'stock-items-form', root: 'stock-items-page-root', formRoot: 'stock-items-form-root', first: 'stock-item-name-input' },
    { label: 'Godowns', path: '/masters/godowns', list: 'godowns-list', form: 'godowns-form', root: 'godown-page-root', formRoot: 'godown-form-root', first: 'godown-name-input' },
    { label: 'Job Workers', path: '/masters/job-workers', list: 'job-workers-list', form: 'job-workers-form', root: 'job-worker-page-root', formRoot: 'job-worker-form-root', first: 'job-worker-name-input' },
    { label: 'Voucher Types', path: '/masters/voucher-types', list: 'voucher-types-list', form: 'voucher-types-form', root: 'voucher-types-page-root', formRoot: 'voucher-types-form-root', createFirst: 'voucher-type-name', first: 'voucher-type-tally-name' },
    { label: 'Colours', path: '/masters/colours', list: 'master-colour-list', form: 'master-colour-form', root: 'master-colour-page-root', formRoot: 'master-colour-form-root', first: 'master-name-input' },
    { label: 'Sizes', path: '/masters/sizes', list: 'master-size-list', form: 'master-size-form', root: 'master-size-page-root', formRoot: 'master-size-form-root', first: 'master-name-input' },
    { label: 'Processes', path: '/masters/processes', list: 'master-process-list', form: 'master-process-form', root: 'master-process-page-root', formRoot: 'master-process-form-root', first: 'master-name-input' },
    { label: 'Tax Classifications', path: '/masters/tax-classifications', list: 'master-taxclassification-list', form: 'master-taxclassification-form', root: 'master-taxclassification-page-root', formRoot: 'master-taxclassification-form-root', first: 'master-name-input' },
    { label: 'JWO', path: '/vouchers/job-work-out-order', list: 'jwo-list', form: 'jwo-form', root: 'jwo-page-root', formRoot: 'jwo-form-root', createFirst: 'jwo-voucher-type-input', first: 'jwo-date-input', confirmExit: true, doubleEscape: true },
    { label: 'Material Out', path: '/vouchers/material-out', list: 'material-out-list', form: 'material-out-form', root: 'mo-page-root', formRoot: 'mo-form-root', createFirstAny: ['mo-voucher-number', 'mo-date'], first: 'mo-date', confirmExit: true, doubleEscape: true, lookupEscape: true }
];

(async () => {
    const browser = await chromium.launch({
        headless: true,
        executablePath: process.env.TEXTRACK_BROWSER_EXECUTABLE || undefined
    });
    const page = await browser.newPage();
    const results = [];

    async function activeContext() {
        return page.evaluate(() => {
            const current = window.texTrackKeyboardContexts.resolveActive();
            return current && {
                id: current.contextId,
                type: current.contextType,
                mode: current.mode,
                connected: current.element?.isConnected === true,
                count: window.texTrackKeyboardContexts.contexts.size,
                listenerCount: window.texTrackKeyboardContexts.registrationCount
            };
        });
    }

    async function waitContext(id, mode) {
        await page.waitForFunction(([expectedId, expectedMode]) => {
            const current = window.texTrackKeyboardContexts?.resolveActive();
            return current?.contextId === expectedId && current?.mode === expectedMode && current.element?.isConnected;
        }, [id, mode]);
        const current = await activeContext();
        assert.equal(current.id, id);
        assert.equal(current.listenerCount, 1);
    }

    async function open(screen) {
        await page.goto(baseUrl + screen.path, { waitUntil: 'domcontentloaded' });
        await page.waitForFunction(() => window.texTrackKeyboardContexts?.registrationCount === 1);
        await waitContext(screen.list, 'List');
        await page.waitForSelector('tbody tr[id]');
        await page.evaluate(() => {
            window.__ttKeyboardProbe = 0;
            document.addEventListener('keydown', event => {
                if (event.key === 'F8') window.__ttKeyboardProbe += 1;
            }, true);
        });

    }

    async function expectApplicationFocus(expectedIds, label) {
        const choices = Array.isArray(expectedIds) ? expectedIds : [expectedIds];
        await page.waitForFunction(ids => ids.includes(document.activeElement?.id), choices);
        const state = await page.evaluate(() => ({ id: document.activeElement?.id, focused: document.hasFocus() }));
        assert.equal(state.focused, true, `${label}: document lost browser focus`);
        assert.ok(choices.includes(state.id), `${label}: unexpected focus ${state.id}`);
        return state.id;
    }

    async function altD(expectDialog, label) {
        const beforeUrl = page.url();
        const beforeProbe = await page.evaluate(() => window.__ttKeyboardProbe);
        await page.keyboard.press('Alt+d');
        if (expectDialog) {
            await page.waitForSelector('[role="dialog"][aria-modal="true"]');
        } else {
            await page.waitForTimeout(80);
        }
        assert.equal(page.url(), beforeUrl, `${label}: browser location changed`);
        assert.equal(await page.locator('[role="dialog"][aria-modal="true"]').count(), expectDialog ? 1 : 0, `${label}: dialog state`);
        await page.keyboard.press('F8');
        await page.waitForFunction(value => window.__ttKeyboardProbe > value, beforeProbe);
        assert.equal(await page.evaluate(() => document.hasFocus()), true, `${label}: browser chrome took focus`);
    }

    async function dismissDelete(expectedFocus, label) {
        await page.waitForFunction(() => document.activeElement?.closest('[role="dialog"][aria-modal="true"]'));
        await page.keyboard.press('Escape');
        await page.waitForFunction(() => !document.querySelector('[role="dialog"][aria-modal="true"]'));
        await expectApplicationFocus(expectedFocus, label);
    }

    for (const screen of screens) {
        console.log(`Opening ${screen.label}`);
        await open(screen);

        const deleteDialogExpected = screen.deleteDialog !== false;
        await altD(deleteDialogExpected, `${screen.label} list`);
        if (deleteDialogExpected) {
            await page.waitForFunction(() => document.activeElement?.closest('[role="dialog"][aria-modal="true"]'));
            await page.keyboard.press('Escape');
            await page.waitForFunction(() => !document.querySelector('[role="dialog"][aria-modal="true"]'));
            await page.waitForFunction(rootId => document.activeElement?.closest(`#${rootId}`), screen.root);
        }

        await page.keyboard.press('Alt+c');
        await page.waitForSelector(`#${screen.formRoot}`);
        await waitContext(screen.form, 'Create');
        const createFocus = await expectApplicationFocus(screen.createFirstAny || screen.createFirst || screen.first, `${screen.label} create`);
        await altD(false, `${screen.label} create`);
        assert.equal((await activeContext()).mode, 'Create');
        await page.keyboard.press('Enter');
        await page.waitForFunction(([formId, prior]) => {
            const active = document.activeElement;
            return active?.id !== prior && active?.closest(`#${formId}`);
        }, [screen.formRoot, createFocus]);
        await page.keyboard.press('Escape');
        if (screen.confirmExit) {
            if (screen.doubleEscape) {
                if (screen.lookupEscape) {
                    await page.waitForFunction(() => !document.querySelector('.erp-lookup-panel'));
                } else {
                    await page.waitForFunction(formId => document.activeElement?.id === formId, screen.formRoot);
                }
                await page.keyboard.press('Escape');
            }
            await page.waitForFunction(() => document.activeElement?.closest('[role="dialog"][aria-modal="true"]'));
            await page.keyboard.press('ArrowLeft');
            await page.keyboard.press('Enter');
        }
        await page.waitForSelector(`#${screen.formRoot}`, { state: 'detached' });
        await waitContext(screen.list, 'List');
        await page.waitForFunction(rootId => document.activeElement?.closest(`#${rootId}`), screen.root);

        await page.keyboard.press('Enter');
        await page.waitForSelector(`#${screen.formRoot}`);
        await waitContext(screen.form, 'Alteration');
        await expectApplicationFocus(screen.first, `${screen.label} alteration`);
        await altD(deleteDialogExpected, `${screen.label} alteration`);
        if (deleteDialogExpected) {
            await page.keyboard.press('Alt+d');
            assert.equal(await page.locator('[role="dialog"][aria-modal="true"]').count(), 1, `${screen.label}: modal re-entry`);
            await dismissDelete(screen.first, `${screen.label} modal Esc restore`);
            await altD(true, `${screen.label} alteration repeat`);
            await dismissDelete(screen.first, `${screen.label} repeat Esc restore`);
        } else {
            await expectApplicationFocus(screen.first, `${screen.label} protected alteration`);
            await altD(false, `${screen.label} protected alteration repeat`);
        }

        results.push(`${screen.label}|PASS|PASS|PASS|PASS|PASS`);
    }

    for (let cycle = 0; cycle < 3; cycle += 1) {
        for (const screen of screens) {
            await page.evaluate(destination => window.Blazor.navigateTo(destination), screen.path);
            await page.waitForURL(baseUrl + screen.path);
            await waitContext(screen.list, 'List');
            const state = await activeContext();
            assert.equal(state.count, 1, `${screen.label}: stale context after navigation`);
            assert.equal(state.listenerCount, 1, `${screen.label}: duplicate listener`);
        }
    }
    results.push('Repeated enhanced navigation|one live context|one listener|PASS');

    console.log('Screen|List Alt+D|Create consumed|Alteration dialog|Esc restore|No mouse needed');
    console.log(results.join('\n'));
    await browser.close();
})().catch(error => {
    console.error(error);
    process.exit(1);
});