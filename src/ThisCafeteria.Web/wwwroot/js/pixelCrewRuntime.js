/**
 * Artisanal Brew — pixel crew runtime
 *
 * Drives the crew's robots in the GlobalScene background scenario from the
 * trained policy instead of from CSS keyframes. Each frame it observes the
 * world, runs the 9→16→2 network per robot, steps the shared simulation
 * (wwwroot/js/pixelCrewSim.js — the exact file the trainer optimised
 * against), and writes the resulting positions as transforms.
 *
 * The scenario is split across two fixed, data-permanent containers (see
 * Components/Layout/GlobalScene.razor): the root (#ph-scene-root, stacked
 * UNDER the page content) holds the coins and bags, and #ph-scene-root-over
 * (stacked ABOVE the content) holds the robots, because they must stay
 * pointer-reachable while the rest of the sky is a background. Both are
 * viewport-sized, so coordinates measured against the root apply verbatim in
 * the -over container.
 *
 * What stays in CSS: the sprite-sheet hover loop, the idle bob, the collect
 * poses and the +1 pop. Those are decoration and are better off declarative.
 * What moves here: position, and the decision of where to go — which is the
 * part that used to be a hand-timed 90s keyframe cycle with each coin pinned
 * to a robot's destination so the two would coincide. Under the sim a robot
 * collects a coin because it reached it, not because the clock said so.
 *
 * Adding .ph-scene--sim to BOTH containers is what hands control over; the
 * stylesheet keys its sim-mode rules off that class, so if this module never
 * loads (or the visitor prefers reduced motion) the original CSS composition
 * is still what renders.
 *
 * Blazor notes: this is loaded as a JS module via IJSObjectReference from
 * PixelHome.razor, and `destroy` must be called on dispose — enhanced
 * navigation leaves the module cached, so a second init without a teardown
 * would run two rAF loops over the same DOM.
 */

import {
    makeWorld,
    observe,
    step,
    policyForward,
    OBS_SIZE,
    ACT_SIZE,
    PARAMS,
} from './pixelCrewSim.js';

import { SHAPE, WEIGHTS } from './pixelCrewPolicy.js';

const ROLES = ['scout', 'buyer', 'courier', 'inspector'];
const SIM_CLASS = 'ph-scene--sim';
const COLLECTED_CLASS = 'is-collected';
const COLLECTING_CLASS = 'is-collecting';
const HELD_CLASS = 'ph-scene__roam--held';

/* ── Coffee-bag animation contract ────────────────────────────────────────
   The bag catch/drink animation is owned by a separate stylesheet pass. This
   module only guarantees the state; it deliberately does not style any of it.
   The classes it drives, and exactly when:

     .ph-bag.is-live         present for the whole window a bag is catchable.
                             Absent means dormant — the element is still in the
                             DOM and still positioned, just not in play.
     .ph-bag.is-caught       added for one ~900ms window at the instant a robot
                             reaches the bag, then removed. Re-added cleanly on
                             every subsequent catch (a reflow forces the
                             restart), so a one-shot animation will replay.
     .ph-scene__roam.is-boosted
                             present for the whole 6s the robot is caffeinated.
                             Good for a persistent trail/glow.
     .ph-scene__unit.is-drinking
                             one ~900ms shot at the moment of the catch, on the
                             same element that already carries .is-collecting.
                             Good for the drink pose.

   Bag positions are written as transforms under .ph-scene--sim, and the
   per-bag CSS tilt is preserved through the --bag-tilt custom property — so
   any animation that also wants `transform` should compose from a child
   element or from --bag-tilt rather than overwriting the wrapper. */
const BAG_LIVE_CLASS = 'is-live';
const BAG_CAUGHT_CLASS = 'is-caught';
const BOOSTED_CLASS = 'is-boosted';
const DRINKING_CLASS = 'is-drinking';

// Long enough for the grab pose and the +1 to play out, matching the
// one-shot durations in PixelHome.razor.css.
const COLLECT_MS = 900;
// A tab restored after a while must not integrate one enormous step.
const MAX_DT = 1 / 20;

// The crew holds its opening formation this long before the policy takes over.
// The idle bob and sprite loop are CSS and keep running throughout, so the
// pause reads as the crew hanging in place, not as a frozen frame.
const START_HOLD_SECONDS = 1.3;

class Crew {
    constructor(root, checkpoint) {
        this.root = root;
        this.scene = root.querySelector('.ph-scene') || root;
        // The robots live in the companion above-content container (see the
        // module header); fall back to the scene if it is missing.
        this.over = (root.id && document.getElementById(root.id + '-over')) || this.scene;
        this.weights = Float64Array.from(WEIGHTS[checkpoint] || WEIGHTS.trained);
        this.obs = new Float64Array(OBS_SIZE);
        this.act = new Float64Array(ACT_SIZE);

        this.robots = ROLES
            .map((role) => this.over.querySelector('.ph-scene__roam--' + role))
            .filter(Boolean);
        this.coinEls = Array.from(this.scene.querySelectorAll('.ph-coinbit'));
        this.bagEls = Array.from(this.scene.querySelectorAll('.ph-bag'));

        // Measure the keep-out before building the world so even the opening
        // frame has nothing parked on the title.
        this.world = makeWorld({
            seed: (Math.random() * 1e9) | 0,
            agents: this.robots.length,
            coins: this.coinEls.length,
            bags: this.bagEls.length,
            keepOut: this.keepOutRect(this.scene.getBoundingClientRect()),
            // The hero opens on the 2×2 formation; the trainer keeps scattered
            // starts. Initial conditions only — same physics either way.
            startFormation: 'quadrants',
        });
        this.holdRemaining = START_HOLD_SECONDS;
        this.actions = new Float64Array(this.robots.length * ACT_SIZE);
        this.collectTimers = this.robots.map(() => 0);
        this.drinkTimers = this.robots.map(() => 0);
        this.bagCaughtTimers = this.bagEls.map(() => 0);

        this.dragging = null;
        this.last = 0;
        this.frame = 0;

        this.measure();
        this.scene.classList.add(SIM_CLASS);
        this.over.classList.add(SIM_CLASS);

        this._tick = this.tick.bind(this);
        this._resize = this.measure.bind(this);
        this._down = this.onPointerDown.bind(this);
        this._move = this.onPointerMove.bind(this);
        this._up = this.onPointerUp.bind(this);
        this._visibility = () => { this.last = 0; };

        window.addEventListener('resize', this._resize);
        document.addEventListener('visibilitychange', this._visibility);
        // The robots live in this.over, so drag gestures are caught there.
        this.over.addEventListener('pointerdown', this._down);

        // Place everything before the first frame so there is no flash of
        // robots stacked at the scene origin.
        this.render();
        this.frame = requestAnimationFrame(this._tick);
    }

    /**
     * Keep-out rectangle for coin spawns, measured from the live copy column
     * rather than hardcoded: the hero has an intro toggle and a one-column
     * mobile layout, so the box that must stay clear is different per view.
     * Returns null when the copy is hidden — then the whole scene is fair game.
     */
    keepOutRect(sceneRect) {
        // The scene lives in the layout's GlobalScene now, not inside the
        // hero, so the copy column is looked up from the document.
        const copy = document.querySelector('.ph-hero__copy');
        if (!copy || !copy.offsetParent) return null;
        const b = copy.getBoundingClientRect();
        if (!b.width || !b.height) return null;
        const padX = 0.01;
        const padY = 0.02;
        return {
            x0: (b.left - sceneRect.left) / sceneRect.width - padX,
            y0: (b.top - sceneRect.top) / sceneRect.height - padY,
            x1: (b.right - sceneRect.left) / sceneRect.width + padX,
            y1: (b.bottom - sceneRect.top) / sceneRect.height + padY,
        };
    }

    measure() {
        const r = this.scene.getBoundingClientRect();
        this.width = r.width;
        this.height = r.height;
        if (this.world) this.world.keepOut = this.keepOutRect(r);
        this.sizes = this.robots.map((el) => {
            const b = el.getBoundingClientRect();
            return { w: b.width, h: b.height };
        });
        this.coinSizes = this.coinEls.map((el) => {
            const b = el.getBoundingClientRect();
            return { w: b.width, h: b.height };
        });
        this.bagSizes = this.bagEls.map((el) => {
            const b = el.getBoundingClientRect();
            return { w: b.width, h: b.height };
        });
    }

    /** Unit-square position -> top-left pixel offset for an element. */
    place(el, x, y, size, suffix = '') {
        const px = x * this.width - size.w / 2;
        const py = y * this.height - size.h / 2;
        el.style.transform = `translate3d(${px.toFixed(1)}px, ${py.toFixed(1)}px, 0)${suffix}`;
    }

    render() {
        for (let i = 0; i < this.robots.length; i++) {
            const a = this.world.agents[i];
            this.place(this.robots[i], a.x, a.y, this.sizes[i]);
        }
        for (let i = 0; i < this.coinEls.length; i++) {
            const c = this.world.coins[i];
            this.place(this.coinEls[i], c.x, c.y, this.coinSizes[i]);
        }
        for (let i = 0; i < this.bagEls.length; i++) {
            const b = this.world.bags[i];
            // Each bag keeps its designed tilt; --bag-tilt is a CSS custom
            // property so the inline transform can compose with it instead of
            // flattening the sprite.
            this.place(this.bagEls[i], b.x, b.y, this.bagSizes[i], ' rotate(var(--bag-tilt, 0deg))');
        }
    }

    tick(now) {
        this.frame = requestAnimationFrame(this._tick);

        if (!this.last) {
            this.last = now;
            return;
        }
        const dt = Math.min((now - this.last) / 1000, MAX_DT);
        this.last = now;
        if (dt <= 0) return;

        const r = this.scene.getBoundingClientRect();
        if (Math.abs(r.width - this.width) > 1 || Math.abs(r.height - this.height) > 1) {
            this.measure();
        } else if ((this.ticks = (this.ticks || 0) + 1) % 30 === 0) {
            // The intro toggle hides the copy without changing the scene's own
            // size, so no resize fires — refresh the keep-out on a slow beat
            // instead of every frame.
            this.world.keepOut = this.keepOutRect(r);
        }

        // Opening hold: the formation sits still, nothing is stepped — no
        // drifting, no bags appearing, no coins collected — and then the whole
        // simulation starts at once. Dragging a robot cancels the wait, since
        // the visitor has clearly decided the show should already be running.
        if (this.holdRemaining > 0) {
            this.holdRemaining -= dt;
            if (this.holdRemaining > 0 && !this.dragging) {
                this.render();
                return;
            }
            this.holdRemaining = 0;
        }

        for (let i = 0; i < this.robots.length; i++) {
            const agent = this.world.agents[i];
            if (this.dragging && this.dragging.index === i) {
                // A held robot takes no action; the pointer owns it.
                this.actions[i * 2] = 0;
                this.actions[i * 2 + 1] = 0;
                continue;
            }
            observe(this.world, agent, this.obs);
            policyForward(this.weights, SHAPE, this.obs, this.act);
            this.actions[i * 2] = this.act[0];
            this.actions[i * 2 + 1] = this.act[1];
        }

        const aliveBefore = this.world.coins.map((c) => c.alive);
        const bagsBefore = this.world.bags.map((b) => b.alive);
        step(this.world, this.actions, dt);

        for (let i = 0; i < this.robots.length; i++) {
            const agent = this.world.agents[i];
            const unit = this.robots[i].firstElementChild;
            if (agent.justCollected && unit) {
                // Restart the one-shot collect animations: removing and
                // re-adding in the same frame would be coalesced away, so
                // force a reflow between the two.
                unit.classList.remove(COLLECTING_CLASS);
                void unit.offsetWidth;
                unit.classList.add(COLLECTING_CLASS);
                this.collectTimers[i] = COLLECT_MS;
            } else if (this.collectTimers[i] > 0) {
                this.collectTimers[i] -= dt * 1000;
                if (this.collectTimers[i] <= 0 && unit) unit.classList.remove(COLLECTING_CLASS);
            }
        }

        for (let i = 0; i < this.coinEls.length; i++) {
            const c = this.world.coins[i];
            if (aliveBefore[i] && !c.alive) this.coinEls[i].classList.add(COLLECTED_CLASS);
            else if (!aliveBefore[i] && c.alive) this.coinEls[i].classList.remove(COLLECTED_CLASS);
        }

        // Bags: .is-live tracks catchability, .is-caught is a one-shot the
        // frame a robot reaches one. A bag that simply expired uncaught loses
        // .is-live without ever getting .is-caught, so the two animations stay
        // distinguishable.
        const caught = new Set(
            this.world.agents.filter((a) => a.justDrank).map((a) => a.drankBag)
        );
        for (let i = 0; i < this.bagEls.length; i++) {
            const bag = this.world.bags[i];
            const el = this.bagEls[i];
            el.classList.toggle(BAG_LIVE_CLASS, bag.alive);
            if (bagsBefore[i] && !bag.alive && caught.has(i)) {
                el.classList.remove(BAG_CAUGHT_CLASS);
                void el.offsetWidth;
                el.classList.add(BAG_CAUGHT_CLASS);
                this.bagCaughtTimers[i] = COLLECT_MS;
            } else if (this.bagCaughtTimers[i] > 0) {
                this.bagCaughtTimers[i] -= dt * 1000;
                if (this.bagCaughtTimers[i] <= 0) el.classList.remove(BAG_CAUGHT_CLASS);
            }
        }

        // Caffeine state: .is-boosted lasts the whole boost, .is-drinking is a
        // one-shot at the moment of the catch.
        for (let i = 0; i < this.robots.length; i++) {
            const agent = this.world.agents[i];
            const unit = this.robots[i].firstElementChild;
            this.robots[i].classList.toggle(BOOSTED_CLASS, agent.boostUntil > this.world.time);
            if (agent.justDrank && unit) {
                unit.classList.remove(DRINKING_CLASS);
                void unit.offsetWidth;
                unit.classList.add(DRINKING_CLASS);
                this.drinkTimers[i] = COLLECT_MS;
            } else if (this.drinkTimers[i] > 0) {
                this.drinkTimers[i] -= dt * 1000;
                if (this.drinkTimers[i] <= 0 && unit) unit.classList.remove(DRINKING_CLASS);
            }
        }

        for (let i = 0; i < this.robots.length; i++) {
            this.robots[i].classList.toggle(
                'ph-scene__roam--mirrored',
                this.world.agents[i].facing < 0
            );
        }

        this.render();
    }

    /* ── Dragging. Under the sim this is just a teleport: drop a robot
       anywhere and the policy takes over from the new position, which is a
       fair test of whether it generalises. The old drag script had to shift
       the robot's paired coin by the same delta to keep the timed
       choreography valid — there is nothing left to keep in sync. ── */

    onPointerDown(e) {
        if (this.dragging || !e.isPrimary) return;
        if (e.pointerType === 'mouse' && e.button !== 0) return;
        const wrapper = e.target.closest('.ph-scene__roam');
        if (!wrapper) return;
        const index = this.robots.indexOf(wrapper);
        if (index < 0) return;

        const r = this.scene.getBoundingClientRect();
        const b = wrapper.getBoundingClientRect();
        this.dragging = {
            index,
            wrapper,
            grabX: e.clientX - (b.left + b.width / 2),
            grabY: e.clientY - (b.top + b.height / 2),
        };
        wrapper.classList.add(HELD_CLASS);
        wrapper.setPointerCapture(e.pointerId);
        wrapper.addEventListener('pointermove', this._move);
        wrapper.addEventListener('pointerup', this._up);
        wrapper.addEventListener('pointercancel', this._up);
        e.preventDefault();
    }

    onPointerMove(e) {
        if (!this.dragging) return;
        const r = this.scene.getBoundingClientRect();
        const agent = this.world.agents[this.dragging.index];
        const x = (e.clientX - this.dragging.grabX - r.left) / r.width;
        const y = (e.clientY - this.dragging.grabY - r.top) / r.height;
        const lo = PARAMS.margin;
        const hi = 1 - PARAMS.margin;
        agent.x = Math.min(Math.max(x, lo), hi);
        agent.y = Math.min(Math.max(y, lo), hi);
        agent.vx = 0;
        agent.vy = 0;
    }

    onPointerUp(e) {
        if (!this.dragging) return;
        const { wrapper } = this.dragging;
        wrapper.classList.remove(HELD_CLASS);
        try { wrapper.releasePointerCapture(e.pointerId); } catch { /* already released */ }
        wrapper.removeEventListener('pointermove', this._move);
        wrapper.removeEventListener('pointerup', this._up);
        wrapper.removeEventListener('pointercancel', this._up);
        this.dragging = null;
    }

    destroy() {
        cancelAnimationFrame(this.frame);
        window.removeEventListener('resize', this._resize);
        document.removeEventListener('visibilitychange', this._visibility);
        this.over.removeEventListener('pointerdown', this._down);
        this.scene.classList.remove(SIM_CLASS);
        this.over.classList.remove(SIM_CLASS);
        for (const el of this.robots) {
            el.style.transform = '';
            el.classList.remove(HELD_CLASS, 'ph-scene__roam--mirrored', BOOSTED_CLASS);
            if (el.firstElementChild) {
                el.firstElementChild.classList.remove(COLLECTING_CLASS, DRINKING_CLASS);
            }
        }
        for (const el of this.coinEls) {
            el.style.transform = '';
            el.classList.remove(COLLECTED_CLASS);
        }
        for (const el of this.bagEls) {
            el.style.transform = '';
            el.classList.remove(BAG_LIVE_CLASS, BAG_CAUGHT_CLASS);
        }
    }
}

const instances = new Map();

export function init(rootId, checkpoint = 'trained') {
    const root = typeof rootId === 'string' ? document.getElementById(rootId) : rootId;
    if (!root) {
        console.warn('[pixelCrew] root not found:', rootId);
        return false;
    }
    // Reduced motion: leave the CSS composition alone rather than animating a
    // simulation nobody asked to see.
    if (window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches) {
        return false;
    }
    const key = root.id || root;
    if (instances.has(key)) instances.get(key).destroy();
    instances.set(key, new Crew(root, checkpoint));
    return true;
}

/**
 * Swap which checkpoint is steering, without rebuilding the world. Keeping the
 * robots and coins exactly where they are is the entire point: the difference
 * between generation 0 and generation 300 is only legible if you change one
 * variable, and a re-scattered field would hide it.
 */
export function setCheckpoint(rootId, checkpoint) {
    const key = typeof rootId === 'string' ? rootId : rootId && rootId.id;
    const crew = instances.get(key);
    if (!crew) return false;
    const next = WEIGHTS[checkpoint];
    if (!next) return false;
    crew.weights = Float64Array.from(next);
    return true;
}

export function destroy(rootId) {
    const key = typeof rootId === 'string' ? rootId : rootId && rootId.id;
    const crew = instances.get(key);
    if (crew) {
        crew.destroy();
        instances.delete(key);
    }
}
