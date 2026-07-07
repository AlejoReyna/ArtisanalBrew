# Artisanal Brew — WebGL Hero Redesign Plan

## Objective
Replace the static background-image hero with an immersive, built-from-scratch WebGL experience that maintains the existing warm coffee palette and content structure, while drawing visual inspiration from the 3D/interactive aesthetics of `futureoffinance.peachweb.io` and the bold minimalism of `flowty.co`.

---

## Current State
- **Hero.razor**: Static `editorial-hero--coffee` with Unsplash background image + CSS gradient overlay.
- **Palette (locked)**:
  - Surface: `#fbf9f4`
  - Ink: `#2d2421`
  - Accent: `#8d6f55`
  - Muted: `#746a63`
  - Line: `#d8d0c4`
  - Charcoal: `#1f1a18`
  - White: `#fffdf9`
- **Fonts**: Inter (sans), Playfair Display (display), EB Garamond.
- **Text content**: Eyebrow "Est. 2024", H1 "A sanctuary for the *patient brewer.*", Lede, two CTA buttons.

---

## Design Direction: "Coffee Atmosphere"

### Core Visual Concept
A warm, living procedural field that evokes the sensory experience of coffee — rising steam, floating aroma particles, and subtle liquid-surface movement. The hero becomes an atmospheric canvas rather than a static photograph.

### Reference Synthesis
| From `futureoffinance.peachweb.io` | From `flowty.co` |
|---|---|
| WebGL 3D depth & floating elements | Bold typographic hierarchy & dark restraint |
| Interactive particle systems | Generous whitespace & clean structure |
| Immersive background animation | Minimalist color discipline |

### WebGL Effect Specification
1. **Procedural Warm Noise Field** (fragment shader)
   - Layered simplex noise in warm tones (ink → accent → cream)
   - Slow temporal animation (0.15x speed) — gentle, contemplative
   - Mouse-reactive parallax: noise field shifts subtly toward cursor

2. **Floating Aroma Particles** (vertex shader + point sprites)
   - 300–500 soft-edged circular particles
   - Warm palette: cream (`#fbf9f4`), gold-accent (`#c4a574`), muted (`#a08b7a`)
   - Particles rise slowly (steam-like drift) with Perlin noise displacement
   - Mouse attraction: particles within 200px radius gently flow toward cursor
   - Particle size varies 1.5–4px with depth-based opacity

3. **Vignette & Atmosphere**
   - Soft radial vignette darkening edges to `#1f1a18`
   - Subtle film grain overlay (2% intensity) for tactile warmth
   - Bottom-heavy gradient ensuring text readability

### Layout & Typography (unchanged structure)
- Full viewport height (`100dvh`)
- Content aligned bottom-left (self-end)
- All text remains exactly as-is — same copy, same fonts, same hierarchy
- Text has subtle text-shadow/glow for legibility over animated background

### Performance Constraints
- Target 60fps on mid-tier devices
- Use `requestAnimationFrame` with delta-time
- Pause rendering when hero is not visible (`IntersectionObserver`)
- Destroy WebGL context on component disposal (Blazor `DisposeAsync`)
- Total JS payload < 15KB (vanilla WebGL, no Three.js dependency)

---

## Implementation Files

| File | Purpose |
|---|---|
| `wwwroot/js/heroWebGL.js` | Vanilla WebGL module: shader compilation, particle system, render loop, mouse/resize handlers |
| `Components/Home/Hero.razor` | Updated markup: `<canvas>` element + same content overlay, JS interop init/dispose |
| `wwwroot/app.css` | New `.hero-canvas` styles, text-shadow legibility helpers, reduced-motion fallback |

---

## Integration Points
- **Blazor JS Interop**: `OnAfterRenderAsync` calls `heroWebGL.init(canvasId)`
- **Disposal**: `DisposeAsync` calls `heroWebGL.destroy()` to clean up WebGL context
- **Resize**: Handled internally via `ResizeObserver` on the canvas container
- **Reduced Motion**: If `prefers-reduced-motion: reduce`, render static frame only, disable particle animation

---

## Build Order
1. Write `heroWebGL.js` — full vanilla WebGL implementation
2. Update `Hero.razor` — integrate canvas + JS interop
3. Update `app.css` — hero canvas positioning, text legibility, fallbacks
4. Verify: build passes, no console errors, 60fps maintained
