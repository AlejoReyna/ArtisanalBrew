---
name: theme-tokens
description: Work with ArtisanalBrew's CSS custom-property token systems and its light/dark/system theming without breaking the cascade. Use when adding or changing colors in app.css, a .razor.css, or catalog.input.css; adding dark-mode support to a component; tokenizing hardcoded hex values; or debugging symptoms like "the color falls back to black", "a component went dark outside the staking pages", "the theme resets when I navigate", "a fixed header shows a seam", or "my CSS edit didn't take effect".
---

# Theme tokens: four systems, three tiers, one rule that breaks everything

This codebase has 134 custom properties across four deliberately separate token
systems, a three-tier light/dark/system cascade, and one environment quirk that
silently renders text pure black if you write CSS the way every style guide tells
you to.

Read the rule in §1 before touching any custom property here. It inverts the
normal advice, it fails silently, and it has already shipped a bug.

## 1. Never chain custom properties

**Do not write `--tokenA: var(--tokenB);` if `--tokenA` will later be consumed
via `var(--tokenA)`.** Nested custom-property indirection does not resolve
reliably in this environment. The consuming declaration falls through to its
fallback, or — with no fallback — to the browser's initial value. `color:
var(--stake-ink)` rendered **pure `rgb(0,0,0)`**, not the intended ink.

```css
/* BROKEN — the alias layer silently collapses */
.yield-page { --stake-ink: var(--ink); }
.thing      { color: var(--stake-ink); }   /* → rgb(0,0,0) */

/* CORRECT — direct literal, one var() hop at the point of use */
.yield-page { --stake-ink: #F4EFE6; }
.thing      { color: var(--stake-ink); }   /* → #F4EFE6 */
```

A single `var()` hop at the point of consumption always works. It is specifically
**chaining** that fails.

The consequence is uncomfortable and worth stating plainly: **duplicate the
literal.** For values re-consumed through another `var()` hop, duplication is the
safe choice here, even though it contradicts the DRY instinct that produced the
alias layer in the first place. That instinct is what caused the bug — the
`--pl-*` and `--dash-*` alias layers were exactly the broken pattern, and were
deleted in favour of referencing `var(--stake-*)` directly.

> Caveat, stated honestly: this was confirmed through extensive testing in the
> `claude-in-chrome` browser tool. Whether it reproduces in every stock Chrome
> install is unconfirmed. Design around it regardless — the cost is a duplicated
> hex literal, and the failure mode is invisible black text in production.

**When you must derive a color, use `color-mix` rather than an alias:**

```css
background: color-mix(in srgb, var(--stake-surface) 60%, transparent);
```

One hop, computed at use. This is how `.procurement-lab__source` replaced its
hardcoded `rgba(31,25,21,X)`.

## 2. The four token systems — do not merge them

| System | Lives in | Scope | Rule |
|---|---|---|---|
| Storefront | `app.css` `:root` | Global | The default palette: `--ink`, `--surface`, `--muted`, `--line`, `--accent`, `--charcoal`, `--white` |
| Catalog | `Styles/catalog.input.css` | Products, ProductDetails | Tailwind source. **Intentionally different** cream/ink hex values |
| On-chain wing | `app.css`, scoped to `.yield-page, .procurement-lab` | Those two pages | `--stake-*`, `--yield-*` — the "roast" palette, the only one with real dark values |
| Component-local | each `.razor.css` | One component | Locally-declared vars, e.g. `--card-muted` |

Two of these have traps.

**The catalog palette is not drift.** `catalog.input.css` has different cream and
ink values from `app.css` on purpose — it's a distinct reference design. Do not
repoint `--color-ink` / `--color-cream` at `var(--ink)` / `var(--surface)`; that
changes real rendered colors, and it's the chaining anti-pattern besides. Its
`--font-sans` in particular *cannot* be `var(--font-sans)` — both declarations
target the identical property name on `:root`, so the reference is
self-referential and invalid. It's a manually-synced literal.

**`--stake-*` must stay scoped. Never promote it to `:root`.** Shared components
(`ChainBadge`, `AddressChip`, `StatTile`, `TxStepper`, `ChainSelector`) are used
both inside the on-chain wing and outside it (NavMenu, Checkout). They're written
as `var(--stake-x, <light-fallback>)` and **rely on `--stake-x` being undefined**
outside the wing to fall back to light styling. Promoting the palette to `:root`
defines it everywhere and silently forces those components dark across the whole
site. This was caught in browser verification before it shipped — it does not
show up in any file-level review, because every individual file looks correct.

That fallback-when-undefined idiom is the deliberate mechanism for
"dark here, light there." Preserve it when touching shared components.

## 3. The three-tier cascade

In `app.css`, lowest precedence to highest:

```css
:root { --ink: #2d2421; }                                   /* 1. light defaults */

@media (prefers-color-scheme: dark) {
    :root:not([data-theme]) { --ink: …; }                   /* 2. OS preference */
}

:root[data-theme="dark"] { --ink: …; }                      /* 3. explicit choice */
```

The `:not([data-theme])` on tier 2 is what makes "System" distinct from an
explicit choice. `theme.js` (`window.artisanalTheme`) cycles **system → light →
dark → system**, writing `localStorage.theme` and setting or **removing**
`data-theme`. Removing it is how "System" is expressed. "Light" needs no rule of
its own — the `:root` defaults already are light, and the absence of
`data-theme="dark"` is enough to beat an OS dark preference.

Adding a new themed token means declaring it in **all three tiers**, with direct
literals in each.

**Current state, so you don't misread the file:** the base storefront tier's dark
values mirror the light ones 1:1 — the mechanism is wired ahead of the values it
will carry, so toggling to dark doesn't change the storefront look. Real dark
values exist today only in the `.yield-page, .procurement-lab` scope. A dark tier
that looks like a copy-paste mistake is deliberate.

Note also how the scoped tier lifts a shared token where it needs to:

```css
:root[data-theme="dark"] .yield-page { --crypto-positive: #8FB99B; }
```

The global brand green is illegible on the roast ground, so it's re-declared
inside the dark scope. Redeclaring a global token inside a scope is the right
move; aliasing it is not.

## 4. `window.artisanalTheme` vs `window.themeColor`

Two similarly-named globals that do unrelated things. Confusing them wastes time.

- **`artisanalTheme`** (`js/theme.js`) — owns `data-theme` on `<html>`. This is
  the theme.
- **`themeColor`** (`js/publicHeader.js`) — tints the mobile browser-chrome
  `<meta name="theme-color">`. Not the theme.

`theme.js` is loaded **blocking in `<head>`, before `app.css`**, and calls
`apply()` immediately so the attribute is set before first paint. Keep it
blocking; making it `defer` or moving it below the stylesheet reintroduces a
flash of the wrong theme.

**Enhanced navigation resets it.** A route change replaces `<html>`'s attributes
with what the server rendered, which never includes `data-theme` — only client JS
knows the stored choice. `onEnhancedLoad` in `App.razor` reapplies it as its
first line. If that handler is ever refactored, re-verify the theme survives a
click-through; this was found by live testing, not by reading code. See
`blazor-interop` for the full lifecycle.

## 5. Rules learned the expensive way

**Never `background: transparent` a `position: fixed` header to blend it into the
page.** Transparent reveals whatever happens to be rendered behind the fixed
element, which does not reliably equal the page's own background — it showed a
visible seam. **Pin it to the same token the page paints with** (the `/staking`
header uses `--stake-bg-deep`, matching `StakingLayout.razor.css`'s `.page`) so
it's guaranteed to match rather than coincidentally lining up.

**A component's `.razor.css` is not necessarily the only place its styles live.**
`app.css` carries stray global rules for component selectors —
`.chain-selector__control` had its own hardcoded dark background there, a
duplicate that a file-by-file component audit missed entirely. Before assuming
you've found all of a component's styles, grep `app.css` for its class names.

**Gate hardcoded-dark decorative effects by theme.** `.procurement-lab h1`'s
white-to-tan `background-clip: text` gradient was unconditional and went
illegible on a light background. Gradients, glows, and shadows tuned for dark
need a light equivalent or a dark-only gate.

**Keep genuinely theme-invariant colors as literals.** `#blazor-error-ui`'s
`lightyellow` warning background is deliberately not tokenized — but it needs an
**explicit literal text color** alongside it, or it inherits a themed color and
goes unreadable. Same reasoning left destructive reds as literals. Tokenize for
theming, not for tidiness.

**Only alias on exact hex matches.** During tokenization, `#2d2421` → `var(--ink)`
is safe; a near-match is a silent visual change. `#1a1513` → `var(--charcoal)`
was judged close enough — that judgment call should be conscious and noted, not
automatic.

## 6. Making an edit take effect

- **`.razor.css` (scoped)** → needs `dotnet build` before `dotnet run`.
- **`wwwroot/app.css`, `wwwroot/js/*`** → restart `dotnet run`. The browser
  caches `/js/*.js`; verify with `fetch(src, {cache: 'reload'})`.
- **`Styles/catalog.input.css`** → **must be recompiled**, from
  `src/ThisCafeteria.Web` (that's where the `package.json` defining it lives):

```bash
npm --prefix src/ThisCafeteria.Web run build:catalog-css
```

  It runs `tailwindcss -i Styles/catalog.input.css -o wwwroot/css/catalog.css
  --minify`. Editing the Tailwind source without this leaves
  `wwwroot/css/catalog.css` stale, and you debug a change that was never
  compiled. This is the single most common "my CSS didn't work" cause in this
  repo.

**A stale SignalR circuit fakes visual bugs.** After restarting `dotnet run`, an
open tab can show "Rejoining the server…" and render elements clipped or
misplaced. Not real. Full reload first, then judge. And before concluding
something is cut off, measure with `getBoundingClientRect()` — screenshot pixel
dimensions don't match CSS pixels (a 1336px viewport screenshots at 1512px).

## Checklist for a color-touching diff

- [ ] No `--tokenA: var(--tokenB)` chains — direct literals, or `color-mix`
- [ ] New themed tokens declared in **all three tiers**
- [ ] `--stake-*` / `--yield-*` still scoped to `.yield-page, .procurement-lab`
- [ ] Shared components still use `var(--stake-x, <light-fallback>)`
- [ ] `catalog.input.css` palette left independent of `app.css`
- [ ] `app.css` grepped for stray global rules duplicating the component's styles
- [ ] Fixed/sticky chrome pinned to a token, never `transparent`
- [ ] Dark-tuned gradients/shadows gated or given a light equivalent
- [ ] Theme-invariant colors carry an explicit text color
- [ ] `npm run build:catalog-css` run if the Tailwind source changed
- [ ] Verified in **all three** theme states — light, dark, and system with the
      OS set to dark — plus one in-app route change to prove the theme survives
