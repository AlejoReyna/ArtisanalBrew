window.initAnimations = () => {
    const observerOptions = {
        root: null,
        rootMargin: '0px 0px -10% 0px',
        threshold: 0.1
    };

    const observer = new IntersectionObserver((entries, observer) => {
        entries.forEach(entry => {
            if (entry.isIntersecting) {
                entry.target.classList.add('is-visible');
                observer.unobserve(entry.target);
            }
        });
    }, observerOptions);

    const animatedElements = document.querySelectorAll('[data-animate]:not(.is-visible)');
    animatedElements.forEach(el => observer.observe(el));
};

document.addEventListener('DOMContentLoaded', window.initAnimations);
document.addEventListener('blazor:enhancedload', window.initAnimations);

