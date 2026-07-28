"""Generate the black "staked token" pixel coin for the /staking hero.

Follows the disc/rim/sheen recipe from docs/kimi_ui.md section 3.1, rendered as
dark lead-grey metal: no pure-black contours, stepped multi-tone shading on the
face for weight, and micro light reflections on the raised top rim. The coffee
bean glyph is carved dark so the Razor layer can overlay a colored, glowing
bean mask that cycles Solana -> BSC -> Ethereum colors. Run from the repo root:

    python3 tools/generate_staked_coin.py

Outputs (both in src/ThisCafeteria.Web/wwwroot/images/):
    staked-coin-pixel.png  32x32 coin, bean carved dark for the overlay
    staked-coin-bean.png   32x32 white bean mask (S-crease cut transparent)
"""

from pathlib import Path

from PIL import Image, ImageDraw

LEAD = (46, 42, 39)  # outer contour: very dark lead, not pure black
METAL_DK = (30, 26, 23)  # deepest shadow steps on the face
METAL = (54, 48, 43)  # base dark metal face
METAL_MID = (70, 62, 54)  # raised inner field tone
METAL_HI = (96, 84, 68)  # stepped highlight / rim band
GLINT = (168, 156, 136)  # micro light reflections on the raised rim
DEEP = (16, 13, 11)  # carved bean recess (kept near-black for the overlay)

BEAN_BOUNDS = (12, 10, 20, 22)
BEAN_CREASE = [(16, 11), (15, 14), (16, 17), (15, 20)]

OUTPUT_DIR = Path("src/ThisCafeteria.Web/wwwroot/images")


def draw_coin() -> Image.Image:
    img = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    # Outer disc: lead contour instead of pure black, so the silhouette keeps
    # its definition but reads as the same metal as the face.
    d.ellipse((1, 1, 30, 30), fill=LEAD)
    d.ellipse((3, 3, 28, 28), fill=METAL)

    # Raised rim ring: lead outer line, bright metal band inside it.
    d.ellipse((4, 4, 27, 27), outline=LEAD, width=1)
    d.ellipse((5, 5, 26, 26), outline=METAL_HI, width=1)

    # Weight shading on the field: stepped tone bands, never gradients —
    # a slightly raised inner field, then stepped highlight and shadow arcs.
    d.ellipse((7, 7, 24, 24), fill=METAL_MID)
    d.arc((7, 7, 24, 24), 150, 260, fill=METAL_HI, width=2)
    d.arc((8, 8, 23, 23), 160, 250, fill=METAL_HI, width=1)
    d.arc((7, 7, 24, 24), -30, 80, fill=METAL_DK, width=2)
    d.arc((8, 8, 23, 23), -20, 70, fill=METAL_DK, width=1)
    # Extra bottom weight: a darker crescent pinned low on the face.
    d.arc((9, 9, 22, 22), 35, 100, fill=METAL_DK, width=1)

    # Micro light reflections: a thin glint resting on the raised top rim and
    # two single-pixel catches below it. Subtle — specular, not sparkle.
    d.arc((5, 5, 26, 26), 215, 285, fill=GLINT, width=1)
    d.point((9, 8), fill=GLINT)
    d.point((10, 7), fill=GLINT)

    # Center bean, carved dark: the colored overlay mask sits on top of this,
    # so an un-highlighted bean still reads as a brand recess in the coin.
    d.ellipse(BEAN_BOUNDS, fill=DEEP, outline=METAL_HI, width=1)
    d.line(BEAN_CREASE, fill=METAL_HI, width=1)

    return img


def draw_bean_mask() -> Image.Image:
    img = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.ellipse(BEAN_BOUNDS, fill=(255, 255, 255, 255))
    # The S-crease stays transparent so the coin's dark recess shadows through.
    d.line(BEAN_CREASE, fill=(0, 0, 0, 0), width=1)
    return img


def save(img: Image.Image, name: str) -> Path:
    path = OUTPUT_DIR / name
    img.save(path)
    print(f"wrote {path} ({img.size[0]}x{img.size[1]})")
    return path


def main() -> None:
    coin = draw_coin()
    mask = draw_bean_mask()

    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
    save(coin, "staked-coin-pixel.png")
    save(mask, "staked-coin-bean.png")

    # Contact-sheet preview for eyeballing (3x, mission dark ground, mask shown
    # as the lavender Ethereum tint so the composited look is visible).
    preview = Image.new("RGBA", (192, 96), (16, 13, 11, 255))
    coin_up = coin.resize((96, 96), resample=Image.NEAREST)
    tint = Image.new("RGBA", (32, 32), (98, 126, 234, 255))
    tinted = coin.copy()
    tinted.paste(tint, (0, 0), mask)
    tinted_up = tinted.resize((96, 96), resample=Image.NEAREST)
    preview.paste(coin_up, (0, 0), coin_up)
    preview.paste(tinted_up, (96, 0), tinted_up)
    preview_path = Path("scratch/staked-coin-preview.png")
    preview.save(preview_path)
    print(f"wrote {preview_path}")


if __name__ == "__main__":
    main()
