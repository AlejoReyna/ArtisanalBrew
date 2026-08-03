# Pixel crew training

The four robots on the homepage hero are not choreographed. They run a small
neural network trained offline to collect coffee coins, and this document is the
record of that training: what the problem was, why the method was chosen, what
the numbers came out as, and how to reproduce it.

Everything here was produced on 2026-07-28.

---

## Why there was anything to train

The hero originally moved its crew with CSS keyframes. Each robot had a
`ph-roam-<role>` animation on a shared 90-second clock, and each collectable
coin was positioned at exactly that robot's roam destination using the same
`--<role>-roam-*` custom properties. The coin appeared to be collected because
its vanish keyframe was timed to the arrival keyframe — nothing perceived
anything, and nothing decided anything.

That design had a tell. Dragging a robot required
`pixelHomeDrag.js` to translate the robot's paired coin by the same delta,
because otherwise the choreography came apart: the robot would dwell over empty
space and the coin would pop on a timer somewhere else. The hack was a symptom
of the coin and the robot being two halves of one animation rather than two
objects in a world.

Replacing the keyframes with a simulation is what made "training" a meaningful
word here, and it deleted that hack as a side effect — a dropped robot now just
starts steering from wherever it landed.

## The problem, formally

A continuous-control task in a normalised unit square.

| | |
|---|---|
| **Observation** (13 floats) | unit vector to this robot's own claimed coin (2), distance to it (1), own velocity (2), distance to each of the four walls (4), unit vector to the nearest catchable coffee mug (2), distance to it (1), own caffeine remaining (1) |
| **Action** (2 floats) | acceleration on x and y, each squashed through `tanh` |
| **Reward** | `+1` per coin collected; `+0.9` per coffee mug caught (`PARAMS.bagReward`); `-0.001 x` distance-to-own-coin shaping per step; `-0.0006 |a|²` control cost |
| **Episode** | 1800 steps at a fixed `dt` of 1/60 s — thirty seconds of simulated time |
| **Dynamics** | acceleration, linear drag, speed cap, absorbing walls |

Coins respawn 5.5 s after collection, at a uniform random point that is **not**
inside the hero's copy column. That keep-out rectangle is measured at runtime
from `.ph-hero__copy`, so it follows the intro toggle and the one-column mobile
layout. It exists for a design reason rather than a learning one: a coin resting
on the "YOUR NEXT COFFEE, ON-CHAIN" title is unreadable.

Walls absorb rather than bounce. Hitting one costs time, and losing time is what
teaches the policy to stop steering into them.

### Drifting, not darting

The crew is meant to read as weightless. The physics constants are tuned for
that rather than for efficiency:

| Constant | Value | Why |
|---|---|---|
| `accel` | 0.6 units/s² | Gentle thrust; no snapping into motion |
| `drag` | 0.7 /s | **Low relative to `accel`** — momentum carries a robot well past the point it stops thrusting, which is what reads as vacuum rather than walking |
| `maxSpeed` | 0.12 units/s (20% below the original 0.15 tuning) | A full crossing of the scene takes about nine seconds |
| `respawnSeconds` | 5.5 | A slow crew needs the field to stay put long enough to reach it |
| `bagLifeMin/Max` | 12–20 s | **Must exceed the ~10.6 s crossing time**, or a distant mug is unreachable and the policy correctly ignores it |

Measured on the original 0.15-tuned policy: average speed **0.118 units/s**, or
**8.5 seconds** to cross the scene — scaling down with the 20% speed cut puts
the current crew at roughly **0.094 units/s**, or **10.6 seconds** to cross.
It is something you watch, not something that flickers.

Because a 10-second episode would now end before a slow policy finished its
first approach, episodes were lengthened to 30 seconds. All coin counts below
are per 30-second episode and are not comparable to the earlier 10-second
figures.

### Every robot gets its own coin

Steering all four at whatever coin is nearest makes the crew clump: three
robots converge on one prize, two arrive to nothing, and a robot can go a long
stretch without ever collecting. So coins are **claimed** — one robot each,
and a robot's observation and reward shaping both point at *its* coin rather
than the globally nearest one.

Claims are sticky. Re-running a global nearest-match every frame would make two
robots swap targets whenever their paths crossed, which looks like indecision;
a robot only re-targets when its coin is gone. Pickup stays physical, though —
a robot that drifts through any live coin pockets it, claimed or not, because
refusing a coin it is touching would look broken.

The result, per robot per episode over 5 held-out episodes:

| Scout | Buyer | Courier | Inspector |
|---|---|---|---|
| 6.0 | 7.2 | 7.4 | 6.2 |

A min/max ratio of **0.81** — no robot is starved, which was the entire point.

### Coffee mugs: the difficulty layer

Coins are always on the field. Coffee mugs are not — and that is what makes
them hard. Each of the six mug slots is dormant for a random 6–18 s, then
**suddenly appears somewhere new**, stays catchable for 12–20 s, and goes
dormant again whether or not anyone reached it. Nothing about the timing is
tied to the crew, so a robot cannot plan around it.

Catching one grants a **40% speed boost for 6 seconds**. The boost raises the
speed *cap*, not the thrust — a caffeinated robot keeps the same weightless
acceleration and simply coasts faster once up to speed, so the drink reads as
momentum rather than as a twitch.

This is a genuine decision problem, which is the point of calling it
difficulty: a mug is only worth chasing if the detour costs less than the boost
earns back, and the mug may expire before the robot arrives.

#### Tuning it, and one claim this document got wrong

The first version shipped a mug reward of 0.4 and a 7–12 s catchable window,
and the crew visibly ignored mugs: a 46% catch rate, with one robot down at
0.7 mugs per episode. Two things were wrong.

**The window was shorter than the travel time.** The crew drifts at 0.15
units/s and a full crossing takes about 8.5 s. A mug that appeared across the
scene simply could not be reached inside 7 s, so the policy was correct to
ignore it — the reward was unreachable, not undervalued.

**The reward was too low to justify the detour**, and an earlier draft of this
document asserted that paying a full coin for a mug "made the crew abandon
coins to farm bags". That claim was never measured, and it is wrong. A sweep
(120 generations per configuration, held-out evaluation):

| Mug reward | Catchable window | Coins | Mugs caught / appeared | Catch rate |
|---|---|---|---|---|
| 0.4 | 7–20 s | 28.5 | 5.3 / 10.8 | 48% |
| 0.9 | 7–12 s | 27.6 | 6.3 / 10.6 | 59% |
| 0.9 | 12–20 s | 26.5 | 5.9 / 9.5 | 62% |
| 1.5 | 12–20 s | 26.7 | 6.4 / 9.9 | 65% |

Even at 1.5 — a mug worth more than a coin — coin collection fell only about
6%. The crew never abandoned coins. The shipped setting is **reward 0.9,
window 12–20 s**, which takes most of the available gain while keeping a mug
worth less than a coin, so the reward hierarchy still matches what the scene
is about.

#### Before and after

Both rows evaluated under identical current dynamics (`bagBoost` 1.4, 30-second
episodes, 20 held-out seeds), so this isolates the policy and the economics
rather than comparing across worlds:

| | Coins | Mugs caught / appeared | Catch rate | Time boosted | Mugs per robot |
|---|---|---|---|---|---|
| Stale policy (trained @ boost 1.25, reward 0.4) | 27.6 | 4.2 / 9.1 | **46%** | 17% | 1.4 / 1.2 / 0.7 / 0.9 |
| Retrained (boost 1.4, reward 0.9, window 12–20 s) | 27.1 | 6.8 / 10.2 | **67%** | 24% | 1.8 / 1.4 / 1.8 / 1.8 |
| Untrained baseline | 1.6 | 0.6 / 7.5 | 7% | 3% | 0.3 / 0.1 / 0.1 / 0 |

Coin collection is essentially unchanged (27.6 → 27.1) while mug catching rises
by half, and the per-robot spread evens out — the 0.7 laggard disappears.

Measured in the browser against the real DOM, driving the frame loop with a
synthetic clock over 60 seconds of simulated time: **9 catch events**, mugs
live 41% of the time, robots caffeinated 17% of the time.

The untrained baseline at 7% is the useful control: it confirms the catches are
deliberate detours rather than accidental collisions.

## Architecture

A 13 → 16 → 2 multilayer perceptron, `tanh` on both layers.

```
13 x 16 + 16  +  16 x 2 + 2  =  258 parameters
```

Small enough that a forward pass for the whole crew is a rounding error per
frame, and small enough that the trained weights ship as 7 KB of literal
numbers rather than a model file.

## Why evolution strategies and not PPO

PPO is the reflexive answer and it would have been the wrong one. It needs an
autograd framework, a value head, advantage estimation, and roughly 300 lines of
code that can each be subtly wrong — to solve a 258-parameter steering problem.

OpenAI-style evolution strategies fit the task instead:

- **Mirrored sampling.** Every perturbation `ε` is evaluated as both `θ + σε`
  and `θ - σε`. The pair cancels most of the gradient estimator's variance for
  free.
- **Rank normalisation.** Returns are replaced by their rank in `[-0.5, 0.5]`
  before the update, so the step depends only on the *ordering* of the
  population. One lucky episode with an enormous return cannot dominate.
- **No gradients at all.** The environment is a black box; only episode returns
  are needed. Zero dependencies, and the trainer is about 40 lines of actual
  algorithm.

| Hyperparameter | Value |
|---|---|
| Population | 64 (32 mirrored pairs) |
| σ (noise scale) | 0.1 |
| Learning rate | 0.03 |
| Episodes per evaluation | 3, averaged |
| Generations | 300 |

Coin layouts are reseeded every generation, so the policy has to generalise
rather than memorise one arrangement.

## The one architectural rule

**The simulation lives in exactly one file, and both sides import it.**

`src/ThisCafeteria.Web/wwwroot/js/pixelCrewSim.js` holds the physics, the
observation function, the reward, and the network's forward pass. The Node
trainer imports it. The browser runtime imports it. There is no Python
reimplementation and no "equivalent" port.

This matters more than it sounds. A policy is only valid for the dynamics it was
trained against. A second copy of the environment — even one written carefully
from the same notes — drifts silently: the weights stay loadable, the robots
keep moving, and the behaviour is just quietly worse than the training numbers
promised, with nothing to point at.

The payoff was measurable. Running the shipped weights through the shipped sim
**in the browser** reproduces the trainer's held-out numbers to the decimal:

```
                Node      Browser
untrained        1.8         1.8
early (gen 20)  25.2        25.2
trained         27.0        27.0
```

## Results

Held-out performance, averaged over 5 episodes on coin layouts the trainer never
optimised against:

| Checkpoint | Coins per episode | Behaviour |
|---|---|---|
| `untrained` (generation 0) | **1.8** | Drifts, stalls against walls, collects only by accident |
| `early` (generation 20) | **25.2** | Heads the right way, overshoots, coasts back |
| `trained` (generation 300) | **27.0** | Thrusts early, lets momentum carry it in, breaks off for a mug when the detour is cheap |

All three ship. The hero's **CREW BRAIN** switcher swaps which one is steering
without rebuilding the world — the robots and coins stay exactly where they are,
so the only variable that changes is the behaviour. That is the whole point of
shipping the untrained checkpoint: the difference is only legible if nothing
else moves.

### Learning curve

Total training time: **275 seconds** on one CPU core.

| Generation | Mean reward | Coins/episode |
|---|---|---|
| 1 | 0.00 | 3.0 |
| 10 | 14.03 | 15.3 |
| 20 | 23.97 | 24.7 |
| 30 | 25.73 | 28.0 |
| 50 | 22.95 | 26.0 |
| 100 | 25.77 | 27.7 |
| 150 | 26.07 | 28.0 |
| 200 | 26.87 | 29.7 |
| 250 | 26.31 | 29.0 |
| 300 | 25.98 | 28.7 |

Full data in `scratch/pixel-crew-training.csv`.

Two honest observations about this curve:

1. **It plateaus around generation 20–30**, harder than before. Nearly all the
   competence is bought in the first 30 seconds of training. The remaining 270
   generations buy a modest, noisy improvement — visible in the held-out gap
   between 25.2 and 27.0, but not the dramatic climb the generation count
   implies. Per-coin claiming is largely responsible: pointing each robot at
   its own target makes the problem substantially easier to learn.
2. **It is noisy, and the noise is not the policy getting worse.** Each point is
   3 episodes on fresh layouts; a generation that happens to draw awkward coin
   placements scores lower with identical weights. Generation 200 scoring above
   generation 300 is sampling variance, not regression.

## The caveat worth stating plainly

`accel = normalize(coin - pos)` is five lines of steering and looks nearly
identical on screen. No visitor can tell a trained policy from hand-written
steering by watching the hero.

So the learned policy is not there because the motion required it. It is there
because the artifact is the point — and the artifact is only worth anything if
the learning is *visible*, which is why all three checkpoints ship behind a
switcher instead of just the good one. Generation 0 flailing against a wall is
doing more work than generation 300 sweeping the field.

## Reproducing

```bash
node tools/train_pixel_crew.mjs
```

Roughly four and a half minutes. Writes:

- `src/ThisCafeteria.Web/wwwroot/js/pixelCrewPolicy.js` — the three checkpoints
  as an ES module. Generated; do not hand-edit.
- `scratch/pixel-crew-training.csv` — the learning curve.

A shorter run for iteration:

```bash
node tools/train_pixel_crew.mjs --gens 40
```

**If you change the physics, the reward, or the spawn distribution in
`pixelCrewSim.js`, retrain.** The shipped weights encode assumptions about the
world they were optimised in, and nothing will fail loudly if those assumptions
stop holding — the robots will simply get worse at their job.

## Files

| File | Role |
|---|---|
| `wwwroot/js/pixelCrewSim.js` | Physics, observation, reward, forward pass. Imported by trainer *and* browser |
| `tools/train_pixel_crew.mjs` | Evolution strategies trainer. No dependencies |
| `wwwroot/js/pixelCrewPolicy.js` | Generated weights, three checkpoints |
| `wwwroot/js/pixelCrewRuntime.js` | Per-frame loop, DOM writes, dragging, checkpoint swapping |
| `Components/Home/PixelHome.razor` | Hero markup, the CREW BRAIN switcher, module lifecycle |

The runtime signals it has taken over by adding `.ph-scene--sim` to the scene,
and every simulation-mode CSS rule is scoped to that class. Without it — no JS,
or `prefers-reduced-motion: reduce`, where the runtime declines to start — the
original keyframe composition is still what renders. That fallback is
deliberate, not leftover.
