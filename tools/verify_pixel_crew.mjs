/**
 * Release gate for the homepage's trained pixel crew.
 *
 * This intentionally imports the exact policy and simulation shipped to the
 * browser. It catches truncated weights, accidental environment drift, and a
 * retraining run that produces a policy which no longer materially beats the
 * untrained checkpoint.
 */

import { GENERATIONS, SHAPE, WEIGHTS } from '../src/ThisCafeteria.Web/wwwroot/js/pixelCrewPolicy.js';
import {
    EPISODE_STEPS,
    makeWorld,
    observe,
    paramCount,
    policyForward,
    rollout,
    step,
    ACT_SIZE,
    OBS_SIZE,
} from '../src/ThisCafeteria.Web/wwwroot/js/pixelCrewSim.js';

const CHECKPOINTS = ['untrained', 'early', 'trained'];
// Calibrated against the slow-drift physics: the trained policy averages ~27
// coins per 30-second episode, so 20 leaves headroom for seed variance while
// still catching a genuinely degraded retrain.
const RELEASE_FLOOR = 20;
const HELD_OUT_SEEDS = Array.from({ length: 100 }, (_, index) => 120000 + index * 37);
const FRAME_RATE_SEEDS = Array.from({ length: 25 }, (_, index) => 220000 + index * 41);

function assert(condition, message) {
    if (!condition) {
        throw new Error(message);
    }
}

function mean(values) {
    return values.reduce((total, value) => total + value, 0) / values.length;
}

function evaluateCheckpoint(name) {
    const weights = Float64Array.from(WEIGHTS[name]);
    const collected = HELD_OUT_SEEDS.map(
        seed => rollout(weights, SHAPE, { seed, steps: EPISODE_STEPS }).collected
    );

    return {
        mean: mean(collected),
        minimum: Math.min(...collected),
        maximum: Math.max(...collected),
        zeroRuns: collected.filter(value => value === 0).length,
    };
}

function rolloutAtFrameRate(weights, seed, framesPerSecond) {
    const world = makeWorld({ seed });
    const observation = new Float64Array(OBS_SIZE);
    const action = new Float64Array(ACT_SIZE);
    const actions = new Float64Array(world.agents.length * ACT_SIZE);

    const seconds = EPISODE_STEPS / 60;
    for (let frame = 0; frame < framesPerSecond * seconds; frame++) {
        for (let index = 0; index < world.agents.length; index++) {
            observe(world, world.agents[index], observation);
            policyForward(weights, SHAPE, observation, action);
            actions[index * ACT_SIZE] = action[0];
            actions[index * ACT_SIZE + 1] = action[1];
        }

        step(world, actions, 1 / framesPerSecond);
    }

    return world.collected;
}

assert(
    SHAPE.length === 3 && SHAPE[0] === OBS_SIZE && SHAPE[2] === ACT_SIZE,
    `Policy shape ${SHAPE.join('→')} does not match the simulation interface.`
);
assert(GENERATIONS >= 300, `Expected a generation-300 policy, found generation ${GENERATIONS}.`);

const expectedParameters = paramCount(SHAPE);
for (const checkpoint of CHECKPOINTS) {
    const weights = WEIGHTS[checkpoint];
    assert(Array.isArray(weights), `Missing ${checkpoint} checkpoint.`);
    assert(
        weights.length === expectedParameters,
        `${checkpoint} has ${weights.length} parameters; expected ${expectedParameters}.`
    );
    assert(weights.every(Number.isFinite), `${checkpoint} contains a non-finite weight.`);
}

const results = Object.fromEntries(
    CHECKPOINTS.map(checkpoint => [checkpoint, evaluateCheckpoint(checkpoint)])
);

assert(
    results.early.mean >= results.untrained.mean * 3,
    `Generation 20 regressed: ${results.early.mean.toFixed(2)} vs ${results.untrained.mean.toFixed(2)} untrained.`
);
assert(
    results.trained.mean >= RELEASE_FLOOR,
    `Generation 300 averaged ${results.trained.mean.toFixed(2)} coins; release floor is ${RELEASE_FLOOR}.`
);
assert(
    results.trained.mean >= results.untrained.mean * 5,
    `Generation 300 is only ${(results.trained.mean / results.untrained.mean).toFixed(2)}× the untrained baseline.`
);
assert(
    results.trained.mean >= results.early.mean + 2,
    `Generation 300 no longer materially beats generation 20 (${results.trained.mean.toFixed(2)} vs ${results.early.mean.toFixed(2)}).`
);
assert(results.trained.zeroRuns === 0, 'Generation 300 produced a zero-collection held-out run.');

const trainedWeights = Float64Array.from(WEIGHTS.trained);
const frameRateMeans = Object.fromEntries(
    [20, 60, 120].map(framesPerSecond => [
        framesPerSecond,
        mean(FRAME_RATE_SEEDS.map(seed => rolloutAtFrameRate(trainedWeights, seed, framesPerSecond))),
    ])
);

for (const [framesPerSecond, coins] of Object.entries(frameRateMeans)) {
    assert(
        coins >= RELEASE_FLOOR,
        `Generation 300 averaged ${coins.toFixed(2)} coins at ${framesPerSecond} FPS; release floor is ${RELEASE_FLOOR}.`
    );
}

for (const checkpoint of CHECKPOINTS) {
    const result = results[checkpoint];
    console.log(
        `${checkpoint.padEnd(10)} mean=${result.mean.toFixed(2)} min=${result.minimum} max=${result.maximum} zero=${result.zeroRuns}`
    );
}
console.log(
    `frame rates  ${Object.entries(frameRateMeans)
        .map(([fps, coins]) => `${fps}fps=${coins.toFixed(2)}`)
        .join(' ')}`
);
console.log(`pixel crew release gate passed (${expectedParameters} parameters, generation ${GENERATIONS})`);
