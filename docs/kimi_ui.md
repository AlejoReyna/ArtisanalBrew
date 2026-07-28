# kimi_ui — the pixel-galaxy UI system behind `/staking` and `/procurement-lab`

This document explains how the two "dark roast wing" pages were built and how to
replicate their signature assets: the golden bean coin and the friendly pixel
robots. Everything is Razor + component-scoped CSS + PNG sprites generated with
Python/Pillow. There are no external runtime image URLs and no generated
AI imagery shipped to the browser.

> [!NOTE]
> **GPT version**
>
> This revision keeps the Kimi-authored staking visual language while rebuilding
> the Procurement Lab robots as deterministic four-frame sprite sheets. The GPT
> pass adds walking poses, role-specific work actions, state-aware activity
> labels, layered hero positioning, and reduced-motion behavior.

---

## 1. The shared visual system

Both pages are siblings: same universe, same tokens, different story.

| Concern | Decision |
| --- | --- |
| Layout | `Components/Layout/StakingLayout.razor` (navbar + `.yield-page` wrapper). `/staking` uses it via `Staking.razor`; `/procurement-lab` opts in with `@layout StakingLayout`. |
| Render mode | `@rendermode InteractiveServer` on both pages (required for wallet actions and state-driven scene classes). |
| Palette | The shared `--stake-*` tokens declared in `wwwroot/app.css` on `.yield-page, .procurement-lab` — light literals in the base block, dark literals under `prefers-color-scheme: dark` and `[data-theme="dark"]`. Components reference `var(--stake-x)` directly; nested token aliasing (`--pl-x: var(--stake-x)`) was observed to fail to resolve and is deliberately avoided. |
| Type | Editorial headlines: `var(--font-display)` (Playfair Display). Status, telemetry, labels, actions: `var(--crypto-mono)`. Body: `var(--font-sans)`. |
| Pixel rendering | Every sprite `img` gets `image-rendering: pixelated`. Sprites are drawn at 16–56 px and scaled up 2–5×, so pixels stay chunky and crisp. |
| Themes | The scene uses only `--stake-*` tokens for stars/deck/glows, so it inverts cleanly: cream ground + coffee stars in light mode, roast ground + cream stars in dark mode. Sprite PNGs have their own baked espresso outlines and read on both. |
| Navbar continuity | `StakingLayout` passes `TransparentHeader="true"` to `NavMenu`, letting the layout background paint beneath the fixed navbar on both routes with no divider. |

Key token values (dark tier): bg-deep `#100D0B`, surface `#1F1915`, ink `#F4EFE6`,
green `#8FB99B`, copper `#C89B6A`, amber `#E5C078`, clay `#E59880`.

---

## 2. Shared primitives

### 2.1 The clipped pixel button

Used for the staking connect CTA (`.staking-intro__cta`) and the lab's
`CREATE TEST MISSION — WALLET SIGNED` button (`.pl-cta`). The recipe:

- `border-radius: 0` + a `clip-path` polygon that cuts 6px/3px stepped corners,
  producing a hard pixel chamfer:

```css
clip-path: polygon(6px 0, calc(100% - 6px) 0, calc(100% - 6px) 3px, 100% 3px,
    100% calc(100% - 6px), calc(100% - 3px) calc(100% - 6px),
    calc(100% - 3px) 100%, 6px 100%, 6px calc(100% - 3px),
    0 calc(100% - 3px), 0 6px, 3px 6px, 3px 3px, 6px 3px);
```

- Hard copper drop shadow via `filter: drop-shadow(5px 5px 0 var(--stake-copper))`
  (a filter, not `box-shadow`, so the shadow follows the clipped silhouette).
- Hover: background flips to `--stake-amber`, shadow grows to `7px 7px`,
  button translates `(-2px, -2px)`. Active: shadow collapses to `2px 2px` and
  `scale(0.97)`.
- Mono uppercase label, `letter-spacing: 0.12em`, min-height `3.15rem`.
- Focus stays visible with `outline: 2px dashed var(--stake-green)` offset 4px.

### 2.2 Cross-shaped pixel stars

A star is one `span` (11×3px bar) plus `::after` (3×11px bar crossing it).
Blink is a **stepped** keyframe so it snaps rather than fades:

```css
.pl-star { height: 3px; width: 11px; background: var(--star-color, var(--stake-faint));
    animation: pl-star-blink var(--star-duration, 2.6s) steps(2, end) var(--star-delay, 0s) infinite; }
.pl-star::after { content: ""; position: absolute; left: 4px; top: -4px; width: 3px; height: 11px; background: inherit; }
@keyframes pl-star-blink {
    0%, 45%  { opacity: .28; transform: scale(.72); }
    50%, 100% { opacity: 1;  transform: scale(1); }
}
```

Irregularity comes from per-star `--star-delay` / `--star-duration` /
`--star-color` custom properties, not from extra keyframes. The same pattern
lives in `YieldPanel.razor.css` (`.staking-space__star`, `.coffee-orbit__star`).

---

## 3. The CAFE coin

Asset: `wwwroot/images/coffee-coin-pixel.png` (32×32). Also reused inside the
escrow vault sprite and (downscaled) wherever a token glyph is needed.

### 3.1 Design recipe (Pillow)

Palette:

```
ESPRESSO = (43, 27, 16)    # outline + glyph
GOLD     = (242, 178, 62)  # coin face
GOLD_HI  = (255, 217, 138) # highlight / bean
GOLD_SH  = (176, 116, 28)  # shadow
```

Drawing order on a transparent 32×32 canvas:

1. Outer disc: `ellipse((1,1)-(30,30))` in ESPRESSO → the chunky outline.
2. Face: `ellipse((3,3)-(28,28))` in GOLD.
3. **Rim ring near the edge** (a real coin's raised border):
   `ellipse((4,4)-(27,27))` outline ESPRESSO 1px, then
   `ellipse((5,5)-(26,26))` outline GOLD_HI 1px.
4. Field sheen (stepped arcs, never gradients):
   `arc((7,7)-(24,24), 150°→260°, GOLD_HI, width=2)` and
   `arc((7,7)-(24,24), -30°→80°, GOLD_SH, width=2)`.
5. Golden bean, small and centered (~⅓ of the face):
   `ellipse((12,10)-(20,22))` fill GOLD_HI, outline ESPRESSO 1px;
   sheen/shadow 1px arcs inside; the S-crease is a 1px polyline
   `[(16,11),(15,14),(16,17),(15,20)]` in ESPRESSO.

Rules that make it read as pixel art: integer coordinates only, Pillow's aliased
drawing (no anti-aliasing), flat fills + 1–2px stepped highlights, and one dark
outline color everywhere.

### 3.2 The rotating 3D coin (`/staking` hero)

A single flat `rotateY` image looks like paper edge-on. The fix in
`YieldPanel.razor` (`.coffee-orbit__coin-scene`) is a layered extrusion:

- A **scene** div owns `perspective: 40rem` (keeps 3D working even though the
  parent has its own float animation, which would otherwise flatten children).
- Inside, `.coffee-orbit__coin3d` (`transform-style: preserve-3d`) runs a
  3.6s linear flip: `from { transform: rotateX(14deg) rotateY(0) }` to
  `rotateX(14deg) rotateY(360deg)` — the constant 14° tilt keeps the edge
  visible through the whole turn.
- **Faces**: two `<img>` of the coin at `translateZ(±6px)`;
  the back face adds `rotateY(180deg)` and both have
  `backface-visibility: hidden`, so the bean is never mirrored.
- **Edge**: eight `<span class="coffee-orbit__coin-layer">` between the faces,
  each `background: url(coffee-coin-pixel.png) center/contain`,
  `filter: brightness(0.45)`, and
  `transform: translateZ(calc(-6px + var(--layer) * 1.5px))`.
  Stacked, they read as the coin's milled edge.
- Entrance (`coffee-orbit-arrive`, a translateY spring) and idle float live on
  outer wrappers so they never fight the flip transform.

---

## 4. The procurement robots

Sprites (all in `wwwroot/images/`): `pl-robot-scout.png`, `pl-robot-buyer.png`,
`pl-robot-courier.png`, `pl-robot-inspector.png` (four 64×64 frames in a
256×64 horizontal sheet), `pl-vault.png` (56×64),
`pl-key.png` (16×16), `pl-planet.png` (32×32), `pl-planet-ringed.png` (40×32),
`pl-satellite.png` (28×20).

### 4.1 Character system

One shared base, per-role props. The source of truth is
`tools/generate_procurement_sprites.py`; rerun it from the repository root
after changing the geometry or palette. Each sheet contains two alternating
walking poses followed by two role-action poses. Palette:

```
BLACK    = (8, 8, 7)       # primary silhouette
CREAM_SH = (178, 165, 126) # side casing and joints
CREAM_HI = (255, 250, 198) # face casing and chest plate
TEAL_DK  = (24, 67, 63)    # screen frame
TEAL     = (57, 119, 109)  # screen
TEAL_HI  = (132, 213, 190) # screen sheen
FACE     = (231, 232, 103) # eyes + smile
GOLD / COPPER / GREEN / CLAY as in section 3.
```

Base robot anatomy (top-left anchor `(x, y)`, roughly 49×60px inside a 64px
frame):

| Part | Rect | Notes |
| --- | --- | --- |
| Legs | two narrow BLACK columns below the body with cream inner rods and 7px feet | frames 0/1 offset opposite feet by 2px |
| Body | BLACK `(x+13,y+27)-(x+36,y+44)`, cream inset | compact body and bright 12×9 chest plate |
| Head | BLACK `(x+4,y+2)-(x+45,y+29)` plus a left ear/casing block | oversized square monitor silhouette copied from the visual reference |
| Screen | TEAL_DK frame with a TEAL inset and 2px top sheen | inset deeply enough to preserve the chunky black bezel |
| Face | two square yellow eyes and a four-segment smile | identical friendly expression across roles and frames |

Props per role (drawn after the base):

- **Scout** — antenna, raised arm and telescope; its two action frames raise the
  telescope and add a tiny signal glint.
- **Buyer** — terminal pedestal (METAL_DK body, TEAL screen with keyhole glyph,
  GREEN/GOLD/CLAY button row) plus an arm and key that advance toward the
  terminal over the four frames. The separate `pl-key.png` remains the
  state-driven permission glow layered by Razor.
- **Courier** — alternating walking legs and a large COPPER crate held at chest,
  with a tiny backpack behind the body.
- **Inspector** — ground crate, raised scanner, and a final frame whose teal
  scan line lands directly across the crate.

### 4.2 Escrow vault (56×64)

1. Frame: OUT rect `(6,8)-(49,58)`, METAL_DK inset, METAL top rim, four corner
   bolts (2px OUT + 1px COPPER).
2. Glass chamber: TEAL_DK border, then a **translucent** fill
   `(47,111,106,110)` — the alpha matters, see §5.2.
3. Stepped glass highlights: 2–3 vertical strips at alpha 50–90.
4. Coin: the real `coffee-coin-pixel.png` pasted at 20×20 (`resize(NEAREST)`),
   centered at `(18,24)`.
5. Padlock on the frame front: shackle (OUT + METAL with a transparent bite),
   METAL_DK body, GOLD keyhole dot.

### 4.3 Planets & satellite

Cratered planet: copper-dark disc, 3px COPPER highlight arc, 2–3 crater ellipses
(OUT ring + COPPER_D fill). Ringed planet: draw the **back ring arc first**
(`arc(180°→360°)`), then the body, then the **front ring arc** (`arc(0°→180°)`)
so the ring visibly crosses in front — each ring pass is 3px OUT + 1px COPPER.
Satellite: cream body + TEAL dot, COPPER panels both sides, 1px antenna.

---

## 5. Scene composition and motion (`/procurement-lab` hero)

### 5.1 Layout

`.pl-hero` is a centered one-column grid. The scene (`.pl-scene`) is a relative
box, `aspect-ratio: 16/7.5`, capped at 48rem, with actors positioned in percents
along an 8% deck strip. Its empty sky is pulled upward behind the copy with a
negative margin; the copy/action/telemetry sit at `z-index: 2`, while the scene
sits at `z-index: 0`:

```
scout left 1% / 15% · buyer left 20% / 17%
vault centered (left:50%, translateX(-50%), 19%)
courier right 25% / 15% · inspector right 1% / 17%
key left 30.5% · controls right 17%
```

The deck is a solid `--stake-surface-2` bar with a 2px `--stake-line-strong`
top border and a stepped highlight lip via `::before` — no gradients.

### 5.2 Motion is state-driven, stepped, and small

| Element | Binding | Animation |
| --- | --- | --- |
| Buyer robot | `_activeEpoch is null` → `is-locked` (desaturate + 55% opacity, frozen on frame 0); epoch present → `is-live` (green drop-shadow) | `pl-buyer-insert` 3.4s `steps(1,end)`: arm back → approach slot → key in → terminal lights, hold → reset |
| Scout robot | always | `pl-scout-sweep` 4.8s `steps(1,end)`: level watch → adjust → telescope rises → amber lock-on hold → reset |
| Courier robot | always | `pl-courier-walk` 1.05s `steps(1,end)`: brisk 4-step gait, body dips on each footfall |
| Inspector robot | always | `pl-inspector-scan` 3.8s `steps(1,end)`: scanner on crate → lift → scan pass → teal beep, hold → reset |
| Key sprite | rendered only when `_activeEpoch is not null` | `pl-key-pulse` 1.6s `steps(2,end)` gold glow |
| Vault glow | always (a gold square **behind** the vault img — it shines through the translucent glass alpha from §4.2) | `pl-vault-glow` 2.8s `steps(2,end)` opacity .45↔.95 |
| Role robots | scout/courier/inspector always; buyer only with an active permission | `pl-robot-work` advances two walking frames and two role-action frames with `steps(1,end)` |
| Activity labels | four local callouts above the actors; buyer text follows permission state | Static role-local labels (`Scanning suppliers`, `Authorizing`/`Awaiting key`, `Delivering crate`, `Inspecting proof`) rather than a lifecycle bar |
| Inspector beam | always | `pl-scan-sweep` 3.8s `steps(5,end)`, 3px teal bar crossing the crate (synced to the inspector's scan cycle) |
| Approve/return controls | `is-live` when any job is `Submitted` | `pl-control-blink` 1.4s `steps(2,end)`, return delayed 0.7s |
| Scout signal | always | 3 dots, `steps(1,end)` blink, 0.3s stagger |
| Satellite + downlink | rendered only while `_loading` | 3 amber dots, `steps(1,end)`, 0.25s stagger |
| Stars | always | §2.2, irregular delays |

`prefers-reduced-motion: reduce` kills every one of these animations and pins
the scan beam/glow to fixed opacities — the composition stays intact as a
still image.

### 5.3 Honest copy rules

- The create button is labeled `CREATE TEST MISSION — WALLET SIGNED` because
  that is what it does (MetaMask signs; no autonomous agent is launched).
- Delegation state says `Permission recorded for this escrow`, never "agent may
  fund this" — the check only matches grant target addresses, not full
  selector/calldata validity.
- The permission panel pill reads `Permission available`; wallet-signed-only and
  agent-executed states stay visually distinct from it.
- Revocation copy already distinguishes "sponsorship revoked" from a still-live
  on-chain epoch — keep it that way.
- **Deliberate exclusion:** no `Discover → Authorize → Fund → Deliver → Inspect → Pay`
  bar/timeline/footer anywhere. The robots act out the process; the per-job
  lifecycle UI inside job cards carries the real state.

---

## 6. Regenerating the assets

The sprites are produced by a single Pillow script (run from the repo root,
output to `src/ThisCafeteria.Web/wwwroot/images/`). To replicate or extend:

1. Copy the palette blocks and the `robot_base()` geometry from §3.1/§4.1 —
   they are the whole character system.
2. Draw on transparent canvases at native pixel size (16–56px), integer coords
   only, never anti-alias, and use `resample=Image.NEAREST` for every rotate or
   resize.
3. Build a contact sheet (`Image.NEAREST` upscale onto a `#100D0B` canvas) and
   eyeball it before saving — small pixel errors are invisible at 32px and
   glaring at 3×.
4. New roles should reuse `robot_base()` and add one prop cluster; new coins
   reuse the disc/rim/sheen recipe and swap the center glyph.

---

## 7. Files that make up the system

- `Components/Pages/Staking.razor`, `Components/Shared/YieldPanel.razor(.css)` —
  coin hero, starfield, pixel CTA.
- `Components/AgenticCommerce/ProcurementLab.razor(.css)` — robot station hero,
  telemetry, escrow panels.
- `Components/Layout/StakingLayout.razor` — shared navbar/offset shell.
- `wwwroot/app.css` — `--stake-*` token tiers (light/dark), `.crypto-dot`,
  fonts.
- `wwwroot/images/` — `coffee-coin-pixel.png`, `eth-pixel.png`, `pl-*.png`.
