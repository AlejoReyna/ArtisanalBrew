#!/usr/bin/env python3
"""Generate the pixel chain marks drifting in the PixelHome hero background.

The source art is drawn on a 24x24 pixel grid and nearest-neighbour scaled to
48x48. That leaves enough room for each logo's characteristic negative space
while keeping the deliberately chunky language used by the rest of the scene:

- pl-chain-ethereum.png — a split neon crystal with four upper facets and a
  detached lower chevron.
- pl-chain-solana.png — three wide, alternating parallelograms carrying one
  continuous violet-to-mint gradient.
- pl-chain-bnb.png — the cube-and-ring construction, with distinct top, side
  and lower pieces instead of a generic five-diamond cross.

(images/eth-pixel.png sounds like the rhombus but is actually a round gold
coin sprite — it is NOT used for this badge.)
"""

from pathlib import Path

from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "src" / "ThisCafeteria.Web" / "wwwroot" / "images"

SCALE = 2
GRID = 24

TRANSPARENT = (0, 0, 0, 0)

SOL_PURPLE = (153, 69, 255)
SOL_GREEN = (20, 241, 149)

BNB_HI = (255, 210, 66, 255)
BNB = (247, 178, 20, 255)
BNB_MID = (219, 137, 9, 255)
BNB_DK = (167, 93, 5, 255)

ETH_BLUE = (76, 138, 226, 255)  # #4c8ae2


def canvas() -> tuple[Image.Image, ImageDraw.ImageDraw]:
    img = Image.new("RGBA", (GRID, GRID), TRANSPARENT)
    return img, ImageDraw.Draw(img)


def save_scaled(img: Image.Image, name: str) -> None:
    img.resize(
        (GRID * SCALE, GRID * SCALE),
        resample=Image.Resampling.NEAREST,
    ).save(OUTPUT / name)


def ethereum() -> None:
    img, d = canvas()

    # Four planes meet off-centre, which gives the upper crystal the folded,
    # asymmetric depth visible in the reference instead of reading as a flat
    # diamond.
    d.polygon([(12, 0), (12, 10), (5, 14)], fill=ETH_BLUE)
    d.polygon([(12, 0), (19, 14), (12, 10)], fill=ETH_BLUE)
    d.polygon([(5, 14), (12, 10), (12, 16)], fill=ETH_BLUE)
    d.polygon([(19, 14), (12, 10), (12, 16)], fill=ETH_BLUE)

    # The detached chevron is intentionally separated by a transparent notch.
    # It keeps the mark airy and still readable when it drifts behind copy.
    d.polygon([(5, 17), (12, 20), (12, 23)], fill=ETH_BLUE)
    d.polygon([(19, 17), (12, 20), (12, 23)], fill=ETH_BLUE)

    save_scaled(img, "pl-chain-ethereum.png")


def gradient_bar(
    img: Image.Image,
    polygon: list[tuple[int, int]],
    shadow_edge: list[tuple[int, int]],
) -> None:
    mask = Image.new("L", (GRID, GRID), 0)
    md = ImageDraw.Draw(mask)
    md.polygon(polygon, fill=255)

    pixels = img.load()
    mask_pixels = mask.load()
    for y in range(GRID):
        for x in range(GRID):
            if not mask_pixels[x, y]:
                continue
            t = x / (GRID - 1)
            pixels[x, y] = (
                round(SOL_PURPLE[0] + (SOL_GREEN[0] - SOL_PURPLE[0]) * t),
                round(SOL_PURPLE[1] + (SOL_GREEN[1] - SOL_PURPLE[1]) * t),
                round(SOL_PURPLE[2] + (SOL_GREEN[2] - SOL_PURPLE[2]) * t),
                255,
            )

    # One hard lower edge adds the same tiny bevel used by the bags and coins.
    d = ImageDraw.Draw(img)
    d.line(shadow_edge, fill=(37, 119, 129, 255), width=1)


def solana() -> None:
    img, _ = canvas()

    # The centre bar reverses direction. That alternating silhouette is a key
    # part of the Solana mark and was missing from the previous sprite.
    gradient_bar(img, [(5, 2), (23, 2), (19, 6), (1, 6)], [(1, 6), (19, 6)])
    gradient_bar(img, [(1, 10), (19, 10), (23, 14), (5, 14)], [(5, 14), (23, 14)])
    gradient_bar(img, [(5, 18), (23, 18), (19, 22), (1, 22)], [(1, 22), (19, 22)])

    save_scaled(img, "pl-chain-solana.png")


def bnb() -> None:
    img, d = canvas()

    # Broken outer ring: a bright roof, two side brackets and a bottom cap.
    d.polygon([(12, 0), (18, 3), (16, 5), (12, 3), (8, 5), (6, 3)], fill=BNB_HI)
    d.polygon([(3, 4), (7, 6), (5, 8), (3, 7), (2, 8), (2, 12), (1, 10), (1, 6)], fill=BNB)
    d.polygon([(21, 4), (17, 6), (19, 8), (21, 7), (22, 8), (22, 12), (23, 10), (23, 6)], fill=BNB_MID)
    d.polygon([(1, 13), (3, 15), (3, 18), (8, 21), (8, 23), (1, 19)], fill=BNB)
    d.polygon([(23, 13), (21, 15), (21, 18), (16, 21), (16, 23), (23, 19)], fill=BNB_MID)
    d.polygon([(12, 20), (15, 22), (12, 23), (9, 22)], fill=BNB_DK)

    # Inner cube: three clearly different faces preserve its volume at 32px.
    d.polygon([(12, 6), (17, 9), (12, 12), (7, 9)], fill=BNB_HI)
    d.polygon([(7, 9), (12, 12), (12, 20), (7, 17)], fill=BNB)
    d.polygon([(17, 9), (12, 12), (12, 20), (17, 17)], fill=BNB_MID)

    # Two tiny cut-outs keep the centre from becoming one solid yellow blob.
    d.polygon([(5, 11), (7, 12), (7, 16), (5, 15)], fill=TRANSPARENT)
    d.polygon([(19, 11), (17, 12), (17, 16), (19, 15)], fill=TRANSPARENT)

    save_scaled(img, "pl-chain-bnb.png")


if __name__ == "__main__":
    ethereum()
    solana()
    bnb()
    print("wrote pl-chain-{ethereum,solana,bnb}.png to", OUTPUT)
