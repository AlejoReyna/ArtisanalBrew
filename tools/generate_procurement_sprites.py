#!/usr/bin/env python3
"""Generate the Procurement Lab robot sheets and hero jet-flame strip.

The sheets are intentionally tiny and aliased. Each contains four 64x64 frames
laid out horizontally: two walking poses followed by two role-action poses.
CSS scales them with nearest-neighbour rendering and advances the frames with
stepped keyframes. The separate 7x12 exhaust frames let the hero runtime turn
the flame fully off during its 0.8-second rest window.
"""

from pathlib import Path

from PIL import Image, ImageDraw, ImageOps


ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "src" / "ThisCafeteria.Web" / "wwwroot" / "images"
FRAME = 64
FRAMES = 4

OUT = (31, 22, 17, 255)
# The robot palette is deliberately cooler and softer than the surrounding
# coffee props. It follows the new character concept: an almost-black navy
# keyline, powder-blue face glass, icy white casing and two tiny coral accents.
BLACK = (17, 22, 39, 255)
CREAM = (219, 232, 234, 255)
CREAM_SH = (151, 181, 197, 255)
CREAM_HI = (244, 247, 239, 255)
TEAL_DK = (63, 101, 132, 255)
TEAL = (147, 190, 211, 255)
TEAL_HI = (199, 222, 229, 255)
FACE = (27, 42, 76, 255)
METAL = (92, 128, 151, 255)
METAL_DK = (43, 66, 91, 255)
COPPER = (181, 126, 72, 255)
COPPER_DK = (112, 72, 42, 255)
GOLD = (236, 178, 66, 255)
GREEN = (143, 185, 155, 255)
CLAY = (242, 133, 137, 255)
SIGNAL = (238, 69, 62, 255)
SCORE = (231, 232, 103, 255)
COFFEE = (98, 64, 38, 255)


def rect(draw: ImageDraw.ImageDraw, box: tuple[int, int, int, int], fill) -> None:
    draw.rectangle(box, fill=fill)


def robot(
    draw: ImageDraw.ImageDraw,
    *,
    x: int,
    y: int,
    frame: int,
    left_hand: tuple[int, int] | None = None,
    right_hand: tuple[int, int] | None = None,
    antenna: bool = False,
    floating: bool = False,
    tuck: bool = False,
) -> None:
    """Draw the shared friendly CRT-headed robot from back to front.

    The silhouette is an original project-specific interpretation of the
    ArtStation reference supplied for the homepage redesign: a large
    powder-blue face window, tiny eyes and blush pixels, offset antennae,
    mitten hands and sturdy little boots. The coffee jetpack and exact 64px
    physics footprint remain unchanged.
    """

    # Floating robots hang their legs and sway them together instead of
    # stepping; grounded ones bob and alternate feet. Tuck pulls the legs up
    # a few pixels — the collecting pose.
    if floating:
        bob = (0, 1, 0, -2)[frame % 4] if frame < 4 else -1
        sway = (-1, 0, 1, 0)[frame % 4] if frame < 4 else 0
        left_step = right_step = sway
    else:
        bob = 1 if frame == 1 else 0
        left_step = -2 if frame == 0 else 2 if frame == 1 else 0
        right_step = 2 if frame == 0 else -2 if frame == 1 else 0
    y += bob

    # Sturdy segmented legs and wide dark soles. Tucked frames raise the whole
    # leg set, like knees pulled up mid-grab.
    ly = y - 3 if tuck else y
    rect(draw, (x + 16, ly + 42, x + 23, ly + 54), BLACK)
    rect(draw, (x + 27, ly + 42, x + 34, ly + 54), BLACK)
    rect(draw, (x + 18, ly + 43, x + 22, ly + 51), CREAM_SH)
    rect(draw, (x + 29, ly + 43, x + 33, ly + 51), CREAM_SH)
    rect(draw, (x + 19, ly + 43, x + 22, ly + 48), CREAM_HI)
    rect(draw, (x + 30, ly + 43, x + 33, ly + 48), CREAM_HI)
    rect(draw, (x + 14 + left_step, ly + 51, x + 24 + left_step, ly + 56), BLACK)
    rect(draw, (x + 26 + right_step, ly + 51, x + 36 + right_step, ly + 56), BLACK)
    rect(draw, (x + 17 + left_step, ly + 51, x + 23 + left_step, ly + 53), CREAM_HI)
    rect(draw, (x + 29 + right_step, ly + 51, x + 35 + right_step, ly + 53), CREAM_HI)

    # Jetpack on the back: a squat copper-and-teal tank tucked against the
    # torso's rear (left) edge and under the head, with a low nozzle.
    # Drawn before the arms so the arm passes in front of it; the head eats
    # its top corner, seating it on the back. The flame is a separate sprite
    # controlled by the runtime's 1.5s thrust / 0.8s rest duty cycle.
    rect(draw, (x + 6, y + 29, x + 13, y + 43), BLACK)
    rect(draw, (x + 8, y + 30, x + 12, y + 42), TEAL_DK)
    rect(draw, (x + 9, y + 32, x + 12, y + 36), TEAL_HI)
    rect(draw, (x + 6, y + 29, x + 13, y + 31), COPPER)
    rect(draw, (x + 6, y + 39, x + 13, y + 41), COPPER_DK)
    rect(draw, (x + 8, y + 44, x + 11, y + 45), BLACK)

    # Chunky arms live behind the body and can still be aimed at role props.
    left_shoulder = (x + 14, y + 33)
    right_shoulder = (x + 36, y + 33)
    if left_hand is None:
        left_hand = (x + 8, y + 40)
    if right_hand is None:
        right_hand = (x + 42, y + 40)
    draw.line((left_shoulder, left_hand), fill=BLACK, width=7)
    draw.line((right_shoulder, right_hand), fill=BLACK, width=7)
    draw.line((left_shoulder, left_hand), fill=CREAM_SH, width=3)
    draw.line((right_shoulder, right_hand), fill=CREAM_SH, width=3)
    rect(draw, (left_hand[0] - 3, left_hand[1] - 3, left_hand[0] + 3, left_hand[1] + 3), BLACK)
    rect(draw, (left_hand[0] - 2, left_hand[1] - 2, left_hand[0] + 1, left_hand[1] + 1), CREAM_HI)
    rect(draw, (right_hand[0] - 3, right_hand[1] - 3, right_hand[0] + 3, right_hand[1] + 3), BLACK)
    rect(draw, (right_hand[0] - 1, right_hand[1] - 2, right_hand[0] + 2, right_hand[1] + 1), CREAM_HI)

    # Compact rounded body: clipped corners, broad white chest and one red
    # status light. A short navy neck keeps the oversized head readable.
    rect(draw, (x + 21, y + 27, x + 29, y + 32), BLACK)
    rect(draw, (x + 13, y + 29, x + 37, y + 45), BLACK)
    rect(draw, (x + 11, y + 33, x + 39, y + 41), BLACK)
    rect(draw, (x + 14, y + 31, x + 36, y + 43), CREAM_SH)
    rect(draw, (x + 13, y + 34, x + 37, y + 40), CREAM_SH)
    rect(draw, (x + 18, y + 32, x + 34, y + 39), CREAM_HI)
    rect(draw, (x + 17, y + 40, x + 33, y + 42), METAL)
    rect(draw, (x + 32, y + 34, x + 34, y + 36), SIGNAL)

    # Two offset antenna posts, part of every crew member's silhouette.
    rect(draw, (x + 19, y - 1, x + 22, y + 5), BLACK)
    rect(draw, (x + 20, y - 2, x + 21, y + 1), CREAM_HI)
    rect(draw, (x + 29, y + 1, x + 32, y + 5), BLACK)
    rect(draw, (x + 30, y, x + 31, y + 2), SIGNAL)

    # Large stepped CRT head with a recessed powder-blue face panel. The left
    # side pod and top-left glint keep the front-facing shape dimensional.
    rect(draw, (x + 5, y + 3, x + 44, y + 30), BLACK)
    rect(draw, (x + 3, y + 8, x + 46, y + 26), BLACK)
    rect(draw, (x + 7, y + 5, x + 42, y + 28), CREAM_SH)
    rect(draw, (x + 5, y + 10, x + 44, y + 24), CREAM_SH)
    rect(draw, (x + 12, y + 6, x + 42, y + 27), CREAM_HI)
    rect(draw, (x + 5, y + 11, x + 10, y + 23), METAL)
    rect(draw, (x + 6, y + 12, x + 8, y + 16), TEAL_HI)
    rect(draw, (x + 15, y + 8, x + 40, y + 25), TEAL_DK)
    rect(draw, (x + 17, y + 10, x + 38, y + 23), TEAL)
    rect(draw, (x + 18, y + 10, x + 37, y + 11), TEAL_HI)

    # Tiny vertical eyes and single-pixel blush blocks: expressive at 64px
    # without the old yellow smile or any anime-scale facial detail.
    rect(draw, (x + 22, y + 14, x + 23, y + 18), FACE)
    rect(draw, (x + 33, y + 14, x + 34, y + 18), FACE)
    rect(draw, (x + 20, y + 19, x + 22, y + 20), CLAY)
    rect(draw, (x + 34, y + 19, x + 36, y + 20), CLAY)

    if antenna:
        # Scout-only receiver sparkle; the physical twin posts stay shared.
        rect(draw, (x + 18, y - 4, x + 20, y - 3), TEAL_HI)
        rect(draw, (x + 19, y - 5, x + 19, y - 2), TEAL_HI)


def telescope(draw: ImageDraw.ImageDraw, x: int, y: int, raised: bool) -> None:
    if raised:
        draw.line(((x, y + 12), (x + 15, y)), fill=BLACK, width=7)
        draw.line(((x + 1, y + 11), (x + 14, y + 1)), fill=METAL, width=3)
        rect(draw, (x + 13, y - 2, x + 18, y + 3), COPPER)
        rect(draw, (x + 16, y - 1, x + 19, y + 2), GOLD)
    else:
        draw.line(((x, y + 5), (x + 17, y + 8)), fill=BLACK, width=7)
        draw.line(((x + 1, y + 5), (x + 16, y + 8)), fill=METAL, width=3)
        rect(draw, (x + 15, y + 5, x + 20, y + 10), COPPER)


def crate(draw: ImageDraw.ImageDraw, box: tuple[int, int, int, int]) -> None:
    x1, y1, x2, y2 = box
    rect(draw, box, BLACK)
    rect(draw, (x1 + 2, y1 + 2, x2 - 2, y2 - 2), COPPER_DK)
    rect(draw, (x1 + 2, y1 + 2, x2 - 2, y1 + 4), COPPER)
    draw.line((x1 + 3, y1 + 3, x2 - 3, y2 - 3), fill=COPPER, width=1)
    draw.line((x2 - 3, y1 + 3, x1 + 3, y2 - 3), fill=COPPER, width=1)
    rect(draw, ((x1 + x2) // 2 - 1, (y1 + y2) // 2 - 2, (x1 + x2) // 2 + 1, (y1 + y2) // 2 + 2), GOLD)


def draw_scout(frame: int) -> Image.Image:
    image = Image.new("RGBA", (FRAME, FRAME), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    raised = frame >= 2
    robot(draw, x=7, y=7, frame=frame, right_hand=(48, 25 if raised else 34), antenna=True)
    telescope(draw, 42, 9 if raised else 27, raised)
    if frame == 3:
        rect(draw, (60, 5, 62, 7), TEAL_HI)
        rect(draw, (57, 9, 58, 10), GREEN)
    return image


def draw_buyer(frame: int) -> Image.Image:
    image = Image.new("RGBA", (FRAME, FRAME), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    hand = (43, 37) if frame < 2 else (48, 34)
    robot(draw, x=0, y=7, frame=frame, right_hand=hand)

    # Authorization terminal.
    rect(draw, (43, 30, 62, 57), BLACK)
    rect(draw, (46, 33, 59, 54), METAL_DK)
    rect(draw, (48, 35, 57, 43), TEAL_DK)
    rect(draw, (50, 37, 55, 39), TEAL)
    rect(draw, (50, 47, 52, 49), GREEN)
    rect(draw, (54, 47, 56, 49), GOLD)
    rect(draw, (58, 47, 59, 49), CLAY)

    # Key advances into the terminal over the action frames.
    key_x = (37, 40, 44, 48)[frame]
    rect(draw, (key_x, 31, key_x + 7, 34), GOLD)
    rect(draw, (key_x, 29, key_x + 3, 36), GOLD)
    rect(draw, (key_x + 6, 33, key_x + 9, 36), GOLD)
    if frame == 3:
        rect(draw, (49, 36, 56, 42), GREEN)
    return image


def draw_courier(frame: int) -> Image.Image:
    image = Image.new("RGBA", (FRAME, FRAME), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    lift = 1 if frame in (1, 3) else 0
    robot(
        draw,
        x=7,
        y=7 - lift,
        frame=frame,
        left_hand=(22, 39 - lift),
        right_hand=(48, 39 - lift),
    )
    crate(draw, (20, 32 - lift, 50, 46 - lift))
    rect(draw, (8, 31 - lift, 13, 46 - lift), COPPER)
    rect(draw, (6, 34 - lift, 9, 43 - lift), COPPER_DK)
    return image


def draw_inspector(frame: int) -> Image.Image:
    image = Image.new("RGBA", (FRAME, FRAME), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    scan_y = 38 if frame < 2 else 31
    robot(draw, x=0, y=7, frame=frame, right_hand=(43, scan_y))
    crate(draw, (42, 40, 62, 55))
    rect(draw, (40, scan_y - 4, 48, scan_y + 2), BLACK)
    rect(draw, (42, scan_y - 3, 47, scan_y), METAL_DK)
    rect(draw, (47, scan_y - 2, 50, scan_y + 1), TEAL_HI)
    if frame == 3:
        rect(draw, (44, 43, 60, 45), TEAL_HI)
    return image


def draw_coincrew(frame: int) -> Image.Image:
    """Prop-less coin collector: empty hands treading gently, dangling legs,
    a soft 4-frame hover cycle so it reads as weightless. Frame 4 is the
    unique collecting pose: legs tucked, both hands thrust forward and down
    to snatch the coin."""
    image = Image.new("RGBA", (FRAME, FRAME), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    if frame == 4:  # grab pose
        robot(
            draw,
            x=7,
            y=7,
            frame=frame,
            left_hand=(24, 51),
            right_hand=(33, 51),
            floating=True,
            tuck=True,
        )
        return image
    # Arms paddle slowly: hands sink a few pixels then lift back over the
    # four frames, like treading water in zero-G.
    stroke = (-3, -1, 1, -1)[frame]
    reach = (0, -1, 0, 1)[frame]
    robot(
        draw,
        x=7,
        y=7,
        frame=frame,
        left_hand=(10 - reach, 45 + stroke),
        right_hand=(47 + reach, 45 + stroke),
        floating=True,
    )
    return image


def coffee_cup(draw: ImageDraw.ImageDraw, x: int, y: int, steam: int = 0) -> None:
    """Tiny cup: cream body, dark coffee surface, nub handle on the right,
    plus optional steam dots creeping up from the rim."""
    rect(draw, (x, y, x + 4, y + 5), BLACK)
    rect(draw, (x + 1, y + 1, x + 3, y + 4), CREAM_HI)
    rect(draw, (x + 1, y + 1, x + 3, y + 2), COFFEE)
    rect(draw, (x + 5, y + 2, x + 6, y + 3), BLACK)
    if steam >= 1:
        rect(draw, (x + 1, y - 3, x + 1, y - 3), CREAM_HI)
    if steam == 2:
        rect(draw, (x + 3, y - 5, x + 3, y - 5), CREAM_HI)


# Cup anchor during the sip: reach (low right), raise to the mouth, sip (same
# spot, steam), and ease back down half-way.
SIP_POSES = ((44, 37), (37, 24), (37, 24), (42, 34))
SIP_STEAM = (0, 0, 2, 1)


def draw_sip(frame: int) -> Image.Image:
    """Coffee-break pose: the coin collector paused mid-hover with its cup.
    Four frames — reach / raise / sip / lower — covering exactly one second in
    the hero (CSS steps), triggered on demand via .ph-scene__roam--sipping."""
    image = Image.new("RGBA", (FRAME, FRAME), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    cx, cy = SIP_POSES[frame]
    robot(
        draw,
        x=7,
        y=7,
        frame=frame,
        right_hand=(cx + 2, cy + 5),
        floating=True,
    )
    coffee_cup(draw, cx, cy, steam=SIP_STEAM[frame])
    return image


PLUS_ONE = (
    "010 010",
    "010 110",
    "111 010",
    "010 010",
    "010 111",
)


def draw_plus_one() -> Image.Image:
    """Pixel "+1" score popup, Mario-style: dark drop copy under a bright
    glyph so it reads on any sky."""
    grid = [row.replace(" ", "") for row in PLUS_ONE]
    image = Image.new("RGBA", (9, 6), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    for yy, row in enumerate(grid):
        for xx, cell in enumerate(row):
            if cell == "1":
                draw.point((xx + 1, yy + 1), fill=BLACK)
    for yy, row in enumerate(grid):
        for xx, cell in enumerate(row):
            if cell == "1":
                draw.point((xx, yy), fill=SCORE)
    return image


JET_FLAME_FRAMES = (
    (
        "000H000",
        "00HH000",
        "00YY000",
        "0YYYY00",
        "0YOOO00",
        "00OO000",
        "00RR000",
        "000R000",
    ),
    (
        "000H000",
        "00HHH00",
        "00YYY00",
        "0YYYY00",
        "0YOOO00",
        "00OOO00",
        "00OR000",
        "00RR000",
        "000R000",
        "000R000",
        "000R000",
    ),
    (
        "000H000",
        "00HH000",
        "00YYY00",
        "0YYYY00",
        "0YOOO00",
        "0OOOO00",
        "00ORR00",
        "000RR00",
        "000R000",
    ),
    (
        "000H000",
        "00HHH00",
        "0YYYY00",
        "0YYYYY0",
        "0YOOO00",
        "00OOO00",
        "00OR000",
        "000RR00",
        "000R000",
        "000R000",
        "000R000",
        "000R000",
    ),
)


def draw_jet_flame(frame: int) -> Image.Image:
    """One hard-edged exhaust frame: white-hot core, gold body, orange shell
    and a red tail. Different lengths make the active burn feel unstable."""
    image = Image.new("RGBA", (7, 12), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    colors = {
        "H": CREAM_HI,
        "Y": GOLD,
        "O": CLAY,
        "R": SIGNAL,
    }
    for yy, row in enumerate(JET_FLAME_FRAMES[frame]):
        for xx, cell in enumerate(row):
            if cell != "0":
                draw.point((xx, yy), fill=colors[cell])
    return image


def save_jet_flame_sheet() -> None:
    sheet = Image.new("RGBA", (7 * len(JET_FLAME_FRAMES), 12), (0, 0, 0, 0))
    for index in range(len(JET_FLAME_FRAMES)):
        sheet.alpha_composite(draw_jet_flame(index), (index * 7, 0))
    sheet.save(OUTPUT / "pl-robot-jetflame.png", optimize=True)


def save_sheet(name: str, renderer, frames: int = FRAMES, mirror: bool = False) -> None:
    sheet = Image.new("RGBA", (FRAME * frames, FRAME), (0, 0, 0, 0))
    for index in range(frames):
        frame_image = renderer(index)
        if mirror:
            frame_image = ImageOps.mirror(frame_image)
        sheet.alpha_composite(frame_image, (index * FRAME, 0))
    sheet.save(OUTPUT / name, optimize=True)


def main() -> None:
    OUTPUT.mkdir(parents=True, exist_ok=True)
    save_sheet("pl-robot-scout.png", draw_scout)
    save_sheet("pl-robot-buyer.png", draw_buyer)
    save_sheet("pl-robot-courier.png", draw_courier)
    save_sheet("pl-robot-inspector.png", draw_inspector)
    save_sheet("pl-robot-coincrew.png", draw_coincrew, frames=5)
    # Mirrored copy for robots anchored on the right side, so they face
    # left — toward the space and their coins.
    save_sheet("pl-robot-coincrew-flip.png", draw_coincrew, frames=5, mirror=True)
    # Coffee-break overlay sheet + mirror, same facing logic.
    save_sheet("pl-robot-coincrew-sip.png", draw_sip)
    save_sheet("pl-robot-coincrew-sip-flip.png", draw_sip, mirror=True)
    save_jet_flame_sheet()
    draw_plus_one().save(OUTPUT / "pl-plus-one.png", optimize=True)


if __name__ == "__main__":
    main()
