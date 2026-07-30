#!/usr/bin/env python3
"""Generate the Sonic-style shoes the pixel crew wears while caffeinated.

The normal and flipped transparent sprites show two overlapping side-view
power sneakers on a 29x12 grid, scaled 3x to match the aliased sprite style of
generate_procurement_sprites.py. Both toes point in the robot's travel
direction, just like a side-view platform-game sprite; the flipped asset keeps
that direction correct for left-facing crew members.

The deliberately chunky silhouette follows the readable parts of the visual
reference: tall white-and-blue ankle cuffs, a bright red body, diagonal white
instep straps, a small gold clasp, and a thick white sole above a dark tread.
The rear shoe is painted first and the front shoe overlaps it by seven cells,
which makes the pair feel like running shoes instead of two tiny red boots.

The crew gets the shoes only while a coffee-bag boost is running: the runtime
already toggles .ph-scene__roam.is-boosted for exactly that window, and
GlobalScene.razor.css maps it to the overlay's visibility. See
docs/pixel-crew-training.md for the mechanic itself.
"""

from pathlib import Path

from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "src" / "ThisCafeteria.Web" / "wwwroot" / "images"

SCALE = 3          # 29x12 conceptual grid -> 87x36 canvas

# Palette: power-sneaker red plus the site's shared dark outline and a cool
# blue cuff shadow borrowed from the crew's face screens.
OUTL = (43, 27, 16, 255)      # same outline as eth-pixel.png and the badges
RED = (234, 42, 40, 255)      # the classic power-sneaker red
RED_DK = (152, 24, 26, 255)   # shaded sole row
WHITE = (250, 248, 244, 255)  # cuff and strap
BLUE = (70, 111, 169, 255)    # cool shadow inside the white ankle cuff
GOLD = (242, 183, 63, 255)    # tiny strap clasp

# One right-facing shoe. The ankle sits over columns 6–13 while the toe reaches
# column 17, giving it the long, low profile of the reference.
SHOE = [
    "......WWWWWWWW....",
    ".....OWWWWWWWWO...",
    ".....OWBBBBBBWO...",
    ".....OOWWWWWWOO...",
    "....ORRRRRRRRRO...",
    "...ORRWWGRRRRO....",
    "..ORRRWWRRRRRRRO..",
    ".ORRRRRRRRRRRRRRO.",
    "ORRRRRRRRRRRRRRRRO",
    "OWWWWWWWWWWWWWWWWO",
    ".ODDDDDDDDDDDDDDO.",
]

SHOE_W = len(SHOE[0])
SHOE_H = len(SHOE)
OVERLAP = 7
FRONT_X = SHOE_W - OVERLAP
GRID_W = SHOE_W + FRONT_X
GRID_H = SHOE_H + 1

PALETTE = {
    "O": OUTL,
    "W": WHITE,
    "R": RED,
    "D": RED_DK,
    "B": BLUE,
    "G": GOLD,
}


def cell(draw: ImageDraw.ImageDraw, cx: int, cy: int, fill) -> None:
    x0, y0 = cx * SCALE, cy * SCALE
    draw.rectangle((x0, y0, x0 + SCALE - 1, y0 + SCALE - 1), fill=fill)


def plot(
    draw: ImageDraw.ImageDraw,
    rows: list[str],
    offset_x: int,
    offset_y: int = 0,
) -> None:
    for y, row in enumerate(rows):
        for x, ch in enumerate(row):
            if ch != ".":
                cell(draw, offset_x + x, offset_y + y, PALETTE[ch])


def main() -> None:
    img = Image.new("RGBA", (GRID_W * SCALE, GRID_H * SCALE), (0, 0, 0, 0))
    draw = ImageDraw.Draw(img)

    # The rear foot is one pixel higher; the front foot then occludes its toe
    # and creates the compact overlap visible in side-view running sprites.
    plot(draw, SHOE, 0)
    plot(draw, SHOE, FRONT_X, 1)

    normal_path = OUTPUT / "pl-sonic-shoes.png"
    flipped_path = OUTPUT / "pl-sonic-shoes-flip.png"
    img.save(normal_path)
    img.transpose(Image.Transpose.FLIP_LEFT_RIGHT).save(flipped_path)
    print("wrote pl-sonic-shoes.png and pl-sonic-shoes-flip.png to", OUTPUT)


if __name__ == "__main__":
    main()
