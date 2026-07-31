(function () {
    const storageKey = 'thiscafeteria.productTransition';
    const easing = 'cubic-bezier(0.16, 1, 0.3, 1)';
    const duration = 500;

    const prefersReducedMotion = () =>
        window.matchMedia('(prefers-reduced-motion: reduce)').matches;

    const isMobileView = () => window.matchMedia('(max-width: 960px)').matches;

    const dismissSheet = hero => {
        if (!hero) {
            return;
        }

        // Let Blazor's SPA navigation to the list proceed underneath. A fixed
        // overlay clone slides down on top so the product grid is revealed
        // progressively as the sheet retreats.
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
                // Mirror of the sheet entry (and of the filter receipt sheet):
                // same drawer curve, same 440ms, reversed path.
                duration: 440,
                easing: 'cubic-bezier(0.32, 0.72, 0, 1)',
                fill: 'forwards'
            });

        const remove = () => clone.remove();
        animation.finished.then(remove, remove);
        window.setTimeout(remove, 700);
    };

    const playDetailDismiss = () => {
        if (!isMobileView() || prefersReducedMotion()) {
            return;
        }

        const hero = document.querySelector('.product-detail-card');
        dismissSheet(hero);
    };

    const wireSheetDismiss = hero => {
        const back = hero.querySelector('.product-detail-card__back a');
        if (!back || back.dataset.sheetWired === '1') {
            return;
        }

        back.dataset.sheetWired = '1';
        back.addEventListener('click', () => playDetailDismiss());
    };

    const cssEscape = value => {
        if (window.CSS && typeof window.CSS.escape === 'function') {
            return window.CSS.escape(value);
        }

        return value.replace(/["\\]/g, '\\$&');
    };

    const readRootPx = name => parseFloat(
        window.getComputedStyle(document.documentElement).getPropertyValue(name)) || 0;

    // Desktop only (mobile returns early from playFromCard). The detail page
    // centres a square image stage between a fixed-width thumbnail rail and a
    // fixed-width buy column, so this recomputes ProductDetails.razor.css's
    // --pdp-stage from the same :root tokens (declared in app.css, which is
    // loaded here on /products where the detail page's own styles are not).
    // The destination is exact — no guessed offsets.
    const getTargetRect = () => {
        const padY = readRootPx('--pdp-pad-y');
        const padX = readRootPx('--pdp-pad-x');
        const rail = readRootPx('--pdp-rail');
        const railGap = readRootPx('--pdp-rail-gap');
        const colGap = readRootPx('--pdp-col-gap');
        const info = readRootPx('--pdp-info');

        const viewportWidth = window.innerWidth;
        const viewportHeight = window.innerHeight;
        const stage = Math.min(
            viewportHeight - 2 * padY,
            viewportWidth - (2 * padX + rail + railGap + colGap + info));
        const tracks = rail + railGap + stage + colGap + info;

        return {
            left: (viewportWidth - tracks) / 2 + rail + railGap,
            top: (viewportHeight - stage) / 2,
            width: stage,
            height: stage
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
        document
            .querySelectorAll('.product-transition-away, .product-transition-source, .product-transition-source-image')
            .forEach(element => {
                element.classList.remove(
                    'product-transition-away',
                    'product-transition-source',
                    'product-transition-source-image');
            });
    };

    // Hand-off: the clone landed exactly on the real media panel, so it simply
    // cross-fades into the real image once that image can paint — no replayed
    // scale/fade on the detail side, and no frame where neither is visible.
    const dismissClone = hero => {
        const clone = document.querySelector('.product-transition-clone');

        if (!clone) {
            return;
        }

        let dismissed = false;
        const remove = () => clone.remove();
        const fade = () => {
            if (dismissed) {
                return;
            }

            dismissed = true;
            const animation = clone.animate(
                [{ opacity: 1 }, { opacity: 0 }],
                { duration: 150, easing: 'ease', fill: 'forwards' });
            animation.finished.then(remove, remove);
            window.setTimeout(remove, 400);
        };

        const image = hero?.querySelector('[data-product-detail-image]');
        if (image && typeof image.decode === 'function') {
            image.decode().then(fade, fade);
            window.setTimeout(fade, 800);
        } else {
            fade();
        }
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
        const sourceRadius = window.getComputedStyle(image).borderRadius || '0px';
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
                    borderRadius: sourceRadius,
                    left: `${imageRect.left}px`,
                    top: `${imageRect.top}px`,
                    width: `${imageRect.width}px`,
                    height: `${imageRect.height}px`
                },
                {
                    borderRadius: `${readRootPx('--pdp-stage-radius')}px`,
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

        await cloneAnimation.finished.catch(() => {});
    };

    const playDetailEntry = slug => {
        const hero = document.querySelector(`[data-product-detail="${cssEscape(slug)}"]`);

        if (!hero) {
            return;
        }

        const hadTransition = takeTransition(slug);
        cleanupListTransition();
        dismissClone(hero);

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
        playDetailEntry,
        playDetailDismiss
    };
}());
