// Hero entrance sequence: the headline types itself out, then the lede and the video
// credit follow in order.
//
// The `has-hero-type` class is added at parse time — this file is a blocking <head>
// script — so the pre-typed state is already in effect the first time the hero markup
// is painted. Setting it any later (DOMContentLoaded, a Blazor callback) lets the full
// headline paint once before it is hidden, which reads as a flash. The flip side is the
// graceful failure: if this file never loads, the class is never added and the hero
// renders as ordinary, fully visible markup.
document.documentElement.classList.add('has-hero-type');

window.heroTypewriter = window.heroTypewriter || {
    frame: null,
    hasTyped: false,

    // All ms. Typing lands around 1.2s, the whole sequence around 2.3s — long for UI,
    // but this is a once-per-load entrance, not something the user drives.
    charDelay: 26,
    spacePause: 34,
    segmentPause: 150,
    startDelay: 380,
    caretHold: 560,
    firstStepDelay: 160,
    stepGap: 260,

    init() {
        this.destroy();

        const headline = document.querySelector('[data-hero-type]');
        if (!headline) return;

        const steps = Array.from(document.querySelectorAll('[data-hero-step]'));
        const chars = this.split(headline);
        const prefersReducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;

        if (!chars.length) {
            this.finish(headline, chars, steps, true);
            return;
        }

        // The entrance earns its ~2.3s once per page load. Replaying it every time the
        // user navigates back to the home page would just be standing in their way, so
        // subsequent visits get the finished state with no transition at all — the same
        // reasoning behind `reveal-instant` for the rest of the site.
        if (this.hasTyped) {
            this.finish(headline, chars, steps, true);
            return;
        }

        // Reduced motion keeps the fade (it explains where the content came from) and
        // drops the character-by-character movement.
        if (prefersReducedMotion) {
            this.hasTyped = true;
            this.finish(headline, chars, steps, false);
            return;
        }

        this.hasTyped = true;
        headline.classList.add('is-typing', 'is-armed');
        this.run(headline, chars, this.schedule(chars), steps);
    },

    // Wraps every character in its own span, and every word in a nowrap box so a line
    // can still only break where it would have before. Characters are revealed with
    // opacity rather than inserted, so the headline occupies its final size and line
    // count from the first frame — nothing below it moves while typing.
    split(headline) {
        const existing = headline.querySelectorAll('.hero-char');
        if (existing.length) return Array.from(existing);

        const label = headline.textContent.replace(/\s+/g, ' ').trim();
        const chars = [];
        let pauseNext = false;

        const makeChar = (text) => {
            const span = document.createElement('span');
            span.className = 'hero-char';
            span.textContent = text;
            if (pauseNext) {
                span.dataset.heroPause = 'segment';
                pauseNext = false;
            }
            chars.push(span);
            return span;
        };

        const walk = (source, target) => {
            Array.from(source.childNodes).forEach((node) => {
                if (node.nodeType === Node.TEXT_NODE) {
                    const text = node.textContent.replace(/\s+/g, ' ');

                    text.split(/( )/).forEach((part) => {
                        if (!part) return;

                        if (part === ' ') {
                            // Razor indentation would otherwise type a leading space.
                            if (!chars.length) return;
                            const space = makeChar(' ');
                            space.classList.add('hero-char--space');
                            target.appendChild(space);
                            return;
                        }

                        const word = document.createElement('span');
                        word.className = 'hero-word';
                        Array.from(part).forEach((character) => word.appendChild(makeChar(character)));
                        target.appendChild(word);
                    });
                } else if (node.nodeType === Node.ELEMENT_NODE) {
                    // Keeps <span class="headline-italic"> and friends intact.
                    const clone = node.cloneNode(false);
                    pauseNext = true;
                    walk(node, clone);
                    target.appendChild(clone);
                }
            });
        };

        const fragment = document.createDocumentFragment();
        walk(headline, fragment);
        if (!chars.length) return [];

        chars[0].classList.add('hero-char--lead');
        // The split spans are decorative; screen readers get the sentence in one piece.
        headline.setAttribute('aria-label', label);
        headline.replaceChildren(fragment);
        return chars;
    },

    // A flat metronome reads as a machine. Spaces and the start of the italic phrase get
    // a longer beat, which is roughly where a person would pause.
    schedule(chars) {
        let time = this.startDelay;

        return chars.map((char) => {
            if (char.dataset.heroPause === 'segment') time += this.segmentPause;

            const at = time;
            time += this.charDelay + (char.classList.contains('hero-char--space') ? this.spacePause : 0);
            return at;
        });
    },

    // One rAF loop against a precomputed timeline rather than a setTimeout per
    // character: it self-corrects if a frame is late, and there is a single thing to
    // cancel when the user navigates away mid-sequence.
    run(headline, chars, times, steps) {
        const start = performance.now();
        const typedAt = times[times.length - 1] + this.charDelay;
        let index = 0;
        let cursor = null;

        const tick = (now) => {
            if (!headline.isConnected) {
                this.frame = null;
                return;
            }

            const elapsed = now - start;

            while (index < chars.length && elapsed >= times[index]) {
                const char = chars[index];
                char.classList.add('is-typed');
                if (cursor) cursor.classList.remove('is-cursor');
                char.classList.add('is-cursor');
                cursor = char;
                index += 1;
            }

            if (index > 0) headline.classList.remove('is-armed');

            steps.forEach((step, position) => {
                if (elapsed >= typedAt + this.firstStepDelay + position * this.stepGap) {
                    step.classList.add('is-revealed');
                }
            });

            const settled = elapsed >= typedAt + this.caretHold;
            if (settled) headline.classList.add('is-done');

            if (index >= chars.length && settled && steps.every((step) => step.classList.contains('is-revealed'))) {
                this.frame = null;
                return;
            }

            this.frame = window.requestAnimationFrame(tick);
        };

        this.frame = window.requestAnimationFrame(tick);
    },

    finish(headline, chars, steps, instant) {
        headline.classList.add('is-typing', 'is-done');
        headline.classList.remove('is-armed');
        if (instant) headline.classList.add('is-instant');

        chars.forEach((char) => char.classList.add('is-typed'));
        steps.forEach((step) => {
            if (instant) step.classList.add('is-instant');
            step.classList.add('is-revealed');
        });
    },

    destroy() {
        if (this.frame) {
            window.cancelAnimationFrame(this.frame);
            this.frame = null;
        }
    }
};
