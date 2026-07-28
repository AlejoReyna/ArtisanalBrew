#!/usr/bin/env python3
"""Generate the pixel chain badges drifting in the PixelHome hero background.

Three transparent 32x32 PNGs, hand-plotted on a 16x16 grid scaled 2x, matching
the aliased sprite style of generate_procurement_sprites.py:

- pl-chain-ethereum.png — the octahedron glyph: two stacked triangles with a
  gap at the belt, four facets alternating light/dark like the real mark.
- pl-chain-solana.png — the three slanted stripes, purple -> green, one
  uniform tone per stripe with a shaded bottom edge.
- pl-chain-bnb.png — the pinwheel of five diamonds with a pale top-left
  highlight, like the two-tone shading on the site's other sprites.

(images/eth-pixel.png sounds like the rhombus but is actually a round gold
coin sprite — it is NOT used for this badge.)
"""

from pathlib import Path

from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "src" / "ThisCafeteria.Web" / "wwwroot" / "images"

SCALE = 2          # 16x16 conceptual grid -> 32x32 canvas
GRID = 16

# Palette: brand colour, its shaded edge, and a pale highlight.
OUTL = (43, 27, 16, 255)        # shared dark outline, as on eth-pixel.png
SOL_TOP = (153, 69, 255, 255)   # purple end of the Solana gradient
SOL_TOP_DK = (96, 40, 168, 255)
SOL_MID = (45, 186, 200, 255)   # teal blend
SOL_MID_DK = (24, 120, 132, 255)
SOL_BOT = (20, 241, 149, 255)   # green end
SOL_BOT_DK = (12, 158, 98, 255)
BNB = (243, 186, 47, 255)
BNB_DK = (176, 128, 22, 255)
BNB_HI = (255, 224, 138, 255)
ETH_FACET_HI = (127, 153, 242, 255)   # light facet, periwinkle
ETH_FACET_DK = (58, 77, 171, 255)     # dark facet


def ethereum() -> None:
    """Octahedron: two stacked triangles with a one-cell gap at the belt.

    The upper pyramid's left facet is light and its right facet dark; the
    lower pyramid flips them, which is what makes the glyph read as folded
    geometry instead of a plain diamond.
    """
    img = Image.new("RGBA", (GRID * SCALE, GRID * SCALE), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    center = 7                      # grid is 0..15, the glyph axis sits on 7

    for y in range(1, 8):           # upper pyramid, tip at row 1, belt row 7
        w = round((y - 1) * 6 / 7 + 0.5)
        for x in range(center - w, center + w + 1):
            cell(d, x, y, ETH_FACET_HI if x <= center else ETH_FACET_DK)

    for y in range(9, 15):          # lower pyramid, belt row 9, tip at row 14
        w = round((14 - y) * 6 / 7 + 0.5)
        for x in range(center - w, center + w + 1):
            cell(d, x, y, ETH_FACET_DK if x <= center else ETH_FACET_HI)

    img.save(OUTPUT / "pl-chain-ethereum.png")


def cell(draw: ImageDraw.ImageDraw, cx: int, cy: int, fill) -> None:
    x0, y0 = cx * SCALE, cy * SCALE
    draw.rectangle((x0, y0, x0 + SCALE - 1, y0 + SCALE - 1), fill=fill)


def solana() -> None:
    img = Image.new("RGBA", (GRID * SCALE, GRID * SCALE), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    # Three parallelogram stripes slanting up to the right. Each stripe is
    # three cells tall; the run shifts one cell right per row inside the
    # stripe, and the stepped ends echo the rounded caps of the real mark.
    stripes = [
        (2, SOL_TOP, SOL_TOP_DK),
        (7, SOL_MID, SOL_MID_DK),
        (12, SOL_BOT, SOL_BOT_DK),
    ]
    for top, tone, shade in stripes:
        for row in range(3):
            y = top + row
            # Slant: the stripe starts further right on lower rows.
            x_start = 4 + row
            x_end = 11 + row
            for x in range(x_start, x_end + 1):
                # Bottom row of each stripe gets the shaded tone, the two
                # upper rows the main tone — a cheap bottom-edge shade.
                cell(d, x, y, shade if row == 2 else tone)
        # Stepped end caps.
        cell(d, 3, top + 1, tone)
        cell(d, 12 + 2, top + 1, tone)

    img.save(OUTPUT / "pl-chain-solana.png")


def bnb() -> None:
    img = Image.new("RGBA", (GRID * SCALE, GRID * SCALE), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    def diamond(cx: int, cy: int, radius: int) -> None:
        """Solid pixel diamond: |dx| + |dy| <= radius."""
        for dy in range(-radius, radius + 1):
            width = radius - abs(dy)
            for dx in range(-width, width + 1):
                x, y = cx + dx, cy + dy
                # Highlight the upper-left facet, shade the lower-right one.
                if dx + dy < -radius // 2:
                    cell(d, x, y, BNB_HI)
                elif dx + dy > radius // 2:
                    cell(d, x, y, BNB_DK)
                else:
                    cell(d, x, y, BNB)

    # Center diamond plus the four corner diamonds of the pinwheel, spaced so
    # a one-cell gutter separates the pieces.
    diamond(8, 8, 2)
    diamond(3, 3, 1)
    diamond(13, 3, 1)
    diamond(3, 13, 1)
    diamond(13, 13, 1)

    img.save(OUTPUT / "pl-chain-bnb.png")


if __name__ == "__main__":
    ethereum()
    solana()
    bnb()
    print("wrote pl-chain-{ethereum,solana,bnb}.png to", OUTPUT)
