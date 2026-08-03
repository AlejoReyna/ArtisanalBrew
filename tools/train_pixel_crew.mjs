/**
 * Artisanal Brew — train the hero's coin-collecting crew.
 *
 * Trains the policy that drives the robots on the "/" hero. Deliberately NOT
 * PPO: the task is a 258-parameter steering problem, and OpenAI-style
 * evolution strategies solve it in seconds with no autograd, no tensor
 * library, and no second copy of the environment. The environment is imported
 * straight from the file the browser runs (wwwroot/js/pixelCrewSim.js), so
 * what is trained here is exactly what ships.
 *
 * Run from the repo root:
 *
 *     node tools/train_pixel_crew.mjs                # full run, writes weights
 *     node tools/train_pixel_crew.mjs --gens 40      # shorter run
 *
 * Outputs:
 *     src/ThisCafeteria.Web/wwwroot/js/pixelCrewPolicy.js   weights, as an ES
 *         module the runtime imports — baked in as literals rather than
 *         fetched as JSON so the page needs no extra request and inherits the
 *         normal static-asset caching.
 *     scratch/pixel-crew-training.csv                learning curve
 *
 * Three checkpoints are saved (an untrained one, an early one, and the final
 * one) so the hero can show the difference between a policy that has learned
 * nothing and one that has.
 */

import { writeFileSync, mkdirSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

import {
    EPISODE_STEPS,
    makeRng,
    policyShape,
    paramCount,
    rollout,
} from '../src/ThisCafeteria.Web/wwwroot/js/pixelCrewSim.js';

const HERE = dirname(fileURLToPath(import.meta.url));
const ROOT = resolve(HERE, '..');
const POLICY_OUT = resolve(ROOT, 'src/ThisCafeteria.Web/wwwroot/js/pixelCrewPolicy.js');
const CURVE_OUT = resolve(ROOT, 'scratch/pixel-crew-training.csv');

function arg(name, fallback) {
    const i = process.argv.indexOf('--' + name);
    return i >= 0 && process.argv[i + 1] ? Number(process.argv[i + 1]) : fallback;
}

const GENERATIONS = arg('gens', 300);
const POPULATION = arg('pop', 64);        // must be even: sampled in ± pairs
const SIGMA = 0.1;
const LEARNING_RATE = 0.03;
const EPISODES_PER_EVAL = 3;              // averaged, so luck in coin layout
// EPISODE_STEPS comes from the simulation (30 s at dt=1/60). It is shared
// rather than redeclared because tools/verify_pixel_crew.mjs gates on the same
// number: a policy trained at one episode length and judged at another looks
// like a regression that isn't one.
const SHAPE = policyShape(16);
const N = paramCount(SHAPE);

const rng = makeRng(20260728);

/** Box–Muller, so the perturbations are Gaussian rather than uniform. */
function gauss() {
    let u = 0;
    let v = 0;
    while (u === 0) u = rng();
    while (v === 0) v = rng();
    return Math.sqrt(-2 * Math.log(u)) * Math.cos(2 * Math.PI * v);
}

function evaluate(w, generation) {
    let total = 0;
    let coins = 0;
    for (let e = 0; e < EPISODES_PER_EVAL; e++) {
        // Layouts vary per generation so the policy has to generalise rather
        // than memorise one arrangement of coins.
        const r = rollout(w, SHAPE, { seed: generation * 977 + e * 31 + 7, steps: EPISODE_STEPS });
        total += r.reward;
        coins += r.collected;
    }
    return { reward: total / EPISODES_PER_EVAL, collected: coins / EPISODES_PER_EVAL };
}

/**
 * Rank-normalise returns to [-0.5, 0.5]. This is what keeps ES stable: the
 * update depends only on the ORDER of the population, so a single lucky
 * episode with a huge return cannot dominate the step.
 */
function rankNormalise(values) {
    const order = values.map((v, i) => [v, i]).sort((a, b) => a[0] - b[0]);
    const out = new Float64Array(values.length);
    order.forEach(([, idx], rank) => {
        out[idx] = rank / (values.length - 1) - 0.5;
    });
    return out;
}

function train() {
    let theta = new Float64Array(N);
    for (let i = 0; i < N; i++) theta[i] = gauss() * 0.1;

    const checkpoints = {};
    const curve = ['generation,mean_reward,best_reward,coins_per_episode'];

    checkpoints.untrained = Array.from(theta);
    const baseline = evaluate(theta, 0);
    console.log(`gen   0  reward ${baseline.reward.toFixed(2)}  coins ${baseline.collected.toFixed(1)}  (untrained)`);

    const half = POPULATION / 2;
    const noise = [];

    for (let gen = 1; gen <= GENERATIONS; gen++) {
        noise.length = 0;
        const returns = new Array(POPULATION);

        // Mirrored sampling: each perturbation is evaluated as +eps and -eps,
        // which cancels most of the gradient's variance for free.
        for (let k = 0; k < half; k++) {
            const eps = new Float64Array(N);
            for (let i = 0; i < N; i++) eps[i] = gauss();
            noise.push(eps);

            const plus = new Float64Array(N);
            const minus = new Float64Array(N);
            for (let i = 0; i < N; i++) {
                plus[i] = theta[i] + SIGMA * eps[i];
                minus[i] = theta[i] - SIGMA * eps[i];
            }
            returns[k] = evaluate(plus, gen).reward;
            returns[k + half] = evaluate(minus, gen).reward;
        }

        const shaped = rankNormalise(returns);
        const grad = new Float64Array(N);
        for (let k = 0; k < half; k++) {
            const weight = shaped[k] - shaped[k + half];
            const eps = noise[k];
            for (let i = 0; i < N; i++) grad[i] += eps[i] * weight;
        }

        const scale = LEARNING_RATE / (POPULATION * SIGMA);
        for (let i = 0; i < N; i++) theta[i] += scale * grad[i];

        if (gen % 10 === 0 || gen === 1) {
            const evalNow = evaluate(theta, gen);
            const best = Math.max(...returns);
            curve.push(`${gen},${evalNow.reward.toFixed(4)},${best.toFixed(4)},${evalNow.collected.toFixed(2)}`);
            console.log(
                `gen ${String(gen).padStart(3)}  reward ${evalNow.reward.toFixed(2)}` +
                `  coins ${evalNow.collected.toFixed(1)}`
            );
        }

        if (gen === 20) checkpoints.early = Array.from(theta);
    }

    checkpoints.trained = Array.from(theta);
    if (!checkpoints.early) checkpoints.early = Array.from(theta);

    return { checkpoints, curve };
}

function summarise(name, w) {
    let coins = 0;
    for (let s = 0; s < 5; s++) {
        coins += rollout(Float64Array.from(w), SHAPE, { seed: 90000 + s * 13, steps: EPISODE_STEPS }).collected;
    }
    return { name, coinsPerEpisode: coins / 5 };
}

function main() {
    console.log(`training ${N} parameters, shape ${SHAPE.join('→')}, pop ${POPULATION}, ${GENERATIONS} generations`);
    const started = Date.now();
    const { checkpoints, curve } = train();
    const seconds = ((Date.now() - started) / 1000).toFixed(1);

    // Held-out layouts the trainer never optimised against.
    const report = ['untrained', 'early', 'trained'].map((k) => summarise(k, checkpoints[k]));
    console.log('\nheld-out coins per episode:');
    for (const r of report) console.log(`  ${r.name.padEnd(10)} ${r.coinsPerEpisode.toFixed(1)}`);

    const round = (arr) => arr.map((v) => Number(v.toFixed(5)));
    const body = `/**
 * Artisanal Brew — trained coin-collecting policy for the "/" hero.
 *
 * GENERATED FILE — do not hand-edit. Produced by tools/train_pixel_crew.mjs,
 * which trains against wwwroot/js/pixelCrewSim.js (the same physics the
 * browser runs). Regenerate with:
 *
 *     node tools/train_pixel_crew.mjs
 *
 * Three checkpoints ship so the hero can show what training bought: the
 * untrained policy drifts into walls, the early one overshoots and circles,
 * the trained one sweeps the field. Held-out coins per episode at build time:
${report.map((r) => ` *     ${r.name.padEnd(10)} ${r.coinsPerEpisode.toFixed(1)}`).join('\n')}
 */

export const SHAPE = [${SHAPE.join(', ')}];
export const GENERATIONS = ${GENERATIONS};

export const WEIGHTS = {
    untrained: ${JSON.stringify(round(checkpoints.untrained))},
    early: ${JSON.stringify(round(checkpoints.early))},
    trained: ${JSON.stringify(round(checkpoints.trained))}
};
`;

    mkdirSync(dirname(POLICY_OUT), { recursive: true });
    writeFileSync(POLICY_OUT, body);
    mkdirSync(dirname(CURVE_OUT), { recursive: true });
    writeFileSync(CURVE_OUT, curve.join('\n') + '\n');

    console.log(`\ntrained in ${seconds}s`);
    console.log(`wrote ${POLICY_OUT}`);
    console.log(`wrote ${CURVE_OUT}`);
}

main();
