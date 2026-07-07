window.journalProgress = window.journalProgress || {
    _handler: null,

    init() {
        this.destroy();

        const bar = document.querySelector('.journal-newsletter__progress-bar');
        if (!bar) {
            return;
        }

        const update = () => {
            const doc = document.documentElement;
            const scrollTop = window.scrollY || doc.scrollTop;
            const scrollable = doc.scrollHeight - doc.clientHeight;
            const pct = scrollable > 0 ? Math.min(100, Math.max(0, (scrollTop / scrollable) * 100)) : 0;
            bar.style.width = pct + '%';
        };

        this._handler = update;
        window.addEventListener('scroll', update, { passive: true });
        window.addEventListener('resize', update);
        update();
    },

    destroy() {
        if (this._handler) {
            window.removeEventListener('scroll', this._handler);
            window.removeEventListener('resize', this._handler);
            this._handler = null;
        }
    }
};
