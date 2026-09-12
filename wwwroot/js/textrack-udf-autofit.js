// Auto-fit text sizing for UDF/custom-field inputs (currently Stock Items'
// Attributes). Keeps the field's own box size completely fixed - only the
// rendered font-size shrinks, bounded, so the full value stays visible
// instead of scrolling out of view or being ellipsis-truncated. Uses an
// offscreen canvas for text measurement (no layout thrashing) and only ever
// measures the specific input that actually changed, never the whole form.
(function () {
    if (window.texTrackUdfAutoFitInstalled) return;
    window.texTrackUdfAutoFitInstalled = true;

    var SELECTOR = 'input.stock-item-udf-autofit';
    var NORMAL_SIZE = 11.5;
    var MIN_SIZE = 9;

    var measureCanvas = null;
    function measureContext() {
        if (!measureCanvas) measureCanvas = document.createElement('canvas');
        return measureCanvas.getContext('2d');
    }

    function fitInput(input) {
        if (!input || !input.matches || !input.matches(SELECTOR)) return;
        var value = input.value;
        input.style.fontSize = NORMAL_SIZE + 'px';
        if (!value) return;

        var style = getComputedStyle(input);
        var available = input.clientWidth
            - (parseFloat(style.paddingLeft) || 0)
            - (parseFloat(style.paddingRight) || 0)
            - (parseFloat(style.borderLeftWidth) || 0)
            - (parseFloat(style.borderRightWidth) || 0);
        if (available <= 0) return;

        var ctx = measureContext();
        ctx.font = style.fontStyle + ' ' + style.fontWeight + ' ' + NORMAL_SIZE + 'px ' + style.fontFamily;
        var textWidth = ctx.measureText(value).width;
        if (textWidth <= available) return;

        var size = NORMAL_SIZE * (available / textWidth);
        size = Math.max(MIN_SIZE, Math.min(NORMAL_SIZE, size));
        input.style.fontSize = size.toFixed(2) + 'px';

        // Font metrics aren't perfectly linear with size - confirm the shrunk
        // size actually fits, and fall back to the floor size if not (the
        // value still stays fully readable via the input's own native
        // scroll-on-focus and the title attribute tooltip already set in markup).
        if (size > MIN_SIZE) {
            ctx.font = style.fontStyle + ' ' + style.fontWeight + ' ' + size + 'px ' + style.fontFamily;
            if (ctx.measureText(value).width > available) {
                input.style.fontSize = MIN_SIZE + 'px';
            }
        }
    }

    function fitAllWithin(root) {
        if (!root || !root.querySelectorAll) return;
        var inputs = root.matches && root.matches(SELECTOR) ? [root] : root.querySelectorAll(SELECTOR);
        for (var i = 0; i < inputs.length; i++) fitInput(inputs[i]);
    }

    // Typing: fit only the field being edited, on every input event - cheap
    // (one canvas measurement), no debounce, so it never lags keyboard input.
    document.addEventListener('input', function (event) {
        if (event.target && event.target.matches && event.target.matches(SELECTOR)) {
            fitInput(event.target);
        }
    }, true);

    // Window resize changes each field's available width - re-fit only the
    // fields currently on screen. A short setTimeout debounce (not
    // requestAnimationFrame, which can simply never fire in a backgrounded
    // or non-visible tab/pane) coalesces the rapid-fire resize events a drag
    // produces into one pass once the size settles.
    var resizeTimer = null;
    window.addEventListener('resize', function () {
        if (resizeTimer) clearTimeout(resizeTimer);
        resizeTimer = setTimeout(function () {
            resizeTimer = null;
            document.querySelectorAll(SELECTOR).forEach(fitInput);
        }, 80);
    });

    // Edit mode loads existing (possibly long) values without any input
    // event firing - catch the fields appearing in the DOM (initial render,
    // or switching back to the Item & Attributes tab) and fit them once.
    var rootObserver = new MutationObserver(function (mutations) {
        for (var i = 0; i < mutations.length; i++) {
            var mutation = mutations[i];
            if (mutation.type !== 'childList' || mutation.addedNodes.length === 0) continue;
            for (var n = 0; n < mutation.addedNodes.length; n++) {
                var node = mutation.addedNodes[n];
                if (node.nodeType === 1) fitAllWithin(node);
            }
        }
    });
    rootObserver.observe(document.body, { childList: true, subtree: true });

    // Cover whatever is already on the page when this script first runs.
    fitAllWithin(document.body);
})();
