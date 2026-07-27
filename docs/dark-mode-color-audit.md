# ArtisanalBrew Color & Typography Audit (pre–dark-mode)

Generated 2026-07-26. Scope: every `.razor`, `.razor.css`, and global stylesheet under
`src/ThisCafeteria.Web` that declares a `color`, `background`/`background-color`,
`font-family`, or CSS custom property (excluding `obj/`, `wwwroot/lib` vendor CSS,
and `node_modules`). 41 source files reviewed.

## 1. Headline finding

**The app is not "starting from zero" on dark mode — it already has three independent,
undocumented dark palettes**, built ad hoc for the on-chain/crypto surfaces:

| Palette | Scope | Vars |
|---|---|---|
| `--stake-*` / `--yield-*` | `.yield-page` (Staking + Profile pages), defined in `app.css` | `--stake-bg #171310`, `--stake-surface #1F1915`, `--stake-ink #F4EFE6`, `--stake-green #8FB99B`, `--stake-copper #C89B6A`, `--stake-amber #E5C078`, `--stake-clay #E59880` |
| `--pl-*` | `.procurement-lab` only, defined locally in `ProcurementLab.razor.css` | Near-identical values to `--stake-*` (`--pl-bg #100d0b`, `--pl-surface #1f1915`, `--pl-ink #f4efe6`, `--pl-green #8fb99b`, `--pl-copper #c89b6a`, `--pl-amber #e5c078`, `--pl-clay #e59880`) — a **duplicate** of the stake palette under different names |
| `--dash-*` | `Profile.razor.css` only | `--dash-card #1F1915`, `--dash-espresso #F4EFE6` — same values again, third name for the same colors. Note: named `--dash-cream`/`--dash-sand` but both hold **dark** values (`#100D0B`) — a naming/light-dark mismatch baked into the source |
| Local, unrelated | `Intro.razor.css` (splash screen) | `--bg #050403`, `--ink #f5efe4`, `--accent #c9a063` — its own tiny dark system, doesn't reuse any of the above |

Plus **banded dark sections inside otherwise-light pages**: `StakingBand.razor` (home),
the hero + subscribe bands in `Journal.razor`, the yield/pour-log bands in `Story.razor`,
the transaction ledger in `Orders.razor`, and the wallet modal in `NavMenu.razor.css` —
all hand-coded as permanently-dark `.charcoal`-background blocks, not theme-reactive.

**Net effect**: dark-mode-capable visual language already exists for the "on-chain wing"
(staking/profile/procurement) but the storefront, checkout, cart, admin, and marketing
pages are 100% light-only, and none of the three dark palettes talk to each other or to
a `prefers-color-scheme`/`[data-theme]` switch. There is no light/dark toggle anywhere.

## 2. Competing light-mode token systems

Three separate "light palette" token sets exist, with different values for concepts
that should be the same:

| Token role | `app.css :root` (site-wide) | `catalog.input.css` (Tailwind, Products/ProductDetails only) | `Products.razor.css --shop-*` (aliases app.css) |
|---|---|---|---|
| Page background | `--surface: #fbf9f4` | `--color-cream: #F5F2EC` | `--shop-cream: var(--surface, #fbf9f4)` |
| Ink / body text | `--ink: #2d2421` | `--color-ink: #1C1B1A` | `--shop-ink: var(--ink, #2d2421)` |
| Muted text | `--muted: #746a63` | `--color-mute: #8B8880` | `--shop-muted: var(--muted, #746a63)` |
| Hairline border | `--line: #d8d0c4` | `--color-line: #E5E0D8` | `--shop-line: var(--line, #d8d0c4)` |
| Accent | `--accent: #8d6f55` (tan) | `--color-accent: #E8781E` (orange) | `--shop-accent: #b8532e` (a **third** accent hue, rust) |
| Dark surface | `--charcoal: #1f1a18` | *(none)* | *(none)* |
| Off-white | `--white: #fffdf9` | `--color-white: #fff` | — |

`ProductDetails.razor.css` re-declares the Tailwind set a **fourth** time as local
`--card-*` vars (`--card-bg #ffffff`, `--card-ink #1c1b1a`, `--card-accent #e8781e`,
`--card-positive #47614e`) because `catalog.input.css` only loads on the Products page.

**Consequence for dark mode**: retrofitting a single `[data-theme="dark"]` override
onto `--surface`/`--ink`/`--line` in `app.css` will **not** re-theme the Products or
ProductDetails pages, because they read from a different variable namespace
(`--color-*` / `--card-*`) with different literal fallback values baked in.

## 3. Section-by-section inventory

### 3.1 Global stylesheet (`wwwroot/app.css`)

- `:root` tokens: `--surface #fbf9f4`, `--surface-deep #f2eee5`, `--ink #2d2421`,
  `--muted #746a63`, `--line #d8d0c4`, `--accent #8d6f55`, `--charcoal #1f1a18`,
  `--white #fffdf9`, `--crypto-positive #47614e`.
- Fonts: `--font-sans: 'Inter', 'Helvetica Neue', Helvetica, Arial, sans-serif`;
  `--font-display: 'Playfair Display', Georgia, serif`;
  `--font-sidebar-display: 'EB Garamond', 'Times New Roman', Times, serif`;
  `--crypto-mono: ui-monospace, 'SF Mono', 'Cascadia Mono', Menlo, Consolas, monospace`.
- `html, body` sets page background `#fbf9f4` / text `#2d2421` directly (not via var).
- `::selection` — bg `#2d2421`, text `#fbf9f4`.
- Nav "theme skins" driven by body/section selectors, all hardcoded per-context:
  homepage hero nav → transparent/cream text; procurement-lab nav → `#171714` bg,
  white text; yield-page nav → `rgba(23,19,16,.86)` bg, `#F4EFE6` text.
- Local sub-systems defined further down the same file: `.yield-page` dark palette
  (§1 table), `--menu-*` (dark overlay menu region: `--menu-sand rgba(0,0,0,.28)`),
  `--recruiter-*` and `--journal-*` (both alias `--charcoal`/`--surface` with their
  own muted/line rgba ramps — `rgba(33,27,23,.62)` etc.).
- `#blazor-error-boundary` — hardcoded `#b32121` bg, white text (framework banner).

### 3.2 Home / marketing (`Components/Home/*.razor`)

All five files (Concept, Hero, Offerings, Provenance, StakingBand) route color
through the global vars above — **the most theme-ready code in the app** — with two
exceptions: `Hero.razor`'s credit line is hardcoded `white`, and a radial-gradient
stop in `Offerings.razor` is a literal `rgba(255, 252, 246, 0.95)`.

`StakingBand.razor` is already fully dark (bg `var(--charcoal)`, text `var(--white)`
at alphas `0.04`–`0.65`) — a good visual reference for target dark values elsewhere.

Each `<section>` also carries a `data-theme-color="#hex"` attribute (JS sets the
mobile browser-chrome color) — these will need dark equivalents too: Concept/Offerings/
Provenance `#ffffff`, Hero `#523d26`, StakingBand `#1f1a18`.

### 3.3 Navigation & layout (`Components/Layout/*`)

- **`NavMenu.razor.css`** — the single largest, most color-dense file in the app
  (1169 lines) and **entirely hardcoded**, no CSS variables for its palette. Recurring
  literals: `#1a1513`/`#2d2421` (ink), `#746a63` (muted), `#fbf9f4`/`#fffdf9` (off-white),
  `#8d6f55`/`#c2a47d` (tan/accent), `#b23b2f`/`#942f25` (destructive red). Contains a
  permanently-dark wallet-connect modal (`rgba(40,20,10,.6)` bg, `#fffdf9` text) sitting
  inside an otherwise light component — i.e. it already renders one dark surface
  without any variable indirection to reuse.
- **`AdminLayout.razor.css`** — sidebar is hardcoded dark (`#1f1a18` bg / `#fbf9f4`
  text, same values as `--charcoal`/`--surface` but not referencing them), while the
  admin content area (`.admin-layout` bg `#f4f1eb`) is light and unrelated.
- **`MainLayout.razor.css` / `StakingLayout.razor.css`** — both set
  `#blazor-error-ui { color-scheme: light only; background: lightyellow }`. This
  **actively blocks** dark rendering for that one framework element and must be
  revisited explicitly (not just "add a dark override" — the light-only opt-out has
  to be removed first).
- **`ReconnectModal.razor.css`** — untouched Blazor framework default (white modal,
  blue `#6b9ed2` buttons, blue `#0087ff` spinner). No app tokens at all.

### 3.4 Storefront (Products / ProductDetails / Cart)

- **`Products.razor.css`** — defines local `--shop-*` aliases (see §2) plus category
  tint colors `--shop-tint-beans #b8532e`, `--shop-tint-equipment #6f7a5e`,
  `--shop-tint-ceramics #b08d6a`. Header banner text is hardcoded `#fbf9f4` /
  `rgba(251,249,244,.82)` regardless of theme.
- **`ProductDetails.razor.css`** — fully hardcoded `--card-*` local palette (see §2),
  none of it wired to `--surface`/`--ink`/etc. `font-family: 'Inter', sans-serif` is
  repeated ~18 times inline rather than referencing `--font-sans`.
- **`Cart.razor.css`** — cleanly var-driven throughout (`--surface`, `--ink`,
  `--muted`, `--line`, `--charcoal`, `--white`, `--accent`), the best-behaved storefront
  file. One hardcoded semantic color: `--cart-summary__value--accent: #2a9d5c` (success
  green, not tied to any var).

### 3.5 Checkout

- **`Checkout.razor.css`** (1309 lines) — almost entirely var-driven, but with a
  scattering of hardcoded status colors that don't route through the palette:
  `#9a3b2f`/`#b32121` (errors), `#326b3a` (success), `#c9a227` (a one-off gold used
  only in the confetti/firework particles).

### 3.6 Admin (Coupons / Orders / Products / Login)

- All four admin pages use **inline `<style>` blocks in the `.razor` file itself**
  (not `.razor.css`), and are **100% hardcoded hex**, no variables anywhere:
  bg `#fffdf9`/`#fbf9f4`, ink `#2d2421`, muted `#746a63`/`#8d6f55`, hairline
  `#d8d0c4`, destructive `#b32121` (hover tint `#fdf2f2`).
- `AdminLogin.razor` is the one partial exception — it uses `var(--white)`,
  `var(--ink)`, `var(--line)` for its input styling, but its error text is still
  hardcoded `#b32121`.
- These four pages, plus the admin sidebar's separate hardcoding (§3.3), mean the
  **entire admin surface needs a from-scratch tokenization pass**, not just a
  dark-value override — there's no variable layer to hook into yet.

### 3.7 Staking / Profile / Procurement (already-dark surfaces)

- `Profile.razor.css`, `.yield-page` styles in `app.css`, and `ProcurementLab.razor.css`
  are all **permanently dark**, each with its own near-duplicate palette (§1). None
  reacts to a theme switch because there's no light variant to switch *from* — they're
  hardcoded dark today.
- Shared components used inside this dark wing (`StatTile.razor.css`, `ChainBadge`,
  `ChainSelector`, `AddressChip`, `TxStepper.razor.css`, `YieldPanel.razor.css`) are
  themselves hardcoded to dark values (`#1F1915`, `#F4EFE6`, `rgba(244,239,230,*)`)
  with light-mode fallbacks in `var(--stake-x, <light-value>)` form — meaning they
  *would* render acceptably on a light page via their fallback, but were clearly
  designed dark-first.
- **Gap**: `FaucetPanel.razor.css` and `SmartAccountPanel.razor.css` — both used on
  these dark pages — are hardcoded **light** (`background: #faf9f5`, text `#2c1b12`).
  They do not participate in the dark theme at all today; confirm visually whether
  this is an existing bug or intentional light card-on-dark-page contrast.
- `TestnetInfoModal.razor.css` is dark but generically so (`#333333` bg, `#ffffff`
  text) — off-brand gray rather than the `#1F1915`/`#100D0B` roast tones used
  everywhere else.

### 3.8 Editorial (Journal / Story / Intro)

- `Journal.razor` and `Story.razor` both mix light default sections with hardcoded
  `var(--charcoal)` bands (hero/subscribe, yield/pour-log respectively) — i.e. these
  pages already know how to render a dark band inside a light page, just not the
  reverse.
- `Intro.razor.css` is a standalone, permanently-dark splash screen with its own
  tiny local var set (`--bg #050403`, `--ink #f5efe4`, `--accent #c9a063`, `--glow`)
  that doesn't reuse any other file's naming — a fourth independent "dark palette."

## 4. Typography inventory

| Role | Stack | Where used |
|---|---|---|
| Primary UI sans | `'Inter', 'Helvetica Neue', Helvetica, Arial, sans-serif` (`--font-sans`) | Body text, buttons, nav, most components |
| Editorial display | `'Playfair Display', Georgia, serif` (`--font-display`) | H1–H3, hero headlines, card titles, staking/procurement headings |
| Sidebar display | `'EB Garamond', 'Times New Roman', Times, serif` (`--font-sidebar-display`) | Profile page headings only |
| Monospace (crypto) | `ui-monospace, 'SF Mono', 'Cascadia Mono', Menlo, Consolas, monospace` (`--crypto-mono`) | Addresses, tx hashes, numeric stats |
| **Second monospace stack** (inconsistent) | `'JetBrains Mono', 'SF Mono', 'Fira Code', 'Cascadia Code', monospace` | `NavMenu.razor.css` drawer address/stat only — doesn't reuse `--crypto-mono` |
| **Fourth sans stack** | `'Plus Jakarta Sans', 'Inter', 'Helvetica Neue', Helvetica, Arial, sans-serif` | `NavMenu.razor.css` wallet modal only |
| Utility | `Outfit, sans-serif` (`.font-Outfit`, Google Font `@import`) | Ad hoc utility class, scattered use |
| Intro-only display | `'Playfair Display', Georgia, 'Times New Roman', serif` (local `--font-display` re-declaration) | `Intro.razor.css` |
| Intro-only caption | `-apple-system, BlinkMacSystemFont, 'Segoe UI', Helvetica, Arial, sans-serif` (local `--font-caption`) | `Intro.razor.css` |
| Tailwind catalog sans | `"Inter", ui-sans-serif, system-ui, sans-serif` (`--font-sans` redefined) | Products/ProductDetails only, via `catalog.input.css` |

Four different sans stacks and two different mono stacks are in play for what is
functionally "the same" UI font — worth consolidating before or during the dark-mode
pass so there's one source of truth per role.

## 5. Semantic / status color inconsistency

No single success/error/warning token exists. Observed literal values for the same
semantic role, scattered across files with no shared variable:

- **Error / destructive red**: `#b32121`, `#9a3b2f`, `#e50000`, `#b23b2f`, `#942f25`,
  `#8a3b2a`, `#ffb0a8`, `#E59880` (the dark-wing's clay/error tone)
- **Success / positive green**: `#2a9d5c`, `#326b3a`, `#47614e` (=`--crypto-positive`),
  `#8FB99B` (dark-wing positive)
- **Warning / amber**: `#E5C078` (dark wing only), `#c9a227` (one-off, Checkout confetti)
- **Crypto blue** (wallet brand color, not app semantic): `#627eea`/`#627ee9`

## 6. Risk register for the dark-mode project

1. **Four parallel light-token systems** (`app.css`, `catalog.input.css`,
   `Products.razor.css`, `ProductDetails.razor.css`) must be reconciled to one
   source of truth before a single dark override can cover the whole app.
2. **Three duplicate dark palettes** (`--stake-*`, `--pl-*`, `--dash-*`) should be
   consolidated into one set of dark tokens rather than maintained in triplicate.
3. **`color-scheme: light only`** on `#blazor-error-ui` in two layout files actively
   fights dark mode and must be removed/reworked.
4. **Admin surface has zero variable indirection** — needs tokenization work before
   it can respond to a theme switch at all (not just new dark values).
5. **FaucetPanel / SmartAccountPanel** are light-hardcoded despite living exclusively
   on the app's darkest pages — confirm whether that's a known/intentional gap.
6. **No `prefers-color-scheme` or `[data-theme]` mechanism exists anywhere** — the
   entire switching mechanism has to be built from scratch, there's nothing to extend.
7. **Typography has four sans stacks and two mono stacks** for what should likely be
   2 roles total — worth a quick consolidation pass alongside the dark-mode work
   since both touch the same files.
