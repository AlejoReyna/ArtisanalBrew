window.initPublicHeader = () => {
    const hero = document.querySelector('.editorial-hero');
    const header = document.querySelector('.public-header');

    if (window.publicHeaderObserver) {
        window.publicHeaderObserver.disconnect();
        window.publicHeaderObserver = null;
    }

    if (!hero || !header) {
        header?.classList.remove('public-header--solid');
        return;
    }

    const headerHeight = header.offsetHeight || 76;
    const rootMargin = `-${headerHeight}px 0px 0px 0px`;

    const setSolid = (overHero) => {
        header.classList.toggle('public-header--solid', !overHero);
    };

    window.publicHeaderObserver = new IntersectionObserver(
        ([entry]) => setSolid(entry.isIntersecting),
        { root: null, rootMargin, threshold: 0 }
    );

    window.publicHeaderObserver.observe(hero);

    const rect = hero.getBoundingClientRect();
    setSolid(rect.bottom > headerHeight);
};
