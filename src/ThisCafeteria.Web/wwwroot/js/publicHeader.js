window.themeColor = {
    _original: null,
    set(color) {
        const meta = document.querySelector('meta[name="theme-color"]');
        if (!meta) {
            return;
        }

        if (this._original === null) {
            this._original = meta.getAttribute('content');
        }

        meta.setAttribute('content', color);
    },
    restore() {
        if (this._original === null) {
            return;
        }

        const meta = document.querySelector('meta[name="theme-color"]');
        if (meta) {
            meta.setAttribute('content', this._original);
        }

        this._original = null;
    }
};

window.initPublicHeader = () => {
    const hero = document.querySelector('.editorial-hero, .journal-hero');
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

    if (!hero || !header) {
        header?.classList.add('public-header--solid');
        const fallbackMeta = document.querySelector('meta[name="theme-color"]');
        if (fallbackMeta) fallbackMeta.content = '#fbf9f4';

        const staleBrand = document.querySelector('.navbar-brand');
        if (staleBrand) {
            staleBrand.style.removeProperty('color');
            staleBrand.classList.remove('navbar-brand--jump-to-hero', 'navbar-brand--jump-to-concept');
        }

        return;
    }

    const headerHeight = header.offsetHeight || 76;
    const rootMargin = `-${headerHeight}px 0px 0px 0px`;

    const themeMeta = document.querySelector('meta[name="theme-color"]');

    const updateHeader = () => {
        const heroGone = hero.getBoundingClientRect().bottom <= window.innerHeight * 0.7;
        header.classList.toggle('public-header--solid', heroGone);
        if (themeMeta) themeMeta.content = heroGone ? '#fbf9f4' : '#050505';
    };

    window.publicHeaderObserver = new IntersectionObserver(
        () => updateHeader(),
        { root: null, rootMargin, threshold: 0 }
    );

    window.publicHeaderObserver.observe(hero);

    const brand = document.querySelector('.navbar-brand');
    const conceptSection = document.getElementById('concept-section');

    if (brand) {
        const HERO_COLOR = '#fbf9f4';
        const CONCEPT_COLOR = '#000';
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

        brand.addEventListener('animationend', () => {
            brand.style.color = brandState === 'concept' ? CONCEPT_COLOR : HERO_COLOR;
        });

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
