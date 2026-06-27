window.initPublicHeader = () => {
    const hero = document.querySelector('.editorial-hero, .journal-hero');
    const header = document.querySelector('.public-header');

    if (window.publicHeaderObserver) {
        window.publicHeaderObserver.disconnect();
        window.publicHeaderObserver = null;
    }

    if (window.publicHeaderController) {
        window.publicHeaderController.abort();
        window.publicHeaderController = null;
    }

    if (!hero || !header) {
        header?.classList.add('public-header--solid');
        const fallbackMeta = document.querySelector('meta[name="theme-color"]');
        if (fallbackMeta) fallbackMeta.content = '#fbf9f4';
        return;
    }

    const headerHeight = header.offsetHeight || 76;
    const rootMargin = `-${headerHeight}px 0px 0px 0px`;

    const themeMeta = document.querySelector('meta[name="theme-color"]');

    const updateHeader = () => {
        const heroGone = hero.getBoundingClientRect().bottom <= window.innerHeight * 0.7;
        header.classList.toggle('public-header--solid', heroGone);
        if (themeMeta) themeMeta.content = heroGone ? '#fbf9f4' : '#050505';
    };

    window.publicHeaderObserver = new IntersectionObserver(
        () => updateHeader(),
        { root: null, rootMargin, threshold: 0 }
    );

    window.publicHeaderObserver.observe(hero);

    window.publicHeaderController = new AbortController();
    const { signal } = window.publicHeaderController;

    window.addEventListener('scroll', updateHeader, { passive: true, signal });
    window.addEventListener('resize', updateHeader, { signal });

    updateHeader();
};
