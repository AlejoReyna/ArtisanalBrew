# Starbucks-Inspired Design Specification for ThisCafeteria

## 1. Color Palette (Starbucks-Adapted)

### Primary Greens (Starbucks → Artisanal)
```css
--sb-green: #00754A;          /* Fun Green — primary brand green */
--sb-green-deep: #006241;    /* Starbucks Green — deeper variant */
--sb-green-dark: #1E3932;   /* Dark Green — for dark sections */
--sb-green-muted: #2b5148;  /* Medium Green — for accents */
--sb-green-light: #D4E9E2;  /* Skeptic — soft green for backgrounds */
--sb-green-glow: rgba(0, 117, 74, 0.15); /* Green glow effect */
```

### Warm Neutrals (Preserve artisanal warmth)
```css
--sb-cream: #fbf9f4;          /* Warm cream — main canvas */
--sb-cream-warm: #F2EEE5;    /* Deeper cream — for cards */
--sb-white: #fffdf9;         /* Off-white */
--sb-ink: #2d2421;           /* Dark ink — primary text */
--sb-charcoal: #1f1a18;      /* Charcoal — headings */
--sb-muted: #746a63;         /* Muted brown — secondary text */
--sb-line: #d8d0c4;          /* Warm line — borders */
--sb-accent: #8d6f55;        /* Warm brown accent — preserve for warmth */
```

### Gold System (Rewards/Stars only)
```css
--sb-gold: #CBA258;           /* Starbucks gold — stars, rewards */
--sb-gold-light: #E8D5A3;     /* Light gold — highlights */
--sb-gold-glow: rgba(203, 162, 88, 0.2); /* Gold glow */
```

## 2. Typography Hierarchy

```css
--font-display: 'Playfair Display', Georgia, serif;  /* Keep — editorial feel */
--font-sans: 'Inter', 'Helvetica Neue', Helvetica, Arial, sans-serif; /* Keep */
```

### Scale
- **H1 (Hero)**: `clamp(3.5rem, 9vw, 7rem)`, weight 400, line-height 0.92, letter-spacing -0.035em
- **H2 (Section)**: `clamp(2.5rem, 5.5vw, 5rem)`, weight 400, line-height 0.95
- **H3 (Card)**: `clamp(1.4rem, 2.2vw, 2.2rem)`, weight 400
- **Eyebrow**: `0.75rem`, weight 600, letter-spacing 0.18em, uppercase, color: var(--sb-green)
- **Body**: `clamp(1rem, 1.5vw, 1.15rem)`, line-height 1.75, color: var(--sb-muted)
- **CTA Button**: `0.85rem`, weight 600, letter-spacing 0.08em, uppercase

## 3. Button System (Starbucks Pill Style)

### Primary CTA (Green Pill)
```css
.btn-sb-primary {
    background: var(--sb-green);
    border: 2px solid var(--sb-green);
    border-radius: 999px;
    color: var(--sb-white);
    font-size: 0.85rem;
    font-weight: 600;
    letter-spacing: 0.08em;
    min-height: 3.25rem;
    padding: 0.85rem 2rem;
    text-transform: uppercase;
    transition: all 300ms cubic-bezier(0.16, 1, 0.3, 1);
    box-shadow: 0 4px 16px rgba(0, 117, 74, 0.25);
}
.btn-sb-primary:hover {
    background: var(--sb-green-deep);
    border-color: var(--sb-green-deep);
    box-shadow: 0 8px 28px rgba(0, 117, 74, 0.35);
    transform: translateY(-2px);
}
```

### Secondary CTA (Ghost Pill)
```css
.btn-sb-ghost {
    background: transparent;
    border: 2px solid var(--sb-green);
    border-radius: 999px;
    color: var(--sb-green);
    font-size: 0.85rem;
    font-weight: 600;
    letter-spacing: 0.08em;
    min-height: 3.25rem;
    padding: 0.85rem 2rem;
    text-transform: uppercase;
    transition: all 300ms cubic-bezier(0.16, 1, 0.3, 1);
}
.btn-sb-ghost:hover {
    background: var(--sb-green);
    color: var(--sb-white);
    transform: translateY(-2px);
}
```

### Gold CTA (Rewards)
```css
.btn-sb-gold {
    background: var(--sb-gold);
    border: 2px solid var(--sb-gold);
    border-radius: 999px;
    color: var(--sb-charcoal);
    font-size: 0.85rem;
    font-weight: 700;
    letter-spacing: 0.06em;
    min-height: 3.25rem;
    padding: 0.85rem 2rem;
    text-transform: uppercase;
    transition: all 300ms cubic-bezier(0.16, 1, 0.3, 1);
    box-shadow: 0 4px 16px rgba(203, 162, 88, 0.3);
}
.btn-sb-gold:hover {
    background: var(--sb-gold-light);
    box-shadow: 0 8px 28px rgba(203, 162, 88, 0.4);
    transform: translateY(-2px);
}
```

## 4. Animation System

### Keyframes Library
```css
@keyframes sb-float {
    0%, 100% { transform: translateY(0) rotate(0deg); }
    50% { transform: translateY(-20px) rotate(3deg); }
}

@keyframes sb-pulse-glow {
    0%, 100% { box-shadow: 0 0 20px rgba(0, 117, 74, 0.2); }
    50% { box-shadow: 0 0 40px rgba(0, 117, 74, 0.4); }
}

@keyframes sb-shimmer {
    0% { background-position: -200% 0; }
    100% { background-position: 200% 0; }
}

@keyframes sb-star-spin {
    0% { transform: rotate(0deg) scale(1); }
    50% { transform: rotate(180deg) scale(1.1); }
    100% { transform: rotate(360deg) scale(1); }
}

@keyframes sb-gradient-shift {
    0% { background-position: 0% 50%; }
    50% { background-position: 100% 50%; }
    100% { background-position: 0% 50%; }
}

@keyframes sb-reveal-up {
    from { opacity: 0; transform: translateY(40px); filter: blur(8px); }
    to { opacity: 1; transform: translateY(0); filter: blur(0); }
}

@keyframes sb-scale-in {
    from { opacity: 0; transform: scale(0.9); }
    to { opacity: 1; transform: scale(1); }
}

@keyframes sb-line-draw {
    from { transform: scaleX(0); }
    to { transform: scaleX(1); }
}
```

### Scroll Animation Classes
```css
.sb-animate { opacity: 0; transform: translateY(30px); transition: all 800ms cubic-bezier(0.16, 1, 0.3, 1); }
.sb-animate.is-visible { opacity: 1; transform: translateY(0); }
.sb-animate-delay-1 { transition-delay: 100ms; }
.sb-animate-delay-2 { transition-delay: 200ms; }
.sb-animate-delay-3 { transition-delay: 300ms; }
```

## 5. Section Specifications

### Hero Section
- **Layout**: Full viewport, centered content, dark gradient overlay on hero image
- **Background**: Keep Unsplash coffee image but add **animated green gradient overlay** (subtle)
- **Add**: Floating coffee bean particles (CSS-only, 5-8 particles)
- **Eyebrow**: "EST. 2024" in green (#00754A) with tracking animation
- **Headline**: "A sanctuary for the patient brewer." — white, with word-by-word reveal
- **Lede**: White text, max-width 520px
- **CTAs**: 
  - Primary: "Explore the Menu" (green pill, pulse-glow animation)
  - Secondary: "Our Story" (ghost pill, white border)
- **Bottom**: Animated downward chevron / scroll indicator

### Concept Section (Web3)
- **Layout**: Two-column split (60/40), cream left, dark green (#1E3932) right
- **Left**: Headline in green, body text, CTA "Explore Roasts" (green pill)
- **Right**: Dark green background with subtle animated gradient mesh
- **Add**: Floating "On-Chain" badge with green glow pulse
- **Trust Badges**: Animated green checkmarks, "Immutable Transactions", "Ethereum Powered"

### Offerings Section (Menu Highlights)
- **Layout**: 3-column grid, cards with **3D tilt on hover** (vanilla JS, not CSS-only)
- **Card Style**: Cream background, green top border accent, rounded-2xl
- **Card Hover**: Lift + shadow + green glow border
- **Animations**:
  - Espresso card: Steam rising (enhanced — 3 steam wisps, staggered)
  - Latte card: Pouring milk line + expanding foam ring
  - Cold Brew card: Floating ice cubes (3, rotating)
- **Index Badge**: Green circle with white number, floating top-right
- **Add**: "View Full Menu" bottom CTA (green ghost pill, centered)

### Journal Section (Editorial)
- **Layout**: Sidebar (sticky) + article list
- **Accent**: Green horizontal line at top that draws itself on scroll
- **Cards**: Cream background, hover: lift + image zoom + green left border
- **Dark card variant**: Dark green (#1E3932) background, white text
- **Meta badges**: Green text for category labels

### NEW: Rewards Banner Section
- **Layout**: Full-width, green gradient background (#00754A → #1E3932)
- **Content**: 
  - Headline: "Every sip earns you stars" (white)
  - Sub: "Connect your wallet and earn rewards with every on-chain purchase." (green-light)
  - Animated star constellation (3-5 stars, slow rotation, gold color)
  - CTA: "Start Earning Stars" (gold pill button)
- **Decor**: Floating coffee beans, subtle particle field
- **Placement**: Between Offerings and Journal

## 6. Global Effects

### Floating Particles (Hero only, CSS)
- 6-8 small circles (4-12px)
- Colors: green glow, gold glow, white glow
- Animation: slow float + drift, 15-25s duration, infinite
- Opacity: 0.3-0.6

### Green Gradient Mesh (Concept right column, Rewards background)
```css
background: 
    radial-gradient(ellipse at 20% 30%, rgba(0, 117, 74, 0.15) 0%, transparent 50%),
    radial-gradient(ellipse at 80% 70%, rgba(30, 57, 50, 0.2) 0%, transparent 50%),
    linear-gradient(135deg, #1E3932 0%, #2b5148 100%);
background-size: 200% 200%;
animation: sb-gradient-shift 15s ease infinite;
```

### Scroll Progress Bar (Top of page)
- Thin green line (#00754A) at top of viewport
- Width = scroll progress %
- Fixed position, z-index 100

### Reduced Motion
All animations must respect `prefers-reduced-motion: reduce` — disable transforms, set opacity to 1, remove animations.

## 7. File Mapping

| Component | Razor File | CSS File | Notes |
|-----------|------------|----------|-------|
| Hero | `Components/Home/Hero.razor` | `Components/Home/Hero.razor.css` | New scoped CSS |
| Concept | `Components/Home/Concept.razor` | `Components/Home/Concept.razor.css` | New scoped CSS |
| Offerings | `Components/Home/Offerings.razor` | `Components/Home/Offerings.razor.css` | New scoped CSS |
| Journal | `Components/Home/Journal.razor` | `Components/Home/Journal.razor.css` | New scoped CSS |
| RewardsBanner | `Components/Home/RewardsBanner.razor` | `Components/Home/RewardsBanner.razor.css` | NEW component |
| Home (container) | `Components/Pages/Home.razor` | — | Inline styles + script |
| Global | — | `wwwroot/app.css` | Palette + buttons + keyframes |
| Animations | — | `wwwroot/js/animations.js` | Enhanced IntersectionObserver |
