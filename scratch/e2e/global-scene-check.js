// Verifies the GlobalScene background scenario across enhanced navigation:
// the sky containers must keep their exact DOM nodes between routes while the
// per-route layers cross-fade. Run with the app serving on :5417:
//   node global-scene-check.js
const puppeteer = require('puppeteer-core');

const BASE = 'http://localhost:5417';
const wait = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

(async () => {
    const browser = await puppeteer.launch({
        executablePath: '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome',
        headless: 'new'
    });

    const page = await browser.newPage();
    page.on('pageerror', (e) => console.log('PAGE ERROR:', e.message));
    page.on('console', (m) => {
        if (m.type() === 'error') console.log('CONSOLE ERROR:', m.text());
    });
    await page.setViewport({ width: 1440, height: 900 });

    const failures = [];
    const check = (name, ok) => {
        console.log(`${ok ? 'PASS' : 'FAIL'} — ${name}`);
        if (!ok) failures.push(name);
    };

    // Home: base sky + home layer visible.
    await page.goto(`${BASE}/`, { waitUntil: 'networkidle0', timeout: 60000 });
    await wait(1500);

    const home = await page.evaluate(() => ({
        route: document.documentElement.dataset.sceneRoute,
        node: (window.__scene = document.getElementById('ph-scene-root')) !== null,
        over: (window.__sceneOver = document.getElementById('ph-scene-root-over')) !== null,
        homeOpacity: parseFloat(getComputedStyle(document.querySelector('.gs-layer--home')).opacity),
        stakingOpacity: parseFloat(getComputedStyle(document.querySelector('.gs-layer--staking')).opacity),
    }));
    check('home: data-scene-route=home', home.route === 'home');
    check('home: both scene containers present', home.node && home.over);
    check('home: home layer visible', home.homeOpacity === 1);
    check('home: staking layer hidden', home.stakingOpacity === 0);

    // Crew runtime should have taken over (interactive circuit + module).
    const sim = await page.evaluate(() =>
        document.getElementById('ph-scene-root').classList.contains('ph-scene--sim') &&
        document.getElementById('ph-scene-root-over').classList.contains('ph-scene--sim'));
    check('home: crew runtime drives both containers', sim);

    await page.screenshot({ path: '../screenshots/gs-home.png' });

    // Navigate to /staking through the CTA (enhanced navigation).
    await Promise.all([
        page.waitForNavigation({ waitUntil: 'networkidle0', timeout: 60000 }).catch(() => {}),
        page.click('a[href="/staking"]'),
    ]);
    await wait(250); // mid-fade sample
    await page.screenshot({ path: '../screenshots/gs-home-to-staking-mid.png' });
    await wait(1500);

    const staking = await page.evaluate(() => ({
        url: location.pathname,
        route: document.documentElement.dataset.sceneRoute,
        sameNode: document.getElementById('ph-scene-root') === window.__scene,
        sameOver: document.getElementById('ph-scene-root-over') === window.__sceneOver,
        homeOpacity: parseFloat(getComputedStyle(document.querySelector('.gs-layer--home')).opacity),
        stakingOpacity: parseFloat(getComputedStyle(document.querySelector('.gs-layer--staking')).opacity),
        introCoins: document.querySelectorAll('.staking-intro__space').length,
    }));
    check('staking: url /staking', staking.url === '/staking');
    check('staking: data-scene-route=staking', staking.route === 'staking');
    check('staking: base sky node preserved', staking.sameNode);
    check('staking: crew node preserved', staking.sameOver);
    check('staking: home layer faded out', staking.homeOpacity === 0);
    check('staking: staking layer faded in', staking.stakingOpacity === 1);
    check('staking: intro no longer paints its own space', staking.introCoins === 0);
    await page.screenshot({ path: '../screenshots/gs-staking.png' });

    // Navigate to /procurement-lab through the header link.
    await Promise.all([
        page.waitForNavigation({ waitUntil: 'networkidle0', timeout: 60000 }).catch(() => {}),
        page.click('a[href="procurement-lab"]'),
    ]);
    await wait(1500);

    const lab = await page.evaluate(() => ({
        url: location.pathname,
        route: document.documentElement.dataset.sceneRoute,
        sameNode: document.getElementById('ph-scene-root') === window.__scene,
        homeOpacity: parseFloat(getComputedStyle(document.querySelector('.gs-layer--home')).opacity),
        stakingOpacity: parseFloat(getComputedStyle(document.querySelector('.gs-layer--staking')).opacity),
        chains: document.querySelectorAll('.ph-chain').length,
        planets: document.querySelectorAll('.ph-sky__planet').length,
        andromeda: document.querySelectorAll('.ph-sky__andromeda').length,
        stars: document.querySelectorAll('.ph-star').length,
        oldSky: document.querySelectorAll('.pl-sky').length,
    }));
    check('lab: url /procurement-lab', lab.url === '/procurement-lab');
    check('lab: data-scene-route=lab', lab.route === 'lab');
    check('lab: base sky node preserved', lab.sameNode);
    check('lab: both route layers hidden (sky keeps its initial state)', lab.homeOpacity === 0 && lab.stakingOpacity === 0);
    check('lab: 6 chain badges + 2 planets + andromeda + 16 stars', lab.chains === 6 && lab.planets === 2 && lab.andromeda === 1 && lab.stars === 16);
    check('lab: old pl-sky removed', lab.oldSky === 0);
    await page.screenshot({ path: '../screenshots/gs-lab.png' });

    // Back home twice through history (lab → staking → home): home layer fades
    // in again over the very same sky node.
    await page.goBack();
    await wait(1000);
    await page.goBack();
    await wait(1500);

    const back = await page.evaluate(() => ({
        url: location.pathname,
        sameNode: document.getElementById('ph-scene-root') === window.__scene,
        homeOpacity: parseFloat(getComputedStyle(document.querySelector('.gs-layer--home')).opacity),
        sim: document.getElementById('ph-scene-root').classList.contains('ph-scene--sim'),
    }));
    check('back home: url /', back.url === '/');
    check('back home: base sky node still preserved', back.sameNode);
    check('back home: home layer visible again', back.homeOpacity === 1);
    check('back home: crew runtime re-mounted', back.sim);
    await page.screenshot({ path: '../screenshots/gs-back-home.png' });

    await browser.close();

    if (failures.length) {
        console.log(`\n${failures.length} check(s) failed.`);
        process.exit(1);
    }
    console.log('\nAll GlobalScene checks passed.');
})().catch((error) => {
    console.error(error);
    process.exit(1);
});
