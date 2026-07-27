// Product card hover blurb: the description panel sits at zero height until the card is
// hovered or focused, then it opens and the copy types itself in. Leaving reverses both —
// the text un-types and the panel closes back to the card's resting height.
//
// The `has-card-blurb` class is added at parse time — this file is a blocking <head>
// script, like heroType.js — so cards are already in their collapsed state the first time
// the grid paints. Adding it later would let every description paint at full height and
// then snap shut, which reads as a flicker across the whole grid. It also fixes the
// no-script story: without this file the class is never set, the collapse rules never
// apply, and the description is just a visible line of card copy.
document.documentElement.classList.add('has-card-blurb');

window.productCardBlurb = window.productCardBlurb || {
    // All ms per character. Typing a 150-character teaser lands around 1.6s; erasing is
    // roughly twice as fast, because nobody is reading text on its way out.
    charDelay: 11,
    eraseDelay: 5,

    frame: null,
    active: new Set(),
    split: new WeakMap(),

    prefersReducedMotion() {
        return window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    },

    // Touch has no hover: a tap would fire pointerover, open the card, and leave it open
    // with no matching pointerout. Those devices get the resting card and the product
    // page, which is where the full description lives anyway.
    canHover() {
        return window.matchMedia('(hover: hover)').matches;
    },

    // Wraps every character in its own span, and every word in an inline-block so the
    // split can't introduce a line break the browser wouldn't have taken. Characters are
    // revealed with opacity rather than inserted, so the panel's full height is known
    // from the first frame — the height animation is one clean move instead of a step
    // per wrapped line.
    chars(card) {
        const cached = this.split.get(card);
        if (cached && cached.length && cached[0].isConnected) return cached;

        const text = card.querySelector('[data-card-blurb-text]');
        if (!text) return [];

        const label = text.textContent.replace(/\s+/g, ' ').trim();
        if (!label) return [];

        const chars = [];
        const fragment = document.createDocumentFragment();

        const makeChar = (character) => {
            const span = document.createElement('span');
            span.className = 'product-card__char';
            span.textContent = character;
            chars.push(span);
            return span;
        };

        label.split(/( )/).forEach((part) => {
            if (!part) return;

            if (part === ' ') {
                const space = makeChar(' ');
                space.classList.add('product-card__char--space');
                fragment.appendChild(space);
                return;
            }

            const word = document.createElement('span');
            word.className = 'product-card__word';
            Array.from(part).forEach((character) => word.appendChild(makeChar(character)));
            fragment.appendChild(word);
        });

        // The split spans are decorative; screen readers get the sentence in one piece.
        text.setAttribute('aria-label', label);
        text.replaceChildren(fragment);
        this.split.set(card, chars);
        return chars;
    },

    // Hover and focus are tracked apart so that moving the mouse off a card the keyboard
    // is still sitting on doesn't yank the panel shut underneath the focus ring.
    hold(card, reason, held) {
        const key = reason === 'focus' ? 'blurbFocus' : 'blurbHover';

        if (held) {
            card.dataset[key] = '1';
        } else {
            delete card.dataset[key];
        }

        if (card.dataset.blurbHover === '1' || card.dataset.blurbFocus === '1') {
            this.open(card);
        } else {
            this.close(card);
        }
    },

    open(card) {
        if (card.dataset.blurbOpen === '1') return;
        card.dataset.blurbOpen = '1';

        const chars = this.chars(card);
        if (!chars.length) return;

        // The class drives the height; the typing is the rAF loop below. Both start on
        // the same frame: the panel is open well before the caret reaches line two.
        card.classList.add('is-blurb-open');

        if (this.prefersReducedMotion()) {
            this.settle(card, chars, chars.length);
            return;
        }

        this.arm(card);
    },

    close(card) {
        if (card.dataset.blurbOpen !== '1') return;
        delete card.dataset.blurbOpen;

        card.classList.remove('is-blurb-open');

        const chars = this.split.get(card);
        if (!chars || !chars.length) return;

        if (this.prefersReducedMotion()) {
            this.settle(card, chars, 0);
            return;
        }

        this.arm(card);
    },

    // Jumps straight to a character count with no animation, and drops the card out of
    // the loop. Used for reduced motion, and as the resting state at either end.
    settle(card, chars, index) {
        chars.forEach((char, position) => {
            char.classList.toggle('is-typed', position < index);
            char.classList.remove('is-cursor');
        });

        card.dataset.blurbIndex = String(index);
        this.active.delete(card);
    },

    arm(card) {
        card.dataset.blurbLast = String(performance.now());
        this.active.add(card);

        if (this.frame === null) {
            this.frame = window.requestAnimationFrame((now) => this.tick(now));
        }
    },

    // One loop for every animating card rather than a timer per character: re-entering a
    // card mid-collapse just flips the target it is walking toward, so a fast in-and-out
    // reverses smoothly instead of queueing up two fights over the same spans.
    tick(now) {
        this.frame = null;

        this.active.forEach((card) => {
            if (!card.isConnected) {
                this.active.delete(card);
                return;
            }

            const chars = this.split.get(card) || [];
            const opening = card.dataset.blurbOpen === '1';
            const target = opening ? chars.length : 0;
            const delay = opening ? this.charDelay : this.eraseDelay;

            let index = Number(card.dataset.blurbIndex) || 0;
            // A backgrounded tab hands back one frame with a huge delta; without the
            // clamp the card would finish typing in a single invisible step.
            let last = Math.max(Number(card.dataset.blurbLast) || now, now - delay * 8);

            while (index !== target && now - last >= delay) {
                index += opening ? 1 : -1;
                last += delay;
            }

            chars.forEach((char, position) => {
                char.classList.toggle('is-typed', position < index);
                char.classList.toggle('is-cursor', opening && position === index - 1);
            });

            card.dataset.blurbIndex = String(index);
            card.dataset.blurbLast = String(last);

            if (index === target) {
                if (!opening) chars.forEach((char) => char.classList.remove('is-cursor'));
                this.active.delete(card);
            }
        });

        if (this.active.size) {
            this.frame = window.requestAnimationFrame((next) => this.tick(next));
        }
    },

    cardFor(target) {
        return target instanceof Element ? target.closest('[data-product-card]') : null;
    },

    // pointerover/out rather than pointerenter/leave: these bubble, so one pair of
    // document listeners covers every card the grid will ever render — including the
    // ones Blazor swaps in when a filter changes.
    listen() {
        if (this.listening) return;
        this.listening = true;

        document.addEventListener('pointerover', (event) => {
            if (event.pointerType === 'touch' || !this.canHover()) return;

            const card = this.cardFor(event.target);
            if (card && !card.contains(event.relatedTarget)) this.hold(card, 'hover', true);
        });

        document.addEventListener('pointerout', (event) => {
            const card = this.cardFor(event.target);
            if (card && !card.contains(event.relatedTarget)) this.hold(card, 'hover', false);
        });

        // Keyboard users reach the same panel by tabbing to the card's link or buttons.
        document.addEventListener('focusin', (event) => {
            const card = this.cardFor(event.target);
            if (card) this.hold(card, 'focus', true);
        });

        document.addEventListener('focusout', (event) => {
            const card = this.cardFor(event.target);
            if (card && !card.contains(event.relatedTarget)) this.hold(card, 'focus', false);
        });
    }
};

window.productCardBlurb.listen();
