"""Generate the pixel Andromeda galaxy for the PixelHome (/) hero sky.

A recognisable M31: a steeply inclined disc (so it reads as a long ellipse, not
a face-on spiral), a bright warm core bulge, cooler blue-white arms speckled
with stipple rather than drawn as smooth bands, one dark dust lane cutting the
near edge, and the two satellite companions (M32, M110). Everything is drawn by
stochastic per-pixel density on a small canvas so it stays chunky when the hero
scales it up with image-rendering: pixelated — no smooth gradients, which is
what would give the pixel illusion away.

The sprite is generated at full brightness; the hero dims it and pushes it
behind the rest of the sky in CSS (.ph-sky__andromeda), so this file stays the
"true colour" version. Run from the repo root:

    python3 tools/generate_andromeda.py

Output (in src/ThisCafeteria.Web/wwwroot/images/):
    pl-andromeda.png   132x84 RGBA sprite, transparent background
"""

import math
import random
from pathlib import Path

from PIL import Image

OUT = Path("src/ThisCafeteria.Web/wwwroot/images/pl-andromeda.png")

W, H = 132, 84
CX, CY = W / 2, H / 2

# Disc geometry: a long major axis against a shallow minor axis is the whole
# reason it reads as Andromeda and not as a generic spiral blob.
MAJOR, MINOR = 58.0, 18.5
TILT = math.radians(-24.0)

# Stipple palette, warm in the core and cooling outward through the arms.
CORE_HOT = (255, 248, 230)
CORE_WARM = (248, 230, 190)
CORE_EDGE = (226, 198, 152)
ARM_BRIGHT = (222, 228, 250)
ARM_MID = (176, 190, 228)
ARM_DIM = (122, 136, 188)
HALO = (78, 88, 138)
DUST = (44, 46, 78)

# Fixed seed: the sprite is committed, so it must regenerate byte-identical.
RNG = random.Random(31415)


def disc_coords(x, y):
    """Pixel -> disc frame, returning (elliptical radius, along-major axis)."""
    dx, dy = x - CX, y - CY
    cos_t, sin_t = math.cos(TILT), math.sin(TILT)
    u = dx * cos_t + dy * sin_t
    v = -dx * sin_t + dy * cos_t
    return math.hypot(u / MAJOR, v / MINOR), u / MAJOR, v / MINOR


def dust_mask(u, v):
    """Dark lane hugging the near (lower) edge of the disc, as in M31."""
    # A shallow arc offset below the midplane; strongest through the mid-disc
    # and fading out before it reaches the core or the tips.
    lane = v - (0.34 + 0.20 * u * u)
    span = math.exp(-((u / 0.72) ** 2))
    return span * math.exp(-((lane / 0.17) ** 2))


def main():
    img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    px = img.load()

    for y in range(H):
        for x in range(W):
            r, u, v = disc_coords(x, y)
            if r > 1.42:
                continue

            # Brightness: exponential disc + a tight central bulge. The disc
            # falls off steeply so the halo thins out instead of ending on a
            # hard rim, while the inner disc stays dense enough to read as a
            # body rather than as scattered noise.
            disc = math.exp(-r * 2.45)
            bulge = 1.5 * math.exp(-((r / 0.17) ** 2))
            # Faint arm banding along the major axis keeps the stipple from
            # looking like uniform noise.
            arms = 1.0 + 0.28 * math.sin(u * 7.4 + v * 2.1)
            value = (disc * arms) + bulge
            value *= 1.0 - 0.82 * dust_mask(u, v)

            if value <= 0.02 or RNG.random() > min(value * 1.65, 0.97):
                continue

            if r < 0.11:
                color = CORE_HOT
            elif r < 0.19:
                color = CORE_WARM if RNG.random() < 0.75 else CORE_HOT
            elif r < 0.30:
                color = CORE_EDGE if RNG.random() < 0.6 else ARM_BRIGHT
            elif value > 0.62:
                color = ARM_BRIGHT
            elif value > 0.34:
                color = ARM_MID
            elif value > 0.15:
                color = ARM_DIM
            else:
                color = HALO

            alpha = int(max(60, min(255, 255 * min(value * 1.5, 1.0))))
            px[x, y] = (*color, alpha)

    # Dust lane specks: a few explicitly dark pixels so the lane reads as
    # occlusion rather than as a gap in the stipple.
    for _ in range(90):
        u = RNG.uniform(-0.85, 0.85)
        lane = 0.34 + 0.20 * u * u + RNG.gauss(0, 0.05)
        cos_t, sin_t = math.cos(TILT), math.sin(TILT)
        x = int(round(CX + (u * MAJOR) * cos_t - (lane * MINOR) * sin_t))
        y = int(round(CY + (u * MAJOR) * sin_t + (lane * MINOR) * cos_t))
        if 0 <= x < W and 0 <= y < H and px[x, y][3]:
            px[x, y] = (*DUST, 210)

    # Satellite companions: M32 tight and bright below the disc, M110 looser
    # and dimmer above it.
    for (sx, sy, radius, density, bright) in (
        (CX + 21, CY + 17, 3.6, 0.85, ARM_BRIGHT),
        (CX - 30, CY - 15, 5.0, 0.55, ARM_MID),
    ):
        for y in range(int(sy - radius - 1), int(sy + radius + 2)):
            for x in range(int(sx - radius - 1), int(sx + radius + 2)):
                if not (0 <= x < W and 0 <= y < H):
                    continue
                d = math.hypot(x - sx, y - sy) / radius
                if d > 1.0:
                    continue
                v = math.exp(-(d ** 2) * 2.4)
                if RNG.random() > v * density:
                    continue
                color = bright if d < 0.45 else ARM_DIM
                px[x, y] = (*color, int(max(70, min(255, 255 * v))))

    OUT.parent.mkdir(parents=True, exist_ok=True)
    img.save(OUT)
    print(f"wrote {OUT} ({W}x{H})")


if __name__ == "__main__":
    main()
