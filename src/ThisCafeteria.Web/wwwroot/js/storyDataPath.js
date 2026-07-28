// Story · The Path — drives a packet down the checkout rail from scroll position, lighting
// each stop as the packet reaches it. Mirrors the recruiterMotion rAF-throttled scroll pattern.
window.storyDataPath = window.storyDataPath || {
    cleanup: null,

    init() {
        this.destroy();

        const rail = document.querySelector('[data-path-rail]');
        if (!rail) return;

        const fill = rail.querySelector('[data-path-fill]');
        const packet = rail.querySelector('[data-path-packet]');
        const stops = Array.from(rail.querySelectorAll('[data-path-stop]'));

        const reduceMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
        if (reduceMotion) {
            stops.forEach((stop) => stop.classList.add('is-reached'));
            if (fill) fill.style.transform = 'scaleY(1)';
            if (packet) packet.style.opacity = '0';
            return;
        }

        let ticking = false;

        const update = () => {
            ticking = false;

            const rect = rail.getBoundingClientRect();
            const viewportHeight = window.innerHeight;

            // Progress runs 0 → 1 as the rail crosses the lower two-thirds of the viewport.
            const start = viewportHeight * 0.85;
            const distance = rect.height + viewportHeight * 0.35;
            const progress = Math.min(Math.max((start - rect.top) / distance, 0), 1);

            if (fill) fill.style.transform = `scaleY(${progress})`;

            if (packet) {
                packet.style.transform = `translate3d(0, ${progress * rect.height}px, 0)`;
                packet.style.opacity = progress > 0.002 && progress < 0.998 ? '1' : '0';
            }

            const packetY = rect.top + progress * rect.height;
            stops.forEach((stop) => {
                const node = stop.querySelector('.story-path__node');
                const target = node ? node.getBoundingClientRect() : stop.getBoundingClientRect();
                stop.classList.toggle('is-reached', packetY >= target.top + target.height / 2);
            });
        };

        const onScroll = () => {
            if (ticking) return;
            ticking = true;
            window.requestAnimationFrame(update);
        };

        window.addEventListener('scroll', onScroll, { passive: true });
        window.addEventListener('resize', onScroll);
        update();

        this.cleanup = () => {
            window.removeEventListener('scroll', onScroll);
            window.removeEventListener('resize', onScroll);
        };
    },

    destroy() {
        if (this.cleanup) {
            this.cleanup();
            this.cleanup = null;
        }
    },

    async copy(text) {
        try {
            await navigator.clipboard.writeText(text);
            return true;
        } catch {
            return false;
        }
    }
};
