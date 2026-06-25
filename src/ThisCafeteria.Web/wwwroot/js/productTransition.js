(function () {
    const storageKey = 'thiscafeteria.productTransition';
    const easing = 'cubic-bezier(0.16, 1, 0.3, 1)';
    const duration = 620;

    const prefersReducedMotion = () =>
        window.matchMedia('(prefers-reduced-motion: reduce)').matches;

    const isMobileView = () => window.matchMedia('(max-width: 960px)').matches;

    const wireSheetDismiss = hero => {
        const back = hero.querySelector('.product-detail-card__back a');
        if (!back || back.dataset.sheetWired === '1') {
            return;
        }

        back.dataset.sheetWired = '1';
        back.addEventListener('click', () => {
            if (!isMobileView() || prefersReducedMotion()) {
                return;
            }

            // Let Blazor's SPA navigation to the list proceed underneath (no
            // preventDefault). A fixed overlay clone slides down on top so the
            // list is revealed progressively as the sheet retreats.
            hero.classList.remove('product-detail-card--sheet-enter');

            const clone = hero.cloneNode(true);
            clone.classList.add('product-detail-sheet-dismiss-clone');
            clone.removeAttribute('data-product-detail');
            document.body.appendChild(clone);

            const animation = clone.animate(
                [
                    { transform: 'translateY(0)' },
                    { transform: 'translateY(100%)' }
                ],
                {
                    duration: 300,
                    easing: 'cubic-bezier(0.32, 0.72, 0, 1)',
                    fill: 'forwards'
                });

            const remove = () => clone.remove();
            animation.finished.then(remove, remove);
            window.setTimeout(remove, 600);
        });
    };

    const cssEscape = value => {
        if (window.CSS && typeof window.CSS.escape === 'function') {
            return window.CSS.escape(value);
        }

        return value.replace(/["\\]/g, '\\$&');
    };

    const wait = milliseconds => new Promise(resolve => window.setTimeout(resolve, milliseconds));

    const getTargetRect = () => {
        const viewportWidth = window.innerWidth;
        const viewportHeight = window.innerHeight;
        const top = Math.max(96, viewportHeight * 0.16);

        if (viewportWidth <= 960) {
            const width = Math.max(240, viewportWidth - 40);
            return {
                left: (viewportWidth - width) / 2,
                top,
                width,
                height: width * 1.18
            };
        }

        const width = Math.min(560, Math.max(360, viewportWidth * 0.34));
        return {
            left: Math.min(viewportWidth - width - 48, viewportWidth * 0.58),
            top: Math.max(112, viewportHeight * 0.18),
            width,
            height: width * 1.18
        };
    };

    const storeTransition = slug => {
        try {
            window.sessionStorage.setItem(storageKey, JSON.stringify({
                slug,
                at: Date.now()
            }));
        } catch {
        }
    };

    const takeTransition = slug => {
        try {
            const raw = window.sessionStorage.getItem(storageKey);
            if (!raw) {
                return false;
            }

            const state = JSON.parse(raw);
            const isFresh = Date.now() - Number(state.at) < 4000;
            const isMatch = state.slug === slug;

            if (isMatch && isFresh) {
                window.sessionStorage.removeItem(storageKey);
                return true;
            }
        } catch {
        }

        return false;
    };

    const cleanupListTransition = () => {
        document.documentElement.classList.remove('product-transition-active');
        document.querySelectorAll('.product-transition-clone').forEach(element => element.remove());
        document
            .querySelectorAll('.product-transition-away, .product-transition-source, .product-transition-source-image')
            .forEach(element => {
                element.classList.remove(
                    'product-transition-away',
                    'product-transition-source',
                    'product-transition-source-image');
            });
    };

    const playFromCard = async slug => {
        storeTransition(slug);

        if (prefersReducedMotion()) {
            return;
        }

        // On mobile the detail view grows up as a bottom sheet, so skip the
        // desktop shared-element fly animation and navigate immediately.
        if (isMobileView()) {
            return;
        }

        const card = document.querySelector(`[data-product-card][data-product-slug="${cssEscape(slug)}"]`);
        const image = card?.querySelector('[data-product-image]');

        if (!card || !image) {
            return;
        }

        const imageRect = image.getBoundingClientRect();
        const targetRect = getTargetRect();
        const clone = image.cloneNode(true);

        clone.classList.add('product-transition-clone');
        Object.assign(clone.style, {
            left: `${imageRect.left}px`,
            top: `${imageRect.top}px`,
            width: `${imageRect.width}px`,
            height: `${imageRect.height}px`
        });

        document.body.appendChild(clone);
        document.documentElement.classList.add('product-transition-active');
        card.classList.add('product-transition-source');
        image.classList.add('product-transition-source-image');

        document
            .querySelectorAll(`[data-product-card]:not([data-product-slug="${cssEscape(slug)}"]), .category-filter`)
            .forEach(element => element.classList.add('product-transition-away'));

        const cloneAnimation = clone.animate(
            [
                {
                    borderRadius: '0px',
                    left: `${imageRect.left}px`,
                    top: `${imageRect.top}px`,
                    width: `${imageRect.width}px`,
                    height: `${imageRect.height}px`
                },
                {
                    borderRadius: '0px',
                    left: `${targetRect.left}px`,
                    top: `${targetRect.top}px`,
                    width: `${targetRect.width}px`,
                    height: `${targetRect.height}px`
                }
            ],
            {
                duration,
                easing,
                fill: 'forwards'
            });

        await Promise.allSettled([
            cloneAnimation.finished,
            wait(duration)
        ]);
    };

    const playDetailEntry = slug => {
        const hero = document.querySelector(`[data-product-detail="${cssEscape(slug)}"]`);

        if (!hero) {
            return;
        }

        const hadTransition = takeTransition(slug);
        cleanupListTransition();

        if (prefersReducedMotion()) {
            return;
        }

        // Mobile: the card grows up from the bottom via pure CSS on mount; here
        // we only wire the back control to dismiss it downward before nav.
        if (isMobileView()) {
            wireSheetDismiss(hero);
            return;
        }

        if (!hadTransition) {
            return;
        }

        hero.classList.add('product-detail-hero--from-card');
        window.requestAnimationFrame(() => {
            hero.classList.add('product-detail-hero--ready');
        });
    };

    window.productTransition = {
        playFromCard,
        playDetailEntry
    };
}());
