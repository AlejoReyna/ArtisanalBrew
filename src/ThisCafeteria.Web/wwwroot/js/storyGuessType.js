// Story hero's terminal-style tease, in place of the old "Full Disclosure"
// eyebrow: types "guess what . . ." above the headline.
window.storyGuessType = window.storyGuessType || {
    timers: [],
    phrase: 'guess what . . .',
    charDelay: 55,

    init() {
        this.destroy();

        const text = document.querySelector('[data-story-guess-text]');
        if (!text) return;

        if (window.matchMedia('(prefers-reduced-motion: reduce)').matches) {
            text.textContent = this.phrase;
            return;
        }

        text.textContent = '';
        this.type(text, 0);
    },

    type(text, index) {
        if (!text.isConnected) return;
        text.textContent = this.phrase.slice(0, index);
        if (index >= this.phrase.length) return;
        this.timers.push(window.setTimeout(() => this.type(text, index + 1), this.charDelay));
    },

    destroy() {
        this.timers.forEach((id) => window.clearTimeout(id));
        this.timers = [];
    }
};
