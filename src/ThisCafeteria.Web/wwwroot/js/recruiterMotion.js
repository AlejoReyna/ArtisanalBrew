window.recruiterShowcaseMotion = window.recruiterShowcaseMotion || {
    instances: [],
    provenanceCleanup: null,
    init() {
        this.destroy();

        const prefersReducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
        const sections = Array.from(document.querySelectorAll('[data-recruiter-section]'));
        if (!sections.length) return;

        const revealElement = (element) => element.classList.add('is-visible');
        const hideElement = (element) => element.classList.remove('is-visible');

        const animateCount = (element, isEntering) => {
            const target = parseInt(element.dataset.countTo, 10);
            if (Number.isNaN(target)) return;

            if (element._recruiterCountRaf) {
                window.cancelAnimationFrame(element._recruiterCountRaf);
                element._recruiterCountRaf = null;
            }

            if (!isEntering) {
                element.textContent = '0';
                return;
            }

            if (prefersReducedMotion) {
                element.textContent = String(target);
                return;
            }

            const duration = 900;
            const start = performance.now();
            const step = (now) => {
                const elapsed = Math.min((now - start) / duration, 1);
                const eased = 1 - Math.pow(1 - elapsed, 3);
                element.textContent = String(Math.round(target * eased));
                if (elapsed < 1) {
                    element._recruiterCountRaf = window.requestAnimationFrame(step);
                }
            };
            element._recruiterCountRaf = window.requestAnimationFrame(step);
        };

        const sectionObserver = new IntersectionObserver((entries) => {
            entries.forEach((entry) => {
                const section = entry.target;
                const animated = Array.from(section.querySelectorAll('[data-recruiter-animate]'))
                    .filter((element) => !element.hasAttribute('data-recruiter-observe-self'));

                if (entry.isIntersecting) {
                    section.classList.add('is-in-view');
                    animated.forEach((element, index) => {
                        let delay;
                        if (element.dataset.recruiterDelay !== undefined) {
                            delay = `${element.dataset.recruiterDelay}ms`;
                        } else {
                            const staggerGroup = element.closest('[data-recruiter-stagger]');
                            if (staggerGroup) {
                                const groupItems = Array.from(staggerGroup.querySelectorAll('[data-recruiter-animate]'));
                                const localIndex = groupItems.indexOf(element);
                                delay = `${Math.min(Math.max(localIndex, 0) * 150, 500)}ms`;
                            } else {
                                delay = `${Math.min(index * 110, 520)}ms`;
                            }
                        }
                        element.style.setProperty('--recruiter-delay', delay);
                        window.requestAnimationFrame(() => revealElement(element));
                    });
                } else {
                    section.classList.remove('is-in-view');
                    animated.forEach(hideElement);
                }

                const counters = Array.from(section.querySelectorAll('[data-count-to]'))
                    .filter((counter) => !counter.closest('[data-recruiter-observe-self]'));
                counters.forEach((counter) => animateCount(counter, entry.isIntersecting));
            });
        }, {
            threshold: 0.28,
            rootMargin: '0px 0px -12% 0px'
        });

        sections.forEach((section) => sectionObserver.observe(section));
        this.instances.push(sectionObserver);

        // Tall sections with several independently-scrolled items (Provenance stations,
        // RoastClub stats/CTA) can't rely on whole-section intersection ratio — a single
        // item may still be on screen while the section overall dips under threshold.
        // These opt in via [data-recruiter-observe-self] and get their own observer.
        const independentElements = Array.from(document.querySelectorAll('[data-recruiter-observe-self]'));
        if (independentElements.length) {
            const independentObserver = new IntersectionObserver((entries) => {
                entries.forEach((entry) => {
                    const element = entry.target;
                    if (entry.isIntersecting) {
                        revealElement(element);
                    } else {
                        hideElement(element);
                    }

                    const counter = element.hasAttribute('data-count-to') ? element : element.querySelector('[data-count-to]');
                    if (counter) {
                        animateCount(counter, entry.isIntersecting);
                    }
                });
            }, {
                threshold: 0.4,
                rootMargin: '0px 0px -10% 0px'
            });

            independentElements.forEach((element) => independentObserver.observe(element));
            this.instances.push(independentObserver);
        }

        const journalCards = Array.from(document.querySelectorAll('.recruiter-journal-index .recruiter-journal-entry'));
        if (journalCards.length) {
            const cardObserver = new IntersectionObserver((entries) => {
                entries.forEach((entry) => {
                    if (!entry.isIntersecting) {
                        return;
                    }

                    const card = entry.target;
                    const index = journalCards.indexOf(card);
                    const delay = Math.max(index, 0) * 150;
                    card.style.setProperty('--recruiter-delay', `${delay}ms`);
                    card.classList.add('is-visible');
                    cardObserver.unobserve(card);
                });
            }, { threshold: 0.2 });

            journalCards.forEach((card) => cardObserver.observe(card));
            this.instances.push(cardObserver);
        }

        const provenanceRail = document.querySelector('[data-provenance-rail]');
        if (provenanceRail) {
            const fill = provenanceRail.querySelector('.provenance-rail__fill');
            let ticking = false;

            const updateRail = () => {
                ticking = false;
                if (!fill) return;
                const rect = provenanceRail.getBoundingClientRect();
                const viewportHeight = window.innerHeight;
                const progress = (viewportHeight - rect.top) / (rect.height + viewportHeight * 0.5);
                const clamped = Math.min(Math.max(progress, 0), 1);
                fill.style.transform = `scaleY(${clamped})`;
            };

            const onScroll = () => {
                if (ticking) return;
                ticking = true;
                window.requestAnimationFrame(updateRail);
            };

            window.addEventListener('scroll', onScroll, { passive: true });
            window.addEventListener('resize', onScroll);
            updateRail();

            this.provenanceCleanup = () => {
                window.removeEventListener('scroll', onScroll);
                window.removeEventListener('resize', onScroll);
            };
        }
    },
    destroy() {
        this.instances.forEach((observer) => observer.disconnect());
        this.instances = [];
        if (this.provenanceCleanup) {
            this.provenanceCleanup();
            this.provenanceCleanup = null;
        }
    }
};
