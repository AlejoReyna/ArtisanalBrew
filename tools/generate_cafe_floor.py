#!/usr/bin/env python3
"""Generate the isometric café room and its five station props for /about.

Same rules as the rest of the sprite generators here: tiny sources, no
anti-aliasing, drawn from the --stake-* roast palette so the art matches the
dark roast wing's tokens. About.razor.css scales the room 4x with
nearest-neighbour rendering, so every source pixel below stays visible.

The room is a 16x16 diamond of 16x8 iso tiles with two extruded back walls.
The props are anchored bottom-centre and positioned by About.razor at the
screen coordinates this file's ISO_TILES table records — if a station moves,
move it in both places.

Run from the repository root:

    python3 tools/generate_cafe_floor.py

Output:
    src/ThisCafeteria.Web/wwwroot/images/pl-cafe-room.png
    src/ThisCafeteria.Web/wwwroot/images/pl-cafe-bar.png
    src/ThisCafeteria.Web/wwwroot/images/pl-cafe-roaster.png
    src/ThisCafeteria.Web/wwwroot/images/pl-cafe-till.png
    src/ThisCafeteria.Web/wwwroot/images/pl-cafe-vault.png
    src/ThisCafeteria.Web/wwwroot/images/pl-cafe-lab.png
    src/ThisCafeteria.Web/wwwroot/images/pl-cafe-node.png
"""

from pathlib import Path

from PIL import Image, ImageDraw


IMAGES = Path("src/ThisCafeteria.Web/wwwroot/images")

# Roast palette, lifted from the --stake-* block in wwwroot/app.css.
CLEAR = (0, 0, 0, 0)
BG_DEEP = (16, 13, 11, 255)
SURFACE = (31, 25, 21, 255)
SURFACE_2 = (42, 33, 27, 255)
INK = (244, 239, 230, 255)
GREEN = (143, 185, 155, 255)
COPPER = (200, 155, 106, 255)
AMBER = (229, 192, 120, 255)
CLAY = (229, 152, 128, 255)

# Room-only shades: the two floor checks, the two wall faces (left catches the
# light, right falls away) and the hard outline everything is drawn against.
FLOOR_A = (44, 35, 29, 255)
FLOOR_B = (35, 27, 22, 255)
WALL_L = (36, 29, 24, 255)
WALL_R = (26, 21, 17, 255)
OUTLINE = (58, 46, 38, 255)

# Prop shading. The floor is deliberately near-black, so a prop drawn in the
# surface tokens alone dissolves into it — these three run a step lighter and
# carry the espresso keyline the rest of the sprite set uses.
PROP_LIT = (72, 57, 46, 255)
PROP_BASE = (52, 41, 34, 255)
PROP_DARK = (30, 24, 20, 255)
CONTACT = (0, 0, 0, 70)

# Iso geometry. Tile (i, j) runs down-right as i grows and down-left as j
# grows; the tile's bounding box anchor is (OX + (i-j)*8, OY + (i+j)*4).
ROOM_W, ROOM_H = 272, 200
TW, TH = 16, 8
COLS = ROWS = 16
OX, OY = 128, 64
WALL_H = 48

# Where each station stands, in tile coordinates. The screen point is the
# centre of the tile's floor diamond; About.razor turns these into the
# percentage offsets that place each prop's base.
ISO_TILES = {
    "bar": (3, 3),
    "node": (8, 1),
    "roaster": (1, 8),
    "lab": (6, 14),
    "vault": (13, 8),
    "till": (11, 11),
}


def tile_anchor(i: float, j: float) -> tuple[float, float]:
    return OX + (i - j) * (TW / 2), OY + (i + j) * (TH / 2)


def tile_centre(i: float, j: float) -> tuple[float, float]:
    ax, ay = tile_anchor(i, j)
    return ax + TW / 2, ay + TH / 2


def tile_diamond(i: int, j: int) -> list[tuple[float, float]]:
    ax, ay = tile_anchor(i, j)
    return [(ax + 8, ay), (ax + 16, ay + 4), (ax + 8, ay + 8), (ax, ay + 4)]


def rect(draw: ImageDraw.ImageDraw, box: tuple[int, int, int, int], fill) -> None:
    draw.rectangle(box, fill=fill)


def outlined(draw: ImageDraw.ImageDraw, box: tuple[int, int, int, int], fill) -> None:
    """Solid block with the 1px espresso keyline every prop is drawn against."""
    x0, y0, x1, y1 = box
    draw.rectangle((x0 - 1, y0 - 1, x1 + 1, y1 + 1), fill=BG_DEEP)
    draw.rectangle(box, fill=fill)


def contact_shadow(draw: ImageDraw.ImageDraw, cx: int, base_y: int, half_w: int) -> None:
    """Flat iso diamond under a prop so it sits on the floor instead of
    hovering over it. Semi-transparent, so it darkens whichever check it
    lands on rather than punching a hole in the tiling."""
    draw.polygon(
        [
            (cx - half_w, base_y),
            (cx, base_y - half_w // 2),
            (cx + half_w, base_y),
            (cx, base_y + half_w // 2),
        ],
        fill=CONTACT,
    )


def iso_box(
    draw: ImageDraw.ImageDraw,
    cx: int,
    top_y: int,
    half_w: int,
    depth: int,
    rim,
) -> None:
    """A slab seen in iso: lit top diamond, two side faces, coloured rim."""
    half_h = half_w // 2
    top = [
        (cx - half_w, top_y + half_h),
        (cx, top_y),
        (cx + half_w, top_y + half_h),
        (cx, top_y + half_w),
    ]
    draw.polygon(
        [top[0], top[3], (cx, top_y + half_w + depth), (cx - half_w, top_y + half_h + depth)],
        fill=PROP_BASE,
    )
    draw.polygon(
        [top[2], top[3], (cx, top_y + half_w + depth), (cx + half_w, top_y + half_h + depth)],
        fill=PROP_DARK,
    )
    draw.polygon(top, fill=PROP_LIT)
    draw.line(top + [top[0]], fill=rim)


def wall_point(t: float, side: int, lift: float = 0.0) -> tuple[float, float]:
    """A point on a back wall. side=+1 is the right wall, -1 the left one;
    t runs 0 (the room's top corner) to 1 (that wall's floor corner); lift is
    how far up the wall face the point sits."""
    top_x, top_y = OX + 8, OY
    return (top_x + side * COLS * (TW / 2) * t, top_y + COLS * (TH / 2) * t - lift)


def draw_room() -> Image.Image:
    image = Image.new("RGBA", (ROOM_W, ROOM_H), CLEAR)
    draw = ImageDraw.Draw(image)

    # ── Back walls, drawn before the floor so the floor's top row of tiles
    # covers the seam where the two meet. ──
    for side, face in ((-1, WALL_L), (1, WALL_R)):
        draw.polygon(
            [
                wall_point(0, side),
                wall_point(1, side),
                wall_point(1, side, WALL_H),
                wall_point(0, side, WALL_H),
            ],
            fill=face,
        )
        # Lit cap along the top edge, and the skirting where wall meets floor.
        draw.line([wall_point(0, side, WALL_H), wall_point(1, side, WALL_H)], fill=OUTLINE)
        draw.line([wall_point(0, side, 1), wall_point(1, side, 1)], fill=OUTLINE)

    # ── Left wall: the chalk menu board, slanted onto the wall plane. ──
    board = [
        wall_point(0.07, -1, 36),
        wall_point(0.35, -1, 36),
        wall_point(0.35, -1, 16),
        wall_point(0.07, -1, 16),
    ]
    draw.polygon(board, fill=BG_DEEP)
    draw.line(board + [board[0]], fill=COPPER)
    for line_no, (start, end, width, colour) in enumerate(
        (
            (0.11, 0.31, 3, AMBER),
            (0.11, 0.27, 2, INK),
            (0.11, 0.29, 2, INK),
            (0.11, 0.23, 2, GREEN),
        )
    ):
        lift = 31 - line_no * 4
        draw.line(
            [wall_point(start, -1, lift), wall_point(end, -1, lift)],
            fill=colour,
            width=width,
        )

    # ── Right wall: a shelf of bean bags, and a window onto the pixel space
    # the rest of the site floats in. ──
    shelf = [
        wall_point(0.14, 1, 22),
        wall_point(0.66, 1, 22),
        wall_point(0.66, 1, 19),
        wall_point(0.14, 1, 19),
    ]
    draw.polygon(shelf, fill=COPPER)
    for offset, colour in ((0.20, GREEN), (0.34, COPPER), (0.48, CLAY)):
        bx, by = wall_point(offset, 1, 22)
        rect(draw, (int(bx), int(by) - 8, int(bx) + 4, int(by) - 1), colour)
        rect(draw, (int(bx), int(by) - 8, int(bx) + 4, int(by) - 7), BG_DEEP)

    window = [
        wall_point(0.72, 1, 34),
        wall_point(0.96, 1, 34),
        wall_point(0.96, 1, 14),
        wall_point(0.72, 1, 14),
    ]
    draw.polygon(window, fill=BG_DEEP)
    draw.line(window + [window[0]], fill=OUTLINE)
    for star_t, star_lift in ((0.78, 29), (0.87, 24), (0.80, 19), (0.92, 30)):
        sx, sy = wall_point(star_t, 1, star_lift)
        rect(draw, (int(sx), int(sy), int(sx), int(sy)), INK)

    # ── Floor: a checkerboard of iso tiles. ──
    for j in range(ROWS):
        for i in range(COLS):
            draw.polygon(tile_diamond(i, j), fill=FLOOR_A if (i + j) % 2 == 0 else FLOOR_B)

    # Trace the outer rim so the floor reads as a room and not as a gradient.
    top = (OX + 8, OY)
    right = (tile_anchor(COLS - 1, 0)[0] + 16, tile_anchor(COLS - 1, 0)[1] + 4)
    left = (tile_anchor(0, ROWS - 1)[0], tile_anchor(0, ROWS - 1)[1] + 4)
    bottom = (OX + 8, OY + (COLS + ROWS) * (TH / 2))
    draw.line([top, right, bottom, left, top], fill=OUTLINE)

    return image


def draw_bar() -> Image.Image:
    """The counter: an iso slab with the espresso machine standing on it."""
    image = Image.new("RGBA", (52, 44), CLEAR)
    draw = ImageDraw.Draw(image)

    contact_shadow(draw, 26, 40, 24)

    # Machine first — the counter's top face paints over its foot, so the
    # machine reads as standing on the slab rather than behind it.
    outlined(draw, (16, 2, 36, 21), PROP_BASE)
    rect(draw, (16, 2, 36, 4), PROP_LIT)
    rect(draw, (19, 6, 33, 14), BG_DEEP)
    rect(draw, (21, 8, 29, 10), AMBER)
    rect(draw, (21, 11, 26, 12), COPPER)
    rect(draw, (31, 6, 32, 7), GREEN)
    rect(draw, (37, 9, 38, 17), COPPER)  # steam wand
    rect(draw, (23, 21, 29, 24), COPPER)  # group head
    rect(draw, (24, 24, 28, 27), INK)  # the shot landing in the cup

    iso_box(draw, 26, 13, 22, 12, COPPER)

    # Two cups waiting on the near edge of the slab.
    for cup_x in (13, 33):
        rect(draw, (cup_x, 28, cup_x + 5, 32), INK)
        rect(draw, (cup_x, 28, cup_x + 5, 28), COPPER)

    return image


def draw_roaster() -> Image.Image:
    """Drum roaster: hopper, drum with a glass door, chimney, burner."""
    image = Image.new("RGBA", (36, 46), CLEAR)
    draw = ImageDraw.Draw(image)

    contact_shadow(draw, 18, 42, 15)

    # Chimney, standing off the back-right shoulder of the drum.
    outlined(draw, (27, 3, 31, 18), PROP_BASE)
    rect(draw, (26, 2, 32, 4), COPPER)

    # Hopper, tapering down into the drum.
    draw.polygon([(8, 6), (28, 6), (24, 16), (12, 16)], fill=PROP_LIT)
    draw.line([(8, 6), (28, 6)], fill=COPPER)
    draw.line([(8, 6), (12, 16)], fill=BG_DEEP)
    draw.line([(28, 6), (24, 16)], fill=BG_DEEP)

    # Drum, with the round glass door beans get watched through.
    outlined(draw, (3, 17, 32, 36), PROP_BASE)
    rect(draw, (3, 17, 32, 19), PROP_LIT)
    draw.ellipse((9, 21, 24, 33), fill=COPPER)
    draw.ellipse((11, 23, 22, 31), fill=BG_DEEP)
    rect(draw, (13, 25, 15, 26), AMBER)
    rect(draw, (17, 27, 19, 28), CLAY)
    rect(draw, (14, 29, 16, 30), COPPER)
    rect(draw, (26, 22, 29, 25), GREEN)  # temperature readout

    # Legs, and the burner flame licking the drum from underneath.
    rect(draw, (6, 36, 9, 42), PROP_DARK)
    rect(draw, (26, 36, 29, 42), PROP_DARK)
    rect(draw, (13, 36, 22, 39), CLAY)
    rect(draw, (15, 39, 20, 41), AMBER)

    return image


def draw_till() -> Image.Image:
    """Point of sale: a pedestal under a screen showing a CAFE coin."""
    image = Image.new("RGBA", (30, 40), CLEAR)
    draw = ImageDraw.Draw(image)

    contact_shadow(draw, 15, 36, 13)

    # Screen and its post, drawn before the pedestal top covers the foot.
    outlined(draw, (4, 2, 25, 18), PROP_BASE)
    rect(draw, (4, 2, 25, 4), PROP_LIT)
    rect(draw, (7, 6, 22, 16), BG_DEEP)
    draw.ellipse((11, 8, 18, 14), fill=AMBER)
    draw.ellipse((13, 10, 16, 12), fill=COPPER)
    rect(draw, (8, 7, 9, 8), GREEN)
    rect(draw, (12, 18, 18, 26), PROP_LIT)

    iso_box(draw, 15, 22, 13, 9, COPPER)

    return image


def draw_vault() -> Image.Image:
    """The cellar safe the staked beans sit in."""
    image = Image.new("RGBA", (32, 40), CLEAR)
    draw = ImageDraw.Draw(image)

    contact_shadow(draw, 16, 36, 14)

    # A coin resting on the lid, proud of the body.
    draw.ellipse((12, 1, 20, 8), fill=AMBER)
    draw.ellipse((14, 3, 18, 6), fill=COPPER)

    outlined(draw, (3, 8, 28, 34), PROP_BASE)
    rect(draw, (3, 8, 28, 10), PROP_LIT)
    rect(draw, (7, 13, 24, 31), PROP_DARK)
    draw.line([(7, 13), (24, 13), (24, 31), (7, 31), (7, 13)], fill=COPPER)

    # Dial with four spokes, and the handle down the near edge.
    draw.ellipse((12, 18, 19, 25), fill=COPPER)
    rect(draw, (14, 20, 17, 23), AMBER)
    rect(draw, (15, 15, 16, 17), COPPER)
    rect(draw, (15, 26, 16, 28), COPPER)
    rect(draw, (9, 21, 11, 22), COPPER)
    rect(draw, (20, 21, 22, 22), COPPER)
    rect(draw, (26, 18, 27, 26), COPPER)
    rect(draw, (5, 11, 7, 12), GREEN)

    return image


def draw_lab() -> Image.Image:
    """Back-room workbench with one of the crew robots behind it."""
    image = Image.new("RGBA", (46, 42), CLEAR)
    draw = ImageDraw.Draw(image)

    contact_shadow(draw, 23, 38, 21)

    # Robot: antenna, square head, two green optics, blocky torso, two arms.
    rect(draw, (22, 0, 24, 3), COPPER)
    outlined(draw, (15, 3, 31, 16), PROP_BASE)
    rect(draw, (15, 3, 31, 5), PROP_LIT)
    rect(draw, (18, 7, 21, 10), GREEN)
    rect(draw, (25, 7, 28, 10), GREEN)
    rect(draw, (20, 13, 26, 14), COPPER)
    outlined(draw, (17, 17, 29, 27), PROP_BASE)
    rect(draw, (20, 20, 26, 23), COPPER)
    rect(draw, (11, 17, 16, 20), PROP_DARK)
    rect(draw, (30, 15, 35, 18), PROP_DARK)

    iso_box(draw, 23, 22, 20, 10, GREEN)

    # A signed mission slip and an escrowed coin on the bench.
    rect(draw, (11, 30, 19, 35), INK)
    rect(draw, (13, 31, 17, 32), PROP_DARK)
    rect(draw, (13, 33, 16, 34), PROP_DARK)
    draw.ellipse((27, 29, 34, 35), fill=AMBER)
    draw.ellipse((29, 31, 32, 33), fill=COPPER)

    return image


def draw_node() -> Image.Image:
    """The dark node: the Sepolia bundler rack, provisioned but switched off.

    Every other prop in the room carries a green "on" pixel somewhere. This one
    deliberately does not — its blades are drawn a step below PROP_DARK and its
    only lit pixel is a dim clay standby lamp, so the station reads as cold at a
    glance rather than needing the card to say so."""
    image = Image.new("RGBA", (34, 46), CLEAR)
    draw = ImageDraw.Draw(image)

    cold_frame = (38, 32, 28, 255)
    cold_blade = (24, 20, 17, 255)
    cold_vent = (46, 39, 34, 255)

    contact_shadow(draw, 17, 42, 14)

    outlined(draw, (4, 3, 29, 40), cold_frame)
    rect(draw, (4, 3, 29, 4), (56, 47, 40, 255))

    # Four blades in empty slots, each with its own dead status lamp.
    for slot in range(4):
        top = 7 + slot * 8
        rect(draw, (7, top, 26, top + 5), cold_blade)
        rect(draw, (9, top + 2, 18, top + 3), cold_vent)
        rect(draw, (24, top + 2, 25, top + 3), (58, 48, 42, 255))

    # The one lit pixel in the whole rack: a clay standby lamp, not a green one.
    rect(draw, (24, 5, 25, 6), CLAY)

    # Feet, and a power cable trailing off the back with nothing on the end.
    rect(draw, (6, 40, 9, 43), PROP_DARK)
    rect(draw, (24, 40, 27, 43), PROP_DARK)
    rect(draw, (29, 34, 32, 35), cold_frame)
    rect(draw, (31, 35, 32, 41), cold_frame)

    return image


PROPS = {
    "pl-cafe-room": draw_room,
    "pl-cafe-bar": draw_bar,
    "pl-cafe-roaster": draw_roaster,
    "pl-cafe-till": draw_till,
    "pl-cafe-vault": draw_vault,
    "pl-cafe-lab": draw_lab,
    "pl-cafe-node": draw_node,
}


def main() -> None:
    IMAGES.mkdir(parents=True, exist_ok=True)
    for name, factory in PROPS.items():
        image = factory()
        target = IMAGES / f"{name}.png"
        image.save(target, optimize=True)
        print(f"wrote {target} ({image.width}x{image.height})")

    print("\nstation screen points (source px, room is %dx%d):" % (ROOM_W, ROOM_H))
    for name, (i, j) in ISO_TILES.items():
        cx, cy = tile_centre(i, j)
        print(
            f"  {name:<8} tile ({i},{j}) -> ({cx:g}, {cy:g})"
            f"  left {cx / ROOM_W * 100:.2f}%  top {cy / ROOM_H * 100:.2f}%"
        )


if __name__ == "__main__":
    main()
