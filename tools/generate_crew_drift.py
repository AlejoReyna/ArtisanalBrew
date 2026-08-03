#!/usr/bin/env python3
"""Generate the crew's idle drift keyframes from the scene's own physics.

The robots hang in vacuum, so the idle motion under them must not look like
anything with a restoring force. The old `ph-unit-float` was a single-axis
sine (`cubic-bezier(0.45, 0, 0.55, 1)`, `alternate`) between -9px and +13px:
a pendulum signature. Three things gave it away as weight rather than
weightlessness — it only moved on Y (so the scene had a "down"), it slowed at
both ends (so something was pulling it back), and it retraced its own path
(so the physics ran backwards for half the loop).

This script replaces it with a numerically integrated free-drift orbit. The
model is the crew's real one, read straight off wwwroot/js/pixelCrewSim.js
and the jetflame duty cycle in GlobalScene.razor.css:

    v' = -DRAG * v + a(t)          x' = v

`a(t)` is piecewise constant: one thrust vector per burn, held for the flame's
on-window, then zero while the jet is dark. There is no spring term and no
gravity term, because the environment has neither — every direction change in
the output is paid for by a burn you can see.

Closure: for x to be periodic, v must average to zero over the loop, which
needs a(t) to average to zero, which needs the burn vectors to sum to zero.
The script enforces that by subtracting their mean, then converges onto the
periodic orbit by integrating the transient away (it decays as e^-DRAG*t)
before sampling the final loop.

Attitude: the sprite carries its jet at the bottom, so a real body would have
to pitch to steer. A full thrust-vector alignment would put the robots
upside down, so the tilt here is the attenuated version — attitude jets
holding the body near-upright, losing a few degrees to each main burn and
recovering with a first-order lag. It is a small lie in service of a legible
sprite; the translation underneath it is not.

Usage:
    python3 tools/generate_crew_drift.py          # print the CSS block
    python3 tools/generate_crew_drift.py --report # + accuracy diagnostics

Paste the printed block over the `ph-unit-drift-*` keyframes in
src/ThisCafeteria.Web/Components/Layout/GlobalScene.razor.css.
"""

from __future__ import annotations

import argparse
import math

# ── The environment, quoted from the two files that already define it ──────────

# wwwroot/js/pixelCrewSim.js — PARAMS.drag, "velocity damping per second".
DRAG = 0.7

# GlobalScene.razor.css — @keyframes ph-jetflame-fallback-duty: a 2.3s cycle
# that is lit for the first 65.2% and dark for the rest.
BURN_PERIOD = 2.3
BURN_ON_FRACTION = 0.652

# Integration step. 1/240 s keeps the sampled path within a hundredth of a
# pixel of a 1/2000 s reference, which is far below what a keyframe can hold.
DT = 1.0 / 240.0

# Periods of transient to burn off before sampling. The homogeneous solution
# decays as e^(-DRAG * t); 12 loops of the shortest orbit is e^-77.
SETTLE_LOOPS = 12

# Keyframe sampling. 5% steps — the segment error report below stays under a
# third of a pixel, which is invisible on a pixelated sprite.
SAMPLE_STEP_PCT = 5

# Peak excursion from the loop's centre, in px. Matched to the old bob's 22px
# span so no surrounding layout shifts.
AMPLITUDE_PX = 11.0

# Peak attitude loss, in degrees. Small on purpose — see the module docstring.
TILT_DEG = 3.5

# Time constant of the attitude recovery, in seconds.
TILT_TAU = 0.9


def burn_vectors(count: int, seed: int) -> list[tuple[float, float]]:
    """`count` thrust vectors that sum to zero.

    The directions are spread over the full circle so no axis is privileged,
    then jittered from a fixed seed so the four robots trace visibly different
    orbits rather than four phases of one shape. Magnitudes vary too: a crew
    that fires an identical burn every time reads as clockwork.

    Subtracting the mean is what closes the loop. It changes each vector, but
    the result is still just a constant thrust per burn — nothing about it is
    less physical than what went in.
    """
    rng = seed
    def jitter() -> float:
        # Deterministic LCG; the exact numbers do not matter, reproducibility does.
        nonlocal rng
        rng = (1103515245 * rng + 12345) % 2147483648
        return rng / 2147483648.0

    raw: list[tuple[float, float]] = []
    for i in range(count):
        angle = 2.0 * math.pi * (i + 0.35 * (jitter() - 0.5)) / count
        magnitude = 0.65 + 0.7 * jitter()
        raw.append((magnitude * math.cos(angle), magnitude * math.sin(angle)))

    mean_x = sum(v[0] for v in raw) / count
    mean_y = sum(v[1] for v in raw) / count
    return [(x - mean_x, y - mean_y) for x, y in raw]


def integrate(burns: list[tuple[float, float]]) -> list[tuple[float, float, float, float]]:
    """Integrate one settled loop. Returns (t, x, y, tilt) samples at DT."""
    loop = len(burns) * BURN_PERIOD
    burn_on = BURN_PERIOD * BURN_ON_FRACTION
    peak_thrust = max(math.hypot(*v) for v in burns)

    x = y = vx = vy = tilt = 0.0
    track: list[tuple[float, float, float, float]] = []

    total_steps = int(round((SETTLE_LOOPS + 1) * loop / DT))
    sample_from = int(round(SETTLE_LOOPS * loop / DT))

    for step in range(total_steps):
        t = step * DT
        phase = t % loop
        index = int(phase // BURN_PERIOD)
        lit = (phase - index * BURN_PERIOD) < burn_on
        ax, ay = burns[index] if lit else (0.0, 0.0)

        # Semi-implicit Euler: velocity first, then position from the new
        # velocity. Stable under damping at this step size, unlike explicit.
        vx += (ax - DRAG * vx) * DT
        vy += (ay - DRAG * vy) * DT
        x += vx * DT
        y += vy * DT

        # Attitude bleeds toward the lateral thrust and springs back once the
        # jet goes dark. First-order lag, so it always trails the burn.
        target = TILT_DEG * (ax / peak_thrust) if peak_thrust else 0.0
        tilt += (target - tilt) * (DT / TILT_TAU)

        if step >= sample_from:
            track.append((t - sample_from * DT, x, y, tilt))

    return track


def normalise(track: list[tuple[float, float, float, float]]):
    """Centre the orbit on zero and scale it to AMPLITUDE_PX.

    Both axes take the *same* factor. Stretching one to fill a box would
    reshape the trajectory into something the physics never produced.
    """
    mean_x = sum(p[1] for p in track) / len(track)
    mean_y = sum(p[2] for p in track) / len(track)
    mean_tilt = sum(p[3] for p in track) / len(track)

    reach = max(math.hypot(p[1] - mean_x, p[2] - mean_y) for p in track)
    scale = AMPLITUDE_PX / reach if reach else 0.0

    peak_tilt = max(abs(p[3] - mean_tilt) for p in track)
    tilt_scale = TILT_DEG / peak_tilt if peak_tilt else 0.0

    return [
        (p[0], (p[1] - mean_x) * scale, (p[2] - mean_y) * scale, (p[3] - mean_tilt) * tilt_scale)
        for p in track
    ]


def keyframes(track, name: str) -> tuple[str, float]:
    """Render one @keyframes block; also returns the worst interpolation error.

    The timing function on these is `linear` and the direction is `normal`.
    Both matter: an eased segment would reintroduce the acceleration the drag
    model already accounts for, and `alternate` would run the second half of
    every loop with time flowing backwards.
    """
    duration = track[-1][0] + DT
    lines = [f"@keyframes {name} {{"]
    picks = []

    for pct in range(0, 101, SAMPLE_STEP_PCT):
        index = min(int(round(pct / 100.0 * (len(track) - 1))), len(track) - 1)
        if pct == 100:
            index = 0  # close the loop exactly on the start sample
        _, x, y, tilt = track[index]
        picks.append((pct, x, y))
        lines.append(
            f"    {pct}% {{ translate: {x:.2f}px {y:.2f}px; rotate: {tilt:.2f}deg; }}"
        )

    lines.append("}")

    # How far the straight line between two keyframes strays from the real orbit.
    worst = 0.0
    for (p0, x0, y0), (p1, x1, y1) in zip(picks, picks[1:]):
        i0 = int(round(p0 / 100.0 * (len(track) - 1)))
        i1 = int(round(p1 / 100.0 * (len(track) - 1)))
        span = max(i1 - i0, 1)
        for k in range(span):
            f = k / span
            _, tx, ty, _ = track[min(i0 + k, len(track) - 1)]
            worst = max(worst, math.hypot(x0 + (x1 - x0) * f - tx, y0 + (y1 - y0) * f - ty))

    return "\n".join(lines), worst, duration


# role -> (burns per loop, seed). Loop length is burns * 2.3s, so every
# acceleration in the path lands on a lit jet in the no-JS fallback.
ROLES = {
    "courier": (4, 11),
    "scout": (5, 29),
    "buyer": (6, 47),
    "inspector": (7, 83),
}


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--report", action="store_true", help="print accuracy diagnostics")
    args = parser.parse_args()

    blocks = []
    for role, (count, seed) in ROLES.items():
        track = normalise(integrate(burn_vectors(count, seed)))
        block, worst, duration = keyframes(track, f"ph-unit-drift-{role}")
        blocks.append(block)
        if args.report:
            speeds = [
                math.hypot(track[i + 1][1] - track[i][1], track[i + 1][2] - track[i][2]) / DT
                for i in range(len(track) - 1)
            ]
            print(f"/* {role}: loop {duration:.2f}s ({count} burns), "
                  f"keyframe error {worst:.3f}px, "
                  f"speed {min(speeds):.1f}-{max(speeds):.1f}px/s */")

    print("\n\n".join(blocks))


if __name__ == "__main__":
    main()
