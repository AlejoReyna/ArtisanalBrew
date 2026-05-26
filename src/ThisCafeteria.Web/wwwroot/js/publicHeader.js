window.initPublicHeader = () => {
    const hero = document.querySelector('.editorial-hero');
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
        header?.classList.remove('public-header--solid');
        return;
    }

    const headerHeight = header.offsetHeight || 76;
    const rootMargin = `-${headerHeight}px 0px 0px 0px`;

    const updateHeader = () => {
        header.classList.toggle('public-header--solid', hero.getBoundingClientRect().bottom <= headerHeight);
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
