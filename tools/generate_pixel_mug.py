#!/usr/bin/env python3
"""Generate the saucer-free pixel coffee mug used by the homepage crew.

The 24x24 source stays intentionally tiny and aliased. CSS scales it with
nearest-neighbour rendering, so every drawn source pixel remains visible in
the hero. Run from the repository root:

    python3 tools/generate_pixel_mug.py

Output:
    src/ThisCafeteria.Web/wwwroot/images/pl-mug-coffee.png
"""

from pathlib import Path

from PIL import Image, ImageDraw


OUTPUT = Path("src/ThisCafeteria.Web/wwwroot/images/pl-mug-coffee.png")
SIZE = 24

INK = (17, 22, 39, 255)
CERAMIC = (232, 228, 207, 255)
CERAMIC_HI = (255, 252, 239, 255)
CERAMIC_SH = (194, 190, 169, 255)
COFFEE = (72, 39, 20, 255)
COFFEE_HI = (112, 65, 30, 255)
STEAM = (210, 192, 184, 210)
CLEAR = (0, 0, 0, 0)


def rect(draw: ImageDraw.ImageDraw, box: tuple[int, int, int, int], fill) -> None:
    draw.rectangle(box, fill=fill)


def draw_mug() -> Image.Image:
    image = Image.new("RGBA", (SIZE, SIZE), CLEAR)
    draw = ImageDraw.Draw(image)

    # Two sparse, stepped steam trails. They remain detached from the rim so
    # the cup reads cleanly when enlarged against the dark space backdrop.
    rect(draw, (7, 1, 8, 2), STEAM)
    rect(draw, (8, 3, 9, 4), STEAM)
    rect(draw, (7, 5, 8, 6), STEAM)
    rect(draw, (14, 0, 15, 1), STEAM)
    rect(draw, (15, 2, 16, 4), STEAM)
    rect(draw, (14, 5, 15, 6), STEAM)

    # Square handle, drawn behind the cup. The transparent center is the only
    # negative-space cutout; there is deliberately no saucer underneath.
    rect(draw, (16, 10, 22, 17), INK)
    rect(draw, (17, 11, 20, 16), CERAMIC_SH)
    rect(draw, (18, 12, 20, 15), CLEAR)

    # Stepped mug silhouette with clipped lower corners.
    rect(draw, (3, 7, 17, 8), INK)
    rect(draw, (2, 9, 18, 18), INK)
    rect(draw, (3, 18, 17, 20), INK)

    # Warm cream ceramic body and small side shadows.
    rect(draw, (4, 9, 16, 17), CERAMIC)
    rect(draw, (4, 11, 15, 16), CERAMIC_HI)
    rect(draw, (5, 17, 15, 18), CERAMIC)
    rect(draw, (3, 11, 4, 16), CERAMIC_SH)
    rect(draw, (16, 10, 17, 17), CERAMIC_SH)

    # Flat coffee surface with a single lighter roast band at the back.
    rect(draw, (4, 9, 16, 11), COFFEE)
    rect(draw, (5, 9, 15, 9), COFFEE_HI)
    rect(draw, (5, 11, 15, 11), (58, 31, 18, 255))

    # A bright ceramic glint and the stepped base complete the cup itself.
    rect(draw, (4, 12, 5, 15), CERAMIC_HI)
    rect(draw, (5, 19, 15, 19), CERAMIC_SH)

    return image


def main() -> None:
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    draw_mug().save(OUTPUT, optimize=True)
    print(f"wrote {OUTPUT} ({SIZE}x{SIZE})")


if __name__ == "__main__":
    main()
