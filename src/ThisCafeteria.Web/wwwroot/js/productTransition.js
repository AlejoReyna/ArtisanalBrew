(function () {
    const storageKey = 'thiscafeteria.productTransition';
    const easing = 'cubic-bezier(0.16, 1, 0.3, 1)';
    const duration = 620;

    const prefersReducedMotion = () =>
        window.matchMedia('(prefers-reduced-motion: reduce)').matches;

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

        const shouldAnimate = takeTransition(slug) && !prefersReducedMotion();
        cleanupListTransition();

        if (!shouldAnimate) {
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
