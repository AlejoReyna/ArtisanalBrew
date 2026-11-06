// Behaviour for the navbar's "Infraestructura" panel (Components/Layout/InfrastructurePanel.razor).
//
// Blazor owns the open/closed state and the panel's content. This file owns only what Razor
// cannot do declaratively: the <html> class that makes room for the panel (infra-panel.css),
// Escape handling, a focus trap while the panel is a mobile overlay, returning focus to the
// trigger, and making sure the page's scroll position survives an open/close cycle.
(function () {
    'use strict';

    var OPEN_CLASS = 'infra-panel-open';
    var TRIGGER_ID = 'infra-trigger';
    var PANEL_ID = 'infra-panel';
    var OVERLAY_QUERY = '(max-width: 1023.98px)';
    var SETTLE_MS = 450; // panel/page transition is 320ms

    var state = null;

    function panel() { return document.getElementById(PANEL_ID); }

    function isOverlay() {
        return window.matchMedia && window.matchMedia(OVERLAY_QUERY).matches;
    }

    function focusables(root) {
        return Array.prototype.filter.call(
            root.querySelectorAll('a[href], button:not([disabled]), [tabindex]:not([tabindex="-1"])'),
            function (el) { return el.offsetParent !== null || el === document.activeElement; });
    }

    function onKeydown(event) {
        if (!state) { return; }

        if (event.key === 'Escape' && !event.defaultPrevented) {
            event.preventDefault();
            state.ref.invokeMethodAsync('CloseFromScript').catch(function () { });
            return;
        }

        if (event.key === 'Tab' && isOverlay()) {
            var root = panel();
            if (!root) { return; }
            var items = focusables(root);
            if (!items.length) { return; }
            var first = items[0];
            var last = items[items.length - 1];
            if (event.shiftKey && (document.activeElement === first || !root.contains(document.activeElement))) {
                event.preventDefault();
                last.focus();
            } else if (!event.shiftKey && (document.activeElement === last || !root.contains(document.activeElement))) {
                event.preventDefault();
                first.focus();
            }
        }
    }

    function open(dotNetRef) {
        if (state) { return; }
        var trigger = document.getElementById(TRIGGER_ID);
        state = {
            ref: dotNetRef,
            openX: window.scrollX,
            openY: window.scrollY,
            settledY: null,
            opener: document.activeElement && document.activeElement !== document.body ? document.activeElement : trigger
        };

        document.documentElement.classList.add(OPEN_CLASS);
        document.addEventListener('keydown', onKeydown, true);

        // Compressing the page reflows it, and the browser's scroll anchoring may nudge scrollY to
        // keep the same content in view. Remember where it settled so close() can tell whether
        // the reader scrolled on their own while the panel was open.
        state.settleTimer = window.setTimeout(function () {
            if (state) { state.settledY = window.scrollY; }
        }, SETTLE_MS);

        // The panel becomes focusable (it is `inert` while closed) only after Blazor renders it.
        // Wait for that render, then move focus in without letting the browser scroll the page.
        var attempts = 0;
        (function focusPanel() {
            var close = document.querySelector('#' + PANEL_ID + ' [data-infra-close]');
            var root = panel();
            if (close && root && !root.hasAttribute('inert')) {
                close.focus({ preventScroll: true });
            } else if (attempts++ < 20) {
                window.requestAnimationFrame(focusPanel);
            }
        })();
    }

    function close() {
        if (!state) { return; }
        var saved = state;
        state = null;
        window.clearTimeout(saved.settleTimer);

        // If the reader did not scroll while the panel was open, the reflow is the only thing that
        // moved the page: put it back exactly where it was before opening. If they did scroll,
        // leave their position alone (scroll anchoring keeps their content in view).
        var current = window.scrollY;
        var settled = saved.settledY === null ? current : saved.settledY;
        var untouched = Math.abs(current - settled) <= 2;

        document.removeEventListener('keydown', onKeydown, true);
        document.documentElement.classList.remove(OPEN_CLASS);

        // Return focus to whatever opened the panel (normally the navbar button).
        var target = document.getElementById(TRIGGER_ID);
        if (saved.opener && document.contains(saved.opener) && saved.opener.offsetParent !== null) {
            target = saved.opener;
        }
        if (target) { target.focus({ preventScroll: true }); }

        if (untouched) {
            var restore = function () {
                if (Math.abs(window.scrollY - saved.openY) > 1 || Math.abs(window.scrollX - saved.openX) > 1) {
                    // html has scroll-behavior: smooth; restoring must not animate.
                    window.scrollTo({ left: saved.openX, top: saved.openY, behavior: 'instant' });
                }
            };
            window.requestAnimationFrame(restore);
            // Again once the width transition has finished and the layout is back to normal.
            window.setTimeout(function () { if (!state) { restore(); } }, SETTLE_MS);
        }
    }

    window.infraPanel = { open: open, close: close };
})();
