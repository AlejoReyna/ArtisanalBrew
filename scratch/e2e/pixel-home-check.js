const puppeteer = require('puppeteer-core');

(async () => {
    const browser = await puppeteer.launch({
        executablePath: '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome',
        headless: 'new'
    });

    const checks = [
        { name: 'desktop-1440x900', width: 1440, height: 900 },
        { name: 'mobile-390x844', width: 390, height: 844 }
    ];

    for (const check of checks) {
        const page = await browser.newPage();
        await page.setViewport({ width: check.width, height: check.height });
        await page.goto('http://localhost:5299/', { waitUntil: 'networkidle0', timeout: 60000 });
        await new Promise(resolve => setTimeout(resolve, 2000));
        await page.screenshot({ path: `../screenshots/pixel-home-${check.name}.png` });
        await page.close();
    }

    await browser.close();
})().catch(error => {
    console.error(error);
    process.exit(1);
});
