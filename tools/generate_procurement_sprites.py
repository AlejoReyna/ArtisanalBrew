#!/usr/bin/env python3
"""Generate the four Procurement Lab robot sprite sheets.

The sheets are intentionally tiny and aliased. Each contains four 64x64 frames
laid out horizontally: two walking poses followed by two role-action poses.
CSS scales them with nearest-neighbour rendering and advances the frames with
stepped keyframes.
"""

from pathlib import Path

from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "src" / "ThisCafeteria.Web" / "wwwroot" / "images"
FRAME = 64
FRAMES = 4

OUT = (31, 22, 17, 255)
BLACK = (8, 8, 7, 255)
CREAM = (218, 207, 171, 255)
CREAM_SH = (178, 165, 126, 255)
CREAM_HI = (255, 250, 198, 255)
TEAL_DK = (24, 67, 63, 255)
TEAL = (57, 119, 109, 255)
TEAL_HI = (132, 213, 190, 255)
FACE = (231, 232, 103, 255)
METAL = (89, 80, 66, 255)
METAL_DK = (53, 46, 39, 255)
COPPER = (181, 126, 72, 255)
COPPER_DK = (112, 72, 42, 255)
GOLD = (236, 178, 66, 255)
GREEN = (143, 185, 155, 255)
CLAY = (229, 152, 128, 255)


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
) -> None:
    """Draw the shared friendly square-headed robot from back to front."""

    bob = 1 if frame == 1 else 0
    y += bob

    # Legs: deliberately spindly like the reference, with alternating feet.
    left_step = -2 if frame == 0 else 2 if frame == 1 else 0
    right_step = 2 if frame == 0 else -2 if frame == 1 else 0
    rect(draw, (x + 18, y + 41, x + 22, y + 53), BLACK)
    rect(draw, (x + 27, y + 41, x + 31, y + 53), BLACK)
    rect(draw, (x + 19, y + 41, x + 21, y + 50), CREAM_HI)
    rect(draw, (x + 28, y + 41, x + 30, y + 50), CREAM_HI)
    rect(draw, (x + 16 + left_step, y + 51, x + 22 + left_step, y + 55), BLACK)
    rect(draw, (x + 27 + right_step, y + 51, x + 33 + right_step, y + 55), BLACK)

    # Arms live behind the body and can be aimed at role props.
    left_shoulder = (x + 13, y + 32)
    right_shoulder = (x + 36, y + 32)
    if left_hand is None:
        left_hand = (x + 9, y + 40)
    if right_hand is None:
        right_hand = (x + 40, y + 40)
    draw.line((left_shoulder, left_hand), fill=BLACK, width=5)
    draw.line((right_shoulder, right_hand), fill=BLACK, width=5)
    draw.line((left_shoulder, left_hand), fill=CREAM_SH, width=2)
    draw.line((right_shoulder, right_hand), fill=CREAM_SH, width=2)
    rect(draw, (left_hand[0] - 2, left_hand[1] - 2, left_hand[0] + 2, left_hand[1] + 2), BLACK)
    rect(draw, (right_hand[0] - 2, right_hand[1] - 2, right_hand[0] + 2, right_hand[1] + 2), BLACK)

    # Compact body and luminous chest plate.
    rect(draw, (x + 13, y + 27, x + 36, y + 44), BLACK)
    rect(draw, (x + 16, y + 30, x + 33, y + 42), CREAM_SH)
    rect(draw, (x + 19, y + 32, x + 30, y + 40), CREAM_HI)

    # Large monitor head: thick black silhouette, beige side casing, teal face.
    rect(draw, (x + 4, y + 2, x + 45, y + 29), BLACK)
    rect(draw, (x + 1, y + 9, x + 5, y + 22), BLACK)
    rect(draw, (x + 5, y + 5, x + 14, y + 26), CREAM_SH)
    rect(draw, (x + 14, y + 5, x + 42, y + 26), CREAM_HI)
    rect(draw, (x + 17, y + 8, x + 39, y + 23), TEAL_DK)
    rect(draw, (x + 19, y + 10, x + 37, y + 21), TEAL)
    rect(draw, (x + 20, y + 10, x + 35, y + 11), TEAL_HI)

    # Friendly face from the reference.
    rect(draw, (x + 22, y + 14, x + 24, y + 16), FACE)
    rect(draw, (x + 33, y + 14, x + 35, y + 16), FACE)
    draw.line(
        ((x + 24, y + 18), (x + 26, y + 20), (x + 31, y + 20), (x + 33, y + 18)),
        fill=FACE,
        width=2,
    )

    if antenna:
        rect(draw, (x + 24, y - 3, x + 26, y + 2), BLACK)
        rect(draw, (x + 23, y - 5, x + 27, y - 2), TEAL_HI)


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


def save_sheet(name: str, renderer) -> None:
    sheet = Image.new("RGBA", (FRAME * FRAMES, FRAME), (0, 0, 0, 0))
    for index in range(FRAMES):
        sheet.alpha_composite(renderer(index), (index * FRAME, 0))
    sheet.save(OUTPUT / name, optimize=True)


def main() -> None:
    OUTPUT.mkdir(parents=True, exist_ok=True)
    save_sheet("pl-robot-scout.png", draw_scout)
    save_sheet("pl-robot-buyer.png", draw_buyer)
    save_sheet("pl-robot-courier.png", draw_courier)
    save_sheet("pl-robot-inspector.png", draw_inspector)


if __name__ == "__main__":
    main()
