"""Generate the six pixel-art coffee-bag sprites for the / hero's backdrop.

One bag per product on /products. Each sprite is a pixel art REDRAW of that
product's own catalog art: the source illustration (fetched from the live
catalog into scratch/bag-sources/) is keyed off its white studio backdrop by
border flood-fill, trimmed to the silhouette, dropped onto a tiny pixel grid,
and quantized to a small palette with no dithering. The quantization plus the
hero's `image-rendering: pixelated` upscaling is what turns the painted art
into pixel art; keeping every bag on its own adaptive palette (and its own
silhouette — colombia/ethiopia tall, mexico wide, peru tallest) is what keeps
them recognisably the six different products. Run from the repo root:

    python3 tools/generate_pixel_bags.py

Outputs (in src/ThisCafeteria.Web/wwwroot/images/):
    pl-bag-{colombia,costa-rica,ethiopia,guatemala,mexico,peru}.png
"""

from collections import deque
from pathlib import Path

from PIL import Image

OUTPUT_DIR = Path("src/ThisCafeteria.Web/wwwroot/images")
SOURCE_DIR = Path("scratch/bag-sources")

PRODUCTS = ("colombia", "costa-rica", "ethiopia", "guatemala", "mexico", "peru")

WHITE_WALL = 242  # channels at/above this count as studio backdrop
GRID_WIDTH = 20  # pixel-art grid width; per-product height follows the art
PALETTE = 10  # colors per bag after quantization, dither off


def key_out_background(img: Image.Image) -> Image.Image:
    """Flood-fill transparency from the borders through near-white pixels,
    so beige label areas inside the bag survive."""
    img = img.convert("RGBA")
    w, h = img.size
    px = img.load()
    seen = [[False] * w for _ in range(h)]
    queue: deque[tuple[int, int]] = deque()

    def is_wall(x: int, y: int) -> bool:
        r, g, b, a = px[x, y]
        return r >= WHITE_WALL and g >= WHITE_WALL and b >= WHITE_WALL

    for x in range(w):
        for y in (0, h - 1):
            if is_wall(x, y) and not seen[y][x]:
                queue.append((x, y))
                seen[y][x] = True
    for y in range(h):
        for x in (0, w - 1):
            if is_wall(x, y) and not seen[y][x]:
                queue.append((x, y))
                seen[y][x] = True

    while queue:
        x, y = queue.popleft()
        px[x, y] = (0, 0, 0, 0)
        for nx, ny in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)):
            if 0 <= nx < w and 0 <= ny < h and not seen[ny][nx] and is_wall(nx, ny):
                seen[ny][nx] = True
                queue.append((nx, ny))
    return img


def pixelate(source: Path) -> tuple[Path, tuple[int, int]]:
    img = key_out_background(Image.open(source))
    bbox = img.getbbox()
    if bbox is None:
        raise RuntimeError(f"{source} keyed out to nothing — check WHITE_WALL")
    bag = img.crop(bbox)

    height = round(GRID_WIDTH * bag.size[1] / bag.size[0])
    small = bag.resize((GRID_WIDTH, height), Image.LANCZOS)

    # Adaptive small palette per bag, dither off: this is the "redraw" step —
    # soft brushwork collapses into hard pixel steps, and each product keeps
    # its own band/colors because the palette is median-cut from its own art.
    alpha = small.getchannel("A")
    quantized = small.convert("RGB").quantize(colors=PALETTE, dither=Image.Dither.NONE)
    sprite = quantized.convert("RGBA")
    sprite.putalpha(alpha)

    out_path = OUTPUT_DIR / f"pl-bag-{source.stem}.png"
    sprite.save(out_path, optimize=True)
    return out_path, sprite.size


def main() -> None:
    if not SOURCE_DIR.is_dir():
        raise SystemExit(
            f"missing {SOURCE_DIR} — fetch the six bag arts from the live "
            "catalog (see module docstring) before running this"
        )
    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)

    for name in PRODUCTS:
        out, size = pixelate(SOURCE_DIR / f"{name}.png")
        print(f"wrote {out} ({size[0]}x{size[1]})")

    # Contact sheet: all six scaled up on the mission dark ground.
    scale = 3
    cell = 96
    sheet = Image.new("RGBA", (cell * len(PRODUCTS), cell), (16, 13, 11, 255))
    for i, name in enumerate(PRODUCTS):
        s = Image.open(OUTPUT_DIR / f"pl-bag-{name}.png")
        up = s.resize((s.size[0] * scale, s.size[1] * scale), Image.NEAREST)
        sheet.alpha_composite(up, (cell * i + (cell - up.size[0]) // 2, (cell - up.size[1]) // 2))
    preview = Path("scratch/pixel-bags-preview.png")
    sheet.save(preview)
    print(f"wrote {preview}")


if __name__ == "__main__":
    main()
