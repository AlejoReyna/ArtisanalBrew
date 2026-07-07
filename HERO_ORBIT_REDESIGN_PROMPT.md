# Hero Redesign Prompt — "Orbit Composition" Match

## Reference
Screenshot of `futureoffinance.peachweb.io`'s hero (dark navy AI-finance landing page). Large-scale, confident 3D scene; floating glass nav; two-zone bottom content split.

## Objective
Rebuild `src/ThisCafeteria.Web/Components/Home/Hero.razor` (markup + `wwwroot/js/heroWebGL.js` background) to match this reference's **composition and scale-confidence**, not its content. Keep ArtisanalBrew's existing palette tokens and fonts (Playfair Display + Inter) — do not adopt the reference's navy/cyan palette or import a new typeface. Reuse the analytic-shader WebGL approach already built (no mesh geometry, no three.js) rather than starting over; the existing "Suspended Pour" droplet/ripple concept should become the reinterpreted background object, not be discarded.

---

## 1. Structure to copy exactly

- Full-bleed dark hero, content pinned to corners/edges, large open negative space through the middle — nothing centered.
- **Top-left:** wordmark, single line, small.
- **Top-right:** a floating rounded "glass" pill nav bar — blurred dark-translucent capsule containing nav links, followed by a solid high-contrast CTA button with a small circular arrow glyph.
- **Background:** 2–3 large, confidently-scaled graphic/3D elements placed asymmetrically (not scattered evenly like generic particles):
  - One dominant rounded form, upper-right, partially bleeding off the top edge.
  - One large sweeping curved glow/arc, lower-right, sweeping toward the bottom edge.
  - One textured diagonal panel, upper-left, giving the top-left quadrant graphic weight so the wordmark doesn't float on flat black.
- **Bottom-left:** oversized two-line headline, tight leading, no serif.
- **Directly under the headline:** one short tagline line + a small social-proof row (overlapping avatar circles). *(New content beyond current hero copy — see open questions.)*
- **Bottom-right, lower third, right-aligned:** a short two-line supporting sentence + two pill buttons stacked close together (one solid, one secondary/outline).

## 2. What changes: palette

| Reference (navy) | → | ArtisanalBrew token |
|---|---|---|
| Near-black navy background `#0a0e14` | → | deep charcoal, darker than `--charcoal` (`#1f1a18`) — introduce a near-black warm value, e.g. `#14100d` |
| Ribbed panel navy `#131a26` | → | warm dark brown one step up, e.g. `#201a16` |
| Matte black sphere w/ cool cyan rim light | → | dark coffee-orb silhouette w/ warm rim light in `--accent` gold-brown (`#c2a47d`) |
| Pale cyan-white glow arc `#cfe0ea` | → | warm cream/gold glow (`#fbf9f4` / `#c2a47d`) |
| Translucent dark glass nav pill | → | same glass treatment, tinted warm: `rgba(33,27,23,0.55)` + `rgba(251,249,244,.14)` hairline border |
| Solid black CTA pill, white text | → | reuse existing `.button--dark` (ink `#2d2421` bg, cream text) |
| White secondary pill, dark text | → | reuse existing `.button--ghost` inverted for dark backgrounds |

## 3. What stays as-is: fonts + copy

- **Playfair Display**, italic emphasis word, for the headline — this is the site's core typographic signature; do not swap to a bold sans just because the reference uses one. "Similar fonts" = keep using what's already loaded (Playfair Display + Inter), not the reference's font.
- **Inter** for nav links, eyebrow, lede, buttons — same weights/letter-spacing conventions used elsewhere on the site.
- Existing hero copy stays: eyebrow "Est. 2024", H1 "A sanctuary for the *patient brewer.*", lede, "Explore the menu" / "Our story" CTAs — mapped into the reference's zones (see open question 1 on the tagline/avatar row).

## 4. Background scene reinterpretation (extend `heroWebGL.js`, don't rewrite)

- **Dominant sphere (upper-right)** → scale up and reposition the existing droplet from the "Suspended Pour" shader as the hero's single dominant object, warm rim-lit, instead of inventing new geometry.
- **Sweeping glow arc (lower-right)** → this is already coded: the rim term in `shadePlane()`'s ripple shading. Scale it up and reposition so it sweeps from the bottom-right corner, matching the reference's arc instead of sitting small and centered.
- **Ribbed diagonal panel (upper-left)** → reinterpret as soft warm light rays or a steam curtain, OR drop it — it's the most "abstract-financial" element and the least coffee-relevant; least essential to keep literally.
- Keep everything analytic (ray/plane/sphere math in the fragment shader) — no mesh buffers, no added dependencies.

## 5. Layout/CSS notes

- Bottom content needs a **two-column split** (currently a single bottom-left stack): headline column + subhead/CTA column, both bottom-anchored, matching the reference's left/right weight distribution.
- Reuse `.button--dark` / `.button--ghost` for the two CTA pairs rather than inventing new button styles.
- Preserve: `prefers-reduced-motion` static fallback, `aria-hidden` on the canvas, IntersectionObserver pause-when-offscreen.

## 6. Open questions before implementation

1. **Tagline + avatar row** under the headline is new content (nothing like it exists today). Invent copy (e.g. a small "Est. 2024 · Brewed in small batches" line) and reuse product/customer imagery for avatars, or drop this element and let the headline block breathe instead?
2. **Floating glass-pill nav** — scope this to the homepage hero only (cheap, contained), or does this become the site's new global nav treatment across all pages (bigger change touching `NavMenu.razor`)?
3. Confirm the ribbed-panel decision (reinterpret vs. drop) before building it — it's the single element with no clean coffee metaphor.
