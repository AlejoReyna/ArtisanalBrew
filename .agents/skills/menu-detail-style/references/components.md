# Menu-detail style — component snippets

Copy these as a starting point and adapt names/params to the page you're
building on. Each one is scoped CSS (Blazor CSS isolation), matching the
BEM-ish naming and token usage described in [SKILL.md](../SKILL.md).

## 1. Loyalty stamp callout

```razor
@* StampCallout.razor *@
<div class="stamp-callout">
    <span class="stamp-callout__icon" aria-hidden="true">
        <svg width="10" height="10" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round">
            <path d="M20 6 9 17l-5-5" />
        </svg>
    </span>
    <span class="stamp-callout__label">@Label</span>
</div>

@code {
    [Parameter] public string Label { get; set; } = string.Empty;
}
```

```css
/* StampCallout.razor.css */
.stamp-callout {
    align-items: center;
    display: inline-flex;
    gap: 0.5rem;
}

.stamp-callout__icon {
    align-items: center;
    background: var(--crypto-positive, #47614e);
    border-radius: 50%;
    color: var(--white, #fffdf9);
    display: inline-flex;
    flex-shrink: 0;
    height: 1.1rem;
    justify-content: center;
    width: 1.1rem;
}

.stamp-callout__label {
    color: var(--ink, #2d2421);
    font-family: var(--font-sans);
    font-size: 0.82rem;
    font-weight: 500;
}
```

Usage: `<StampCallout Label="+1 Stamp · Garden &amp; Market Challenge" />`

## 2. Certification badge (outlined rounded-square glyph)

```razor
@* CertificationBadge.razor *@
<span class="cert-badge" title="@Title">@Glyph</span>

@code {
    [Parameter] public string Glyph { get; set; } = "K";
    [Parameter] public string? Title { get; set; }
}
```

```css
/* CertificationBadge.razor.css */
.cert-badge {
    align-items: center;
    border: 1.5px solid var(--ink, #2d2421);
    border-radius: 0.5rem;
    color: var(--ink, #2d2421);
    display: inline-flex;
    font-family: var(--font-sans);
    font-size: 0.8rem;
    font-weight: 700;
    height: 1.9rem;
    justify-content: center;
    width: 1.9rem;
}
```

Stack a few side by side (`K`, `V`, `GF`) with `gap: 0.5rem` on a wrapping flex row.

## 3. Outlined pill tag ("Top seller")

```razor
@* PillTag.razor *@
<span class="pill-tag">
    @if (IconMarkup is not null)
    {
        <span class="pill-tag__icon" aria-hidden="true">@IconMarkup</span>
    }
    <span class="pill-tag__label">@Label</span>
</span>

@code {
    [Parameter] public string Label { get; set; } = string.Empty;
    [Parameter] public RenderFragment? IconMarkup { get; set; }
}
```

```css
/* PillTag.razor.css */
.pill-tag {
    align-items: center;
    background: var(--white, #fffdf9);
    border: 1px solid var(--ink, #2d2421);
    border-radius: 999px;
    color: var(--ink, #2d2421);
    display: inline-flex;
    font-family: var(--font-sans);
    font-size: 0.78rem;
    font-weight: 500;
    gap: 0.35rem;
    padding: 0.3rem 0.75rem 0.3rem 0.6rem;
    /* sentence case — do not uppercase, unlike chain-badge elsewhere in the app */
}

.pill-tag__icon {
    align-items: center;
    display: inline-flex;
    height: 0.85rem;
    width: 0.85rem;
}
```

## 4. Media row (label + play-button thumbnail)

```razor
@* MediaRow.razor *@
<div class="media-row">
    <p class="media-row__label">@Label</p>
    <div class="media-row__thumb" style="background-image:url('@ThumbnailSrc')" role="img" aria-label="@Label">
        <span class="media-row__play" aria-hidden="true">
            <svg width="12" height="12" viewBox="0 0 24 24" fill="currentColor"><path d="M8 5v14l11-7z" /></svg>
        </span>
    </div>
</div>

@code {
    [Parameter] public string Label { get; set; } = string.Empty;
    [Parameter] public string ThumbnailSrc { get; set; } = string.Empty;
}
```

```css
/* MediaRow.razor.css */
.media-row {
    align-items: center;
    display: flex;
    gap: 1rem;
    justify-content: space-between;
    padding: 1rem 0;
}

.media-row__label {
    color: var(--ink, #2d2421);
    font-family: var(--font-sans);
    font-size: 0.95rem;
    line-height: 1.35;
    margin: 0;
    max-width: 60%;
}

.media-row__thumb {
    align-items: center;
    background-position: center;
    background-size: cover;
    border-radius: 0.75rem;
    display: flex;
    flex-shrink: 0;
    height: 3.75rem;
    justify-content: center;
    position: relative;
    width: 5rem;
}

.media-row__play {
    align-items: center;
    background: rgba(255, 255, 255, 0.92);
    border-radius: 50%;
    color: var(--ink, #2d2421);
    display: inline-flex;
    height: 1.75rem;
    justify-content: center;
    width: 1.75rem;
}
```

## 5. Section divider

```razor
<hr class="section-divider" />
```

```css
.section-divider {
    background: var(--line, #d8d0c4);
    border: none;
    height: 1px;
    margin: 1.5rem 0;
}
```

Prefer `rgba(45, 36, 33, 0.08)` instead of `var(--line)` if the divider sits on
a colored (non-white) background, matching how `product-detail-card__buybar`
already does its `border-top`.

## 6. Section header (`GARNISH`-style)

```razor
<h2 class="section-header">@Text</h2>
```

```css
.section-header {
    color: var(--ink, #2d2421);
    font-family: var(--font-sans);
    font-size: 1.1rem;
    font-weight: 800;
    letter-spacing: -0.005em;
    margin: 0 0 0.75rem;
    text-transform: uppercase;
}
```

Same tight-tracking rule as the main title — this is a headline-weight label,
not a micro eyebrow, so don't add wide `letter-spacing` here.
