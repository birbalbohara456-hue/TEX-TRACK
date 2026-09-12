(function () {
    'use strict';

    if (window.texTrackKeyboardContexts) {
        return;
    }

    const priorities = Object.freeze({ Shell: 0, Page: 100, List: 200, Form: 300, Modal: 400 });
    const controller = {
        registrationCount: 1,
        activeConfirmation: false,
        contexts: new Map(),
        diagnosticsEnabled: false,
        lastRoutedCommand: null,
        sequence: 0,
        routing: false
    };

    const isVisible = function (element) {
        return element instanceof HTMLElement &&
            element.isConnected !== false &&
            element.getClientRects().length > 0 &&
            element.hidden !== true &&
            element.getAttribute('aria-hidden') !== 'true';
    };

    const prune = function () {
        for (const [contextId, context] of controller.contexts.entries()) {
            // A modal temporarily makes the page root inert/aria-hidden.  That
            // suspends the context; it must not unregister it.  Only detached
            // roots are stale registrations.
            if (!(context.element instanceof HTMLElement) || !context.element.isConnected) {
                controller.contexts.delete(contextId);
            }
        }
    };

    const captureVoucherState = function (element) {
        if (!isVisible(element)) return '[]';
        const controls = Array.from(element.querySelectorAll('input, select, textarea'));
        return JSON.stringify(controls.map(function (control, index) {
            return {
                key: control.id || control.name || `${control.tagName}:${index}`,
                type: control.type || control.tagName,
                value: control.value,
                checked: control.checked === true
            };
        }));
    };

    controller.register = function (contextId, contextType, mode, canDelete, rootElementId, dotNetRef, token, isVoucherEntry, handlesEscape, canCancel) {
        if (!contextId || !rootElementId || !dotNetRef || !token) return false;
        const element = document.getElementById(rootElementId);
        if (!isVisible(element)) return false;
        prune();
        const previous = controller.contexts.get(contextId);
        const voucherEntry = isVoucherEntry === true;
        controller.contexts.set(contextId, {
            contextId: contextId,
            contextType: contextType,
            mode: mode,
            canDelete: canDelete === true,
            priority: priorities[contextType] ?? priorities.Page,
            element: element,
            dotNetRef: dotNetRef,
            token: token,
            isVoucherEntry: voucherEntry,
            canCancel: canCancel === true,
            handlesEscape: voucherEntry || handlesEscape === true,
            initialVoucherState: previous && previous.token === token
                ? previous.initialVoucherState
                : (voucherEntry ? captureVoucherState(element) : null),
            order: ++controller.sequence
        });
        return true;
    };

    controller.unregister = function (contextId, token) {
        const current = controller.contexts.get(contextId);
        if (current && current.token === token) {
            controller.contexts.delete(contextId);
        }
    };

    controller.resolveActive = function () {
        prune();
        const registered = Array.from(controller.contexts.values())
            .filter(function (context) { return isVisible(context.element); })
            .sort(function (left, right) {
                return right.priority - left.priority || right.order - left.order;
            })[0];
        const unregisteredModal = Array.from(document.querySelectorAll('[role="dialog"][aria-modal="true"]'))
            .find(isVisible);
        if (unregisteredModal && (!registered || registered.priority < priorities.Modal)) {
            return {
                contextId: unregisteredModal.id || 'textrack-modal',
                contextType: 'Modal',
                mode: 'Modal',
                canDelete: false,
                priority: priorities.Modal,
                element: unregisteredModal,
                dotNetRef: null
            };
        }
        return registered || null;
    };

    controller.focusInitial = function (elementId) {
        window.requestAnimationFrame(function () {
            if (typeof window.texTrackFocusById === 'function') {
                window.texTrackFocusById(elementId);
            }
        });
    };

    controller.getActiveElementId = function () {
        const active = document.activeElement;
        if (!(active instanceof HTMLElement)) return null;
        if (!active.id) {
            active.id = `tt-voucher-return-${++controller.sequence}`;
        }
        return active.id;
    };

    controller.isVoucherDirty = function (contextId) {
        const context = controller.contexts.get(contextId);
        if (!context || !context.isVoucherEntry || !isVisible(context.element)) return false;
        return captureVoucherState(context.element) !== context.initialVoucherState;
    };

    controller.resetVoucherDirty = function (contextId) {
        const context = controller.contexts.get(contextId);
        if (!context || !context.isVoucherEntry || !isVisible(context.element)) return false;
        context.initialVoucherState = captureVoucherState(context.element);
        return true;
    };

    controller.configureDiagnostics = function (enabled) {
        controller.diagnosticsEnabled = enabled === true;
    };

    controller.getDiagnostics = function () {
        if (!controller.diagnosticsEnabled) return null;
        const active = controller.resolveActive();
        return {
            activeContextId: active?.contextId ?? 'shell',
            contextType: active?.contextType ?? 'Shell',
            mode: active?.mode ?? 'Shell',
            priority: active?.priority ?? priorities.Shell,
            connected: active ? isVisible(active.element) : true,
            focusedElement: document.activeElement?.id || document.activeElement?.tagName || null,
            registeredContextCount: controller.contexts.size,
            globalListenerCount: controller.registrationCount,
            lastRoutedCommand: controller.lastRoutedCommand
        };
    };

    const consume = function (event) {
        event.preventDefault();
        event.stopPropagation();
        event.stopImmediatePropagation();
    };

    // Blazor can replace a register/master search input while an oninput request
    // is being rendered. Keep the user's caret in that field until they
    // deliberately apply the filter or click somewhere else.
    let retainedFilterFocus = null;
    const releaseRetainedFilterFocus = function () {
        retainedFilterFocus = null;
    };
    const restoreRetainedFilterFocus = function () {
        if (!retainedFilterFocus) return;
        const field = document.getElementById(retainedFilterFocus.id);
        if (!(field instanceof HTMLInputElement) || !isVisible(field) ||
            field.dataset.ttFilterInput !== 'true') {
            releaseRetainedFilterFocus();
            return;
        }

        const active = document.activeElement;
        const activeOwnsFocus = active instanceof HTMLElement &&
            active !== document.body && active.isConnected !== false &&
            active !== field && active.matches('input, textarea, select, button, a[href]');
        if (activeOwnsFocus) return;

        field.focus({ preventScroll: true });
        if (typeof field.setSelectionRange === 'function') {
            try {
                const end = Math.min(retainedFilterFocus.end, field.value.length);
                field.setSelectionRange(Math.min(retainedFilterFocus.start, end), end);
            } catch { }
        }
    };
    const handleRetainedFilterInput = function (event) {
        const field = event.target;
        if (!(field instanceof HTMLInputElement) || field.dataset.ttFilterInput !== 'true' || !field.id) return;
        retainedFilterFocus = {
            id: field.id,
            start: field.selectionStart ?? field.value.length,
            end: field.selectionEnd ?? field.value.length
        };
        window.requestAnimationFrame(restoreRetainedFilterFocus);
        window.setTimeout(restoreRetainedFilterFocus, 45);
        window.setTimeout(restoreRetainedFilterFocus, 120);
        window.setTimeout(restoreRetainedFilterFocus, 300);
        window.setTimeout(restoreRetainedFilterFocus, 700);
        window.setTimeout(restoreRetainedFilterFocus, 1400);
    };
    document.addEventListener('pointerdown', function (event) {
        if (!retainedFilterFocus) return;
        const field = document.getElementById(retainedFilterFocus.id);
        if (event.target !== field) releaseRetainedFilterFocus();
    }, true);

    const voucherControls = function (form) {
        return Array.from(form.querySelectorAll(
            'input:not([type="hidden"]), select, textarea, button[data-enter-save="true"]'
        )).filter(function (element) {
            if (!(element instanceof HTMLElement)) return false;
            if (element.hasAttribute('disabled')) return false;
            if (element.getAttribute('aria-hidden') === 'true') return false;
            if (element.tabIndex < 0) return false;
            if (element instanceof HTMLInputElement && element.readOnly) return false;
            if (element instanceof HTMLTextAreaElement && element.readOnly) return false;
            return isVisible(element);
        });
    };

    const freshVoucherDates = new WeakSet();
    const pristineVoucherControls = new WeakSet();
    const monthNames = Object.freeze(['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun',
        'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec']);

    const isVoucherDate = function (element) {
        return element instanceof HTMLInputElement && element.matches('[data-tt-voucher-date="true"]');
    };

    const selectControl = function (element) {
        pristineVoucherControls.add(element);
        element.focus({ preventScroll: true });
        if (element instanceof HTMLInputElement || element instanceof HTMLTextAreaElement) {
            try { element.select(); } catch { }
        }
    };

    const moveRelative = function (form, current, offset) {
        const controls = voucherControls(form);
        const currentIndex = controls.indexOf(current);
        // A control can be legitimately focused (mouse click, or reached via the
        // Enter-forward engine's own "no next field -> focus Save" fallback) while
        // being excluded from `controls` itself - e.g. a Save/Accept button given
        // tabindex="-1" specifically so it isn't a normal indexed stop when its DOM
        // position doesn't match its logical place in the sequence (a relocated
        // titlebar Save button sitting before the fields it should follow). Such a
        // control is always logically positioned AFTER every real field, so
        // Backspace from it should walk back into the true last field rather than
        // silently doing nothing.
        const target = currentIndex >= 0
            ? controls[currentIndex + offset]
            : (offset < 0 ? controls[controls.length - 1] : undefined);
        if (!(target instanceof HTMLElement) || target === current) return false;
        selectControl(target);
        return true;
    };

    const activeVoucherFor = function (element) {
        const active = controller.resolveActive();
        if (active && active.contextType !== 'Modal' && active.isVoucherEntry === true &&
            active.element instanceof HTMLElement && active.element.contains(element)) {
            return active;
        }

        const renderedForm = element.closest('[data-tt-voucher-form-root="true"]');
        return renderedForm instanceof HTMLElement
            ? { element: renderedForm, isVoucherEntry: true, contextType: 'Form' }
            : null;
    };

    const dateNavigationRootFor = function (element) {
        const active = controller.resolveActive();
        if (active && active.element instanceof HTMLElement && active.element.contains(element)) {
            return active.element;
        }

        const root = element.closest('[data-tt-date-navigation="true"], [data-enter-navigation="true"], [data-tt-voucher-form-root="true"], [role="dialog"]');
        return root instanceof HTMLElement ? root : null;
    };

    const lookupPanelFor = function (element) {
        const lookupField = element.closest('.lookup-field');
        return lookupField?.querySelector('.erp-lookup-panel') || null;
    };

    const handleVoucherControlFocus = function (event) {
        const current = event.target;
        if (!(current instanceof HTMLElement) || !activeVoucherFor(current)) return;
        if (!['INPUT', 'SELECT', 'TEXTAREA'].includes(current.tagName)) return;

        const panel = lookupPanelFor(current);
        if (panel instanceof HTMLElement && panel.hidden) panel.hidden = false;
        pristineVoucherControls.add(current);
        window.requestAnimationFrame(function () {
            window.setTimeout(function () {
                if (document.activeElement === current && pristineVoucherControls.has(current)) {
                    selectControl(current);
                }
            }, 0);
        });
    };

    const handleVoucherControlInput = function (event) {
        const current = event.target;
        if (!(current instanceof HTMLElement) || !activeVoucherFor(current)) return;
        pristineVoucherControls.delete(current);
        const panel = lookupPanelFor(current);
        if (panel instanceof HTMLElement && panel.hidden) panel.hidden = false;
    };

    const markVoucherEditedByKey = function (event) {
        const current = event.target;
        if (!(current instanceof HTMLElement) || !activeVoucherFor(current)) return;
        if (event.altKey || event.ctrlKey || event.metaKey) return;
        if (String(event.key).length === 1 || event.key === 'Delete') {
            pristineVoucherControls.delete(current);
        }
    };

    const closeOpenLookup = function (event) {
        if (event.key !== 'Escape' || event.altKey || event.ctrlKey || event.metaKey) return false;
        const current = event.target;
        if (!(current instanceof HTMLElement) || !activeVoucherFor(current)) return false;
        const panel = lookupPanelFor(current);
        if (!isVisible(panel)) return false;

        consume(event);
        panel.hidden = true;
        delete panel.dataset.activeIndex;
        current.focus({ preventScroll: true });
        return true;
    };

    // A lookup's dropdown should close the moment the user moves on to a different field,
    // same as it would on Escape - not linger open (still showing stale matches) until some
    // other field's own focus handler happens to overwrite Blazor's activeLookupKey. Clicking
    // an option in the panel itself never reaches this: those buttons use
    // @onmousedown:preventDefault="true" specifically so the input never blurs during that
    // click, so a genuine focusout here always means focus is leaving for an unrelated field.
    // Client-side hide only (matching closeOpenLookup) - Blazor's own activeLookupKey re-syncs
    // next time this field is focused again (handleVoucherControlFocus below un-hides it).
    const closeLookupOnBlur = function (event) {
        const current = event.target;
        if (!(current instanceof HTMLElement) || !current.matches('[data-erp-lookup="true"]')) return;
        if (!activeVoucherFor(current)) return;
        const panel = lookupPanelFor(current);
        if (!isVisible(panel)) return;
        panel.hidden = true;
        delete panel.dataset.activeIndex;
        // The client-side hide above is not durable on its own: any later Blazor
        // re-render triggered by an unrelated event anywhere on the page regenerates
        // this field's @if block from the server's own activeLookupKey, which never
        // learned the lookup closed - resurrecting a dropdown the user already moved
        // away from (seen via Enter, Backspace, and likely any other navigation).
        // Tell the server directly wherever a page has registered a clear handler for
        // this field id; a no-op for ids no page has claimed yet.
        if (relatedMasterRefreshReference && current.id) {
            relatedMasterRefreshReference.invokeMethodAsync('ClearLookupOnBlurAsync', current.id).catch(function () {});
        }
    };

    // Escape on a field the user has actually typed into (not just tabbed onto - see
    // pristineVoucherControls) clears that uncommitted text and keeps focus right there,
    // same as Backspace-when-pristine jumps back instead of deleting nothing. Without this,
    // Escape had no field-local effect once closeOpenLookup found no open dropdown to close
    // (e.g. typed text with zero matches, so no panel to close) and fell straight through to
    // the voucher-level exit/quit-confirmation handling below - a stray Escape while
    // correcting a typo could pop the "Quit without saving?" prompt for the whole voucher.
    const clearDirtyFieldText = function (event) {
        if (event.key !== 'Escape' || event.altKey || event.ctrlKey || event.metaKey) return false;
        const current = event.target;
        if (!(current instanceof HTMLInputElement) && !(current instanceof HTMLTextAreaElement)) return false;
        if (!activeVoucherFor(current)) return false;
        if (pristineVoucherControls.has(current)) return false;
        if (current.value === '') return false;

        consume(event);
        current.value = '';
        current.dispatchEvent(new Event('input', { bubbles: true }));
        current.dispatchEvent(new Event('change', { bubbles: true }));
        pristineVoucherControls.add(current);
        current.focus({ preventScroll: true });
        return true;
    };

    const parseIsoDate = function (value) {
        const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value || '');
        if (!match) return null;
        return { year: Number(match[1]), month: Number(match[2]), day: Number(match[3]) };
    };

    const validDateParts = function (year, month, day) {
        if (year < 1 || month < 1 || month > 12 || day < 1) return false;
        const candidate = new Date(Date.UTC(year, month - 1, day));
        return candidate.getUTCFullYear() === year && candidate.getUTCMonth() === month - 1 && candidate.getUTCDate() === day;
    };

    const expandVoucherDate = function (element) {
        const raw = String(element.value || '').trim();
        const base = parseIsoDate(element.dataset.ttVoucherDateValue) || (() => {
            const today = new Date();
            return { year: today.getFullYear(), month: today.getMonth() + 1, day: today.getDate() };
        })();
        const digits = raw.replace(/\D/g, '');
        let year;
        let month;
        let day;

        const numericParts = raw.split(/[.\/\-\s]+/).filter(Boolean);
        const isNumericParts = numericParts.length > 1 && numericParts.every(part => /^\d+$/.test(part));
        if (isNumericParts && numericParts.length === 2) {
            day = Number(numericParts[0]);
            month = Number(numericParts[1]);
            year = base.year;
        } else if (isNumericParts && numericParts.length === 3) {
            day = Number(numericParts[0]);
            month = Number(numericParts[1]);
            year = Number(numericParts[2]);
            if (numericParts[2].length === 2) year += 2000;
        } else if (/^\d{1,2}$/.test(raw)) {
            day = Number(digits);
            month = base.month;
            year = base.year;
        } else if (/^\d{3,4}$/.test(raw)) {
            day = Number(digits.slice(0, digits.length - 2));
            month = Number(digits.slice(-2));
            year = base.year;
        } else if (/^\d{6}$/.test(raw)) {
            day = Number(digits.slice(0, 2));
            month = Number(digits.slice(2, 4));
            year = 2000 + Number(digits.slice(4));
        } else if (/^\d{8}$/.test(raw)) {
            day = Number(digits.slice(0, 2));
            month = Number(digits.slice(2, 4));
            year = Number(digits.slice(4));
        } else {
            const normalized = /^(\d{1,2})[-\s]([A-Za-z]{3})[-\s](\d{4})$/.exec(raw);
            if (!normalized) return null;
            day = Number(normalized[1]);
            month = monthNames.findIndex(name => name.toLowerCase() === normalized[2].toLowerCase()) + 1;
            year = Number(normalized[3]);
        }

        if (!validDateParts(year, month, day)) return null;
        const pad = value => String(value).padStart(2, '0');
        return {
            display: `${pad(day)}-${monthNames[month - 1]}-${year}`,
            iso: `${String(year).padStart(4, '0')}-${pad(month)}-${pad(day)}`
        };
    };

    const prepareNextVoucherDate = function (event) {
        if (event.key !== 'Enter' || event.altKey || event.ctrlKey || event.metaKey || event.shiftKey) return;
        const current = event.target;
        if (!(current instanceof HTMLElement) || isVoucherDate(current)) return;
        const active = controller.resolveActive();
        if (!active || active.contextType === 'Modal' || active.isVoucherEntry !== true ||
            !(active.element instanceof HTMLElement) || !active.element.contains(current)) return;
        const controls = voucherControls(active.element);
        const next = controls[controls.indexOf(current) + 1];
        if (!isVoucherDate(next)) return;

        freshVoucherDates.add(next);
        window.requestAnimationFrame(function () {
            if (document.activeElement === next) selectControl(next);
            window.setTimeout(function () {
                if (document.activeElement === next) selectControl(next);
            }, 0);
        });
    };

    const handleVoucherDateFocus = function (event) {
        const current = event.target;
        if (!isVoucherDate(current)) return;
        if (!dateNavigationRootFor(current)) return;

        freshVoucherDates.add(current);
        window.requestAnimationFrame(function () {
            window.setTimeout(function () {
                if (document.activeElement === current) selectControl(current);
            }, 0);
        });
    };

    const handleVoucherDateKey = function (event) {
        const current = event.target;
        if (!isVoucherDate(current)) return false;
        const navigationRoot = dateNavigationRootFor(current);
        if (!navigationRoot) return false;

        if (!event.altKey && !event.ctrlKey && !event.metaKey && /^\d$/.test(event.key)) {
            freshVoucherDates.delete(current);
            return false;
        }

        if (event.key === 'Backspace' && !event.altKey && !event.ctrlKey && !event.metaKey &&
            freshVoucherDates.has(current)) {
            consume(event);
            freshVoucherDates.delete(current);
            moveRelative(navigationRoot, current, -1);
            return true;
        }

        if (event.key === 'Backspace' && !event.altKey && !event.ctrlKey && !event.metaKey &&
            current.value.length === 0) {
            consume(event);
            const original = parseIsoDate(current.dataset.ttVoucherDateValue);
            if (original && validDateParts(original.year, original.month, original.day)) {
                current.value = `${String(original.day).padStart(2, '0')}-${monthNames[original.month - 1]}-${original.year}`;
            }
            moveRelative(navigationRoot, current, -1);
            return true;
        }

        if (event.key !== 'Enter' || event.altKey || event.ctrlKey || event.metaKey || event.shiftKey) return false;
        consume(event);
        const expanded = expandVoucherDate(current);
        if (!expanded) {
            selectControl(current);
            return true;
        }

        current.value = expanded.display;
        current.dataset.ttVoucherDateValue = expanded.iso;
        freshVoucherDates.delete(current);
        current.dispatchEvent(new Event('change', { bubbles: true }));
        moveRelative(navigationRoot, current, 1);
        return true;
    };

    const handleVoucherBackspace = function (event) {
        if (event.key !== 'Backspace' || event.altKey || event.ctrlKey || event.metaKey) return false;
        const current = event.target;
        if (!(current instanceof HTMLElement)) return false;
        const active = activeVoucherFor(current);
        if (!active) return false;
        const form = active.element;

        if (pristineVoucherControls.has(current)) {
            consume(event);
            moveRelative(form, current, -1);
            return true;
        }

        if (current instanceof HTMLInputElement || current instanceof HTMLTextAreaElement) {
            const value = current.value || '';
            const start = current.selectionStart;
            const end = current.selectionEnd;
            const hasSelection = start !== null && end !== null && start !== end;
            const canDeleteCharacter = value.length > 0 && (start === null || start > 0);
            if (hasSelection || canDeleteCharacter) return false;
        }

        consume(event);
        moveRelative(form, current, -1);
        return true;
    };

    const deleteFocusedLine = function (active, eventTarget) {
        if (!active || active.isVoucherEntry !== true || !(active.element instanceof HTMLElement) ||
            !(eventTarget instanceof HTMLElement) || !active.element.contains(eventTarget)) return false;
        const line = eventTarget.closest('[data-tt-line-item="true"], tr');
        const remove = line?.querySelector('[data-tt-line-delete="true"], button.line-remove');
        if (!(remove instanceof HTMLElement) || remove.hasAttribute('disabled') || !isVisible(remove)) return false;
        controller.lastRoutedCommand = {
            command: 'LineDelete',
            contextId: active.contextId,
            contextType: active.contextType,
            mode: active.mode
        };
        remove.click();
        return true;
    };

    const modalAcceptButton = function (active) {
        if (!active || active.contextType !== 'Modal' || !(active.element instanceof HTMLElement)) return null;
        const selectors = [
            '.dialog-selected:not([disabled])',
            '[data-tt-modal-accept="true"]:not([disabled])',
            '.tt-dialog-actions .action-save:not([disabled])',
            '.tt-dialog-actions .action-danger:not([disabled])',
            '.tt-dialog-actions .danger-button:not([disabled])',
            '.tt-dialog-actions .primary-button:not([disabled])',
            '.tt-dialog-actions button:first-child:not([disabled])'
        ];
        for (const selector of selectors) {
            const candidate = active.element.querySelector(selector);
            if (candidate instanceof HTMLElement && isVisible(candidate)) return candidate;
        }
        return null;
    };

    const route = function (active, command, methodName) {
        controller.routing = true;
        controller.lastRoutedCommand = {
            command: command,
            contextId: active.contextId,
            contextType: active.contextType,
            mode: active.mode
        };
        Promise.resolve(active.dotNetRef.invokeMethodAsync(methodName)).catch(function () {
            if (controller.contexts.get(active.contextId) === active) {
                controller.contexts.delete(active.contextId);
            }
        }).finally(function () {
            controller.routing = false;
        });
    };

    const focusVoucherDate = function (active) {
        if (!active || active.contextType === 'Modal' || active.isVoucherEntry !== true ||
            !(active.element instanceof HTMLElement)) return false;
        const date = active.element.querySelector('[data-tt-voucher-date="true"]');
        if (!(date instanceof HTMLInputElement) || !isVisible(date) || date.disabled || date.readOnly) return false;
        selectControl(date);
        controller.lastRoutedCommand = {
            command: 'VoucherDate', contextId: active.contextId,
            contextType: active.contextType, mode: active.mode
        };
        return true;
    };

    const openPeriodDialog = function () {
        const command = document.getElementById('tt-period-change-command');
        if (!(command instanceof HTMLElement) || command.hasAttribute('disabled')) return false;
        command.click();
        controller.lastRoutedCommand = {
            command: 'PeriodChange', contextId: 'application-shell',
            contextType: 'Shell', mode: 'Shell'
        };
        return true;
    };

    const clickShellCommand = function (elementId, commandName) {
        const command = document.getElementById(elementId);
        if (!(command instanceof HTMLElement) || command.hasAttribute('disabled')) return false;
        command.click();
        controller.lastRoutedCommand = {
            command: commandName, contextId: 'application-shell',
            contextType: 'Shell', mode: 'Shell'
        };
        return true;
    };

    window.texTrackLeaveCurrentPage = function (returnMenu, returnFocusId) {
        const routed = clickShellCommand('tt-leave-page-command', 'LeavePage');
        if (!routed) return false;

        let attempts = 0;
        let requestedMenu = false;
        const focusReturnedMenu = function () {
            attempts += 1;
            const root = document.querySelector('[data-tt-menu-root="true"]');
            if (!root && returnMenu && !requestedMenu &&
                document.getElementById('dashboard-page-root')) {
                requestedMenu = clickShellCommand(`tt-${returnMenu}-menu-command`, 'RestoreMenu');
                window.setTimeout(focusReturnedMenu, 35);
                return;
            }

            if (root && returnFocusId && !document.getElementById(returnFocusId)) {
                const childMatch = /^([a-z]+)-child-(\d+)-\d+$/.exec(returnFocusId);
                const parent = childMatch
                    ? document.getElementById(`${childMatch[1]}-item-${childMatch[2]}`)
                    : null;
                if (parent instanceof HTMLElement && parent.getAttribute('aria-expanded') !== 'true') {
                    parent.click();
                    window.setTimeout(focusReturnedMenu, 35);
                    return;
                }
            }

            const requestedTarget = returnFocusId ? document.getElementById(returnFocusId) : null;
            const target = requestedTarget ||
                root?.querySelector('[data-tt-menu-item="true"].keyboard-selected') ||
                root?.querySelector('[data-tt-menu-item="true"]');
            if (target instanceof HTMLElement && isVisible(target)) {
                target.focus({ preventScroll: true });
                if (document.activeElement === target) return;
            }
            if (attempts < 40) window.setTimeout(focusReturnedMenu, 35);
        };
        window.setTimeout(focusReturnedMenu, 0);
        return true;
    };

    // F3 related-master windows are deliberately short lived. The voucher
    // remains mounted in its original tab, so closing this window restores the
    // exact unsaved voucher and originating lookup field.
    window.texTrackCompleteRelatedMaster = function () {
        const returnFocusId = window.texTrackRelatedMasterReturnFocusId || '';
        const opener = window.opener;
        if (!opener || opener.closed) return false;
        try {
            opener.postMessage({
                type: 'textrack-related-master-complete',
                returnFocusId: returnFocusId
            }, window.location.origin);
            opener.focus();
            if (returnFocusId && typeof opener.texTrackFocusById === 'function') {
                opener.texTrackFocusById(returnFocusId);
            }
            window.setTimeout(function () { window.close(); }, 0);
            return true;
        } catch {
            return false;
        }
    };

    let relatedMasterRefreshReference = null;
    window.texTrackRegisterRelatedMasterRefresh = function (dotNetReference) {
        relatedMasterRefreshReference = dotNetReference || null;
    };
    window.texTrackUnregisterRelatedMasterRefresh = function (dotNetReference) {
        if (!dotNetReference || relatedMasterRefreshReference === dotNetReference) {
            relatedMasterRefreshReference = null;
        }
    };

    const installRelatedMasterCreateFlow = function () {
        const search = typeof window.location?.search === 'string' ? window.location.search : '';
        if (!/(?:\?|&)ttRelatedMaster=1(?:&|$)/.test(search)) return;

        const focusMatch = /(?:\?|&)ttReturnFocus=([^&]*)/.exec(search);
        window.texTrackRelatedMasterReturnFocusId = focusMatch
            ? decodeURIComponent(focusMatch[1].replace(/\+/g, ' '))
            : '';

        // The master page is server-rendered before its Blazor event handlers
        // become interactive. A single early click can therefore be ignored.
        // Retry the visible Create command until the form itself proves that
        // the click was handled; do not consider the first click sufficient.
        let createAttempts = 0;
        let lastCreateAttemptAt = 0;
        let createFormSeen = false;
        let completed = false;
        let retryTimer = 0;
        let observer = null;
        const stopCreateRetries = function () {
            if (!retryTimer) return;
            window.clearInterval(retryTimer);
            retryTimer = 0;
        };
        const finish = function () {
            completed = window.texTrackCompleteRelatedMaster();
            if (!completed) return;
            stopCreateRetries();
            if (observer) observer.disconnect();
        };
        const inspect = function () {
            if (completed) return;
            const save = document.querySelector('[data-enter-save="true"]');
            if (save instanceof HTMLElement && isVisible(save)) {
                createFormSeen = true;
                stopCreateRetries();
                return;
            }
            if (createFormSeen) {
                finish();
                return;
            }
            if (createAttempts >= 40) {
                stopCreateRetries();
                return;
            }
            const create = Array.from(document.querySelectorAll('.page-header button'))
                .find(function (button) {
                    return button instanceof HTMLElement && isVisible(button) &&
                        !button.hasAttribute('disabled') && /create/i.test(button.textContent || '');
                });
            if (create instanceof HTMLElement) {
                const now = Date.now();
                if (now - lastCreateAttemptAt < 150) return;
                lastCreateAttemptAt = now;
                createAttempts += 1;
                create.click();
            }
        };

        if (typeof MutationObserver === 'function' && document.documentElement) {
            observer = new MutationObserver(inspect);
            observer.observe(document.documentElement, { childList: true, subtree: true });
        }
        document.addEventListener('enhancedload', inspect, true);
        retryTimer = window.setInterval(inspect, 175);
        window.requestAnimationFrame(inspect);
        window.setTimeout(inspect, 50);
        window.setTimeout(inspect, 150);
        window.setTimeout(inspect, 400);
        window.setTimeout(inspect, 900);
    };

    window.addEventListener('message', async function (event) {
        if (event.origin !== window.location.origin ||
            event.data?.type !== 'textrack-related-master-complete') return;
        const returnFocusId = String(event.data.returnFocusId || '');
        if (relatedMasterRefreshReference) {
            try {
                await relatedMasterRefreshReference.invokeMethodAsync(
                    'RefreshRelatedMasterLookupsAsync', returnFocusId);
                return;
            } catch {
                relatedMasterRefreshReference = null;
            }
        }
        if (returnFocusId && typeof window.texTrackFocusById === 'function') {
            window.texTrackFocusById(returnFocusId);
        }
    });
    installRelatedMasterCreateFlow();

    const handleGlobalCommand = function (event) {
        prepareNextVoucherDate(event);
        if (handleVoucherDateKey(event)) return;
        if (handleVoucherBackspace(event)) return;
        if (closeOpenLookup(event)) return;
        if (clearDirtyFieldText(event)) return;
        markVoucherEditedByKey(event);

        const rawKey = String(event.key).toLowerCase();
        const altCodeKey = event.altKey && /^Key[A-Z]$/.test(String(event.code || ''))
            ? String(event.code).slice(3).toLowerCase()
            : '';
        const key = altCodeKey || rawKey;
        const isDelete = event.altKey && !event.ctrlKey && !event.metaKey && key === 'd';
        const isVoucherEscape = !event.altKey && !event.ctrlKey && !event.metaKey && key === 'escape';
        const isVoucherAccept = event.altKey && !event.ctrlKey && !event.metaKey && key === 'a';
        const isVoucherCancel = event.altKey && !event.ctrlKey && !event.metaKey && key === 'x';
        const isVoucherDateCommand = !event.altKey && !event.ctrlKey && !event.metaKey && key === 'f2';
        const isRelatedMasterCommand = !event.altKey && !event.ctrlKey && !event.metaKey && key === 'f3';
        const isPeriodCommand = event.altKey && !event.ctrlKey && !event.metaKey && key === 'f2';
        const isMastersMenuCommand = event.altKey && !event.ctrlKey && !event.metaKey && key === 'm';
        const isEntriesMenuCommand = event.altKey && !event.ctrlKey && !event.metaKey && (key === 't' || key === 'e');
        const isReportsMenuCommand = event.altKey && !event.ctrlKey && !event.metaKey && key === 'r';
        const isReportFilterCommand = event.altKey && !event.ctrlKey && !event.metaKey && key === 'f';

        if (isVoucherAccept) releaseRetainedFilterFocus();

        // F3 belongs to the focused ERP lookup. Open the related master in a
        // reusable companion tab so the unsaved voucher and its exact focus are
        // preserved. Even an intentionally unmapped lookup consumes F3, keeping
        // the browser Find bar out of the ERP workflow.
        if (isRelatedMasterCommand) {
            const field = event.target;
            if (!(field instanceof HTMLElement) || !field.matches('[data-erp-lookup="true"]')) return;
            consume(event);
            if (event.repeat || controller.routing) return;
            const route = String(field.dataset.ttRelatedMaster || '').trim();
            controller.lastRoutedCommand = {
                command: 'RelatedMaster', contextId: field.id || 'erp-lookup',
                contextType: 'Lookup', mode: route || 'Unmapped'
            };
            if (!route) return;
            const separator = route.includes('?') ? '&' : '?';
            const createRoute = `${route}${separator}ttRelatedMaster=1&ttReturnFocus=${encodeURIComponent(field.id || '')}`;
            const relatedMasterWindow = window.open(createRoute, 'textrack-related-master');
            if (relatedMasterWindow && typeof relatedMasterWindow.focus === 'function') {
                relatedMasterWindow.focus();
            }
            return;
        }

        if (isMastersMenuCommand || isEntriesMenuCommand || isReportsMenuCommand) {
            const active = controller.resolveActive();
            if (active?.contextType === 'Modal') return;
            const elementId = isMastersMenuCommand
                ? 'tt-masters-menu-command'
                : isEntriesMenuCommand
                    ? 'tt-entries-menu-command'
                    : 'tt-reports-menu-command';
            const commandName = isMastersMenuCommand ? 'MastersMenu' : isEntriesMenuCommand ? 'EntriesMenu' : 'ReportsMenu';
            const command = document.getElementById(elementId);
            if (!(command instanceof HTMLElement) || command.hasAttribute('disabled')) return;
            consume(event);
            if (event.repeat || controller.routing) return;
            clickShellCommand(elementId, commandName);
            return;
        }

        // Once a shell menu is open it owns Escape, arrows and Enter.  Do not
        // allow those keys to leak through to the voucher or report underneath
        // the menu.  The Alt+M/T/E/R branch above remains available so users can
        // switch directly between global menus without first closing one.
        if (document.querySelector('[data-tt-menu-root="true"]')) return;

        if (isReportFilterCommand) {
            const active = controller.resolveActive();
            if (active?.contextType === 'Modal') return;
            const command = document.getElementById('tt-report-voucher-search-command');
            if (!(command instanceof HTMLElement) || !isVisible(command) || command.hasAttribute('disabled')) return;
            consume(event);
            if (event.repeat || controller.routing) return;
            controller.lastRoutedCommand = {
                command: 'ReportVoucherSearch', contextId: active?.contextId ?? 'shell',
                contextType: active?.contextType ?? 'Shell', mode: active?.mode ?? 'Shell'
            };
            command.click();
            return;
        }

        if (!isDelete && !isVoucherEscape && !isVoucherAccept && !isVoucherCancel &&
            !isVoucherDateCommand && !isPeriodCommand) return;

        const active = controller.resolveActive();
        if (isPeriodCommand) {
            if (active?.contextType === 'Modal') return;
            const command = document.getElementById('tt-period-change-command');
            if (!(command instanceof HTMLElement) || command.hasAttribute('disabled')) return;
            consume(event);
            if (event.repeat || controller.routing) return;
            openPeriodDialog();
            return;
        }

        if (isVoucherDateCommand) {
            if (!active || active.contextType === 'Modal' || active.isVoucherEntry !== true) return;
            const date = active.element?.querySelector?.('[data-tt-voucher-date="true"]');
            if (!(date instanceof HTMLInputElement) || !isVisible(date) || date.disabled || date.readOnly) return;
            consume(event);
            if (event.repeat || controller.routing) return;
            focusVoucherDate(active);
            return;
        }

        if (isVoucherAccept && active?.contextType === 'Modal') {
            consume(event);
            if (event.repeat || controller.routing) return;
            const accept = modalAcceptButton(active);
            if (accept instanceof HTMLElement) {
                controller.lastRoutedCommand = {
                    command: 'ModalAccept', contextId: active.contextId,
                    contextType: active.contextType, mode: active.mode
                };
                accept.click();
            }
            return;
        }

        if (isDelete) {
            consume(event);
            if (event.repeat || controller.routing || !active || active.contextType === 'Modal') return;
            if (deleteFocusedLine(active, event.target)) return;
            if (!active.dotNetRef) return;
            route(active, 'Delete', 'HandleDeleteAsync');
            return;
        }

        if (isVoucherCancel) {
            if (!active || active.contextType === 'Modal' || active.isVoucherEntry !== true) return;
            consume(event);
            if (event.repeat || controller.routing || active.canCancel !== true || !active.dotNetRef) return;
            route(active, 'VoucherCancel', 'HandleCancelAsync');
            return;
        }

        const handlesCommand = isVoucherEscape ? active?.handlesEscape === true : active?.isVoucherEntry === true;
        if (!active || active.contextType === 'Modal' || !active.dotNetRef || !handlesCommand) return;

        consume(event);
        if (event.repeat || controller.routing) return;
        route(active, isVoucherEscape ? 'VoucherEscape' : 'VoucherAccept',
            isVoucherEscape ? 'HandleEscapeAsync' : 'HandleAcceptAsync');
    };

    document.addEventListener('keydown', handleGlobalCommand, true);
    document.addEventListener('focusin', handleVoucherDateFocus, true);
    document.addEventListener('focusin', handleVoucherControlFocus, true);
    document.addEventListener('focusout', closeLookupOnBlur, true);
    document.addEventListener('input', handleVoucherControlInput, true);
    document.addEventListener('input', handleRetainedFilterInput, true);
    window.texTrackKeyboardContexts = controller;
    window.texTrackConfirmDelete = function (title, message, returnFocusId) {
        if (controller.activeConfirmation) {
            return Promise.resolve(false);
        }

        controller.activeConfirmation = true;
        const previousFocus = document.activeElement;

        return new Promise(function (resolve) {
            const overlay = document.createElement('div');
            overlay.className = 'tt-dialog-overlay';
            overlay.setAttribute('role', 'presentation');
            overlay.setAttribute('data-tt-delete-confirmation', 'true');

            const dialog = document.createElement('section');
            dialog.className = 'tt-dialog';
            dialog.setAttribute('role', 'dialog');
            dialog.setAttribute('aria-modal', 'true');
            dialog.setAttribute('aria-labelledby', 'tt-global-delete-title');
            dialog.tabIndex = 0;

            const titleBar = document.createElement('header');
            titleBar.className = 'tt-dialog-titlebar';
            const titleText = document.createElement('strong');
            titleText.id = 'tt-global-delete-title';
            titleText.textContent = title || 'Delete?';
            titleBar.appendChild(titleText);

            const body = document.createElement('div');
            body.className = 'tt-dialog-body';
            const prompt = document.createElement('p');
            prompt.textContent = message || 'Delete the selected record?';
            body.appendChild(prompt);

            const actions = document.createElement('footer');
            actions.className = 'tt-dialog-actions';
            const yes = document.createElement('button');
            yes.type = 'button';
            yes.textContent = 'Yes';
            const no = document.createElement('button');
            no.type = 'button';
            no.textContent = 'No';
            no.classList.add('dialog-selected');
            actions.appendChild(yes);
            actions.appendChild(no);

            const hint = document.createElement('div');
            hint.className = 'tt-dialog-hint';
            hint.textContent = '\u2190/\u2192 Select \u00b7 Enter Confirm \u00b7 Esc Cancel';

            dialog.appendChild(titleBar);
            dialog.appendChild(body);
            dialog.appendChild(actions);
            dialog.appendChild(hint);
            overlay.appendChild(dialog);

            let selectYes = false;
            const updateSelection = function () {
                yes.classList.toggle('dialog-selected', selectYes);
                no.classList.toggle('dialog-selected', !selectYes);
                (selectYes ? yes : no).focus({ preventScroll: true });
            };

            const restoreFocus = function () {
                if (returnFocusId && typeof window.texTrackFocusById === 'function') {
                    window.texTrackFocusById(returnFocusId);
                    return;
                }
                if (previousFocus instanceof HTMLElement && previousFocus.isConnected) {
                    previousFocus.focus({ preventScroll: true });
                }
            };

            const close = function (confirmed) {
                dialog.removeEventListener('keydown', handleDialogKeyDown, true);
                overlay.remove();
                controller.activeConfirmation = false;
                window.requestAnimationFrame(restoreFocus);
                resolve(confirmed);
            };

            const handleDialogKeyDown = function (event) {
                if (event.key === 'ArrowLeft' || event.key === 'Left' ||
                    event.key === 'ArrowRight' || event.key === 'Right' ||
                    event.key === 'Tab') {
                    consume(event);
                    selectYes = !selectYes;
                    updateSelection();
                    return;
                }
                if (event.key === 'Enter') {
                    consume(event);
                    close(selectYes);
                    return;
                }
                if (event.key === 'Escape') {
                    consume(event);
                    close(false);
                }
            };

            yes.addEventListener('click', function () { close(true); }, { once: true });
            no.addEventListener('click', function () { close(false); }, { once: true });
            dialog.addEventListener('keydown', handleDialogKeyDown, true);
            document.body.appendChild(overlay);
            window.requestAnimationFrame(updateSelection);
        });
    };
}());

// Shared modal focus ownership. Blazor pages render several confirmation dialogs;
// this keeps the background inert and prevents a selected grid row from remaining
// the effective keyboard target while any modal is open.
(function () {
    'use strict';

    let activeDialog = null;
    let previousFocus = null;
    let inerted = [];

    const visible = function (element) {
        return element instanceof HTMLElement && element.isConnected &&
            element.hidden !== true && element.getClientRects().length > 0;
    };

    const focusables = function (dialog) {
        return Array.from(dialog.querySelectorAll(
            'button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), a[href], [tabindex]:not([tabindex="-1"])'))
            .filter(visible);
    };

    const setBackgroundInert = function (dialog) {
        inerted = [];
        let branch = dialog;
        while (branch && branch.parentElement) {
            const parent = branch.parentElement;
            for (const sibling of parent.children) {
                if (sibling === branch || !(sibling instanceof HTMLElement)) continue;
                inerted.push({ element: sibling, wasInert: sibling.inert === true, ariaHidden: sibling.getAttribute('aria-hidden') });
                sibling.inert = true;
                sibling.setAttribute('aria-hidden', 'true');
            }
            if (parent === document.body) break;
            branch = parent;
        }
        document.body.classList.add('tt-modal-open');
    };

    const restoreBackground = function () {
        for (const state of inerted) {
            if (!state.element.isConnected) continue;
            state.element.inert = state.wasInert;
            if (state.ariaHidden === null) state.element.removeAttribute('aria-hidden');
            else state.element.setAttribute('aria-hidden', state.ariaHidden);
        }
        inerted = [];
        document.body.classList.remove('tt-modal-open');
    };

    const initialTarget = function (dialog) {
        return dialog.querySelector('.dialog-selected:not([disabled]), [data-tt-modal-cancel="true"]:not([disabled]), textarea:not([disabled]), input:not([disabled]), .tt-dialog-actions button:last-child:not([disabled])') || dialog;
    };

    const handleKeyDown = function (event) {
        if (!activeDialog || event.key !== 'Tab') return;
        const items = focusables(activeDialog);
        if (items.length === 0) {
            event.preventDefault();
            activeDialog.focus({ preventScroll: true });
            return;
        }
        const current = items.indexOf(document.activeElement);
        const next = event.shiftKey
            ? (current <= 0 ? items.length - 1 : current - 1)
            : (current < 0 || current === items.length - 1 ? 0 : current + 1);
        event.preventDefault();
        items[next].focus({ preventScroll: true });
    };

    const handleFocusIn = function (event) {
        if (!activeDialog || activeDialog.contains(event.target)) return;
        const target = initialTarget(activeDialog);
        if (target instanceof HTMLElement) target.focus({ preventScroll: true });
    };

    const activate = function (dialog) {
        if (dialog === activeDialog) return;
        if (activeDialog) deactivate(false);
        activeDialog = dialog;
        previousFocus = document.activeElement instanceof HTMLElement ? document.activeElement : null;
        setBackgroundInert(dialog);
        document.addEventListener('keydown', handleKeyDown, true);
        document.addEventListener('focusin', handleFocusIn, true);
        window.requestAnimationFrame(function () {
            if (activeDialog !== dialog) return;
            const target = initialTarget(dialog);
            if (target instanceof HTMLElement) target.focus({ preventScroll: true });
        });
    };

    const deactivate = function (restoreFocus) {
        const target = previousFocus;
        activeDialog = null;
        previousFocus = null;
        document.removeEventListener('keydown', handleKeyDown, true);
        document.removeEventListener('focusin', handleFocusIn, true);
        restoreBackground();
        if (restoreFocus !== false && target instanceof HTMLElement && target.isConnected) {
            window.requestAnimationFrame(function () { target.focus({ preventScroll: true }); });
        }
    };

    const sync = function () {
        const dialogs = Array.from(document.querySelectorAll('[role="dialog"][aria-modal="true"]')).filter(visible);
        const top = dialogs.length === 0 ? null : dialogs[dialogs.length - 1];
        if (top) activate(top);
        else if (activeDialog) deactivate(true);
    };

    const observer = new MutationObserver(sync);
    const start = function () {
        observer.observe(document.body, { childList: true, subtree: true, attributes: true, attributeFilter: ['hidden', 'class', 'style'] });
        sync();
    };
    if (document.body) start();
    else document.addEventListener('DOMContentLoaded', start, { once: true });
}());
