#!/usr/bin/env python3
"""Convert heavyweight wwwroot images to WebP siblings for faster page loads.

The site ships ~26 MB of PNGs through a single Container App with no CDN, and
users were reporting multi-second image loads. This script walks the images
directory and writes a `.webp` sibling next to every eligible PNG/JPEG:

- Pixel-art sprites (`pl-*`, `*-pixel*`) under 1024px are encoded LOSSLESS so
  the crisp, nearest-neighbour pixel look survives — lossy encoding visibly
  blurs tiny sprites.
- Large pixel-style art (1254px+ scenes displayed scaled-down) uses lossy
  quality 90: the sharp-edge softening is invisible at display size and the
  files come out 3-5x smaller than lossless.
- Photographic/AI art (origin shots, backgrounds, coins) is encoded lossy
  at quality 82, which is visually transparent for these assets.

Files that already have a `.webp` sibling and tiny icons/logos (< 50 KB) are
skipped. Originals are kept on disk (tools/docs still reference them); Razor
and CSS references are repointed to the `.webp` files separately.

Run from the repository root:

    python3 tools/optimize_images.py
"""

from pathlib import Path

from PIL import Image

IMAGES_DIR = Path("src/ThisCafeteria.Web/wwwroot/images")
MIN_SOURCE_BYTES = 50 * 1024
LOSSY_QUALITY = 82
# Large pixel-style scenes get a high-but-lossy quality: at display size the
# edge softening is invisible and the savings over lossless are 3-5x.
PIXEL_ART_QUALITY = 90
PIXEL_ART_LOSSLESS_MAX_DIMENSION = 1024


def is_pixel_art(path: Path) -> bool:
    name = path.stem.lower()
    return name.startswith("pl-") or "pixel" in name


def convert(path: Path) -> tuple[int, int, str] | None:
    target = path.with_suffix(".webp")
    if target.exists():
        return None
    source_bytes = path.stat().st_size
    if source_bytes < MIN_SOURCE_BYTES:
        return None

    with Image.open(path) as image:
        if is_pixel_art(path) and max(image.size) <= PIXEL_ART_LOSSLESS_MAX_DIMENSION:
            image.save(target, "WEBP", lossless=True, quality=100, method=6)
            mode = "lossless"
        elif is_pixel_art(path):
            image.save(target, "WEBP", quality=PIXEL_ART_QUALITY, method=6)
            mode = f"q{PIXEL_ART_QUALITY}"
        else:
            image.save(target, "WEBP", quality=LOSSY_QUALITY, method=6)
            mode = f"q{LOSSY_QUALITY}"

    return source_bytes, target.stat().st_size, mode


def main() -> None:
    total_before = 0
    total_after = 0
    converted = 0

    for path in sorted(IMAGES_DIR.iterdir()):
        if path.suffix.lower() not in {".png", ".jpg", ".jpeg"}:
            continue
        result = convert(path)
        if result is None:
            continue
        before, after, mode = result
        total_before += before
        total_after += after
        converted += 1
        print(f"{path.name}: {before / 1024:.0f} KB -> {after / 1024:.0f} KB ({mode})")

    print(
        f"\n{converted} files converted, "
        f"{total_before / 1024 / 1024:.1f} MB -> {total_after / 1024 / 1024:.1f} MB "
        f"({100 - total_after * 100 // max(total_before, 1)}% smaller)"
    )


if __name__ == "__main__":
    main()
