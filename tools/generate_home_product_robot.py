#!/usr/bin/env python3
"""Generate the seated marketplace-browser robot used on the homepage.

The illustration deliberately reuses the crew's CRT body and palette, then
gives this particular character a desk-bound role: square glasses, a wooden
chair and table, and a silver laptop with a tiny storefront on its screen.
"""

from pathlib import Path

from PIL import Image, ImageDraw

from generate_procurement_sprites import (
    BLACK,
    COPPER,
    COPPER_DK,
    CLAY,
    CREAM_HI,
    CREAM_SH,
    FACE,
    GOLD,
    METAL,
    METAL_DK,
    TEAL,
    TEAL_DK,
    TEAL_HI,
    rect,
    robot,
)


ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "src" / "ThisCafeteria.Web" / "wwwroot" / "images"
WIDTH = 180
HEIGHT = 132
WOOD_DARK = (68, 39, 24, 255)
WOOD = (116, 69, 38, 255)
WOOD_LIGHT = (174, 111, 58, 255)


def draw_scene() -> Image.Image:
    image = Image.new("RGBA", (WIDTH, HEIGHT), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)

    # High-backed, stepped wooden chair. It is drawn first so the robot sits
    # inside its silhouette rather than looking pasted in front of it.
    rect(draw, (7, 42, 13, 117), BLACK)
    rect(draw, (10, 45, 18, 112), WOOD_DARK)
    rect(draw, (12, 49, 42, 55), BLACK)
    rect(draw, (14, 51, 40, 54), WOOD_LIGHT)
    rect(draw, (12, 60, 42, 65), BLACK)
    rect(draw, (14, 61, 40, 64), WOOD)
    rect(draw, (10, 81, 50, 90), BLACK)
    rect(draw, (14, 82, 47, 87), WOOD_LIGHT)
    rect(draw, (13, 88, 20, 121), BLACK)
    rect(draw, (15, 90, 18, 118), WOOD)
    rect(draw, (41, 87, 48, 115), BLACK)
    rect(draw, (43, 89, 46, 112), WOOD_DARK)

    # The established friendly crew body, aimed toward the keyboard. The desk
    # covers its lower legs, which makes the bent seated pose read cleanly at
    # this tiny scale.
    robot(
        draw,
        x=20,
        y=28,
        frame=2,
        left_hand=(65, 77),
        right_hand=(74, 79),
    )

    # Bring the chair's seat rail back in front of the straight base sprite.
    # It hides the crew sheet's standing boots and leaves the torso planted on
    # the cushion, so this one-off role unmistakably reads as seated.
    rect(draw, (11, 74, 67, 84), BLACK)
    rect(draw, (15, 77, 63, 81), WOOD_LIGHT)
    rect(draw, (18, 82, 59, 84), WOOD_DARK)

    # Thin square spectacles fitted inside the original face window. The
    # lenses stay transparent blue and the exact hero eyes/blush are redrawn
    # over them, preserving the established character rather than replacing
    # its expression with two large white blocks.
    rect(draw, (38, 39, 47, 47), BLACK)
    rect(draw, (49, 39, 58, 47), BLACK)
    rect(draw, (47, 42, 49, 43), BLACK)
    rect(draw, (40, 41, 45, 45), TEAL)
    rect(draw, (51, 41, 56, 45), TEAL)
    rect(draw, (41, 41, 42, 42), TEAL_HI)
    rect(draw, (52, 41, 53, 42), TEAL_HI)
    rect(draw, (42, 42, 43, 46), FACE)
    rect(draw, (53, 42, 54, 46), FACE)
    rect(draw, (40, 47, 42, 48), CLAY)
    rect(draw, (54, 47, 56, 48), CLAY)

    # The laptop screen faces left toward the robot. We therefore see the
    # square silver back of its lid rather than a storefront facing us.
    # A thin blue strip along the left edge is the screen glow on the user's
    # side of the hinge.
    rect(draw, (77, 44, 134, 84), BLACK)
    rect(draw, (80, 47, 131, 81), METAL)
    rect(draw, (80, 49, 83, 78), TEAL_HI)
    rect(draw, (84, 48, 128, 50), CREAM_HI)

    # Centered pixel-apple mark: a compact fruit silhouette, leaf and bite.
    # It is intentionally simplified so it stays readable after 2x scaling.
    rect(draw, (105, 57, 109, 59), METAL_DK)
    rect(draw, (109, 55, 112, 57), METAL_DK)
    rect(draw, (100, 61, 113, 70), METAL_DK)
    rect(draw, (98, 63, 115, 68), METAL_DK)
    rect(draw, (101, 69, 112, 73), METAL_DK)
    rect(draw, (113, 61, 116, 64), METAL)
    rect(draw, (112, 70, 114, 72), METAL)

    rect(draw, (76, 81, 138, 87), BLACK)
    rect(draw, (80, 82, 134, 84), CREAM_HI)
    rect(draw, (99, 84, 116, 86), METAL_DK)

    # Almost full-width timber desk with hard pixel corners and irregular
    # grain. It sits in front of the robot and laptop base.
    rect(draw, (2, 86, 176, 101), BLACK)
    rect(draw, (5, 88, 173, 98), WOOD)
    rect(draw, (8, 89, 64, 91), WOOD_LIGHT)
    rect(draw, (82, 94, 142, 96), WOOD_DARK)
    rect(draw, (149, 89, 169, 91), WOOD_LIGHT)
    rect(draw, (8, 98, 169, 101), COPPER_DK)
    rect(draw, (13, 101, 23, 129), BLACK)
    rect(draw, (16, 101, 21, 126), WOOD_DARK)
    rect(draw, (152, 101, 163, 129), BLACK)
    rect(draw, (155, 101, 160, 126), WOOD_DARK)

    # Seated shins hang below the tabletop. Their upper joints begin behind
    # the desk's front rail, so the surface correctly occludes the knees.
    draw.line((42, 100, 42, 113, 37, 119), fill=BLACK, width=8)
    draw.line((42, 102, 42, 112, 38, 117), fill=CREAM_SH, width=4)
    draw.line((55, 100, 57, 112, 63, 117), fill=BLACK, width=8)
    draw.line((55, 102, 58, 111, 62, 115), fill=CREAM_SH, width=4)
    rect(draw, (32, 116, 45, 123), BLACK)
    rect(draw, (35, 116, 44, 119), CREAM_HI)
    rect(draw, (59, 114, 72, 121), BLACK)
    rect(draw, (60, 114, 69, 117), CREAM_HI)

    # Coffee mug beside the computer: a tiny warm detail that links this
    # workstation back to the marketplace.
    rect(draw, (140, 71, 153, 86), BLACK)
    rect(draw, (143, 73, 150, 83), CREAM_HI)
    rect(draw, (144, 74, 149, 76), WOOD_DARK)
    rect(draw, (151, 75, 156, 81), BLACK)
    rect(draw, (151, 77, 153, 79), CREAM_HI)

    return image


def main() -> None:
    OUTPUT.mkdir(parents=True, exist_ok=True)
    draw_scene().save(OUTPUT / "ph-marketplace-robot.png", optimize=True)


if __name__ == "__main__":
    main()
