// Scroll-driven hero → discovery transition for the pixel homepage.
//
// heroScroll.init() writes --hero-scroll (0 at the top → 1 one viewport down)
// onto .ph-page and opts in with .has-hero-scroll, which the scoped stylesheets
// (PixelHome.razor.css, ProductDiscovery.razor.css) read to pin the hero in
// place, dissolve its copy letter-by-letter, and slide the Market Scout panel
// up over it. Scrolling back up reverses it. .is-past-hero marks the fully-gone
// end state so the copy can drop out of the tab order.
//
// Without JS — or under prefers-reduced-motion — the class is never added and
// the page keeps its plain document scroll: content is never hidden by default.
//
// Blazor survival: PixelHome renders InteractiveServer, so the circuit swaps
// out the prerendered .ph-page nodes after this module's DOMContentLoaded run,
// and later re-renders can recreate the subtree again — both with no
// enhancedload to hook. Three layers cover it: update() re-queries .ph-page on
// every frame instead of closing over it, a childList-only MutationObserver
// re-runs init() the moment a fresh .ph-page lands, and PixelHome calls
// queueHeroScroll() from OnAfterRenderAsync(firstRender).
window.heroScroll = window.heroScroll || {
    _controller: null,
    _ticking: false,
    _observer: null,

    prefersReducedMotion() {
        return window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    },

    init() {
        this.destroy();

        if (this.prefersReducedMotion()) {
            return;
        }

        if (!document.querySelector('.ph-page')) {
            return;
        }

        const update = () => {
            // Re-queried every frame: a prerender handoff or re-render may have
            // replaced the node init() first saw, and the scroll handler must
            // keep writing to the live one.
            const page = document.querySelector('.ph-page');
            if (!page) {
                return;
            }

            if (!page.classList.contains('has-hero-scroll')) {
                page.classList.add('has-hero-scroll');
            }

            // innerHeight can read 0 right after a navigation, before the
            // viewport is measured; fall back to the document client height.
            const viewport = window.innerHeight || document.documentElement.clientHeight || 1;
            const progress = Math.min(1, Math.max(0, window.scrollY / viewport));
            page.style.setProperty('--hero-scroll', progress.toFixed(4));
            page.classList.toggle('is-past-hero', progress >= 0.85);
        };

        const onScroll = () => {
            if (this._ticking) {
                return;
            }

            this._ticking = true;
            requestAnimationFrame(() => {
                this._ticking = false;
                update();
            });
        };

        this._controller = new AbortController();
        const { signal } = this._controller;

        window.addEventListener('scroll', onScroll, { passive: true, signal });
        window.addEventListener('resize', onScroll, { signal });

        // Synchronous: a restored scroll offset (back navigation) must render
        // the matching state before the first paint, not a frame later.
        update();
    },

    destroy() {
        if (this._controller) {
            this._controller.abort();
            this._controller = null;
        }

        this._ticking = false;

        const page = document.querySelector('.ph-page');
        if (page) {
            page.classList.remove('has-hero-scroll', 'is-past-hero');
            page.style.removeProperty('--hero-scroll');
        }
    },

    // Re-applies init() when Blazor lands a fresh .ph-page with no
    // enhancedload to hook (prerender → interactive handoff, re-renders).
    // Installed once; document.body survives enhanced navigation. Only
    // childList is observed — init() mutates classes, and observing attributes
    // would feed that straight back in as a loop. The callback runs as a
    // microtask, before the next paint, so the fresh nodes never flash undriven.
    ensureObserved() {
        if (this._observer || !('MutationObserver' in window) || !document.body) {
            return;
        }

        const observer = new MutationObserver(records => {
            for (const record of records) {
                for (const node of record.addedNodes) {
                    if (node.nodeType !== 1) {
                        continue;
                    }

                    if (node.matches('.ph-page') || node.querySelector('.ph-page')) {
                        const page = document.querySelector('.ph-page');
                        if (page && !page.classList.contains('has-hero-scroll') && !this.prefersReducedMotion()) {
                            this.init();
                        }

                        return;
                    }
                }
            }
        });

        observer.observe(document.body, { childList: true, subtree: true });
        this._observer = observer;
    }
};

window.queueHeroScroll = () => {
    requestAnimationFrame(() => {
        requestAnimationFrame(() => {
            if (window.heroScroll) {
                window.heroScroll.init();
                window.heroScroll.ensureObserved();
            }
        });
    });
};
