// Light/dark/system theme switch. Distinct from window.themeColor
// (publicHeader.js), which tints the mobile browser-chrome meta tag — this
// module owns the `data-theme` attribute on <html> that app.css's CSS
// variables key off of.
//
// Loaded blocking in <head>, before app.css, the same way heroType.js and
// productCardBlurb.js are: the immediate apply() call below runs before the
// stylesheet is applied and before first paint, so there's no flash of the
// wrong theme.
window.artisanalTheme = {
    STORAGE_KEY: 'theme',
    _systemListenerBound: false,

    getStored() {
        try {
            return localStorage.getItem(this.STORAGE_KEY);
        } catch {
            return null;
        }
    },

    // What the toggle should read as right now: the explicit choice if one is
    // stored, else 'system'.
    current() {
        const stored = this.getStored();
        return stored === 'light' || stored === 'dark' ? stored : 'system';
    },

    // Sets (or clears) the data-theme attribute. 'system' removes it entirely
    // so the @media (prefers-color-scheme: dark) tier in app.css decides.
    apply(value) {
        if (value === 'light' || value === 'dark') {
            document.documentElement.setAttribute('data-theme', value);
        } else {
            document.documentElement.removeAttribute('data-theme');
        }
    },

    set(value) {
        try {
            if (value === 'light' || value === 'dark') {
                localStorage.setItem(this.STORAGE_KEY, value);
            } else {
                localStorage.removeItem(this.STORAGE_KEY);
            }
        } catch {
            // localStorage unavailable (private browsing, etc.) — still apply
            // for the current page load even though it won't persist.
        }
        this.apply(value);
    },

    // Cycles system -> light -> dark -> system. Returns the new state so
    // Blazor call sites can update their own toggle icon/label without a
    // second round trip.
    cycle() {
        const order = ['system', 'light', 'dark'];
        const next = order[(order.indexOf(this.current()) + 1) % order.length];
        this.set(next);
        return next;
    }
};

window.artisanalTheme.apply(window.artisanalTheme.getStored());
