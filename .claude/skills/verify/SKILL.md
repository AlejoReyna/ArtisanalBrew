---
name: verify
description: Build, run, and drive ArtisanalBrew (ThisCafeteria.Web) locally to verify changes at the browser surface.
---

# Verify ArtisanalBrew changes

## Fastest handle: dotnet run against the compose Postgres

The docker-compose stack is usually already up (postgres on :5433, web on :8080),
but the web container image is stale relative to the working tree. Don't rebuild
the image — run the working tree directly against the compose database:

```bash
dotnet build src/ThisCafeteria.Web -v q
set -a && source .env && set +a
export ConnectionStrings__DefaultConnection="Host=localhost;Port=5433;Database=${POSTGRES_DB};Username=${POSTGRES_USER};Password=${POSTGRES_PASSWORD}"
dotnet run --project src/ThisCafeteria.Web --no-build --urls http://localhost:5286
```

Poll `http://localhost:5286/products` until 200 (a few seconds). Kill with
`pkill -f "dotnet run --project src/ThisCafeteria.Web"` when done; leave the
docker stack alone.

Do NOT run bare `dotnet run` without a connection string — every routed page
500s in Development (DI validate-on-build crashes; see memory).

## Driving the UI

- Mobile layout kicks in at max-width 960px CSS pixels. Resize the Chrome window
  to ~414 wide; note screenshots come back at 2x DPR.
- Product flows: `/products` grid → tap a card → `/products/{slug}` detail sheet
  → back arrow (top-left circle) dismisses back to the grid.
- Blazor uses enhanced navigation: `<style>`/`<head>` injections are wiped on
  each page transition, but `Element.prototype` monkeypatches persist. To
  inspect Web-Animations-API animations (e.g. productTransition.js), patch
  `Element.prototype.animate` to record keyframes/options — CSS animations can
  be paused via `el.getAnimations()[0].pause(); a.currentTime=…` then screenshot.
- The dismiss clone in productTransition.js is force-removed after 600ms by a
  setTimeout fallback — slowed-down animation experiments on it get cut off.
- wwwroot JS/CSS edits are picked up by restarting `dotnet run` (scoped .razor.css
  needs `dotnet build` first); browser may cache `/js/*.js`, verify with
  `fetch(src, {cache:'reload'})`.

## Gotchas

- The `/products` hero banner can render blank for a few seconds on a cold
  load — remote Unsplash images; not a regression.
