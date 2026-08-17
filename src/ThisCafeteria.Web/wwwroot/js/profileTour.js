export function showStep(selector, instant = false) {
    const target = document.querySelector(selector);
    if (!target) return;

    const reduceMotion = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
    target.scrollIntoView({
        behavior: instant || reduceMotion ? "instant" : "smooth",
        block: "center",
        inline: "nearest"
    });
}
