#!/usr/bin/env python3
"""Generate the dress-up sheets for the profile avatar robot.

One horizontal 64x64 sheet per wearable slot. Every frame in every sheet draws
the *same* robot at the *same* origin, so the browser can stack six layers with
absolute positioning and they register pixel-for-pixel — there is no per-item
offset anywhere in the CSS.

Layer order, back to front, matching AvatarCatalog.Slots:

    backdrop (CSS colour, no sheet) -> chassis -> wear -> visor -> hat -> hold

The chassis draws the whole robot but leaves its face a dark empty socket; the
visor sheet paints the screen into it. That split is what lets any of the six
expressions sit on any of the six colourways without 36 combined sprites.

The item ids and their column order are dictated by
`src/ThisCafeteria.Domain/Avatars/AvatarCatalog.cs`. They are restated here
rather than parsed out of the C#, so a `RobotAvatarSheetTests` case asserts the
sheet widths still match the catalog's frame counts. If that test fails,
someone added an item without regenerating — run this script.

The palette is imported, not copied: these robots are the /procurement-lab crew
wearing different plating, and a second colour table would drift.
"""

import sys
from pathlib import Path
from typing import Callable, NamedTuple

from PIL import Image, ImageDraw

sys.path.insert(0, str(Path(__file__).resolve().parent))

from generate_procurement_sprites import (  # noqa: E402
    BLACK,
    CLAY,
    COFFEE,
    COPPER,
    COPPER_DK,
    CREAM_HI,
    CREAM_SH,
    FACE,
    GOLD,
    GREEN,
    METAL,
    METAL_DK,
    SCORE,
    SIGNAL,
    TEAL,
    TEAL_DK,
    TEAL_HI,
    rect,
)

ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "src" / "ThisCafeteria.Web" / "wwwroot" / "images" / "avatar"
FRAME = 64

# Where the robot stands inside its frame. Every draw function below works in
# (AVX + dx, AVY + dy) so the layers cannot drift apart: change these two
# numbers and the whole cast moves together.
#
# The offsets themselves mirror the crew proportions in
# generate_procurement_sprites.robot() — same head box, same torso, same hand
# anchors — but the pose is deliberately different: front-facing, mid-stride
# neutral (no step, no bob), and with the coffee jetpack removed, because a
# prop on the robot's back would fight every uniform in the wear sheet.
AVX = 8
AVY = 4

# Landmarks, in frame coordinates. Named so a garment says where it sits.
HEAD_TOP = AVY + 3
FACE_BOX = (AVX + 15, AVY + 8, AVX + 40, AVY + 25)
SCREEN_BOX = (AVX + 17, AVY + 10, AVX + 38, AVY + 23)
EYE_LEFT_X = AVX + 22
EYE_RIGHT_X = AVX + 33
EYE_TOP = AVY + 14
TORSO_BOX = (AVX + 11, AVY + 29, AVX + 39, AVY + 45)
HAND_LEFT = (AVX + 8, AVY + 40)
HAND_RIGHT = (AVX + 42, AVY + 40)


class Colourway(NamedTuple):
    """One chassis finish. The black keyline and the red status light stay
    constant across all six — they are what keeps every avatar reading as the
    same model of robot rather than six unrelated toys."""

    id: str
    shade: tuple
    light: tuple
    metal: tuple


COLOURWAYS = (
    Colourway("cream", CREAM_SH, CREAM_HI, METAL),
    Colourway("teal", (79, 131, 150, 255), TEAL, TEAL_DK),
    Colourway("copper", (150, 99, 58, 255), (206, 150, 94, 255), COPPER_DK),
    Colourway("clay", (188, 96, 100, 255), CLAY, (140, 66, 72, 255)),
    Colourway("moss", (106, 142, 116, 255), GREEN, (74, 105, 86, 255)),
    Colourway("graphite", (78, 86, 104, 255), (126, 136, 158, 255), (52, 60, 76, 255)),
)


def new_frame() -> tuple[Image.Image, ImageDraw.ImageDraw]:
    image = Image.new("RGBA", (FRAME, FRAME), (0, 0, 0, 0))
    return image, ImageDraw.Draw(image)


# ── Chassis ────────────────────────────────────────────────────────────────


def draw_chassis(index: int) -> Image.Image:
    """The robot itself: legs, arms, torso, antennae and head shell, with the
    face left as a dark recess for the visor layer to fill."""
    way = COLOURWAYS[index]
    image, draw = new_frame()
    x, y = AVX, AVY

    # Sturdy segmented legs and wide dark soles. Neutral stance — the crew's
    # stepping frames would make a static portrait look mid-stumble.
    rect(draw, (x + 16, y + 42, x + 23, y + 54), BLACK)
    rect(draw, (x + 27, y + 42, x + 34, y + 54), BLACK)
    rect(draw, (x + 18, y + 43, x + 22, y + 51), way.shade)
    rect(draw, (x + 29, y + 43, x + 33, y + 51), way.shade)
    rect(draw, (x + 19, y + 43, x + 22, y + 48), way.light)
    rect(draw, (x + 30, y + 43, x + 33, y + 48), way.light)
    rect(draw, (x + 14, y + 51, x + 24, y + 56), BLACK)
    rect(draw, (x + 26, y + 51, x + 36, y + 56), BLACK)
    rect(draw, (x + 17, y + 51, x + 23, y + 53), way.light)
    rect(draw, (x + 29, y + 51, x + 35, y + 53), way.light)

    # Arms live behind the torso so a uniform can paint over the shoulder.
    for shoulder, hand in (((x + 14, y + 33), HAND_LEFT), ((x + 36, y + 33), HAND_RIGHT)):
        draw.line((shoulder, hand), fill=BLACK, width=7)
        draw.line((shoulder, hand), fill=way.shade, width=3)

    for hand, glint in ((HAND_LEFT, -2), (HAND_RIGHT, -1)):
        rect(draw, (hand[0] - 3, hand[1] - 3, hand[0] + 3, hand[1] + 3), BLACK)
        rect(draw, (hand[0] + glint, hand[1] - 2, hand[0] + glint + 3, hand[1] + 1), way.light)

    # Short navy neck, then the compact rounded body with clipped corners.
    rect(draw, (x + 21, y + 27, x + 29, y + 32), BLACK)
    rect(draw, (x + 13, y + 29, x + 37, y + 45), BLACK)
    rect(draw, (x + 11, y + 33, x + 39, y + 41), BLACK)
    rect(draw, (x + 14, y + 31, x + 36, y + 43), way.shade)
    rect(draw, (x + 13, y + 34, x + 37, y + 40), way.shade)
    rect(draw, (x + 18, y + 32, x + 34, y + 39), way.light)
    rect(draw, (x + 17, y + 40, x + 33, y + 42), way.metal)
    rect(draw, (x + 32, y + 34, x + 34, y + 36), SIGNAL)

    # Two offset antenna posts — part of every crew silhouette, and the reason
    # the beanie and the toque have to sit a little forward.
    rect(draw, (x + 19, y - 1, x + 22, y + 5), BLACK)
    rect(draw, (x + 20, y - 2, x + 21, y + 1), way.light)
    rect(draw, (x + 29, y + 1, x + 32, y + 5), BLACK)
    rect(draw, (x + 30, y, x + 31, y + 2), SIGNAL)

    # Large stepped CRT head.
    rect(draw, (x + 5, y + 3, x + 44, y + 30), BLACK)
    rect(draw, (x + 3, y + 8, x + 46, y + 26), BLACK)
    rect(draw, (x + 7, y + 5, x + 42, y + 28), way.shade)
    rect(draw, (x + 5, y + 10, x + 44, y + 24), way.shade)
    rect(draw, (x + 12, y + 6, x + 42, y + 27), way.light)
    rect(draw, (x + 5, y + 11, x + 10, y + 23), way.metal)
    rect(draw, (x + 6, y + 12, x + 8, y + 16), TEAL_HI)

    # The face recess, left dark on purpose. A robot whose visor sheet failed
    # to load reads as powered-down, not as a hole in its head.
    rect(draw, FACE_BOX, BLACK)
    rect(draw, SCREEN_BOX, FACE)
    return image


# ── Visor ──────────────────────────────────────────────────────────────────


def screen(draw: ImageDraw.ImageDraw, fill=TEAL) -> None:
    rect(draw, SCREEN_BOX, TEAL_DK)
    rect(draw, (SCREEN_BOX[0] + 1, SCREEN_BOX[1] + 1, SCREEN_BOX[2] - 1, SCREEN_BOX[3] - 1), fill)
    rect(draw, (SCREEN_BOX[0] + 1, SCREEN_BOX[1], SCREEN_BOX[2] - 1, SCREEN_BOX[1] + 1), TEAL_HI)


def blush(draw: ImageDraw.ImageDraw) -> None:
    rect(draw, (AVX + 20, AVY + 19, AVX + 22, AVY + 20), CLAY)
    rect(draw, (AVX + 34, AVY + 19, AVX + 36, AVY + 20), CLAY)


def draw_visor(index: int) -> Image.Image:
    image, draw = new_frame()

    if index == 0:  # dot — the crew's own expression
        screen(draw)
        rect(draw, (EYE_LEFT_X, EYE_TOP, EYE_LEFT_X + 1, EYE_TOP + 4), FACE)
        rect(draw, (EYE_RIGHT_X, EYE_TOP, EYE_RIGHT_X + 1, EYE_TOP + 4), FACE)
        blush(draw)

    elif index == 1:  # happy — eyes arched into carets
        screen(draw)
        for ex in (EYE_LEFT_X, EYE_RIGHT_X):
            rect(draw, (ex - 1, EYE_TOP + 3, ex, EYE_TOP + 4), FACE)
            rect(draw, (ex + 1, EYE_TOP + 1, ex + 2, EYE_TOP + 2), FACE)
            rect(draw, (ex + 3, EYE_TOP + 3, ex + 4, EYE_TOP + 4), FACE)
        blush(draw)

    elif index == 2:  # sleepy — lids most of the way down
        screen(draw)
        rect(draw, (EYE_LEFT_X - 1, EYE_TOP + 3, EYE_LEFT_X + 3, EYE_TOP + 4), FACE)
        rect(draw, (EYE_RIGHT_X - 1, EYE_TOP + 3, EYE_RIGHT_X + 3, EYE_TOP + 4), FACE)
        blush(draw)

    elif index == 3:  # shades — one dark band, two glints
        screen(draw, fill=TEAL_DK)
        rect(draw, (SCREEN_BOX[0] + 1, EYE_TOP - 1, SCREEN_BOX[2] - 1, EYE_TOP + 5), BLACK)
        rect(draw, (EYE_LEFT_X, EYE_TOP, EYE_LEFT_X + 2, EYE_TOP + 1), CREAM_HI)
        rect(draw, (EYE_RIGHT_X, EYE_TOP, EYE_RIGHT_X + 2, EYE_TOP + 1), CREAM_HI)

    elif index == 4:  # scanline — CRT banding, kept off the eye rows
        screen(draw)
        for row in range(SCREEN_BOX[1] + 2, SCREEN_BOX[3], 3):
            if EYE_TOP - 1 <= row <= EYE_TOP + 5:
                # Banding straight through the eyes drowns them: FACE navy and
                # TEAL_DK are too close in value at this size.
                continue
            rect(draw, (SCREEN_BOX[0] + 1, row, SCREEN_BOX[2] - 1, row), TEAL_DK)
        rect(draw, (EYE_LEFT_X, EYE_TOP, EYE_LEFT_X + 1, EYE_TOP + 4), FACE)
        rect(draw, (EYE_RIGHT_X, EYE_TOP, EYE_RIGHT_X + 1, EYE_TOP + 4), FACE)
        blush(draw)

    else:  # glitch — one displaced slice, eyes fringed red and cyan
        screen(draw)
        rect(draw, (SCREEN_BOX[0] + 4, EYE_TOP + 1, SCREEN_BOX[2] - 1, EYE_TOP + 3), TEAL_HI)
        rect(draw, (SCREEN_BOX[0] + 1, EYE_TOP + 1, SCREEN_BOX[0] + 3, EYE_TOP + 3), TEAL_DK)
        rect(draw, (EYE_LEFT_X - 2, EYE_TOP, EYE_LEFT_X - 1, EYE_TOP + 4), SIGNAL)
        rect(draw, (EYE_LEFT_X + 2, EYE_TOP, EYE_LEFT_X + 3, EYE_TOP + 4), TEAL_HI)
        rect(draw, (EYE_LEFT_X, EYE_TOP, EYE_LEFT_X + 1, EYE_TOP + 4), FACE)
        rect(draw, (EYE_RIGHT_X - 2, EYE_TOP + 1, EYE_RIGHT_X - 1, EYE_TOP + 5), SIGNAL)
        rect(draw, (EYE_RIGHT_X, EYE_TOP + 1, EYE_RIGHT_X + 1, EYE_TOP + 5), FACE)

    return image


# ── Headgear ───────────────────────────────────────────────────────────────
# Headgear lives in a 14-row band: never above AVY-4, which is the top of the
# frame, and never below AVY+9, which is the row before the screen starts. A
# hat that breaks the first rule gets silently clipped into a flat slab; one
# that breaks the second covers the eyes and turns the avatar into a blank box.
#
# Brimmed hats span close to the head's own width (AVX+3..AVX+46) and overlap
# its top few rows. Drawn any narrower they read as balanced on the head rather
# than worn on it.
HAT_CEILING = AVY - 4
HAT_FLOOR = AVY + 9


def draw_hat(index: int) -> Image.Image:
    image, draw = new_frame()
    x, y = AVX, AVY

    if index == 0:  # beanie — knit body with a folded band, pom on top
        rect(draw, (x + 6, y - 1, x + 43, y + 7), BLACK)
        rect(draw, (x + 7, y, x + 42, y + 6), SIGNAL)
        rect(draw, (x + 10, y + 1, x + 26, y + 3), CLAY)
        rect(draw, (x + 4, y + 4, x + 45, y + 9), BLACK)
        rect(draw, (x + 5, y + 5, x + 44, y + 8), CLAY)
        rect(draw, (x + 21, y - 4, x + 28, y + 1), BLACK)
        rect(draw, (x + 22, y - 3, x + 27, y), CREAM_HI)

    elif index == 1:  # barista cap — flat crown, short stiff brim, badge
        rect(draw, (x + 7, y, x + 42, y + 7), BLACK)
        rect(draw, (x + 8, y + 1, x + 41, y + 6), FACE)
        rect(draw, (x + 10, y + 1, x + 30, y + 2), METAL_DK)
        rect(draw, (x + 3, y + 6, x + 46, y + 9), BLACK)
        rect(draw, (x + 4, y + 7, x + 45, y + 8), METAL_DK)
        rect(draw, (x + 22, y + 1, x + 28, y + 5), BLACK)
        rect(draw, (x + 23, y + 2, x + 27, y + 4), COPPER)

    elif index == 2:  # hard hat — domed shell, centre crest, wide brim
        rect(draw, (x + 8, y, x + 41, y + 7), BLACK)
        rect(draw, (x + 9, y + 1, x + 40, y + 6), GOLD)
        rect(draw, (x + 22, y - 2, x + 28, y + 6), BLACK)
        rect(draw, (x + 23, y - 1, x + 27, y + 6), SCORE)
        rect(draw, (x + 2, y + 6, x + 47, y + 9), BLACK)
        rect(draw, (x + 3, y + 7, x + 46, y + 8), GOLD)

    elif index == 3:  # headphones — band over the crown, cups on the head edge
        rect(draw, (x + 9, y - 4, x + 40, y + 1), BLACK)
        rect(draw, (x + 10, y - 3, x + 39, y), METAL)
        rect(draw, (x + 12, y - 3, x + 30, y - 2), CREAM_HI)
        # Cups straddle the head's outer edge (AVX+3 and AVX+46) rather than
        # hanging beside it. Narrower or darker than this and they read as two
        # stray pixels of outline instead of as headphones.
        for cup in (x - 2, x + 41):
            rect(draw, (cup, y + 7, cup + 8, y + 20), BLACK)
            rect(draw, (cup + 1, y + 8, cup + 7, y + 19), METAL)
            rect(draw, (cup + 2, y + 10, cup + 6, y + 15), SIGNAL)
            rect(draw, (cup + 2, y + 9, cup + 5, y + 10), CREAM_HI)

    elif index == 4:  # crown — three peaks on a banded circlet
        rect(draw, (x + 8, y + 3, x + 41, y + 9), BLACK)
        rect(draw, (x + 9, y + 4, x + 40, y + 8), GOLD)
        rect(draw, (x + 9, y + 6, x + 40, y + 7), COPPER)
        for peak in (x + 9, x + 21, x + 33):
            rect(draw, (peak, y - 3, peak + 6, y + 5), BLACK)
            rect(draw, (peak + 1, y - 2, peak + 5, y + 4), GOLD)
            rect(draw, (peak + 2, y - 1, peak + 4, y + 1), SCORE)

    elif index == 5:  # antenna bulbs — a glass cap on each of the crew's posts
        rect(draw, (x + 16, y - 4, x + 25, y + 2), BLACK)
        rect(draw, (x + 17, y - 3, x + 24, y + 1), GOLD)
        rect(draw, (x + 18, y - 2, x + 21, y), SCORE)
        rect(draw, (x + 18, y + 2, x + 23, y + 4), COPPER_DK)
        rect(draw, (x + 26, y - 2, x + 35, y + 4), BLACK)
        rect(draw, (x + 27, y - 1, x + 34, y + 3), GOLD)
        rect(draw, (x + 28, y, x + 31, y + 2), SCORE)
        rect(draw, (x + 28, y + 4, x + 33, y + 6), COPPER_DK)

    else:  # toque — banded chef hat, puff stepped outward so it reads round
        rect(draw, (x + 7, y + 4, x + 42, y + 9), BLACK)
        rect(draw, (x + 8, y + 5, x + 41, y + 8), CREAM_SH)
        # Three stacked widths instead of three separate lobes: discrete lobes
        # at this scale read as castle battlements, not as a puffed hat.
        rect(draw, (x + 5, y + 1, x + 44, y + 6), BLACK)
        rect(draw, (x + 6, y + 2, x + 43, y + 5), CREAM_HI)
        rect(draw, (x + 7, y - 2, x + 42, y + 2), BLACK)
        rect(draw, (x + 8, y - 1, x + 41, y + 2), CREAM_HI)
        rect(draw, (x + 11, y - 4, x + 38, y), BLACK)
        rect(draw, (x + 12, y - 3, x + 37, y), CREAM_HI)
        rect(draw, (x + 15, y - 3, x + 26, y - 2), CREAM_SH)

    return image


# ── Uniform ────────────────────────────────────────────────────────────────
# Garments stay inside x+13..x+37 so they sit on the torso and never clip the
# arms, which the chassis already drew fanning out to x+8 and x+42.


def draw_wear(index: int) -> Image.Image:
    image, draw = new_frame()
    x, y = AVX, AVY

    if index == 0:  # apron
        rect(draw, (x + 22, y + 30, x + 24, y + 34), COPPER_DK)
        rect(draw, (x + 26, y + 30, x + 28, y + 34), COPPER_DK)
        rect(draw, (x + 17, y + 33, x + 33, y + 45), BLACK)
        rect(draw, (x + 18, y + 34, x + 32, y + 44), COPPER)
        rect(draw, (x + 18, y + 39, x + 32, y + 40), COPPER_DK)
        rect(draw, (x + 21, y + 41, x + 26, y + 43), COPPER_DK)

    elif index == 1:  # hi-vis vest
        rect(draw, (x + 13, y + 30, x + 20, y + 45), BLACK)
        rect(draw, (x + 30, y + 30, x + 37, y + 45), BLACK)
        rect(draw, (x + 14, y + 31, x + 19, y + 44), SCORE)
        rect(draw, (x + 31, y + 31, x + 36, y + 44), SCORE)
        rect(draw, (x + 14, y + 36, x + 19, y + 37), CREAM_HI)
        rect(draw, (x + 31, y + 36, x + 36, y + 37), CREAM_HI)
        rect(draw, (x + 14, y + 40, x + 36, y + 41), SCORE)
        rect(draw, (x + 14, y + 41, x + 36, y + 42), CREAM_HI)

    elif index == 2:  # scarf
        rect(draw, (x + 17, y + 27, x + 33, y + 33), BLACK)
        rect(draw, (x + 18, y + 28, x + 32, y + 32), SIGNAL)
        rect(draw, (x + 18, y + 30, x + 32, y + 31), CLAY)
        rect(draw, (x + 27, y + 32, x + 32, y + 42), BLACK)
        rect(draw, (x + 28, y + 33, x + 31, y + 41), SIGNAL)
        rect(draw, (x + 28, y + 36, x + 31, y + 37), CLAY)

    elif index == 3:  # bow tie
        rect(draw, (x + 18, y + 28, x + 32, y + 31), BLACK)
        rect(draw, (x + 19, y + 29, x + 31, y + 30), FACE)
        rect(draw, (x + 19, y + 30, x + 24, y + 36), BLACK)
        rect(draw, (x + 26, y + 30, x + 31, y + 36), BLACK)
        rect(draw, (x + 20, y + 31, x + 23, y + 35), SIGNAL)
        rect(draw, (x + 27, y + 31, x + 30, y + 35), SIGNAL)
        rect(draw, (x + 24, y + 31, x + 26, y + 35), COPPER_DK)

    elif index == 4:  # lab coat
        rect(draw, (x + 13, y + 30, x + 37, y + 45), BLACK)
        rect(draw, (x + 14, y + 31, x + 36, y + 44), CREAM_HI)
        rect(draw, (x + 23, y + 31, x + 27, y + 44), CREAM_SH)
        rect(draw, (x + 24, y + 31, x + 26, y + 44), METAL_DK)
        rect(draw, (x + 16, y + 31, x + 22, y + 36), CREAM_SH)
        rect(draw, (x + 28, y + 31, x + 34, y + 36), CREAM_SH)
        rect(draw, (x + 30, y + 38, x + 34, y + 42), CREAM_SH)
        rect(draw, (x + 31, y + 39, x + 33, y + 40), TEAL_DK)

    else:  # bandolier
        draw.line(((x + 15, y + 30), (x + 35, y + 44)), fill=BLACK, width=7)
        draw.line(((x + 15, y + 30), (x + 35, y + 44)), fill=COPPER_DK, width=4)
        for step in range(3):
            px = x + 18 + step * 6
            py = y + 32 + step * 4
            rect(draw, (px, py, px + 4, py + 4), BLACK)
            rect(draw, (px + 1, py + 1, px + 3, py + 3), GOLD)

    return image


# ── Held items ─────────────────────────────────────────────────────────────
# Objects overlap the right mitten at HAND_RIGHT so they read as carried; the
# arm pose stays neutral, because posing it would have to live in the chassis
# sheet and multiply every colourway by every prop.


def draw_hold(index: int) -> Image.Image:
    image, draw = new_frame()
    hx, hy = HAND_RIGHT

    if index == 0:  # mug
        rect(draw, (hx - 2, hy - 8, hx + 6, hy + 1), BLACK)
        rect(draw, (hx - 1, hy - 7, hx + 5, hy), CREAM_HI)
        rect(draw, (hx - 1, hy - 7, hx + 5, hy - 5), COFFEE)
        rect(draw, (hx + 6, hy - 5, hx + 8, hy - 2), BLACK)
        rect(draw, (hx + 7, hy - 4, hx + 7, hy - 3), CREAM_HI)
        rect(draw, (hx, hy - 11, hx, hy - 10), CREAM_HI)
        rect(draw, (hx + 3, hy - 13, hx + 3, hy - 12), CREAM_HI)

    elif index == 1:  # bean bag
        rect(draw, (hx - 3, hy - 7, hx + 7, hy + 5), BLACK)
        rect(draw, (hx - 2, hy - 6, hx + 6, hy + 4), COPPER_DK)
        rect(draw, (hx - 2, hy - 6, hx + 6, hy - 4), COPPER)
        rect(draw, (hx, hy - 2, hx + 4, hy + 2), COPPER)
        rect(draw, (hx + 1, hy - 1, hx + 3, hy + 1), COFFEE)
        rect(draw, (hx - 1, hy - 9, hx + 5, hy - 6), BLACK)
        rect(draw, (hx, hy - 8, hx + 4, hy - 7), COPPER_DK)

    elif index == 2:  # key
        rect(draw, (hx - 4, hy - 2, hx + 5, hy + 1), GOLD)
        rect(draw, (hx - 4, hy - 4, hx, hy + 3), GOLD)
        rect(draw, (hx - 3, hy - 3, hx - 1, hy + 2), COPPER_DK)
        rect(draw, (hx + 4, hy + 1, hx + 6, hy + 4), GOLD)
        rect(draw, (hx + 2, hy + 1, hx + 3, hy + 3), GOLD)

    elif index == 3:  # wrench
        draw.line(((hx - 3, hy + 6), (hx + 3, hy - 6)), fill=BLACK, width=7)
        draw.line(((hx - 3, hy + 6), (hx + 3, hy - 6)), fill=METAL, width=3)
        rect(draw, (hx, hy - 12, hx + 9, hy - 3), BLACK)
        rect(draw, (hx + 1, hy - 11, hx + 8, hy - 4), METAL)
        rect(draw, (hx + 2, hy - 10, hx + 7, hy - 8), METAL_DK)
        # Carve the jaw back out. The image is RGBA, so painting alpha 0 here
        # cuts a real hole rather than laying a black notch on top.
        rect(draw, (hx + 3, hy - 12, hx + 6, hy - 9), (0, 0, 0, 0))

    elif index == 4:  # terminal
        rect(draw, (hx - 4, hy - 9, hx + 7, hy + 4), BLACK)
        rect(draw, (hx - 3, hy - 8, hx + 6, hy + 3), METAL_DK)
        rect(draw, (hx - 2, hy - 7, hx + 5, hy - 2), TEAL_DK)
        rect(draw, (hx - 1, hy - 6, hx + 3, hy - 5), TEAL)
        rect(draw, (hx - 2, hy, hx - 1, hy + 1), GREEN)
        rect(draw, (hx + 1, hy, hx + 2, hy + 1), GOLD)
        rect(draw, (hx + 4, hy, hx + 5, hy + 1), CLAY)

    else:  # CAFE coin
        rect(draw, (hx - 3, hy - 8, hx + 5, hy), BLACK)
        rect(draw, (hx - 4, hy - 7, hx + 6, hy - 1), BLACK)
        rect(draw, (hx - 2, hy - 7, hx + 4, hy - 1), GOLD)
        rect(draw, (hx - 3, hy - 6, hx + 5, hy - 2), GOLD)
        rect(draw, (hx - 2, hy - 6, hx + 1, hy - 5), SCORE)
        rect(draw, (hx, hy - 5, hx + 2, hy - 3), COPPER_DK)

    return image


# ── Sheets ─────────────────────────────────────────────────────────────────

SHEETS: tuple[tuple[str, Callable[[int], Image.Image], int], ...] = (
    ("avatar-chassis.png", draw_chassis, len(COLOURWAYS)),
    ("avatar-visor.png", draw_visor, 6),
    ("avatar-wear.png", draw_wear, 6),
    ("avatar-hat.png", draw_hat, 7),
    ("avatar-hold.png", draw_hold, 6),
)


def save_sheet(name: str, renderer: Callable[[int], Image.Image], frames: int) -> None:
    sheet = Image.new("RGBA", (FRAME * frames, FRAME), (0, 0, 0, 0))
    for index in range(frames):
        sheet.alpha_composite(renderer(index), (index * FRAME, 0))
    sheet.save(OUTPUT / name, optimize=True)
    print(f"{name}: {frames} frames, {FRAME * frames}x{FRAME}")


def main() -> None:
    OUTPUT.mkdir(parents=True, exist_ok=True)
    for name, renderer, frames in SHEETS:
        save_sheet(name, renderer, frames)


if __name__ == "__main__":
    main()
