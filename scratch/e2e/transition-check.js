const puppeteer = require('puppeteer-core');

const BASE = process.env.BASE_URL || 'http://localhost:5286';
const sleep = ms => new Promise(r => setTimeout(r, ms));

(async () => {
    const browser = await puppeteer.launch({
        executablePath: '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome',
        headless: 'new'
    });

    const report = { base: BASE, console: [], pageErrors: [], requestFailures: [] };

    async function newPage(width, height, tag) {
        const page = await browser.newPage();
        await page.setViewport({ width, height });
        page.on('console', m => {
            if (m.type() === 'error' || m.type() === 'warning') {
                report.console.push(`[${tag}] ${m.type()}: ${m.text()}`);
            }
        });
        page.on('pageerror', e => report.pageErrors.push(`[${tag}] ${e.message}`));
        page.on('requestfailed', r =>
            report.requestFailures.push(`[${tag}] ${r.url()} :: ${r.failure()?.errorText}`));
        return page;
    }

    // ---------- Desktop ----------
    const page = await newPage(1440, 900, 'desktop');
    await page.goto(`${BASE}/products`, { waitUntil: 'networkidle0', timeout: 60000 });
    await sleep(1500);
    await page.screenshot({ path: '../screenshots/transition-desktop-grid.png' });

    const slug = await page.evaluate(() => {
        const cards = [...document.querySelectorAll('[data-product-card]')];
        const match = cards.find(c => (c.getAttribute('data-product-slug') || '').includes('colombia')) || cards[0];
        return match?.getAttribute('data-product-slug') || null;
    });
    report.slug = slug;

    // Real mouse click on the card's image link (Blazor @onclick target).
    const linkSelector = `[data-product-card][data-product-slug="${slug}"] a[href="products/${slug}"]`;
    await page.waitForSelector(linkSelector, { visible: true });
    const linkBox = await (await page.$(linkSelector)).boundingBox();
    await page.mouse.click(linkBox.x + linkBox.width / 2, linkBox.y + linkBox.height / 2);

    // Mid-transition frame (~250ms into the 500ms flight).
    await sleep(250);
    report.midTransition = await page.evaluate(() => {
        const clone = document.querySelector('.product-transition-clone');
        const r = clone?.getBoundingClientRect();
        return {
            clonePresent: !!clone,
            cloneRect: r ? { left: Math.round(r.left), top: Math.round(r.top), width: Math.round(r.width), height: Math.round(r.height) } : null,
            flightTarget: { left: 0, top: 0, width: Math.round(window.innerWidth * 0.58), height: window.innerHeight },
            bodyCursor: getComputedStyle(document.body).cursor,
            transitionActive: document.documentElement.classList.contains('product-transition-active')
        };
    });
    await page.screenshot({ path: '../screenshots/transition-desktop-mid.png' });

    await page.waitForFunction(
        () => /^\/products\/[^/]+$/.test(location.pathname),
        { timeout: 20000 });
    await sleep(1500);
    await page.screenshot({ path: '../screenshots/transition-desktop-detail.png' });

    report.desktopDetail = await page.evaluate(() => {
        const q = s => document.querySelector(s);
        const cs = el => (el ? getComputedStyle(el) : null);
        const media = q('.product-detail-card__media');
        const sheet = q('.product-detail-card');
        const info = q('.product-detail-card__info') || sheet?.querySelector('[class*="__info"]');
        const img = q('[data-product-detail-image]');
        const priceSymbol = q('.product-detail-card__price-symbol');
        const stepper = q('.quantity-selector');
        const addCart = q('.btn-add-cart') || [...document.querySelectorAll('button')].find(b => /add to cart/i.test(b.textContent));
        const mediaRect = media?.getBoundingClientRect();
        const imgRect = img?.getBoundingClientRect();
        return {
            url: location.pathname,
            viewport: { w: innerWidth, h: innerHeight },
            mediaRect: mediaRect ? { left: Math.round(mediaRect.left), top: Math.round(mediaRect.top), width: Math.round(mediaRect.width), height: Math.round(mediaRect.height) } : null,
            imageRect: imgRect ? { left: Math.round(imgRect.left), top: Math.round(imgRect.top), width: Math.round(imgRect.width), height: Math.round(imgRect.height) } : null,
            imageLoaded: img ? (img.complete && img.naturalWidth > 0) : null,
            imageSrc: img?.currentSrc || img?.src || null,
            cloneLeftover: !!q('.product-transition-clone'),
            heroFromCardClass: sheet?.className.includes('product-detail-hero--from-card') || null,
            heroReadyClass: sheet?.className.includes('product-detail-hero--ready') || null,
            priceSymbolColor: cs(priceSymbol)?.color || null,
            stepperBorderRadius: cs(stepper)?.borderRadius || null,
            addCartBorderRadius: cs(addCart)?.borderRadius || null,
            addCartBg: cs(addCart)?.backgroundColor || null,
            sheetBg: cs(sheet)?.backgroundColor || null,
            sheetFontFamily: cs(sheet)?.fontFamily || null,
            bodyCursor: getComputedStyle(document.body).cursor,
            horizontalScroll: document.documentElement.scrollWidth > window.innerWidth + 1
        };
    });
    await page.close();

    // ---------- Mobile ----------
    const mpage = await newPage(390, 844, 'mobile');
    await mpage.goto(`${BASE}/products`, { waitUntil: 'networkidle0', timeout: 60000 });
    await sleep(1500);
    await mpage.waitForSelector(linkSelector, { visible: true });
    await mpage.evaluate(s => document.querySelector(s).scrollIntoView({ block: 'center' }), linkSelector);
    await sleep(300);
    await mpage.click(linkSelector);
    await sleep(250);
    report.mobileMidTransition = await mpage.evaluate(() => ({
        clonePresent: !!document.querySelector('.product-transition-clone'),
        url: location.pathname
    }));
    try {
        await mpage.waitForFunction(
            () => /^\/products\/[^/]+$/.test(location.pathname),
            { timeout: 20000 });
    } catch (e) {
        report.mobileNavError = e.message;
        report.mobileUrlAfterClick = await mpage.evaluate(() => location.pathname);
        await mpage.screenshot({ path: '../screenshots/transition-mobile-stuck.png' });
    }
    await sleep(1500);
    await mpage.screenshot({ path: '../screenshots/transition-mobile-detail.png' });
    report.mobileDetail = await mpage.evaluate(() => {
        const q = s => document.querySelector(s);
        const sheet = q('.product-detail-card');
        const r = sheet?.getBoundingClientRect();
        const img = q('[data-product-detail-image]');
        return {
            url: location.pathname,
            sheetRect: r ? { left: Math.round(r.left), top: Math.round(r.top), width: Math.round(r.width), height: Math.round(r.height) } : null,
            imageLoaded: img ? (img.complete && img.naturalWidth > 0) : null,
            cloneLeftover: !!q('.product-transition-clone')
        };
    });
    await mpage.close();

    await browser.close();
    console.log(JSON.stringify(report, null, 2));
})().catch(error => {
    console.error('FATAL', error);
    process.exit(1);
});
