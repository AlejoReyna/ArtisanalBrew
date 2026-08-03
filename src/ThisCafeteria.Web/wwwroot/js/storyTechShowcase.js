window.storyTechShowcase = window.storyTechShowcase || {
    cleanup: null,

    init() {
        this.destroy();

        const root = document.querySelector('[data-story-tech]');
        if (!root) return;

        const sticky = root.querySelector('.story-tech__sticky');
        const infraCards = Array.from(root.querySelectorAll('[data-story-tech-layer="infra"] [data-story-tech-card]'));
        const contractCards = Array.from(root.querySelectorAll('[data-story-tech-layer="contracts"] [data-story-tech-card]'));
        const infraTitle = root.querySelector('[data-story-tech-title="infra"]');
        const bridgeTitle = root.querySelector('[data-story-tech-title="bridge"]');
        const contractsTitle = root.querySelector('[data-story-tech-title="contracts"]');
        const progressBar = root.querySelector('[data-story-tech-progress]');
        const currentLabel = root.querySelector('[data-story-tech-current]');
        const navButtons = Array.from(root.querySelectorAll('[data-story-tech-jump]'));
        const reduceMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;

        if (!sticky) return;
        if (reduceMotion) {
            contractCards.forEach((card) => card.removeAttribute('tabindex'));
            return;
        }

        const clamp = (value, min = 0, max = 1) => Math.min(Math.max(value, min), max);
        const ease = (value) => 1 - Math.pow(1 - clamp(value), 3);
        const fadeRange = (value, start, end) => clamp((value - start) / (end - start));
        let ticking = false;

        const positionCards = (cards, localProgress, isEntering) => {
            const rect = sticky.getBoundingClientRect();
            const compact = rect.width <= 760;
            const cardWidth = compact ? Math.min(rect.width * 0.41, 168) : Math.min(rect.width * 0.16, 224);
            const cardHeight = compact ? 96 : 116;
            const rangeX = Math.max((rect.width - cardWidth) / 2 - (compact ? 6 : 24), 24);
            const rangeY = Math.max((rect.height - cardHeight) / 2 - (compact ? 72 : 86), 24);
            const movement = ease(localProgress);

            cards.forEach((card, index) => {
                const xFactor = Number(card.dataset.x || 0);
                const yFactor = Number(card.dataset.y || 0);
                const rotation = Number(card.dataset.rotate || 0);
                const stagger = clamp(movement * 1.18 - index * 0.018);
                const moved = ease(stagger);
                const x = xFactor * rangeX * 2 * (isEntering ? moved : 1 - moved);
                const y = yFactor * rangeY * 2 * (isEntering ? moved : 1 - moved);
                const rotate = rotation * (isEntering ? moved : 1 - moved);
                const scale = isEntering ? 0.84 + moved * 0.16 : 1 - moved * 0.18;

                card.style.transform = `translate3d(calc(-50% + ${x}px), calc(-50% + ${y}px), 0) rotate(${rotate}deg) scale(${scale})`;
            });
        };

        const setTitle = (element, opacity, y, scale) => {
            if (!element) return;
            element.style.opacity = String(clamp(opacity));
            element.style.transform = `translate3d(-50%, calc(-50% + ${y}px), 0) scale(${scale})`;
        };

        const update = () => {
            ticking = false;

            const rootRect = root.getBoundingClientRect();
            const scrollable = root.offsetHeight - window.innerHeight;
            const progress = scrollable > 0 ? clamp(-rootRect.top / scrollable) : 0;
            const infraProgress = clamp(progress / 0.46);
            const contractProgress = clamp((progress - 0.44) / 0.48);
            const infraFade = 1 - fadeRange(progress, 0.34, 0.49);
            const contractFade = fadeRange(progress, 0.44, 0.57);

            positionCards(infraCards, infraProgress, false);
            positionCards(contractCards, contractProgress, true);

            infraCards.forEach((card) => {
                card.style.opacity = String(infraFade);
            });
            contractCards.forEach((card) => {
                card.style.opacity = String(contractFade);
                card.tabIndex = contractFade > 0.8 ? 0 : -1;
            });

            setTitle(
                infraTitle,
                1 - fadeRange(progress, 0.19, 0.36),
                -18 * fadeRange(progress, 0.19, 0.36),
                1 - 0.04 * fadeRange(progress, 0.19, 0.36));

            const bridgeIn = fadeRange(progress, 0.34, 0.43);
            const bridgeOut = 1 - fadeRange(progress, 0.49, 0.59);
            setTitle(bridgeTitle, bridgeIn * bridgeOut, 18 * (1 - bridgeIn) - 18 * (1 - bridgeOut), 0.96 + 0.04 * bridgeIn);

            const contractsIn = fadeRange(progress, 0.53, 0.68);
            setTitle(contractsTitle, contractsIn, 18 * (1 - contractsIn), 0.96 + 0.04 * contractsIn);

            if (progressBar) progressBar.style.transform = `scaleX(${progress})`;

            const contractIsActive = progress >= 0.5;
            if (currentLabel) currentLabel.textContent = contractIsActive ? '02' : '01';
            navButtons.forEach((button, index) => {
                button.classList.toggle('is-active', index === (contractIsActive ? 1 : 0));
            });
        };

        const requestUpdate = () => {
            if (ticking) return;
            ticking = true;
            window.requestAnimationFrame(update);
        };

        const jumpHandlers = navButtons.map((button) => {
            const handler = () => {
                const destination = Number(button.dataset.storyTechJump) === 1 ? 0.72 : 0.04;
                const top = window.scrollY + root.getBoundingClientRect().top;
                const scrollable = root.offsetHeight - window.innerHeight;
                window.scrollTo({
                    top: top + scrollable * destination,
                    behavior: 'smooth'
                });
            };
            button.addEventListener('click', handler);
            return [button, handler];
        });

        window.addEventListener('scroll', requestUpdate, { passive: true });
        window.addEventListener('resize', requestUpdate);
        update();

        this.cleanup = () => {
            window.removeEventListener('scroll', requestUpdate);
            window.removeEventListener('resize', requestUpdate);
            jumpHandlers.forEach(([button, handler]) => button.removeEventListener('click', handler));
        };
    },

    destroy() {
        if (this.cleanup) {
            this.cleanup();
            this.cleanup = null;
        }
    }
};
