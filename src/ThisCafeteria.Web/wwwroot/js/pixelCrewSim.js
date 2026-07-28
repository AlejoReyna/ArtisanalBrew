/**
 * Artisanal Brew — pixel crew simulation
 *
 * The physics and the reward for the hero's coin-collecting crew, in one file
 * that BOTH sides import: tools/train_pixel_crew.mjs runs it headless for
 * training, and wwwroot/js/pixelCrewRuntime.js runs it per animation frame in
 * the browser. There is deliberately no second implementation — a policy is
 * only valid for the dynamics it was trained against, and a re-derived
 * "equivalent" sim in Python or C# would drift silently.
 *
 * Everything works in a normalised unit square: x and y both run 0→1 across
 * the scene regardless of its pixel size or aspect ratio. The runtime scales
 * back to pixels when it writes transforms, so the same policy behaves the
 * same on a phone and on a 1440-wide desktop.
 *
 * Pure ESM, no dependencies, no DOM access.
 */

export const OBS_SIZE = 13;
export const ACT_SIZE = 2;

/**
 * Canonical episode length, in 1/60 s steps — thirty seconds of simulated
 * time. Exported rather than hardcoded per caller because the trainer, the
 * release gate and the docs all have to agree: the crew drifts slowly enough
 * that a shorter episode measures a different task, and a gate calibrated
 * against one length will reject a policy trained at another.
 */
export const EPISODE_STEPS = 1800;

/**
 * Weightless drift, not darting. `drag` is deliberately low relative to
 * `accel`: momentum carries a robot well past the point it stops thrusting,
 * which is what reads as floating in vacuum rather than walking. `maxSpeed`
 * puts a full crossing of the scene at roughly seven seconds, so the crew is
 * something you watch rather than something that flickers.
 *
 * Changing any of these invalidates the shipped weights — retrain.
 */
export const PARAMS = {
    accel: 0.6,        // units/s² at full throttle
    drag: 0.7,         // velocity damping per second — low, so glide persists
    maxSpeed: 0.15,    // units/s
    pickupRadius: 0.05,
    respawnSeconds: 5.5,
    margin: 0.03,      // keeps agents off the exact edge

    // Coffee bags: the difficulty layer. Unlike coins they are not always
    // there — each slot goes dormant for a random stretch, then appears
    // somewhere new and stays only briefly, so a robot has to decide whether
    // a bag is worth breaking off its coin run for.
    bagBoost: 1.25,        // speed multiplier while caffeinated
    bagBoostSeconds: 6,    // how long a drink lasts
    bagLifeMin: 7,         // a bag stays catchable for 7–12 s
    bagLifeMax: 12,
    bagDormantMin: 6,      // then the slot is empty for 6–18 s
    bagDormantMax: 18,
    bagRadius: 0.055,      // slightly bigger than a coin — bags are chunkier
};

/** Deterministic RNG — the trainer and the browser must agree on layouts. */
export function makeRng(seed) {
    let a = seed >>> 0;
    return function rng() {
        a |= 0;
        a = (a + 0x6d2b79f5) | 0;
        let t = Math.imul(a ^ (a >>> 15), 1 | a);
        t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
        return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
    };
}

/**
 * The hero's copy column, as a unit-square rectangle. Coins never SPAWN
 * inside it, which is the whole reason it exists: a coin resting on the "YOUR
 * NEXT COFFEE, ON-CHAIN" title is unreadable, and the old CSS layout hand-
 * placed every coin in the margins to avoid exactly that. Robots may still
 * cross it in transit — the traffic is welcome, the parking is not.
 *
 * The runtime overwrites this per layout by measuring .ph-hero__copy; the
 * default is a representative desktop box so training sees the same
 * distribution the browser will.
 */
export const DEFAULT_KEEP_OUT = { x0: 0.24, y0: 0.25, x1: 0.76, y1: 0.79 };

function inRect(rect, x, y) {
    return rect && x > rect.x0 && x < rect.x1 && y > rect.y0 && y < rect.y1;
}

/**
 * Nudge a fixed anchor clear of the copy column if an unusual layout puts it
 * inside — pushed to whichever edge, top or bottom, is nearer.
 */
function clampOutOfKeepOut(world, point) {
    const r = world.keepOut;
    if (!inRect(r, point.x, point.y)) return { x: point.x, y: point.y };
    const lo = PARAMS.margin;
    const hi = 1 - PARAMS.margin;
    const above = Math.max(lo, r.y0 - 0.04);
    const below = Math.min(hi, r.y1 + 0.04);
    const y = Math.abs(point.y - r.y0) < Math.abs(point.y - r.y1) ? above : below;
    return { x: point.x, y };
}

/** Uniform point in the scene that is not inside the keep-out rectangle. */
export function spawnPoint(world) {
    const lo = PARAMS.margin;
    const span = 1 - 2 * PARAMS.margin;
    for (let tries = 0; tries < 40; tries++) {
        const x = lo + world.rng() * span;
        const y = lo + world.rng() * span;
        if (!inRect(world.keepOut, x, y)) return { x, y };
    }
    // Degenerate keep-out (covers nearly everything): fall back to a band
    // along the top rather than looping forever.
    return { x: lo + world.rng() * span, y: lo + world.rng() * 0.1 };
}

/**
 * Opening formation: one robot per quadrant, a tidy 2×2 square.
 *
 * The anchors sit at 0.18/0.82 on both axes, which is outside the copy column
 * on every layout — including one-column mobile, where the keep-out spans
 * almost the full width but only the middle band vertically, so the 0.18 and
 * 0.82 rows clear it above and below.
 *
 * This is an initial condition, not a dynamics change: the trainer keeps the
 * default scattered starts (more diverse episodes make for a more robust
 * policy) while the hero opens on the formation. Switching between them does
 * not invalidate the shipped weights — the physics, observation and reward are
 * identical either way.
 */
export const QUADRANT_STARTS = [
    { x: 0.18, y: 0.18 },
    { x: 0.82, y: 0.18 },
    { x: 0.18, y: 0.82 },
    { x: 0.82, y: 0.82 },
];

export function makeWorld({
    seed = 1,
    agents = 4,
    coins = 7,
    bags = 6,
    keepOut = DEFAULT_KEEP_OUT,
    startFormation = 'scattered',
} = {}) {
    const rng = makeRng(seed);
    const world = {
        rng,
        time: 0,
        collected: 0,
        drank: 0,
        keepOut,
        agents: [],
        coins: [],
    };
    for (let i = 0; i < agents; i++) {
        const p = startFormation === 'quadrants' && i < QUADRANT_STARTS.length
            ? clampOutOfKeepOut(world, QUADRANT_STARTS[i])
            : spawnPoint(world);
        world.agents.push({
            x: p.x,
            y: p.y,
            vx: 0,
            vy: 0,
            // Purely cosmetic, but the runtime needs it to flip the sprite.
            facing: 1,
            justCollected: false,
            // Index of the coin this robot has claimed; see assignTargets.
            target: -1,
            // Simulation time until which this robot is caffeinated. The
            // runtime reads boostUntil > time to drive the boosted classes.
            boostUntil: -1,
            justDrank: false,
            drankBag: -1,
        });
    }
    for (let i = 0; i < coins; i++) {
        const p = spawnPoint(world);
        world.coins.push({ x: p.x, y: p.y, alive: true, respawnAt: 0, claimedBy: -1 });
    }
    world.bags = [];
    for (let i = 0; i < bags; i++) {
        // Staggered initial dormancy so they don't all pop in together on the
        // first second of the page.
        world.bags.push({
            x: 0.5, y: 0.5, alive: false,
            appearAt: rng() * PARAMS.bagDormantMax,
            expiresAt: 0,
        });
    }
    assignTargets(world);
    return world;
}

function bagDormancy(world) {
    return PARAMS.bagDormantMin + world.rng() * (PARAMS.bagDormantMax - PARAMS.bagDormantMin);
}

/** Index of the nearest catchable bag, or -1 when none is out. */
export function nearestBag(world, agent) {
    let best = -1;
    let bestD = Infinity;
    for (let i = 0; i < world.bags.length; i++) {
        const b = world.bags[i];
        if (!b.alive) continue;
        const d = (b.x - agent.x) ** 2 + (b.y - agent.y) ** 2;
        if (d < bestD) {
            bestD = d;
            best = i;
        }
    }
    return best;
}

/**
 * Bags blink in and out on their own schedule: dormant for a random stretch,
 * then suddenly present somewhere new for a short window. Nothing about the
 * timing is tied to the crew — that unpredictability is the difficulty. A bag
 * that expires uncaught simply goes dormant again.
 */
function stepBags(world) {
    for (const b of world.bags) {
        if (b.alive) {
            if (world.time >= b.expiresAt) {
                b.alive = false;
                b.appearAt = world.time + bagDormancy(world);
            }
        } else if (world.time >= b.appearAt) {
            const p = spawnPoint(world);
            b.x = p.x;
            b.y = p.y;
            b.alive = true;
            b.expiresAt = world.time
                + PARAMS.bagLifeMin
                + world.rng() * (PARAMS.bagLifeMax - PARAMS.bagLifeMin);
        }
    }
}

function respawnCoin(world, coin) {
    const p = spawnPoint(world);
    coin.x = p.x;
    coin.y = p.y;
    coin.alive = true;
    coin.claimedBy = -1;
}

/** Index of the live coin nearest to an agent, or -1 when the field is empty. */
export function nearestCoin(world, agent, skipClaimed = false) {
    let best = -1;
    let bestD = Infinity;
    for (let i = 0; i < world.coins.length; i++) {
        const c = world.coins[i];
        if (!c.alive) continue;
        if (skipClaimed && c.claimedBy >= 0) continue;
        const d = (c.x - agent.x) ** 2 + (c.y - agent.y) ** 2;
        if (d < bestD) {
            bestD = d;
            best = i;
        }
    }
    return best;
}

/**
 * Give every robot its own coin to hunt.
 *
 * Steering all four at whatever coin happens to be nearest makes the crew
 * clump: three robots converge on the same prize, two of them arrive to
 * nothing, and one robot can go a whole minute without ever collecting.
 * Claiming fixes that — a coin belongs to one robot until it is collected, so
 * all four always have somewhere of their own to be.
 *
 * Claims are sticky on purpose. Re-running a global nearest-match every frame
 * would make two robots swap targets each time their paths crossed, which
 * looks like indecision. A robot only re-targets when its coin is gone.
 */
export function assignTargets(world) {
    for (const c of world.coins) {
        if (!c.alive) c.claimedBy = -1;
    }

    // Drop claims whose coin died, then let unassigned robots pick.
    for (let i = 0; i < world.agents.length; i++) {
        const a = world.agents[i];
        const c = a.target >= 0 ? world.coins[a.target] : null;
        if (!c || !c.alive || c.claimedBy !== i) a.target = -1;
    }

    for (let i = 0; i < world.agents.length; i++) {
        const a = world.agents[i];
        if (a.target >= 0) continue;
        // Prefer an unclaimed coin; fall back to the nearest live one when
        // there are more robots than coins on the field.
        let idx = nearestCoin(world, a, true);
        if (idx < 0) idx = nearestCoin(world, a, false);
        if (idx < 0) continue;
        a.target = idx;
        world.coins[idx].claimedBy = i;
    }
}

/**
 * Observation vector, written into `out` to keep the hot loop allocation-free.
 *
 *   0,1   unit vector to this robot's OWN claimed coin
 *   2     distance to it
 *   3,4   own velocity
 *   5–8   distance to each wall
 *   9,10  unit vector to the nearest catchable coffee bag (0,0 when none)
 *   11    distance to it (1 when none is out)
 *   12    own caffeine remaining, 0–1
 *
 * Thirteen numbers, all roughly in [-1, 1]. The last four are what let a robot
 * decide whether a bag is worth the detour.
 */
export function observe(world, agent, out = new Float64Array(OBS_SIZE)) {
    const idx = agent.target >= 0 && world.coins[agent.target].alive
        ? agent.target
        : nearestCoin(world, agent);
    if (idx < 0) {
        out[0] = 0;
        out[1] = 0;
        out[2] = 1;
    } else {
        const c = world.coins[idx];
        const dx = c.x - agent.x;
        const dy = c.y - agent.y;
        const d = Math.hypot(dx, dy) || 1e-6;
        out[0] = dx / d;
        out[1] = dy / d;
        out[2] = Math.min(d, 1);
    }
    out[3] = agent.vx / PARAMS.maxSpeed;
    out[4] = agent.vy / PARAMS.maxSpeed;
    out[5] = agent.x;
    out[6] = 1 - agent.x;
    out[7] = agent.y;
    out[8] = 1 - agent.y;

    const bagIdx = nearestBag(world, agent);
    if (bagIdx < 0) {
        out[9] = 0;
        out[10] = 0;
        out[11] = 1;
    } else {
        const b = world.bags[bagIdx];
        const bx = b.x - agent.x;
        const by = b.y - agent.y;
        const bd = Math.hypot(bx, by) || 1e-6;
        out[9] = bx / bd;
        out[10] = by / bd;
        out[11] = Math.min(bd, 1);
    }

    out[12] = agent.boostUntil > world.time
        ? (agent.boostUntil - world.time) / PARAMS.bagBoostSeconds
        : 0;

    return out;
}

/**
 * Advance the world by `dt` seconds given one [ax, ay] action per agent.
 * Returns the reward summed over agents — the trainer uses it, the runtime
 * ignores it.
 */
export function step(world, actions, dt) {
    let reward = 0;
    world.time += dt;

    for (let i = 0; i < world.agents.length; i++) {
        const a = world.agents[i];
        a.justCollected = false;
        a.justDrank = false;
        a.drankBag = -1;

        const ax = Math.max(-1, Math.min(1, actions[i * 2]));
        const ay = Math.max(-1, Math.min(1, actions[i * 2 + 1]));

        a.vx += ax * PARAMS.accel * dt;
        a.vy += ay * PARAMS.accel * dt;

        const damp = Math.max(0, 1 - PARAMS.drag * dt);
        a.vx *= damp;
        a.vy *= damp;

        // Caffeine raises the ceiling, not the thrust: a boosted robot keeps
        // the same weightless acceleration but coasts faster once it is up to
        // speed, so the drink reads as momentum rather than as a twitch.
        const cap = a.boostUntil > world.time
            ? PARAMS.maxSpeed * PARAMS.bagBoost
            : PARAMS.maxSpeed;
        const speed = Math.hypot(a.vx, a.vy);
        if (speed > cap) {
            a.vx = (a.vx / speed) * cap;
            a.vy = (a.vy / speed) * cap;
        }

        a.x += a.vx * dt;
        a.y += a.vy * dt;

        // Walls absorb rather than bounce: hitting one wastes time, which is
        // what teaches the policy to stop steering into them.
        const lo = PARAMS.margin;
        const hi = 1 - PARAMS.margin;
        if (a.x < lo) { a.x = lo; a.vx = 0; }
        if (a.x > hi) { a.x = hi; a.vx = 0; }
        if (a.y < lo) { a.y = lo; a.vy = 0; }
        if (a.y > hi) { a.y = hi; a.vy = 0; }

        if (Math.abs(a.vx) > 1e-4) a.facing = a.vx < 0 ? -1 : 1;

        // Shaping is measured against this robot's OWN claimed coin, so the
        // gradient rewards working its assignment rather than crowding
        // whichever coin happens to be closest to the pack.
        const idx = a.target >= 0 && world.coins[a.target].alive ? a.target : nearestCoin(world, a);
        if (idx >= 0) {
            const c = world.coins[idx];
            const d = Math.hypot(c.x - a.x, c.y - a.y);
            reward -= 0.001 * d;
        }

        // Pickup is still physical: a robot that drifts through any live coin
        // pockets it, claimed or not. Refusing a coin it is touching because
        // another robot owns it would look broken.
        for (let k = 0; k < world.coins.length; k++) {
            const c = world.coins[k];
            if (!c.alive) continue;
            if (Math.hypot(c.x - a.x, c.y - a.y) >= PARAMS.pickupRadius) continue;
            c.alive = false;
            c.claimedBy = -1;
            c.respawnAt = world.time + PARAMS.respawnSeconds;
            a.justCollected = true;
            world.collected++;
            reward += 1;
            break;
        }

        // Catching a bag pays less than a coin. The bag is worth chasing
        // mainly for what the boost earns afterwards, and a direct reward this
        // size is just enough for evolution to find that out — pay it as much
        // as a coin and the crew abandons coins to farm bags.
        for (let k = 0; k < world.bags.length; k++) {
            const b = world.bags[k];
            if (!b.alive) continue;
            if (Math.hypot(b.x - a.x, b.y - a.y) >= PARAMS.bagRadius) continue;
            b.alive = false;
            b.appearAt = world.time + bagDormancy(world);
            a.boostUntil = world.time + PARAMS.bagBoostSeconds;
            a.justDrank = true;
            // Which bag, so the runtime can tell a catch from an expiry — both
            // clear `alive`, but only one should play the catch animation.
            a.drankBag = k;
            world.drank++;
            reward += 0.4;
            break;
        }

        reward -= 0.0006 * (ax * ax + ay * ay);
    }

    for (const c of world.coins) {
        if (!c.alive && world.time >= c.respawnAt) respawnCoin(world, c);
    }

    stepBags(world);

    // Re-issue claims after pickups and respawns so the next frame's
    // observations already reflect who is hunting what.
    assignTargets(world);

    return reward;
}

/* ── Policy: a 9→H→2 MLP with tanh throughout. Small enough that a forward
   pass for the whole crew is a rounding error per frame, and small enough that
   the trained weights ship as a couple of KB of literal numbers. ── */

export function policyShape(hidden = 16) {
    return [OBS_SIZE, hidden, ACT_SIZE];
}

export function paramCount(shape) {
    const [i, h, o] = shape;
    return i * h + h + h * o + o;
}

/** Forward pass. `w` is the flat parameter vector, `out` receives [ax, ay]. */
export function policyForward(w, shape, obs, out = new Float64Array(ACT_SIZE)) {
    const [nIn, nHid, nOut] = shape;
    let p = 0;
    const hidden = new Float64Array(nHid);
    for (let j = 0; j < nHid; j++) {
        let sum = 0;
        for (let k = 0; k < nIn; k++) sum += w[p + k] * obs[k];
        p += nIn;
        hidden[j] = Math.tanh(sum + w[p + j]);
    }
    p += nHid;
    for (let j = 0; j < nOut; j++) {
        let sum = 0;
        for (let k = 0; k < nHid; k++) sum += w[p + k] * hidden[k];
        p += nHid;
        out[j] = Math.tanh(sum + w[p + j]);
    }
    return out;
}

/** One episode; returns total reward. Used by the trainer and by evaluation. */
export function rollout(w, shape, { seed = 1, steps = 1800, dt = 1 / 60, agents = 4, coins = 7, bags = 6 } = {}) {
    const world = makeWorld({ seed, agents, coins, bags });
    const obs = new Float64Array(OBS_SIZE);
    const act = new Float64Array(ACT_SIZE);
    const actions = new Float64Array(agents * ACT_SIZE);
    let total = 0;
    for (let t = 0; t < steps; t++) {
        for (let i = 0; i < world.agents.length; i++) {
            observe(world, world.agents[i], obs);
            policyForward(w, shape, obs, act);
            actions[i * 2] = act[0];
            actions[i * 2 + 1] = act[1];
        }
        total += step(world, actions, dt);
    }
    return { reward: total, collected: world.collected, drank: world.drank };
}
