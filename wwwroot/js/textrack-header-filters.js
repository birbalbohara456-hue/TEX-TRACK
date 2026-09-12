(function () {
    'use strict';

    if (window.texTrackHeaderFiltersInstalled) return;
    window.texTrackHeaderFiltersInstalled = true;

    const states = new WeakMap();
    let popup = null;

    const rowsFor = function (table) {
        const columnCount = table.tHead?.rows[0]?.cells.length || 0;
        return Array.from(table.querySelectorAll(':scope > tbody > tr'))
            .filter(function (row) {
                return row instanceof HTMLTableRowElement && row.cells.length === columnCount &&
                    !row.classList.contains('jwa-detail-row') && !row.classList.contains('jwe-detail-row');
            });
    };

    const cellText = function (row, index) {
        return (row.cells[index]?.textContent || '').replace(/\s+/g, ' ').trim();
    };

    const apply = function (table) {
        const state = states.get(table);
        if (!state) return;
        for (const row of rowsFor(table)) {
            let shown = true;
            for (const [index, allowed] of state.filters.entries()) {
                if (allowed.size > 0 && !allowed.has(cellText(row, index))) { shown = false; break; }
            }
            row.hidden = !shown;
        }
        table.querySelectorAll('thead th').forEach(function (th, index) {
            th.classList.toggle('tt-column-filtered', state.filters.has(index) && state.filters.get(index).size > 0);
        });
        sessionStorage.setItem(state.storageKey, JSON.stringify(Array.from(state.filters, function (entry) {
            return [entry[0], Array.from(entry[1])];
        })));
        renderSummary(table, state);
    };

    const renderSummary = function (table, state) {
        if (!state.summary) return;
        const signature = JSON.stringify(Array.from(state.filters, function (entry) { return [entry[0], Array.from(entry[1])]; }));
        if (state.summarySignature === signature) return;
        state.summarySignature = signature;
        state.summary.replaceChildren();
        for (const [index, values] of state.filters.entries()) {
            if (values.size === 0) continue;
            const chip = document.createElement('button');
            chip.type = 'button';
            const title = table.tHead?.rows[0]?.cells[index]?.childNodes[0]?.textContent?.trim() || `Column ${index + 1}`;
            chip.textContent = `${title}: ${values.size === 1 ? Array.from(values)[0] : `${values.size} selected`} ×`;
            chip.addEventListener('click', function () { state.filters.delete(index); apply(table); });
            state.summary.appendChild(chip);
        }
        if (state.filters.size > 0) {
            const clear = document.createElement('button');
            clear.type = 'button'; clear.className = 'tt-clear-column-filters'; clear.textContent = 'Clear all filters';
            clear.addEventListener('click', function () { state.filters.clear(); apply(table); });
            state.summary.appendChild(clear);
        }
        state.summary.hidden = state.filters.size === 0;
    };

    const close = function () {
        if (popup) popup.remove();
        popup = null;
    };

    const open = function (table, index, button) {
        close();
        const state = states.get(table);
        if (!state) return;
        const allValues = Array.from(new Set(rowsFor(table).map(function (row) { return cellText(row, index); })))
            .sort(function (a, b) { return a.localeCompare(b, undefined, { numeric: true, sensitivity: 'base' }); });
        const active = state.filters.get(index);
        const selected = new Set(active && active.size > 0 ? active : allValues);

        popup = document.createElement('section');
        popup.className = 'tt-header-filter-popup';
        popup.setAttribute('role', 'dialog');
        popup.setAttribute('aria-modal', 'true');
        popup.setAttribute('aria-label', `Filter ${table.tHead?.rows[0]?.cells[index]?.textContent?.trim() || 'column'}`);
        popup.innerHTML = '<input class="tt-header-filter-search" type="search" placeholder="Search values…" aria-label="Search filter values">' +
            '<div class="tt-header-filter-actions"><button type="button" data-action="all">Select all</button><button type="button" data-action="current">Add current selection</button><button type="button" data-action="clear">Clear filter</button></div>' +
            '<div class="tt-header-filter-values"></div>' +
            '<footer><button type="button" data-action="apply">Apply</button><button type="button" data-action="cancel">Cancel</button></footer>';

        const valuesHost = popup.querySelector('.tt-header-filter-values');
        const allButton = popup.querySelector('[data-action="all"]');
        const matchingValues = function (search) {
            const term = (search || '').toLocaleLowerCase();
            return allValues.filter(function (value) { return value.toLocaleLowerCase().includes(term); });
        };
        const updateAllAction = function (visibleValues) {
            const allVisibleSelected = visibleValues.length > 0 && visibleValues.every(function (value) { return selected.has(value); });
            allButton.textContent = allVisibleSelected ? 'Deselect all' : 'Select all';
            allButton.dataset.mode = allVisibleSelected ? 'deselect' : 'select';
            allButton.disabled = visibleValues.length === 0;
        };
        const renderValues = function (search) {
            valuesHost.replaceChildren();
            const visibleValues = matchingValues(search);
            visibleValues.forEach(function (value) {
                    const label = document.createElement('label');
                    const check = document.createElement('input');
                    check.type = 'checkbox'; check.checked = selected.has(value);
                    check.addEventListener('change', function () {
                        check.checked ? selected.add(value) : selected.delete(value);
                        updateAllAction(visibleValues);
                    });
                    const span = document.createElement('span'); span.textContent = value || '(Blank)';
                    label.append(check, span); valuesHost.appendChild(label);
                });
            updateAllAction(visibleValues);
        };
        renderValues('');
        const search = popup.querySelector('.tt-header-filter-search');
        search.addEventListener('input', function () { renderValues(search.value || ''); });
        popup.addEventListener('click', function (event) {
            const action = event.target instanceof HTMLElement ? event.target.closest('[data-action]')?.dataset.action : null;
            if (!action) return;
            if (action === 'all') {
                const visibleValues = matchingValues(search.value || '');
                const shouldDeselect = visibleValues.length > 0 && visibleValues.every(function (value) { return selected.has(value); });
                visibleValues.forEach(function (value) { shouldDeselect ? selected.delete(value) : selected.add(value); });
                renderValues(search.value || '');
            }
            if (action === 'clear') { state.filters.delete(index); apply(table); close(); button.focus(); }
            if (action === 'current') {
                const current = rowsFor(table).find(function (row) { return row.classList.contains('selected-row') || row.classList.contains('selected'); });
                if (current) selected.add(cellText(current, index));
                renderValues(search.value || '');
            }
            if (action === 'apply') {
                if (selected.size === allValues.length) state.filters.delete(index);
                else state.filters.set(index, new Set(selected));
                apply(table); close(); button.focus();
            }
            if (action === 'cancel') { close(); button.focus(); }
        });
        popup.addEventListener('keydown', function (event) {
            if (event.key === 'Escape') { event.preventDefault(); close(); button.focus(); }
        });
        document.body.appendChild(popup);
        const rect = button.getBoundingClientRect();
        popup.style.top = `${Math.min(rect.bottom + 4, window.innerHeight - popup.offsetHeight - 8)}px`;
        popup.style.left = `${Math.max(8, Math.min(rect.right - popup.offsetWidth, window.innerWidth - popup.offsetWidth - 8))}px`;
        search.focus();
    };

    const enhance = function (table) {
        if (!(table instanceof HTMLTableElement) || table.dataset.ttHeaderFiltersReady === 'true') return;
        const headers = Array.from(table.querySelectorAll(':scope > thead > tr:first-child > th'));
        if (headers.length < 2 || rowsFor(table).length === 0) return;
        table.dataset.ttHeaderFiltersReady = 'true';
        const root = table.closest('[data-keyboard-list-root="true"], .page-card, .voucher-card');
        if (root instanceof HTMLElement) root.classList.add('tt-has-header-filters');
        const key = table.dataset.ttFilterKey || table.id || `${Array.from(document.querySelectorAll('table')).indexOf(table)}`;
        const storageKey = `textrack:header-filters:${location.pathname}:${key}`;
        let restored = [];
        try { restored = JSON.parse(sessionStorage.getItem(storageKey) || '[]'); } catch { restored = []; }
        const summary = document.createElement('div');
        summary.className = 'tt-active-column-filters'; summary.hidden = true;
        table.parentElement?.insertBefore(summary, table);
        const state = { filters: new Map(restored.map(function (entry) { return [Number(entry[0]), new Set(entry[1])]; })), storageKey, summary };
        states.set(table, state);
        headers.forEach(function (th, index) {
            const button = document.createElement('button');
            button.type = 'button'; button.className = 'tt-header-filter-button';
            button.setAttribute('aria-label', `Filter ${th.textContent.trim() || `column ${index + 1}`}`);
            button.textContent = '▼';
            button.addEventListener('click', function (event) { event.stopPropagation(); open(table, index, button); });
            th.appendChild(button);
        });
        apply(table);
    };

    const scan = function () {
        document.querySelectorAll('table.data-grid, table.selectable-grid, table[data-tt-header-filters="true"]').forEach(function (table) {
            enhance(table);
            apply(table);
        });
    };
    new MutationObserver(function () { window.requestAnimationFrame(scan); })
        .observe(document.documentElement, { childList: true, subtree: true });
    document.addEventListener('click', function (event) {
        if (popup && !popup.contains(event.target) && !(event.target instanceof HTMLElement && event.target.closest('.tt-header-filter-button'))) close();
    }, true);
    document.addEventListener('keydown', function (event) {
        if (event.key !== 'F4') return;
        const root = event.target instanceof HTMLElement ? event.target.closest('.tt-has-header-filters') : null;
        if (!root) return;
        event.preventDefault(); event.stopPropagation();
        if (event.stopImmediatePropagation) event.stopImmediatePropagation();
    }, true);
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', scan, { once: true }); else scan();
}());
