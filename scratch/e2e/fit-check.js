const puppeteer = require('puppeteer-core');

(async () => {
    const browser = await puppeteer.launch({
        executablePath: '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome',
        headless: 'new'
    });

    const checks = [
        { name: 'desktop-1440x900', width: 1440, height: 900 },
        { name: 'desktop-1920x1080', width: 1920, height: 1080 },
        { name: 'mobile-390x844', width: 390, height: 844 }
    ];

    for (const check of checks) {
        const page = await browser.newPage();
        await page.setViewport({ width: check.width, height: check.height });
        await page.goto('http://localhost:5299/products', { waitUntil: 'networkidle0', timeout: 60000 });
        await new Promise(resolve => setTimeout(resolve, 1500));

        const metrics = await page.evaluate(() => {
            const pagination = document.querySelector('.products-catalog__pagination');
            const footer = document.querySelector('.journal-footer');
            const catalog = document.querySelector('.products-catalog');
            const cards = [...document.querySelectorAll('[data-product-card]')];
            const paginationRect = pagination?.getBoundingClientRect();
            return {
                innerHeight: window.innerHeight,
                scrollHeight: document.documentElement.scrollHeight,
                bodyScrollHeight: document.body.scrollHeight,
                hasVerticalScroll: document.documentElement.scrollHeight > window.innerHeight + 1,
                footerDisplay: footer ? getComputedStyle(footer).display : 'missing',
                catalogHeight: catalog?.getBoundingClientRect().height,
                cardCount: cards.length,
                cardHeights: cards.map(card => Math.round(card.getBoundingClientRect().height)),
                paginationBottom: paginationRect ? Math.round(paginationRect.bottom) : null,
                paginationVisible: paginationRect ? paginationRect.bottom <= window.innerHeight && paginationRect.top >= 0 : null,
                lastCardBottom: cards.length ? Math.round(cards[cards.length - 1].getBoundingClientRect().bottom) : null
            };
        });

        console.log(`--- ${check.name} ---`);
        console.log(JSON.stringify(metrics, null, 2));
        await page.screenshot({ path: `../screenshots/fit-${check.name}.png` });
        await page.close();
    }

    await browser.close();
})().catch(error => {
    console.error(error);
    process.exit(1);
});
