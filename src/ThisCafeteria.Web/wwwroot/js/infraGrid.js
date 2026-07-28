window.infraGridExpand = window.infraGridExpand || {
    grid: null,
    activeCard: null,
    originRect: null,
    listeners: [],
    keyHandler: null,
    resizeHandler: null,

    init() {
        this.destroy();

        const grid = document.querySelector('[data-infra-grid]');
        if (!grid) return;
        this.grid = grid;

        Array.from(grid.querySelectorAll('[data-infra-card]')).forEach((card) => {
            const openBtn = card.querySelector('[data-infra-open]');
            const closeBtn = card.querySelector('[data-infra-close]');
            if (!openBtn || !closeBtn) return;

            const onOpen = () => this.expand(card);
            const onClose = (event) => {
                event.stopPropagation();
                this.collapse();
            };

            openBtn.addEventListener('click', onOpen);
            closeBtn.addEventListener('click', onClose);

            this.listeners.push([openBtn, 'click', onOpen], [closeBtn, 'click', onClose]);
        });

        this.keyHandler = (event) => {
            if (event.key === 'Escape' && this.activeCard) this.collapse();
        };
        document.addEventListener('keydown', this.keyHandler);

        this.resizeHandler = () => {
            if (!this.activeCard || !this.grid) return;
            const rect = this.grid.getBoundingClientRect();
            Object.assign(this.activeCard.style, {
                width: `${rect.width}px`,
                height: `${rect.height}px`
            });
        };
        window.addEventListener('resize', this.resizeHandler);
    },

    prefersReducedMotion() {
        return window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    },

    expand(card) {
        if (this.activeCard || !this.grid) return;

        const grid = this.grid;
        const gridRect = grid.getBoundingClientRect();
        const cardRect = card.getBoundingClientRect();

        grid.style.minHeight = `${gridRect.height}px`;

        const originTop = cardRect.top - gridRect.top;
        const originLeft = cardRect.left - gridRect.left;
        this.originRect = { top: originTop, left: originLeft, width: cardRect.width, height: cardRect.height };

        Array.from(grid.querySelectorAll('[data-infra-card]'))
            .filter((sibling) => sibling !== card)
            .forEach((sibling) => sibling.classList.add('is-dimmed'));

        Object.assign(card.style, {
            position: 'absolute',
            top: `${originTop}px`,
            left: `${originLeft}px`,
            width: `${cardRect.width}px`,
            height: `${cardRect.height}px`,
            transition: 'none'
        });

        card.classList.add('is-expanded');
        const openBtn = card.querySelector('[data-infra-open]');
        if (openBtn) openBtn.setAttribute('aria-expanded', 'true');

        this.activeCard = card;

        const targetWidth = gridRect.width;
        const targetHeight = gridRect.height;

        if (this.prefersReducedMotion()) {
            Object.assign(card.style, { top: '0px', left: '0px', width: `${targetWidth}px`, height: `${targetHeight}px` });
            return;
        }

        // Force a layout flush so the browser commits the origin rect before
        // the transitioned values are applied on the next frame.
        void card.offsetWidth;

        requestAnimationFrame(() => {
            card.style.transition = 'top 480ms cubic-bezier(0.16,1,0.3,1), left 480ms cubic-bezier(0.16,1,0.3,1), width 480ms cubic-bezier(0.16,1,0.3,1), height 480ms cubic-bezier(0.16,1,0.3,1)';
            card.style.top = '0px';
            card.style.left = '0px';
            card.style.width = `${targetWidth}px`;
            card.style.height = `${targetHeight}px`;
        });
    },

    collapse() {
        const card = this.activeCard;
        const grid = this.grid;
        if (!card || !grid || !this.originRect) return;

        const openBtn = card.querySelector('[data-infra-open]');
        if (openBtn) openBtn.setAttribute('aria-expanded', 'false');

        const origin = this.originRect;
        const finish = () => {
            Object.assign(card.style, {
                position: '', top: '', left: '', width: '', height: '', transition: ''
            });
            card.classList.remove('is-expanded');

            Array.from(grid.querySelectorAll('[data-infra-card]')).forEach((sibling) => sibling.classList.remove('is-dimmed'));
            grid.style.minHeight = '';

            this.activeCard = null;
            this.originRect = null;
        };

        if (this.prefersReducedMotion()) {
            finish();
            return;
        }

        card.style.transition = 'top 420ms cubic-bezier(0.16,1,0.3,1), left 420ms cubic-bezier(0.16,1,0.3,1), width 420ms cubic-bezier(0.16,1,0.3,1), height 420ms cubic-bezier(0.16,1,0.3,1)';
        card.style.top = `${origin.top}px`;
        card.style.left = `${origin.left}px`;
        card.style.width = `${origin.width}px`;
        card.style.height = `${origin.height}px`;

        const onEnd = (event) => {
            if (event.target !== card || event.propertyName !== 'width') return;
            card.removeEventListener('transitionend', onEnd);
            finish();
        };
        card.addEventListener('transitionend', onEnd);
    },

    destroy() {
        this.listeners.forEach(([element, type, handler]) => element.removeEventListener(type, handler));
        this.listeners = [];

        if (this.keyHandler) document.removeEventListener('keydown', this.keyHandler);
        if (this.resizeHandler) window.removeEventListener('resize', this.resizeHandler);
        this.keyHandler = null;
        this.resizeHandler = null;

        if (this.activeCard) {
            Object.assign(this.activeCard.style, {
                position: '', top: '', left: '', width: '', height: '', transition: ''
            });
            this.activeCard.classList.remove('is-expanded');
        }

        if (this.grid) {
            this.grid.style.minHeight = '';
            Array.from(this.grid.querySelectorAll('[data-infra-card]')).forEach((card) => card.classList.remove('is-dimmed'));
        }

        this.activeCard = null;
        this.originRect = null;
        this.grid = null;
    }
};
