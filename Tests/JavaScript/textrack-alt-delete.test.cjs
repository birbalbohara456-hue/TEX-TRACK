const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const projectRoot = path.resolve(__dirname, '../..');
const script = fs.readFileSync(path.join(projectRoot, 'wwwroot/js/textrack-alt-delete.js'), 'utf8');

function createHarness({ locationSearch = '', createFormOnAttempt = 0 } = {}) {
    const listeners = [];
    const elements = new Map();
    let modalElements = [];
    let menuRoot = null;
    let relatedMasterSave = null;
    let currentTime = 1000;
    const intervals = new Map();
    let nextIntervalId = 1;

    class FakeElement {
        constructor(id, { connected = true, visible = true } = {}) {
            this.id = id;
            this.isConnected = connected;
            this.visible = visible;
            this.hidden = false;
            this.tagName = 'DIV';
            this.controls = [];
            this.style = {};
            this.tabIndex = 0;
            this.disabled = false;
            this.readOnly = false;
            this.backspaceForm = null;
            this.lookupField = null;
            this.lineItem = null;
            this.lineDelete = null;
            this.modalAccept = null;
            this.clickCount = 0;
            this.dataset = {};
            this.dispatchedEvents = [];
            elements.set(id, this);
        }
        getClientRects() { return this.visible ? [{}] : []; }
        getAttribute(name) { return name === 'aria-hidden' ? null : null; }
        hasAttribute(name) { return name === 'disabled' ? this.disabled : false; }
        querySelectorAll(selector) {
            return selector === 'input, select, textarea' || selector.startsWith('input:not([type="hidden"])')
                ? this.controls
                : [];
        }
        querySelector(selector) {
            if (selector === '.erp-lookup-panel') return this.lookupPanel || null;
            if (selector === '[data-tt-menu-item="true"].keyboard-selected') return this.menuSelected || null;
            if (selector === '[data-tt-menu-item="true"]') return this.menuFirst || null;
            if (selector === '[data-tt-voucher-date="true"]') {
                return this.controls.find(control => control.dataset?.ttVoucherDate === 'true') || null;
            }
            if (selector.includes('data-tt-line-delete') || selector.includes('button.line-remove')) return this.lineDelete || null;
            if (selector.includes('dialog-selected') || selector.includes('data-tt-modal-accept') || selector.includes('tt-dialog-actions')) return this.modalAccept || null;
            return null;
        }
        closest(selector) {
            if (selector === '.lookup-field') return this.lookupField;
            if (selector.includes('data-tt-line-item') || selector.includes('tr')) return this.lineItem;
            return null;
        }
        contains(element) { return element === this || element.backspaceForm === this; }
        matches(selector) {
            if (selector === '[data-tt-voucher-date="true"]') return this.dataset.ttVoucherDate === 'true';
            if (selector === '[data-erp-lookup="true"]') return this.dataset.erpLookup === 'true';
            if (selector === 'input, textarea, select, button, a[href]') {
                return ['INPUT', 'TEXTAREA', 'SELECT', 'BUTTON', 'A'].includes(this.tagName);
            }
            return false;
        }
        focus() { document.activeElement = this; }
        select() {
            this.selected = true;
            if (typeof this.value === 'string') {
                this.selectionStart = 0;
                this.selectionEnd = this.value.length;
            }
        }
        setSelectionRange(start, end) { this.selectionStart = start; this.selectionEnd = end; }
        dispatchEvent(event) { this.dispatchedEvents.push(event.type); return true; }
        click() { this.clickCount += 1; }
    }

    class FakeInput extends FakeElement {
        constructor(id, options = {}) {
            super(id, options);
            this.tagName = 'INPUT';
            this.type = 'text';
            this.name = '';
            this.value = '';
            this.checked = false;
            this.selectionStart = 0;
            this.selectionEnd = 0;
        }
    }

    class FakeTextArea extends FakeElement {
        constructor(id, options = {}) {
            super(id, options);
            this.tagName = 'TEXTAREA';
            this.name = '';
            this.value = '';
            this.selectionStart = 0;
            this.selectionEnd = 0;
        }
    }

    const body = new FakeElement('body');
    body.appendChild = () => {};
    const relatedMasterCreate = locationSearch ? new FakeElement('related-master-create') : null;
    if (relatedMasterCreate) {
        relatedMasterCreate.tagName = 'BUTTON';
        relatedMasterCreate.textContent = 'Alt+C Create';
        relatedMasterCreate.click = function () {
            this.clickCount += 1;
            if (createFormOnAttempt > 0 && this.clickCount >= createFormOnAttempt && !relatedMasterSave) {
                relatedMasterSave = new FakeElement('related-master-save');
                relatedMasterSave.tagName = 'BUTTON';
            }
        };
    }
    const document = {
        activeElement: body,
        body,
        addEventListener(type, handler, capture) { listeners.push({ type, handler, capture }); },
        getElementById(id) { return elements.get(id) || null; },
        querySelectorAll(selector) {
            if (selector === '.page-header button') return relatedMasterCreate ? [relatedMasterCreate] : [];
            return selector === '[role="dialog"][aria-modal="true"]' ? modalElements : [];
        },
        querySelector(selector) {
            if (selector === '[data-enter-save="true"]') return relatedMasterSave;
            return selector === '[data-tt-menu-root="true"]' ? menuRoot : null;
        },
        createElement() { return new FakeElement(`created-${elements.size}`); }
    };
    const openedWindows = [];
    const windowListeners = [];
    const window = {
        requestAnimationFrame: callback => callback(),
        setTimeout: callback => callback(),
        setInterval(callback) { const id = nextIntervalId++; intervals.set(id, callback); return id; },
        clearInterval(id) { intervals.delete(id); },
        location: { search: locationSearch, origin: 'http://localhost:5091' },
        addEventListener(type, handler) { windowListeners.push({ type, handler }); },
        open(route, name) {
            const opened = { route, name, focusCount: 0, focus() { this.focusCount += 1; } };
            openedWindows.push(opened);
            return opened;
        }
    };
    class FakeEvent { constructor(type, options = {}) { this.type = type; this.bubbles = options.bubbles === true; } }
    class FakeMutationObserver {
        constructor(callback) { this.callback = callback; }
        observe() {}
        disconnect() {}
    }
    const context = vm.createContext({
        window, document, HTMLElement: FakeElement, HTMLInputElement: FakeInput,
        HTMLTextAreaElement: FakeTextArea, Event: FakeEvent, MutationObserver: FakeMutationObserver,
        Promise, Object, Array, String,
        Date: class extends Date { static now() { return currentTime; } }
    });
    vm.runInContext(script, context);

    return {
        context,
        listeners,
        element(id, options) { return new FakeElement(id, options); },
        input(id, options) { return new FakeInput(id, options); },
        textarea(id, options) { return new FakeTextArea(id, options); },
        openedWindows,
        windowListeners,
        relatedMasterCreate,
        advanceIntervals(milliseconds = 200) {
            currentTime += milliseconds;
            for (const callback of [...intervals.values()]) callback();
        },
        setModal(element) { modalElements = element ? [element] : []; },
        setMenuRoot(element) { menuRoot = element; }
    };
}

function altD(overrides = {}) {
    const calls = { prevent: 0, stop: 0, immediate: 0 };
    return {
        calls,
        event: {
            key: 'd', altKey: true, ctrlKey: false, metaKey: false, repeat: false,
            preventDefault() { calls.prevent += 1; },
            stopPropagation() { calls.stop += 1; },
            stopImmediatePropagation() { calls.immediate += 1; },
            ...overrides
        }
    };
}

function keyEvent(key, overrides = {}) {
    const calls = { prevent: 0, stop: 0, immediate: 0 };
    return {
        calls,
        event: {
            key, altKey: false, ctrlKey: false, metaKey: false, repeat: false,
            preventDefault() { calls.prevent += 1; },
            stopPropagation() { calls.stop += 1; },
            stopImmediatePropagation() { calls.immediate += 1; },
            ...overrides
        }
    };
}

function target(calls) {
    return { invokeMethodAsync(method) { calls.push(method); return Promise.resolve(); } };
}

async function dispatch(harness, event) {
    harness.listeners.find(x => x.type === 'keydown').handler(event);
    await new Promise(resolve => setImmediate(resolve));
}

test('installs exactly one capture listener even when evaluated twice', () => {
    const harness = createHarness();
    vm.runInContext(script, harness.context);
    assert.equal(harness.listeners.filter(x => x.type === 'keydown').length, 1);
    assert.equal(harness.listeners.filter(x => x.type === 'focusin').length, 2);
    assert.equal(harness.listeners.filter(x => x.type === 'input').length, 2);
    assert.equal(harness.listeners.filter(x => x.type === 'pointerdown').length, 1);
    assert.equal(harness.listeners.every(x => x.capture === true), true);
    assert.equal(harness.context.window.texTrackKeyboardContexts.registrationCount, 1);
});

test('F3 opens the related master and consumes the browser shortcut', async () => {
    const harness = createHarness();
    const field = harness.input('jwo-job-worker-input');
    field.dataset.erpLookup = 'true';
    field.dataset.ttRelatedMaster = '/masters/ledgers';
    const key = keyEvent('F3', { target: field });

    await dispatch(harness, key.event);

    assert.deepEqual(key.calls, { prevent: 1, stop: 1, immediate: 1 });
    assert.equal(harness.openedWindows.length, 1);
    assert.equal(harness.openedWindows[0].route, '/masters/ledgers?ttRelatedMaster=1&ttReturnFocus=jwo-job-worker-input');
    assert.equal(harness.openedWindows[0].name, 'textrack-related-master');
    assert.equal(harness.openedWindows[0].focusCount, 1);
});

test('F3 is safely consumed for an ERP lookup without a related master', async () => {
    const harness = createHarness();
    const field = harness.input('jwo-order-number');
    field.dataset.erpLookup = 'true';
    const key = keyEvent('F3', { target: field });

    await dispatch(harness, key.event);

    assert.deepEqual(key.calls, { prevent: 1, stop: 1, immediate: 1 });
    assert.equal(harness.openedWindows.length, 0);
});

test('related master retries Create when the server-rendered first click is ignored', () => {
    const harness = createHarness({
        locationSearch: '?ttRelatedMaster=1&ttReturnFocus=jwo-job-worker-input',
        createFormOnAttempt: 2
    });

    assert.equal(harness.relatedMasterCreate.clickCount, 1);
    harness.advanceIntervals(200);
    assert.equal(harness.relatedMasterCreate.clickCount, 2);

    // The second click exposed the creation form. Further polling detects its
    // Save command and stops instead of clicking Create again.
    harness.advanceIntervals(200);
    harness.advanceIntervals(200);
    assert.equal(harness.relatedMasterCreate.clickCount, 2);
});

test('a re-rendered register filter keeps focus and its caret while typing', () => {
    const harness = createHarness();
    const oldField = harness.input('voucher-history-search');
    oldField.dataset.ttFilterInput = 'true';
    oldField.value = 'BOM-MO';
    oldField.selectionStart = oldField.selectionEnd = 6;
    oldField.isConnected = false;

    const replacement = harness.input('voucher-history-search');
    replacement.dataset.ttFilterInput = 'true';
    replacement.value = 'BOM-MO';
    harness.context.document.activeElement = oldField;

    const retainedInputHandler = harness.listeners.filter(x => x.type === 'input')[1].handler;
    retainedInputHandler({ target: oldField });

    assert.equal(harness.context.document.activeElement, replacement);
    assert.equal(replacement.selectionStart, 6);
    assert.equal(replacement.selectionEnd, 6);
});

test('related-master completion refreshes voucher lookups before restoring focus', async () => {
    const harness = createHarness();
    const calls = [];
    harness.context.window.texTrackRegisterRelatedMasterRefresh({
        invokeMethodAsync(method, focusId) {
            calls.push({ method, focusId });
            return Promise.resolve();
        }
    });
    const handler = harness.windowListeners.find(x => x.type === 'message').handler;

    await handler({
        origin: 'http://localhost:5091',
        data: { type: 'textrack-related-master-complete', returnFocusId: 'jwo-job-worker-input' }
    });

    assert.deepEqual(calls, [{
        method: 'RefreshRelatedMasterLookupsAsync',
        focusId: 'jwo-job-worker-input'
    }]);
});

test('Alt+D is always consumed when no screen context is registered', async () => {
    const harness = createHarness();
    const key = altD();
    await dispatch(harness, key.event);
    assert.deepEqual(key.calls, { prevent: 1, stop: 1, immediate: 1 });
});

test('list context receives one delete command independently of focus', async () => {
    const harness = createHarness();
    const calls = [];
    harness.element('ledger-groups-root');
    harness.context.window.texTrackKeyboardContexts.register(
        'ledger-groups-list', 'List', 'List', true, 'ledger-groups-root', target(calls), 'list-token');
    const key = altD();
    await dispatch(harness, key.event);
    assert.deepEqual(calls, ['HandleDeleteAsync']);
    assert.equal(key.calls.prevent, 1);
});

test('form outranks list and create mode remains inside TexTrack', async () => {
    const harness = createHarness();
    const listCalls = [];
    const formCalls = [];
    harness.element('page-root');
    harness.element('form-root');
    const manager = harness.context.window.texTrackKeyboardContexts;
    manager.register('screen-list', 'List', 'List', true, 'page-root', target(listCalls), 'list');
    manager.register('screen-form', 'Form', 'Create', false, 'form-root', target(formCalls), 'form');
    const key = altD();
    await dispatch(harness, key.event);
    assert.deepEqual(listCalls, []);
    assert.deepEqual(formCalls, ['HandleDeleteAsync']);
    assert.deepEqual(key.calls, { prevent: 1, stop: 1, immediate: 1 });
});

test('modal precedence blocks duplicate Alt+D routing', async () => {
    const harness = createHarness();
    const calls = [];
    harness.element('form-root');
    harness.context.window.texTrackKeyboardContexts.register(
        'form', 'Form', 'Alteration', true, 'form-root', target(calls), 'form');
    harness.setModal(harness.element('delete-dialog'));
    await dispatch(harness, altD().event);
    assert.deepEqual(calls, []);
});

test('disconnected and hidden contexts are ignored and pruned', async () => {
    const harness = createHarness();
    const staleCalls = [];
    const listCalls = [];
    const stale = harness.element('stale-form');
    harness.element('live-list');
    const manager = harness.context.window.texTrackKeyboardContexts;
    manager.register('stale', 'Form', 'Alteration', true, 'stale-form', target(staleCalls), 'stale');
    manager.register('live', 'List', 'List', true, 'live-list', target(listCalls), 'live');
    stale.isConnected = false;
    await dispatch(harness, altD().event);
    assert.deepEqual(staleCalls, []);
    assert.deepEqual(listCalls, ['HandleDeleteAsync']);
    assert.equal(manager.contexts.has('stale'), false);
});

test('a context hidden by an inert modal stays registered - only detached roots are pruned', async () => {
    // Regression check: a modal makes the page root inert/aria-hidden, which makes the
    // suspended context's element temporarily invisible (isVisible() false) without ever
    // disconnecting it (isConnected stays true). prune() must key off isConnected only - if
    // it started pruning on visibility too, a context would lose its registration every time
    // a modal opened over it, instead of resuming once the modal closes.
    const harness = createHarness();
    const hiddenCalls = [];
    const form = harness.element('hidden-form');
    const manager = harness.context.window.texTrackKeyboardContexts;
    const registered = manager.register('hidden-context', 'Form', 'Alteration', true, 'hidden-form', target(hiddenCalls), 'hidden-token');
    assert.equal(registered, true);
    assert.equal(manager.contexts.has('hidden-context'), true);

    // A modal opening over the form makes it inert/aria-hidden - invisible, but still
    // connected to the DOM.
    form.visible = false;
    assert.equal(form.isConnected, true);
    manager.resolveActive();

    assert.equal(manager.contexts.has('hidden-context'), true);
});

test('duplicate IDs replace safely and stale disposal cannot unregister the replacement', async () => {
    const harness = createHarness();
    const oldCalls = [];
    const newCalls = [];
    harness.element('old-root');
    harness.element('new-root');
    const manager = harness.context.window.texTrackKeyboardContexts;
    manager.register('voucher-form', 'Form', 'Alteration', true, 'old-root', target(oldCalls), 'old-token');
    manager.register('voucher-form', 'Form', 'Alteration', true, 'new-root', target(newCalls), 'new-token');
    manager.unregister('voucher-form', 'old-token');
    await dispatch(harness, altD().event);
    assert.deepEqual(oldCalls, []);
    assert.deepEqual(newCalls, ['HandleDeleteAsync']);
    manager.unregister('voucher-form', 'new-token');
    assert.equal(manager.contexts.size, 0);
});

test('development diagnostics report ownership, focus, listener and last command', async () => {
    const harness = createHarness();
    harness.element('form-root');
    harness.context.document.activeElement = harness.element('first-field');
    const manager = harness.context.window.texTrackKeyboardContexts;
    manager.configureDiagnostics(true);
    manager.register('jwo-form', 'Form', 'Alteration', true, 'form-root', target([]), 'token');
    await dispatch(harness, altD().event);
    const diagnostic = manager.getDiagnostics();
    assert.equal(diagnostic.activeContextId, 'jwo-form');
    assert.equal(diagnostic.contextType, 'Form');
    assert.equal(diagnostic.focusedElement, 'first-field');
    assert.equal(diagnostic.registeredContextCount, 1);
    assert.equal(diagnostic.globalListenerCount, 1);
    assert.equal(diagnostic.lastRoutedCommand.command, 'Delete');
});

test('Escape, Alt+A and permitted Alt+X route only to the active voucher form and are consumed once', async () => {
    const harness = createHarness();
    const calls = [];
    harness.element('voucher-root');
    harness.context.window.texTrackKeyboardContexts.register(
        'voucher-form', 'Form', 'Alteration', false, 'voucher-root', target(calls), 'voucher-token', true, false, true);

    const escape = keyEvent('Escape');
    await dispatch(harness, escape.event);
    const accept = keyEvent('a', { altKey: true });
    await dispatch(harness, accept.event);
    const cancel = keyEvent('x', { altKey: true });
    await dispatch(harness, cancel.event);

    assert.deepEqual(calls, ['HandleEscapeAsync', 'HandleAcceptAsync', 'HandleCancelAsync']);
    assert.deepEqual(escape.calls, { prevent: 1, stop: 1, immediate: 1 });
    assert.deepEqual(accept.calls, { prevent: 1, stop: 1, immediate: 1 });
    assert.deepEqual(cancel.calls, { prevent: 1, stop: 1, immediate: 1 });
});

test('Ctrl+A remains native text selection and never routes voucher acceptance', async () => {
    const harness = createHarness();
    const calls = [];
    harness.element('voucher-root');
    harness.context.window.texTrackKeyboardContexts.register(
        'voucher-form', 'Form', 'Create', false, 'voucher-root', target(calls), 'voucher-token', true, false, false);

    const selectAll = keyEvent('a', { ctrlKey: true });
    await dispatch(harness, selectAll.event);

    assert.deepEqual(calls, []);
    assert.deepEqual(selectAll.calls, { prevent: 0, stop: 0, immediate: 0 });
});

test('F2 focuses and selects the shared Date field only in an active voucher entry', async () => {
    const harness = createHarness();
    const form = harness.element('voucher-root');
    const date = harness.input('voucher-date');
    date.dataset.ttVoucherDate = 'true';
    date.value = '30-Jul-2026';
    date.backspaceForm = form;
    form.controls.push(date);
    harness.context.window.texTrackKeyboardContexts.register(
        'voucher-form', 'Form', 'Create', false, 'voucher-root', target([]), 'voucher-token', true);

    const f2 = keyEvent('F2', { target: form });
    await dispatch(harness, f2.event);
    assert.deepEqual(f2.calls, { prevent: 1, stop: 1, immediate: 1 });
    assert.equal(harness.context.document.activeElement, date);
    assert.equal(date.selected, true);
    assert.equal(harness.context.window.texTrackKeyboardContexts.getDiagnostics(), null);

    const unsupported = createHarness();
    unsupported.element('ordinary-page');
    unsupported.context.window.texTrackKeyboardContexts.register(
        'ordinary', 'Page', 'Page', false, 'ordinary-page', target([]), 'page-token');
    const ignored = keyEvent('F2');
    await dispatch(unsupported, ignored.event);
    assert.deepEqual(ignored.calls, { prevent: 0, stop: 0, immediate: 0 });
});

test('Alt+F2 opens the one shell period command and does not open behind a modal', async () => {
    const harness = createHarness();
    const command = harness.element('tt-period-change-command');
    const period = keyEvent('F2', { altKey: true });
    await dispatch(harness, period.event);
    assert.deepEqual(period.calls, { prevent: 1, stop: 1, immediate: 1 });
    assert.equal(command.clickCount, 1);

    harness.setModal(harness.element('existing-modal'));
    const blocked = keyEvent('F2', { altKey: true });
    await dispatch(harness, blocked.event);
    assert.deepEqual(blocked.calls, { prevent: 0, stop: 0, immediate: 0 });
    assert.equal(command.clickCount, 1);
});

test('Alt+F opens voucher-number search only on a supporting report', async () => {
    const harness = createHarness();
    const command = harness.element('tt-report-voucher-search-command');
    const search = keyEvent('f', { altKey: true });
    await dispatch(harness, search.event);
    assert.deepEqual(search.calls, { prevent: 1, stop: 1, immediate: 1 });
    assert.equal(command.clickCount, 1);

    harness.setModal(harness.element('existing-modal'));
    const blocked = keyEvent('f', { altKey: true });
    await dispatch(harness, blocked.event);
    assert.deepEqual(blocked.calls, { prevent: 0, stop: 0, immediate: 0 });
    assert.equal(command.clickCount, 1);

    const unsupported = createHarness();
    const ignored = keyEvent('f', { altKey: true });
    await dispatch(unsupported, ignored.event);
    assert.deepEqual(ignored.calls, { prevent: 0, stop: 0, immediate: 0 });
});

test('Alt+M, Alt+T, Alt+E and Alt+R open shell menus from any non-modal screen', async () => {
    const harness = createHarness();
    const masters = harness.element('tt-masters-menu-command');
    const entries = harness.element('tt-entries-menu-command');
    const reports = harness.element('tt-reports-menu-command');

    for (const [keyName, expected] of [['m', masters], ['t', entries], ['e', entries], ['r', reports]]) {
        const key = keyEvent(keyName, { altKey: true });
        await dispatch(harness, key.event);
        assert.deepEqual(key.calls, { prevent: 1, stop: 1, immediate: 1 });
        assert.equal(expected.clickCount, keyName === 'e' ? 2 : 1);
    }

    harness.setModal(harness.element('existing-modal'));
    const blocked = keyEvent('r', { altKey: true });
    await dispatch(harness, blocked.event);
    assert.deepEqual(blocked.calls, { prevent: 0, stop: 0, immediate: 0 });
    assert.equal(reports.clickCount, 1);
});

test('shell menu shortcuts outrank focused fields in voucher creation and alteration', async () => {
    for (const mode of ['Create', 'Alteration']) {
        const harness = createHarness();
        const form = harness.element(`voucher-${mode}`);
        const lookup = harness.input(`party-${mode}`);
        lookup.backspaceForm = form;
        form.controls.push(lookup);
        harness.context.document.activeElement = lookup;
        harness.context.window.texTrackKeyboardContexts.register(
            `voucher-${mode}`, 'Form', mode, true, `voucher-${mode}`,
            target([]), `${mode}-token`, true, true, true);

        const masters = harness.element('tt-masters-menu-command');
        const entries = harness.element('tt-entries-menu-command');
        const reports = harness.element('tt-reports-menu-command');
        for (const [keyName, expected] of [['m', masters], ['t', entries], ['e', entries], ['r', reports]]) {
            const key = keyEvent(keyName, { altKey: true, target: lookup });
            await dispatch(harness, key.event);
            assert.deepEqual(key.calls, { prevent: 1, stop: 1, immediate: 1 });
            assert.equal(expected.clickCount, keyName === 'e' ? 2 : 1);
        }
    }
});

test('an open shell menu owns Escape without exiting the voucher underneath it', async () => {
    const harness = createHarness();
    const calls = [];
    harness.element('voucher-form');
    harness.context.window.texTrackKeyboardContexts.register(
        'voucher-form', 'Form', 'Alteration', true, 'voucher-form',
        target(calls), 'voucher-token', true, true, true);
    harness.setMenuRoot(harness.element('open-shell-menu'));

    const escape = keyEvent('Escape');
    await dispatch(harness, escape.event);
    assert.deepEqual(escape.calls, { prevent: 0, stop: 0, immediate: 0 });
    assert.deepEqual(calls, []);

    const reports = harness.element('tt-reports-menu-command');
    const switchMenu = keyEvent('r', { altKey: true });
    await dispatch(harness, switchMenu.event);
    assert.deepEqual(switchMenu.calls, { prevent: 1, stop: 1, immediate: 1 });
    assert.equal(reports.clickCount, 1);
});

test('Alt+R has no voucher-local binding that can compete with the global Reports menu', () => {
    const jwo = fs.readFileSync(path.join(projectRoot, 'Components/Pages/Vouchers/JobWorkOutOrder.razor'), 'utf8');
    assert.equal(jwo.includes('e.Key.Equals("r", StringComparison.OrdinalIgnoreCase)'), false);
});

test('shell menu shortcuts use physical letter codes when the browser changes Alt key text', async () => {
    const harness = createHarness();
    const reports = harness.element('tt-reports-menu-command');
    const shortcut = keyEvent('®', { altKey: true, code: 'KeyR' });
    await dispatch(harness, shortcut.event);
    assert.deepEqual(shortcut.calls, { prevent: 1, stop: 1, immediate: 1 });
    assert.equal(reports.clickCount, 1);
});

test('report return command restores focus to the selected menu item', () => {
    const harness = createHarness();
    const command = harness.element('tt-leave-page-command');
    const root = harness.element('reports-menu');
    const selected = harness.element('reports-selected-item');
    root.menuSelected = selected;
    root.menuFirst = selected;
    harness.setMenuRoot(root);

    assert.equal(harness.context.window.texTrackLeaveCurrentPage(), true);
    assert.equal(command.clickCount, 1);
    assert.equal(harness.context.document.activeElement, selected);
});

test('Alt+A confirms the active modal and Alt+D removes the focused voucher line', async () => {
    const modalHarness = createHarness();
    const dialog = modalHarness.element('confirm-dialog');
    const acceptButton = modalHarness.element('confirm-yes');
    dialog.modalAccept = acceptButton;
    modalHarness.setModal(dialog);

    const accept = keyEvent('a', { altKey: true, target: dialog });
    await dispatch(modalHarness, accept.event);
    assert.deepEqual(accept.calls, { prevent: 1, stop: 1, immediate: 1 });
    assert.equal(acceptButton.clickCount, 1);

    const lineHarness = createHarness();
    const form = lineHarness.element('jwo-form');
    const field = lineHarness.input('jwo-component');
    const row = lineHarness.element('component-row');
    const remove = lineHarness.element('component-remove');
    field.backspaceForm = form;
    field.lineItem = row;
    row.lineDelete = remove;
    form.controls.push(field);
    const calls = [];
    lineHarness.context.window.texTrackKeyboardContexts.register(
        'jwo-form', 'Form', 'Create', false, 'jwo-form', target(calls), 'token', true);

    const removeKey = altD({ target: field });
    await dispatch(lineHarness, removeKey.event);
    assert.deepEqual(removeKey.calls, { prevent: 1, stop: 1, immediate: 1 });
    assert.equal(remove.clickCount, 1);
    assert.deepEqual(calls, []);
});

test('Alt+X is consumed without routing when the active voucher cannot be cancelled', async () => {
    const harness = createHarness();
    const calls = [];
    harness.element('voucher-root');
    harness.context.window.texTrackKeyboardContexts.register(
        'voucher-form', 'Form', 'Create', false, 'voucher-root', target(calls), 'voucher-token', true, false, false);
    const cancel = keyEvent('x', { altKey: true });
    await dispatch(harness, cancel.event);
    assert.deepEqual(calls, []);
    assert.deepEqual(cancel.calls, { prevent: 1, stop: 1, immediate: 1 });
});

test('Escape remains untouched on unsupported forms and while a modal owns the keyboard', async () => {
    const harness = createHarness();
    const calls = [];
    harness.element('ordinary-form');
    harness.context.window.texTrackKeyboardContexts.register(
        'ordinary', 'Form', 'Create', false, 'ordinary-form', target(calls), 'ordinary-token');
    const unsupported = keyEvent('Escape');
    await dispatch(harness, unsupported.event);
    assert.deepEqual(calls, []);
    assert.deepEqual(unsupported.calls, { prevent: 0, stop: 0, immediate: 0 });

    harness.setModal(harness.element('quit-dialog'));
    const modalEscape = keyEvent('Escape');
    await dispatch(harness, modalEscape.event);
    assert.deepEqual(modalEscape.calls, { prevent: 0, stop: 0, immediate: 0 });
});

test('a voucher list with explicit Escape ownership exits on the first key press', async () => {
    const harness = createHarness();
    const calls = [];
    harness.element('jwo-list-root');
    harness.context.window.texTrackKeyboardContexts.register(
        'jwo-list', 'List', 'List', true, 'jwo-list-root', target(calls), 'list-token', false, true);
    const escape = keyEvent('Escape');
    await dispatch(harness, escape.event);
    assert.deepEqual(calls, ['HandleEscapeAsync']);
    assert.deepEqual(escape.calls, { prevent: 1, stop: 1, immediate: 1 });
});

test('voucher dirty state compares current controls with the registration baseline', () => {
    const harness = createHarness();
    const root = harness.element('voucher-root');
    root.controls.push({ id: 'party', name: '', tagName: 'INPUT', type: 'text', value: '', checked: false });
    const manager = harness.context.window.texTrackKeyboardContexts;
    manager.register('voucher', 'Form', 'Create', false, 'voucher-root', target([]), 'token', true);
    assert.equal(manager.isVoucherDirty('voucher'), false);
    root.controls[0].value = 'TEST LEDGER';
    assert.equal(manager.isVoucherDirty('voucher'), true);
    manager.resetVoucherDirty('voucher');
    assert.equal(manager.isVoucherDirty('voucher'), false);
});

test('active voucher field receives a stable return-focus id when it has none', () => {
    const harness = createHarness();
    const field = harness.element('');
    harness.context.document.activeElement = field;
    const manager = harness.context.window.texTrackKeyboardContexts;
    const generated = manager.getActiveElementId();
    assert.match(generated, /^tt-voucher-return-/);
    assert.equal(manager.getActiveElementId(), generated);
});

test('focus arrival marks a populated field pristine and Backspace navigates without erasing it', async () => {
    const harness = createHarness();
    const form = harness.element('voucher-form');
    const previous = harness.input('previous-field');
    const current = harness.input('current-field');
    previous.value = 'PREVIOUS';
    current.value = 'UNCHANGED';
    for (const control of [previous, current]) control.backspaceForm = form;
    form.controls.push(previous, current);
    harness.context.window.texTrackKeyboardContexts.register(
        'voucher', 'Form', 'Create', false, 'voucher-form', target([]), 'token', true);

    harness.context.document.activeElement = current;
    for (const listener of harness.listeners.filter(x => x.type === 'focusin')) listener.handler({ target: current });
    assert.equal(current.selected, true);

    const backspace = keyEvent('Backspace', { target: current });
    await dispatch(harness, backspace.event);
    assert.deepEqual(backspace.calls, { prevent: 1, stop: 1, immediate: 1 });
    assert.equal(current.value, 'UNCHANGED');
    assert.equal(harness.context.document.activeElement, previous);
    assert.equal(previous.selected, true);
});

test('Backspace keeps normal character deletion when text exists before the caret', async () => {
    const harness = createHarness();
    const form = harness.element('mo-form');
    const previous = harness.input('party');
    const current = harness.input('order');
    current.value = 'JWO-001';
    current.selectionStart = current.selectionEnd = 4;
    current.backspaceForm = form;
    form.controls.push(previous, current);
    harness.context.window.texTrackKeyboardContexts.register(
        'voucher', 'Form', 'Create', false, 'mo-form', target([]), 'token', true);

    const key = keyEvent('Backspace', { target: current });
    await dispatch(harness, key.event);

    assert.deepEqual(key.calls, { prevent: 0, stop: 0, immediate: 0 });
    assert.notEqual(harness.context.document.activeElement, previous);
});

test('Backspace on an empty field moves to the previous field and selects its value', async () => {
    const harness = createHarness();
    const form = harness.element('mo-form');
    const previous = harness.input('party');
    const current = harness.input('order');
    previous.value = 'TEST LEDGER';
    current.backspaceForm = form;
    form.controls.push(previous, current);
    harness.context.window.texTrackKeyboardContexts.register(
        'voucher', 'Form', 'Create', false, 'mo-form', target([]), 'token', true);

    const key = keyEvent('Backspace', { target: current });
    await dispatch(harness, key.event);

    assert.deepEqual(key.calls, { prevent: 1, stop: 1, immediate: 1 });
    assert.equal(harness.context.document.activeElement, previous);
    assert.equal(previous.selected, true);
    assert.equal(previous.value, 'TEST LEDGER');
});

test('Backspace at the start of populated text navigates backward without changing data', async () => {
    const harness = createHarness();
    const form = harness.element('mo-form');
    const previous = harness.input('party');
    const current = harness.input('order');
    previous.value = 'TEST LEDGER';
    current.value = 'JWO-001';
    current.selectionStart = current.selectionEnd = 0;
    current.backspaceForm = form;
    form.controls.push(previous, current);
    harness.context.window.texTrackKeyboardContexts.register(
        'voucher', 'Form', 'Create', false, 'mo-form', target([]), 'token', true);

    await dispatch(harness, keyEvent('Backspace', { target: current }).event);

    assert.equal(harness.context.document.activeElement, previous);
    assert.equal(current.value, 'JWO-001');
    assert.equal(previous.value, 'TEST LEDGER');
});

test('Backspace edits without hiding a lookup while Escape closes it and input reopens it', async () => {
    const harness = createHarness();
    const form = harness.element('mo-form');
    const lookup = harness.element('lookup');
    const panel = harness.element('lookup-panel');
    const current = harness.input('party');
    current.value = 'JOBBER1';
    current.selectionStart = current.selectionEnd = current.value.length;
    lookup.lookupPanel = panel;
    current.backspaceForm = form;
    current.lookupField = lookup;
    form.controls.push(current);
    harness.context.window.texTrackKeyboardContexts.register(
        'voucher', 'Form', 'Create', false, 'mo-form', target([]), 'token', true);

    const backspace = keyEvent('Backspace', { target: current });
    await dispatch(harness, backspace.event);
    assert.deepEqual(backspace.calls, { prevent: 0, stop: 0, immediate: 0 });
    assert.equal(panel.hidden, false);

    const escape = keyEvent('Escape', { target: current });
    await dispatch(harness, escape.event);
    assert.deepEqual(escape.calls, { prevent: 1, stop: 1, immediate: 1 });
    assert.equal(panel.hidden, true);
    assert.equal(harness.context.document.activeElement, current);

    harness.listeners.find(x => x.type === 'input').handler({ target: current });
    assert.equal(panel.hidden, false);
});

test('Backspace immediately returns from a freshly entered Date and preserves its value', async () => {
    const harness = createHarness();
    const form = harness.element('voucher-form');
    const previous = harness.input('voucher-number');
    const date = harness.input('voucher-date');
    const next = harness.input('party');
    date.dataset.ttVoucherDate = 'true';
    date.dataset.ttVoucherDateValue = '2026-04-01';
    date.value = '01-Apr-2026';
    for (const control of [previous, date, next]) control.backspaceForm = form;
    form.controls.push(previous, date, next);
    harness.context.window.texTrackKeyboardContexts.register(
        'voucher', 'Form', 'Create', false, 'voucher-form', target([]), 'token', true);

    await dispatch(harness, keyEvent('Enter', { target: previous }).event);
    date.focus();
    date.select();
    const backspace = keyEvent('Backspace', { target: date });
    await dispatch(harness, backspace.event);

    assert.deepEqual(backspace.calls, { prevent: 1, stop: 1, immediate: 1 });
    assert.equal(harness.context.document.activeElement, previous);
    assert.equal(date.value, '01-Apr-2026');
});

test('voucher Date is fully selected whenever focus actually lands on it', () => {
    const harness = createHarness();
    const form = harness.element('voucher-form');
    const date = harness.input('voucher-date');
    date.dataset.ttVoucherDate = 'true';
    date.dataset.ttVoucherDateValue = '2026-04-01';
    date.value = '01-Apr-2026';
    date.backspaceForm = form;
    form.controls.push(date);
    harness.context.window.texTrackKeyboardContexts.register(
        'voucher', 'Form', 'Create', false, 'voucher-form', target([]), 'token', true);

    date.focus();
    harness.listeners.find(x => x.type === 'focusin').handler({ target: date });

    assert.equal(date.selectionStart, 0);
    assert.equal(date.selectionEnd, date.value.length);
});

test('typed Date digits use normal Backspace editing before smart day expansion', async () => {
    const harness = createHarness();
    const form = harness.element('voucher-form');
    const previous = harness.input('voucher-number');
    const date = harness.input('voucher-date');
    const next = harness.input('party');
    date.dataset.ttVoucherDate = 'true';
    date.dataset.ttVoucherDateValue = '2026-04-01';
    date.value = '01-Apr-2026';
    for (const control of [previous, date, next]) control.backspaceForm = form;
    form.controls.push(previous, date, next);
    harness.context.window.texTrackKeyboardContexts.register(
        'voucher', 'Form', 'Create', false, 'voucher-form', target([]), 'token', true);

    await dispatch(harness, keyEvent('Enter', { target: previous }).event);
    date.focus();
    date.select();
    await dispatch(harness, keyEvent('5', { target: date }).event);
    date.value = '5';
    date.selectionStart = date.selectionEnd = 1;
    const backspace = keyEvent('Backspace', { target: date });
    await dispatch(harness, backspace.event);
    assert.deepEqual(backspace.calls, { prevent: 0, stop: 0, immediate: 0 });

    date.value = '5';
    date.selectionStart = date.selectionEnd = 1;
    const enter = keyEvent('Enter', { target: date });
    await dispatch(harness, enter.event);
    assert.equal(date.value, '05-Apr-2026');
    assert.equal(date.dataset.ttVoucherDateValue, '2026-04-05');
    assert.deepEqual(date.dispatchedEvents, ['change']);
    assert.equal(harness.context.document.activeElement, next);
});

test('four Date digits expand to day and month in the voucher year', async () => {
    const harness = createHarness();
    const form = harness.element('voucher-form');
    const date = harness.input('voucher-date');
    const next = harness.input('party');
    date.dataset.ttVoucherDate = 'true';
    date.dataset.ttVoucherDateValue = '2026-04-01';
    date.value = '0505';
    date.backspaceForm = form;
    next.backspaceForm = form;
    form.controls.push(date, next);
    harness.context.window.texTrackKeyboardContexts.register(
        'voucher', 'Form', 'Create', false, 'voucher-form', target([]), 'token', true);

    await dispatch(harness, keyEvent('Enter', { target: date }).event);
    assert.equal(date.value, '05-May-2026');
    assert.equal(date.dataset.ttVoucherDateValue, '2026-05-05');
});

test('voucher Date accepts Tally-style day month and year separators', async () => {
    const cases = [
        ['23', '23-Apr-2026', '2026-04-23'],
        ['23.5', '23-May-2026', '2026-05-23'],
        ['23/5', '23-May-2026', '2026-05-23'],
        ['23-5', '23-May-2026', '2026-05-23'],
        ['23 5', '23-May-2026', '2026-05-23'],
        ['23.5.24', '23-May-2024', '2024-05-23']
    ];
    for (const [typed, display, iso] of cases) {
        const harness = createHarness();
        const form = harness.element('voucher-form');
        const date = harness.input('voucher-date');
        const next = harness.input('party');
        date.dataset.ttVoucherDate = 'true';
        date.dataset.ttVoucherDateValue = '2026-04-01';
        date.value = typed;
        date.backspaceForm = form;
        next.backspaceForm = form;
        form.controls.push(date, next);
        harness.context.window.texTrackKeyboardContexts.register(
            'voucher', 'Form', 'Create', false, 'voucher-form', target([]), 'token', true);
        await dispatch(harness, keyEvent('Enter', { target: date }).event);
        assert.equal(date.value, display, typed);
        assert.equal(date.dataset.ttVoucherDateValue, iso, typed);
    }
});

test('voucher Date rejects impossible calendar dates and stays focused', async () => {
    const harness = createHarness();
    const form = harness.element('voucher-form');
    const date = harness.input('voucher-date');
    date.dataset.ttVoucherDate = 'true';
    date.dataset.ttVoucherDateValue = '2026-04-01';
    date.value = '31/2';
    date.backspaceForm = form;
    form.controls.push(date);
    harness.context.window.texTrackKeyboardContexts.register(
        'voucher', 'Form', 'Create', false, 'voucher-form', target([]), 'token', true);
    date.focus();
    await dispatch(harness, keyEvent('Enter', { target: date }).event);
    assert.equal(date.value, '31/2');
    assert.equal(harness.context.document.activeElement, date);
    assert.deepEqual(date.dispatchedEvents, []);
});

test('flexible period dates expand and Enter advances inside the modal', async () => {
    const harness = createHarness();
    const dialog = harness.element('tt-period-dialog');
    const from = harness.input('tt-period-from');
    const to = harness.input('tt-period-to');
    const accept = harness.element('period-accept');
    from.dataset.ttVoucherDate = 'true';
    from.dataset.ttVoucherDateValue = '2026-08-01';
    from.value = '23.5';
    to.dataset.ttVoucherDate = 'true';
    to.dataset.ttVoucherDateValue = '2027-03-31';
    to.value = '31.3.27';
    from.backspaceForm = dialog;
    to.backspaceForm = dialog;
    accept.backspaceForm = dialog;
    dialog.controls = [from, to, accept];
    harness.setModal(dialog);

    const firstEnter = keyEvent('Enter', { target: from });
    await dispatch(harness, firstEnter.event);
    assert.equal(from.value, '23-May-2026');
    assert.equal(to.selected, true);

    const secondEnter = keyEvent('Enter', { target: to });
    await dispatch(harness, secondEnter.event);
    assert.equal(to.value, '31-Mar-2027');
    assert.deepEqual(secondEnter.calls, { prevent: 1, stop: 1, immediate: 1 });
    assert.deepEqual(firstEnter.calls, { prevent: 1, stop: 1, immediate: 1 });
});

test('all implemented screens use the shared scope and no page-specific JS registration remains', () => {
    const files = [
        'Components/Pages/Masters/MasterPage.razor',
        'Components/Pages/Masters/Ledgers.razor',
        'Components/Pages/Masters/StockItems.razor',
        'Components/Pages/Masters/VoucherTypes.razor',
        'Components/Pages/Vouchers/JobWorkOutOrder.razor',
        'Components/Pages/Vouchers/MaterialIn.razor',
        'Components/Pages/Vouchers/MaterialOut.razor'
    ];
    for (const relative of files) {
        const source = fs.readFileSync(path.join(projectRoot, relative), 'utf8');
        assert.match(source, /KeyboardContextScope/);
        assert.doesNotMatch(source, /texTrackRegisterAltDeletePage|texTrackUnregisterAltDeletePage/);
    }
});

test('JWO destination Godown lookup is rendered and constrained inside the voucher card', () => {
    const source = fs.readFileSync(path.join(projectRoot, 'Components/Pages/Vouchers/JobWorkOutOrder.razor'), 'utf8');
    const css = fs.readFileSync(path.join(projectRoot, 'Components/Pages/Vouchers/JobWorkOutOrder.razor.css'), 'utf8');

    assert.match(source, /OpenDestinationGodownLookup/);
    assert.match(source, /destgodown:/);
    assert.match(source, /SelectDestinationGodown/);
    assert.match(css, /\.jwo-entry-form[^}]*position:\s*fixed[^}]*top:\s*0[^}]*left:\s*208px/);
    assert.match(css, /\.erp-lookup-panel[^}]*position:\s*absolute[^}]*left:\s*0[^}]*width:\s*100%[^}]*z-index:\s*99999/);
    assert.match(css, /button\.keyboard-selected/);
    // Component Allocation was merged into the Material Allocation popup (one continuous
    // BOM -> Process -> Component -> Qty -> UQC flow) rather than a separate nested modal -
    // .jwo-modal-overlay/jwo-material-allocation is the one popup context both now share.
    assert.match(css, /\.jwo-modal-overlay[^}]*position:\s*fixed[^}]*z-index:\s*1200/);
    assert.match(source, /ContextId="jwo-material-allocation"/);
});

test('all implemented voucher entry screens expose the Tally quit and Alt+A contract', () => {
    const files = [
        'Components/Pages/Vouchers/JobWorkOutOrder.razor',
        'Components/Pages/Vouchers/MaterialIn.razor',
        'Components/Pages/Vouchers/MaterialOut.razor'
    ];
    for (const relative of files) {
        const source = fs.readFileSync(path.join(projectRoot, relative), 'utf8');
        assert.match(source, /IsVoucherEntry="true"/);
        assert.match(source, /EscapeAsync=/);
        assert.match(source, /AcceptAsync=/);
        assert.match(source, /Quit\?/);
        assert.match(source, /StringComparison\.OrdinalIgnoreCase/);
        assert.match(source, /Alt\+A/);
        assert.doesNotMatch(source, /Ctrl\+A/);
        assert.doesNotMatch(source, /Alt\+S/);
    }
});

test('cancel-capable vouchers expose the shared Alt+X command contract', () => {
    for (const relative of [
        'Components/Pages/Vouchers/JobWorkOutOrder.razor',
        'Components/Pages/Vouchers/MaterialIn.razor',
        'Components/Pages/Vouchers/MaterialOut.razor'
    ]) {
        const source = fs.readFileSync(path.join(projectRoot, relative), 'utf8');
        assert.match(source, /CanCancel=/);
        assert.match(source, /CancelAsync=/);
    }
    const app = fs.readFileSync(path.join(projectRoot, 'Components/App.razor'), 'utf8');
    assert.doesNotMatch(app, /data-voucher-cancel-action/);
});

test('all primary voucher Date fields use the shared Tally date component', () => {
    for (const relative of [
        'Components/Pages/Vouchers/JobWorkOutOrder.razor',
        'Components/Pages/Vouchers/MaterialIn.razor',
        'Components/Pages/Vouchers/MaterialOut.razor'
    ]) {
        const source = fs.readFileSync(path.join(projectRoot, relative), 'utf8');
        assert.match(source, /<VoucherDateInput/);
    }
    const component = fs.readFileSync(
        path.join(projectRoot, 'Components/Keyboard/VoucherDateInput.razor'), 'utf8');
    assert.match(component, /data-tt-voucher-date="true"/);
    assert.match(component, /dd-MMM-yyyy/);
});

test('all voucher entry roots inherit global Backspace navigation and the old global Alt+S hook is absent', () => {
    const vouchers = [
        'Components/Pages/Vouchers/JobWorkOutOrder.razor',
        'Components/Pages/Vouchers/MaterialIn.razor',
        'Components/Pages/Vouchers/MaterialOut.razor'
    ];
    const app = fs.readFileSync(path.join(projectRoot, 'Components/App.razor'), 'utf8');
    const sharedFrame = fs.readFileSync(path.join(projectRoot, 'Components/Vouchers/Shared/VoucherEntryFrame.razor'), 'utf8');
    for (const relative of vouchers) {
        const source = fs.readFileSync(path.join(projectRoot, relative), 'utf8');
        if (source.includes('<VoucherEntryFrame')) {
            assert.match(sharedFrame, /data-tt-voucher-form-root="true"/);
        } else {
            assert.match(source, /data-tt-voucher-form-root="true"/);
        }
        assert.doesNotMatch(source, /data-backspace-navigation=/);
    }
    assert.match(script, /active\.isVoucherEntry !== true/);
    assert.doesNotMatch(app, /data-voucher-save-action/);
    assert.doesNotMatch(app, /Universal voucher Save shortcut/);
});

test('scope contract distinguishes create, alteration, list and modal ownership', () => {
    const source = fs.readFileSync(
        path.join(projectRoot, 'Components/Keyboard/KeyboardContextScope.razor'), 'utf8');
    assert.match(source, /KeyboardContextMode\.List or KeyboardContextMode\.Alteration/);
    assert.match(source, /RootElementId/);
    assert.match(source, /InitialFocusTarget/);
    assert.match(source, /registrationToken/);
    assert.match(source, /DisposeAsync/);
    assert.match(source, /HandleAcceptAsync/);
    assert.match(source, /HandleCancelAsync/);
    assert.match(source, /IsVoucherEntry/);
});
