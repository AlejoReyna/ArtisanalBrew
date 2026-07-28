---
name: menu-detail-style
description: Applies the "menu-detail" visual language from the Steak Chimichurri reference screen — bold all-caps sans headline, loyalty stamp callout, allergen meta line, outlined certification badge (K/V/GF), "Top seller" pill, and a divided media row with a play-button thumbnail — to ArtisanalBrew's Blazor pages and components. Trigger this whenever the user asks to style something "like the chimichurri screen", in the "menu-detail style" or "market-card style", or asks for loyalty/stamp badges, allergen lines, certification badges, "Top seller"-style tags, or thumbnail-with-play media rows. Do not use it for the app's existing Playfair-serif storefront look (ProductDetails.razor's current dressing) unless the user explicitly asks to convert that page to this style.
---

# Menu-detail style

This captures a specific reference screen: a mobile menu-item detail sheet with a
warm hero panel up top, a bold all-caps sans title, a loyalty stamp callout, gray
body copy, an allergen meta line, an outlined certification badge, a "Top seller"
pill, a divided media row with a play-button thumbnail, and an all-caps section
header ("GARNISH") below another divider. It's editorial-restaurant-menu, not
e-commerce-catalog — closer to a tasting menu card than a product listing.

It is a **sibling** to ArtisanalBrew's existing storefront look (the Playfair
Display serif titles on `ProductDetails.razor`), not a replacement for it. Only
apply it where the user asks for this specific aesthetic.

## Ground it in the app's existing tokens, don't invent new ones

ArtisanalBrew already has a warm, artisanal palette in
[app.css](../../../src/ThisCafeteria.Web/wwwroot/app.css) that maps onto this
reference almost one-to-one. Reuse these rather than hardcoding new hex values:

| Role in the reference | Use this token | Notes |
|---|---|---|
| Warm hero panel background | `var(--surface-deep, #f2eee5)` | The beige behind the plate photo |
| White content sheet | `var(--white, #fffdf9)` | `ProductDetails.razor.css` uses literal `#ffffff` for pixel parity with its own image — either is fine, prefer the var for new components |
| Title / price / section headers | `var(--ink, #2d2421)` | Near-black warm ink, not pure `#000` |
| Body copy (description) | `var(--muted, #746a63)` | |
| Allergen / meta line | `var(--muted, #746a63)` at reduced opacity — see below | Lighter than body copy in the reference |
| Divider lines | `var(--line, #d8d0c4)` or `rgba(45, 36, 33, 0.08)` | The app already uses the rgba form for hairlines (see `product-detail-card__buybar`'s `border-top`) |
| Loyalty stamp accent | `var(--crypto-positive, #47614e)` | The app already has a moss-green "positive" color — perfect semantic fit for a loyalty/reward stamp, no new color needed |
| Pills / outlined badges | border `1px solid var(--ink)`, background `var(--white)` | Matches the outlined, not-filled treatment in the reference |
| Font, UI text | `var(--font-sans)` (Inter) | The reference's title is a bold sans, **not** the app's Playfair Display serif |

For the "lighter than body copy" allergen tone, don't add a new named token —
follow the pattern components in this codebase already use (see
`ProductDetails.razor.css`'s `:host, .product-detail-card { --card-muted: ... }`
or `ChainBadge.razor.css`'s `--stake-faint`): declare a local scoped variable at
the component root, e.g. `--meta-ink: rgba(45, 36, 33, 0.5);`, rather than
reaching for a global one that doesn't exist yet.

## Typography rules specific to this look

- **Title** (`STEAK CHIMICHURRI`): `var(--font-sans)`, weight 800–900, uppercase,
  **tight** letter-spacing (`-0.01em` to `0`). This is the one place in the
  reference where uppercase text is *not* wide-tracked — at display size, wide
  tracking on a full headline reads as loose rather than crisp.
- **Eyebrow / section-header labels** (`GARNISH`): same weight and case, but can
  take the app's existing wide-tracking uppercase convention
  (`letter-spacing: 0.06em`–`0.18em`) since it's already established all over
  `app.css` for micro-labels — use judgment based on the label's size.
- **Body copy** (dish description): regular weight, `var(--muted)`, `line-height`
  around `1.6`, sentence case.
- **Allergen / meta line**: smaller than body (`0.72rem`–`0.78rem`), the lighter
  `--meta-ink` tone, sentence case, items joined with a mid-dot (`·`) — see the
  `product-detail-card__highlight-divider` pattern already in the codebase for
  precedent on dot-joined inline lists.
- **Pill / badge labels** ("Top seller"): sentence case, not uppercase — this
  differs from the app's other pill (`chain-badge`), which is uppercase. Match
  what's in the reference, not the existing pill, when building this look.

## Component patterns

Full copy-paste-ready Razor + scoped CSS for each of these is in
[references/components.md](references/components.md). Read that file when
you're about to build one of these — don't reconstruct them from memory, the
exact spacing and radii matter for the look to read as intentional:

1. **Loyalty stamp callout** — small filled circular icon + gray label, e.g.
   `+1 Stamp · Garden & Market Challenge`.
2. **Certification badge** — an outlined rounded-square glyph badge (K / V / GF),
   not a filled icon.
3. **Outlined pill tag** — "Top seller"-style: icon + sentence-case label,
   1px outline, white fill, fully rounded.
4. **Media row** — a label on the left, a rounded thumbnail with a centered
   circular play button on the right, bounded by dividers above and below.
5. **Section divider** — a plain hairline rule with generous margin, used to
   separate description → stamp/allergens → media → next section.
6. **Section header** — the all-caps `GARNISH`-style block label that starts a
   new section after a divider.

## Layout rhythm

- Reuse the existing hero → rounded-sheet transition already built in
  `ProductDetails.razor.css` (`product-detail-card__sheet` overlaps the image
  by `margin-top: -1.5rem` with `border-radius: 1.5rem 1.5rem 0 0`) instead of
  rebuilding that from scratch — it's the same structural idea as the
  reference's beige-panel-into-white-sheet reveal.
- Content padding: `1.5rem` horizontal on mobile (matches `product-detail-card__sheet`),
  `clamp(2rem, 4vw, 4rem)` on desktop if the page has a desktop split view.
- Vertical rhythm between blocks: `1–1.25rem`. Dividers get more breathing room:
  `1.5rem` margin above and below.
- Badges/pills/media-row thumbnails share a radius scale already used elsewhere
  in the app: `999px` for pills (see `chain-badge`, `btn-add-cart`), `0.75rem`
  for the media thumbnail, and a smaller `0.5rem`–`0.75rem` for the certification
  badge's rounded square.

## When wiring this into a real page or component

- Follow the codebase's existing BEM-ish naming: `.block__element--modifier`
  (see `chain-badge__id`, `product-detail-card__price-symbol`). Don't invent a
  different naming convention.
- Scope styles in the component's own `.razor.css` file with Blazor's CSS
  isolation, same as every existing component under `Components/Shared/`.
- If you're adding this to an existing page rather than a new component, check
  whether a structurally similar block already exists (e.g. `ProductDetails.razor`
  already has hero/sheet/title/price/description) and extend it rather than
  parallel-building a second hero+sheet layout on the same page.
- After building, verify visually in the browser preview — this is a visual
  style, screenshots are the real test, not just "does it compile."
