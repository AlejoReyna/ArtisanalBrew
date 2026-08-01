window.themeColor = {
    _original: null,
    _surface: null,
    apply(color) {
        const meta = document.querySelector('meta[name="theme-color"]');
        if (meta) {
            meta.removeAttribute('media');
            meta.setAttribute('content', color);
        }

        document.documentElement.style.setProperty('--active-theme-color', color);
        document.documentElement.style.backgroundColor = color;
        document.body.style.backgroundColor = color;

        let surface = document.querySelector('[data-theme-color-surface]');
        if (!surface) {
            surface = document.createElement('div');
            surface.setAttribute('data-theme-color-surface', '');
            surface.setAttribute('aria-hidden', 'true');
            Object.assign(surface.style, {
                position: 'fixed',
                inset: '0 0 auto',
                zIndex: '51',
                height: 'max(env(safe-area-inset-top, 0px), 1px)',
                pointerEvents: 'none'
            });
            document.body.appendChild(surface);
        }

        surface.style.backgroundColor = color;
        this._surface = surface;
    },
    set(color) {
        const meta = document.querySelector('meta[name="theme-color"]');

        if (this._original === null) {
            this._original = {
                metaContent: meta?.getAttribute('content') ?? null,
                rootBackground: document.documentElement.style.backgroundColor,
                bodyBackground: document.body.style.backgroundColor,
                cssColor: document.documentElement.style.getPropertyValue('--active-theme-color'),
                surfaceColor: this._surface?.style.backgroundColor ?? ''
            };
        }

        this.apply(color);
    },
    restore() {
        if (this._original === null) {
            return;
        }

        const meta = document.querySelector('meta[name="theme-color"]');
        if (meta) {
            if (this._original.metaContent === null) {
                meta.removeAttribute('content');
            } else {
                meta.setAttribute('content', this._original.metaContent);
            }
        }

        document.documentElement.style.backgroundColor = this._original.rootBackground;
        document.body.style.backgroundColor = this._original.bodyBackground;

        if (this._original.cssColor) {
            document.documentElement.style.setProperty('--active-theme-color', this._original.cssColor);
        } else {
            document.documentElement.style.removeProperty('--active-theme-color');
        }

        if (this._surface) {
            this._surface.style.backgroundColor = this._original.surfaceColor;
        }

        this._original = null;
    }
};

// How far the page has to move before the scroll-veil routes (/, /staking,
// /procurement-lab, /about) trade their fully transparent bar for the
// translucent plate: 1% of the viewport, so it reads as "the moment you leave
// the top" and behaves identically on a short page and a long one.
const SCROLL_VEIL_RATIO = 0.01;

window.initPublicHeader = () => {
    const hero = document.querySelector('.editorial-hero, .journal-hero, .story-hero, .ph-hero');
    const header = document.querySelector('.public-header');

    if (window.publicHeaderObserver) {
        window.publicHeaderObserver.disconnect();
        window.publicHeaderObserver = null;
    }

    if (window.conceptSectionObserver) {
        window.conceptSectionObserver.disconnect();
        window.conceptSectionObserver = null;
    }

    if (window.heroSectionObserver) {
        window.heroSectionObserver.disconnect();
        window.heroSectionObserver = null;
    }

    if (window.publicHeaderController) {
        window.publicHeaderController.abort();
        window.publicHeaderController = null;
    }

    if (window.publicHeaderVeilController) {
        window.publicHeaderVeilController.abort();
        window.publicHeaderVeilController = null;
    }

    // Wired before the hero branch below, and on its own AbortController,
    // because the scroll-veil routes have no hero element at all — they take
    // the early return, which would otherwise leave the bar with no scroll
    // listener. The class is toggled on every route rather than gated on
    // --scroll-veil: interactive navigation calls this through queuePublicHeader
    // before the server's re-render has necessarily added the route class, so
    // reading it here would be a race. Only NavMenu.razor.css's
    // .public-header--scroll-veil.public-header--scrolled pair actually paints
    // anything, so the class is inert everywhere else.
    if (header) {
        const updateScrollVeil = () => {
            // innerHeight can still read 0 immediately after a navigation, before
            // the viewport has been measured; clientHeight is the fallback, and
            // the 1px floor keeps "in its original position" meaning the very top
            // rather than collapsing the threshold to zero.
            const viewport = window.innerHeight || document.documentElement.clientHeight || 0;

            header.classList.toggle(
                'public-header--scrolled',
                window.scrollY > Math.max(viewport * SCROLL_VEIL_RATIO, 1)
            );
        };

        window.publicHeaderVeilController = new AbortController();
        const veilSignal = window.publicHeaderVeilController.signal;

        window.addEventListener('scroll', updateScrollVeil, { passive: true, signal: veilSignal });
        window.addEventListener('resize', updateScrollVeil, { signal: veilSignal });

        updateScrollVeil();
    }

    if (!hero || !header) {
        header?.classList.add('public-header--solid');
        const pageThemeColor = document.querySelector('[data-page-theme-color]')
            ?.dataset.pageThemeColor ?? '#fbf9f4';
        window.themeColor.apply(pageThemeColor);

        const staleBrand = document.querySelector('.navbar-brand');
        if (staleBrand) {
            staleBrand.style.removeProperty('color');
            staleBrand.classList.remove('navbar-brand--jump-to-hero', 'navbar-brand--jump-to-concept');
        }

        return;
    }

    const headerHeight = header.offsetHeight || 76;
    const rootMargin = `-${headerHeight}px 0px 0px 0px`;

    const themedSections = Array.from(document.querySelectorAll('[data-theme-color]'));

    const updateThemeColor = () => {
        if (!themedSections.length) {
            window.themeColor.apply('#fbf9f4');
            return;
        }

        // Use the content immediately beneath the fixed header as the source
        // of truth, matching the surface that reaches Safari's top inset.
        const probeY = Math.min(Math.max(headerHeight, 1), window.innerHeight - 1);
        const activeSection = themedSections.find((section) => {
            const rect = section.getBoundingClientRect();
            return rect.top <= probeY && rect.bottom > probeY;
        });

        if (activeSection) {
            window.themeColor.apply(activeSection.dataset.themeColor);
            return;
        }

        // Section margins can leave a small gap at the probe line. In that
        // case, keep the color of the nearest section instead of flashing the
        // document's default color.
        const nearestSection = themedSections.reduce((nearest, section) => {
            const rect = section.getBoundingClientRect();
            const distance = rect.bottom <= probeY
                ? probeY - rect.bottom
                : rect.top >= probeY
                    ? rect.top - probeY
                    : 0;

            return !nearest || distance < nearest.distance
                ? { section, distance }
                : nearest;
        }, null);

        if (nearestSection) {
            window.themeColor.apply(nearestSection.section.dataset.themeColor);
        }
    };

    const updateHeader = () => {
        const heroGone = hero.getBoundingClientRect().bottom <= window.innerHeight * 0.7;
        header.classList.toggle('public-header--solid', heroGone);
        updateThemeColor();
    };

    window.publicHeaderObserver = new IntersectionObserver(
        () => updateHeader(),
        { root: null, rootMargin, threshold: 0 }
    );

    window.publicHeaderObserver.observe(hero);

    const brand = document.querySelector('.navbar-brand');
    const conceptSection = document.getElementById('concept-section');

    if (brand) {
        // The jump used to cover a color swap (cream over the hero, black over
        // the concept section). The bar is dark on every section now, so the
        // wordmark keeps its CSS color and this is just the section cue.
        let brandState = null;

        const setBrandState = (state) => {
            if (state === brandState) {
                return;
            }

            brandState = state;

            brand.classList.remove('navbar-brand--jump-to-hero', 'navbar-brand--jump-to-concept');
            // Force a reflow so the animation-name change is observed and the
            // animation restarts; both classes share the same keyframe name,
            // so swapping them without this would otherwise be a no-op.
            void brand.offsetWidth;
            brand.classList.add(state === 'concept' ? 'navbar-brand--jump-to-concept' : 'navbar-brand--jump-to-hero');
        };

        if (conceptSection) {
            window.conceptSectionObserver = new IntersectionObserver(
                (entries) => {
                    entries.forEach((entry) => {
                        if (entry.isIntersecting) {
                            setBrandState('concept');
                        }
                    });
                },
                { root: null, threshold: 0.1 }
            );

            window.conceptSectionObserver.observe(conceptSection);
        }

        window.heroSectionObserver = new IntersectionObserver(
            (entries) => {
                entries.forEach((entry) => {
                    if (entry.isIntersecting) {
                        setBrandState('hero');
                    }
                });
            },
            { root: null, threshold: 0.1 }
        );

        window.heroSectionObserver.observe(hero);
    }

    window.publicHeaderController = new AbortController();
    const { signal } = window.publicHeaderController;

    window.addEventListener('scroll', updateHeader, { passive: true, signal });
    window.addEventListener('resize', updateHeader, { signal });

    updateHeader();
};
