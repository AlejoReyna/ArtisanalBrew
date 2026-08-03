---
name: blazor-interop
description: Write or fix vanilla-JS modules in ArtisanalBrew's Blazor Web App (interactive server rendering) so they survive enhanced navigation, prerendering, and component re-renders. Use when adding a file under wwwroot/js, wiring IJSRuntime from a .razor component, or debugging symptoms like "works on hard refresh but not after navigating", "runs twice", "state resets on route change", "JSDisconnectedException", "element is null in OnAfterRenderAsync", "animation doesn't replay", or a script whose effect is silently undone by a page transition.
---

# JS interop that survives enhanced navigation

Blazor Web App with interactive server rendering has three separate mechanisms
that replace DOM nodes out from under you, and they have different rules:

1. **Enhanced navigation** — a route change fetches the new page and patches it
   into the live document. No page reload. `<script>` tags in the new document
   are *not* re-executed, `DOMContentLoaded` does *not* fire again.
2. **Prerender → interactive handoff** — a component renders once on the server
   as static HTML, then Blazor connects the circuit and **swaps out the
   prerendered nodes** for interactive ones.
3. **Re-render** — a normal `StateHasChanged` can recreate a whole subtree.

Anything you attached to a node dies in all three. Anything you attached to
`window` or `Element.prototype` survives all three. That asymmetry is the whole
skill, and it's why this codebase has 28 hand-rolled JS modules that all look
alike: they are all shaped by the same constraint.

**Everything here is specific to ArtisanalBrew's `App.razor` bootstrap.** Read
[App.razor](../../../src/ThisCafeteria.Web/Components/App.razor) once before
writing a new module — it is the whole lifecycle in one file.

## The house pattern

Every module in `wwwroot/js` follows the same three-part shape. Match it; a
module that doesn't will work on a hard refresh and quietly break on the first
route change, which is the most expensive way to get this wrong because it
survives casual testing.

**1. Idempotent singleton on `window`.**

```js
window.myFeature = window.myFeature || {
    _observer: null,
    init() { /* safe to call any number of times */ },
};
```

The `|| {}` guard preserves accumulated state if the file is somehow evaluated
twice. `init()` must be **re-entrant**: disconnect the previous observer, clear
the previous timer, re-query the DOM. It gets called on every navigation, not
once. `window.initAnimations` is the reference implementation — its first act is
`if (window.animationObserver) window.animationObserver.disconnect()`.

**2. A `queue*` wrapper, double-rAF.**

```js
window.queueMyFeature = () => {
    requestAnimationFrame(() => {
        requestAnimationFrame(() => {
            if (window.myFeature) window.myFeature.init();
        });
    });
};
```

Two frames, not one. After an enhanced navigation the new nodes exist but have
not been laid out; anything reading `getBoundingClientRect()` or starting a
transition in the first frame reads zeros or gets its starting style coalesced
into the end state. The second rAF runs after layout. The `typeof`/truthiness
guard matters because the module file may be `defer`-loaded and not yet
evaluated on the first call.

Work that must be visible *before the next paint* goes synchronously in the
wrapper, before the rAFs — `queueAnimations` calls `revealInView()` synchronously
so freshly-swapped content is never transparent for a frame, then does the
observer re-arm inside the rAFs.

**3. Registration in both lifecycles.**

```js
document.addEventListener('DOMContentLoaded', window.queueMyFeature);
// ...and inside onEnhancedLoad in App.razor
```

Both. `DOMContentLoaded` covers the cold load and never fires again; `enhancedload`
covers every route change and never fires on the cold load. Miss the first and
the feature is broken on direct entry; miss the second and it's broken on
navigation. The `enhancedload` listener is registered three ways in `App.razor`
(`Blazor.addEventListener`, plus `document` for both `enhancedload` and
`blazor:enhancedload`) because the event name has moved across versions — leave
all three.

> When passing a handler that takes arguments, **wrap it**:
> `() => window.queueAnimations(false)`, never `window.queueAnimations` directly.
> Passed by reference it receives the `Event` object as its first parameter, and
> `Event` is truthy — so `instant` becomes `true` and you silently get the
> route-change behaviour on a cold load.

## What enhanced navigation destroys

Ranked by how long each takes to diagnose.

**`<html>` attributes are replaced with what the server rendered.** This is the
one that costs a full afternoon. `data-theme` is set only by client-side JS
(`theme.js` knows the stored/OS preference; the server does not), so every route
change resets the theme to light until `onEnhancedLoad` reapplies it:

```js
window.artisanalTheme.apply(window.artisanalTheme.getStored());
```

Any state you keep as an attribute on `<html>` or `<body>` needs the same
reapply. Prefer `localStorage` as the source of truth with the attribute as a
derived projection — never the reverse.

**`<head>` injections vanish.** Dynamically inserted `<style>` and `<link>`
elements are gone after a route change. Put styles in `app.css` or a scoped
`.razor.css`, not in an injected tag.

**Element references and listeners go stale.** Anything closed over a node from
the previous page points at a detached node. Re-query in `init()`.

**`Element.prototype` patches persist.** They're on the prototype, not the
document. This is the lever for debugging (see below) and a hazard for
production code — don't ship prototype patches, they'll silently accumulate.

**Nodes appear without any navigation at all.** Prerender→interactive handoff and
re-renders swap subtrees mid-page, with no `enhancedload` to hook. That's why
`watchReveals()` exists: a `MutationObserver` on `document.body` with
`{ childList: true, subtree: true }` that re-reveals matching nodes. Two details
worth copying — it observes **`childList` only** (observing attributes while the
callback mutates classes is an infinite loop), and its callback fires as a
microtask, i.e. before the next paint, so there's no blank frame.

## The C# side

```csharp
protected override async Task OnAfterRenderAsync(bool firstRender)
{
    if (firstRender)
    {
        await JS.InvokeVoidAsync("queueAnimations");
    }
}
```

- **Never call JS from `OnInitializedAsync`.** With prerendering it runs on the
  server with no browser attached, and it runs **twice** — once prerendering,
  once after the circuit connects. Data fetching there must be idempotent, and
  JS interop there throws.
- **`OnAfterRenderAsync` is the earliest safe point**, and only under
  `if (firstRender)` unless you specifically want it per-render.
- **Prefer calling a `queue*` wrapper over the raw `init`** so the component gets
  the same double-rAF timing as the navigation path. `Products.razor` calls
  `JS.InvokeVoidAsync("queueAnimations")`.
- **Guard disposal.** On a dropped circuit, interop throws `JSDisconnectedException`.
  Catch it in `DisposeAsync` — the browser is already gone, so there's nothing to
  clean up and nothing to report:

```csharp
public async ValueTask DisposeAsync()
{
    try { await JS.InvokeVoidAsync("myFeature.teardown"); }
    catch (JSDisconnectedException) { }
}
```

- **Unsubscribe C# events you subscribed in `OnInitialized`.** `ChainSelector`
  subscribes to `SelectedChainAccessor.Changed`; a component that subscribes and
  doesn't unsubscribe leaks for the life of the circuit.
- **`DotNetObjectReference` must be disposed**, and null-checked in JS before
  invoking — the JS side can outlive the component.

## Script placement in `App.razor`

Three tiers, and the choice is load-bearing:

**Blocking in `<head>`, before `app.css`** — `theme.js`, `heroType.js`,
`productCardBlurb.js`. Only for modules that must run **before first paint** to
prevent a flash of wrong state. `theme.js` sets `data-theme` before the
stylesheet applies, so there's no light-mode flash. `productCardBlurb.js` adds
`has-card-blurb` to `<html>` at parse time so the collapse rules apply before the
grid first paints — added later, every description paints at full height and then
snaps shut, a flicker across the whole grid.

That last one also shows the right no-script story: **the collapsed state is
opt-in via a class the script adds**, so without JS the description is simply
visible copy. Never hide content in CSS and reveal it with JS.

**After `blazor.web.js`** — everything else. Normal modules.

**`defer` on vendor bundles** — `web3`, `solana-web3`, `delegation-toolkit`.
Callers must tolerate their absence early; that's what the truthiness guards in
the `queue*` wrappers are for.

## Feature guards worth copying

`productCardBlurb.js` gets two right that are commonly missed:

```js
prefersReducedMotion() { return matchMedia('(prefers-reduced-motion: reduce)').matches; },
canHover()            { return matchMedia('(hover: hover)').matches; },
```

The hover guard is the subtle one. On touch, a tap fires `pointerover`, opens the
panel, and **no matching `pointerout` ever arrives** — the card is stuck open with
no way to close it. Gating on `(hover: hover)` isn't a nicety, it's the
difference between working and permanently wedged. Check both in any
pointer-driven module.

## Debugging

From `verify`, and worth repeating because they exploit exactly the persistence
asymmetry above:

- **`Element.prototype` patches survive navigation** — patch
  `Element.prototype.animate` to record keyframes and options, then navigate. The
  instrumentation is still live.
- **CSS animations can be frozen**: `const a = el.getAnimations()[0]; a.pause(); a.currentTime = 300;`
  then screenshot.
- **`productTransition.js` force-removes its dismiss clone after 600ms** via a
  `setTimeout` fallback — slowed-down experiments on it get cut off. Neutralize
  the timeout first or you'll misread the result as a bug.
- **`wwwroot` JS/CSS edits need a `dotnet run` restart**; scoped `.razor.css`
  needs `dotnet build` first. The browser caches `/js/*.js` — confirm you're
  looking at your edit with `fetch(src, {cache: 'reload'})`.

## Review checklist

For any diff touching `wwwroot/js` or adding `IJSRuntime` to a component:

- [ ] `init()` is re-entrant — disconnects/clears prior observers and timers
- [ ] Registered on **both** `DOMContentLoaded` and `enhancedload`
- [ ] Handlers taking arguments are wrapped, not passed by reference
- [ ] Double-rAF before anything reading layout or starting a transition
- [ ] No reliance on `<head>` injections or `<html>` attributes persisting
- [ ] Nodes re-queried in `init()`, not closed over from a previous page
- [ ] JS interop is in `OnAfterRenderAsync`, not `OnInitializedAsync`
- [ ] `DisposeAsync` catches `JSDisconnectedException`; C# events unsubscribed
- [ ] `DotNetObjectReference` disposed; null-checked on the JS side
- [ ] `(hover: hover)` and `prefers-reduced-motion` guards on pointer/motion code
- [ ] Content is visible without JS; the script adds behaviour, not visibility
