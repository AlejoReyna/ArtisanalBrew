const puppeteer = require('puppeteer-core');
const sleep = ms => new Promise(r => setTimeout(r, ms));

(async () => {
    const browser = await puppeteer.launch({
        executablePath: '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome',
        headless: 'new'
    });
    const page = await browser.newPage();
    await page.setViewport({ width: 1440, height: 900 });
    await page.goto('http://localhost:5286/products', { waitUntil: 'networkidle0', timeout: 60000 });
    await sleep(1500);

    const slug = 'colombia-huila';
    const linkSelector = `[data-product-card][data-product-slug="${slug}"] a[href="products/${slug}"]`;
    await page.waitForSelector(linkSelector, { visible: true });
    const box = await (await page.$(linkSelector)).boundingBox();

    const t0 = Date.now();
    await page.mouse.click(box.x + box.width / 2, box.y + box.height / 2);

    const timeline = [];
    let shotTaken = false;
    for (let i = 0; i < 40; i++) {
        const state = await page.evaluate(() => {
            const clone = document.querySelector('.product-transition-clone');
            const r = clone?.getBoundingClientRect();
            return {
                url: location.pathname,
                clone: r ? { left: Math.round(r.left), width: Math.round(r.width), height: Math.round(r.height), opacity: getComputedStyle(clone).opacity } : null,
                active: document.documentElement.classList.contains('product-transition-active'),
                cursor: getComputedStyle(document.body).cursor
            };
        });
        timeline.push({ t: Date.now() - t0, ...state });
        if (!shotTaken && state.clone) {
            await page.screenshot({ path: '../screenshots/transition-desktop-flight.png' });
            timeline[timeline.length - 1].screenshot = 'transition-desktop-flight.png';
            shotTaken = true;
        }
        if (i >= 4 && /^\/products\/[^/]+$/.test(state.url) && !state.clone) break;
        await sleep(50);
    }
    // Settle, then confirm no clone remains.
    await sleep(1500);
    timeline.push({ t: Date.now() - t0, ...(await page.evaluate(() => ({
        url: location.pathname,
        clone: !!document.querySelector('.product-transition-clone')
    }))) });

    console.log(JSON.stringify(timeline, null, 1));
    await browser.close();
})().catch(e => { console.error('FATAL', e); process.exit(1); });
