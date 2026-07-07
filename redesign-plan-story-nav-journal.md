# Orchestration Plan — /story Redesign · Mobile Drawer Fix · /journal Redesign

**Role**: Creative Designer / Orchestrator
**Scope**: Three workstreams, executable in parallel after a small shared refactor.

---

## 0. Design-Language Audit (what "follow the Homepage" means today)

The live homepage direction is the **recruiter-showcase editorial system** (Hero → Concept → Offerings → Provenance → StakingBand → Journal), *not* the older Starbucks green spec in `design-spec.md`. The tokens to follow:

| Token | Value | Use |
|---|---|---|
| Canvas | `--surface` `#fbf9f4` (warm cream) | default section background |
| Ink | `--charcoal` `#211b17` | headings, dark full-bleed bands |
| Muted | `rgba(33,27,23,.62)` | body copy |
| Hairline | `rgba(33,27,23,.13)` | borders, rails, dividers |
| Accent | `--accent` warm brown (`#8d6f55` light / `#c2a47d` on dark) | eyebrows, nodes, highlights |
| Display | Playfair Display + `<em class="headline-italic">` | serif headlines, italic emphasis word |
| Sans | Inter, 700–850 weight, letter-spaced uppercase micro-labels | eyebrows, meta |
| Mono | `--crypto-mono` (JetBrains Mono) | on-chain/technical labels (`01 · ORIGIN`) |

**Animation primitives already built** (Home.razor inline `recruiterShowcaseMotion` + CSS):
- `data-recruiter-animate="lift"` — blur+rise reveal, staggered via `data-recruiter-stagger`
- `data-recruiter-animate="mask-line"` — line-by-line masked headline reveal
- `data-recruiter-animate="type"` — mono typing line with blinking caret
- `data-recruiter-animate="draw"` — SVG stroke self-draw
- `data-count-to` — eased count-up numbers
- `data-provenance-rail` — scroll-progress vertical rail fill
- StakingBand slow-spinning hairline rings on charcoal
- All gated behind `prefers-reduced-motion: reduce`

**Section rhythm**: cream editorial section → full-bleed charcoal band → cream. Minimal imagery, typography-led, hairline structure.

---

## Stage 1 — Enabling refactor (do first, small)

`recruiterShowcaseMotion` lives inline in `Home.razor`, so `/story` and `/journal` can't use the primitives. Extract it:

1. Move the observer/motion JS from `Home.razor` `<script>` into `wwwroot/js/recruiterMotion.js` (keep the `window.recruiterShowcaseMotion` API; Home keeps its `OnAfterRenderAsync` init/dispose calls).
2. Move the shared CSS (`[data-recruiter-animate]` states, `.mask-line`, `.type-line`, keyframes, reduced-motion block) from Home.razor's `<style>` into `app.css` (or a `recruiter-motion.css`), so Story and Journal pages inherit it.
3. Reference the script in `App.razor` / `MainLayout` alongside `animations.js`.
4. Verify homepage is pixel/behavior-identical after extraction (this is a pure move).

Everything below assumes this shared system.

---

## Stage 2A — `/story` Redesign: **"This café doesn't exist."**

Replace `Components/Pages/Story.razor` entirely (current version is the old brown/EB Garamond fiction page with `:has()` navbar override hacks — delete those overrides; the standard header behavior applies).

**Concept**: an honest, minimal, typography-first page. The café is fiction; the engineering is real. Keep the cafeteria voice — every technical section wears a coffee metaphor. Route stays `/story`, nav label stays "About".

### Section 1 — Hero (cream, minimal)
- Eyebrow (`lift`): `FULL DISCLOSURE`
- H1 (`mask-line`, two lines): `This café` / `<em>doesn't exist.</em>`
- Lede (`lift`): "No storefront. No beans. No baristas. What *is* real: the software — a full commerce, staking, and settlement stack, built as if the café were."
- Mono type-line (`type`): `> a fictional cafeteria · real engineering`
- No hero image. Whitespace + a single hairline rule that draws in (`scaleX` like the Journal index divider).

### Section 2 — The Fiction (cream, two-column narrative)
- Eyebrow `THE FICTION`, H2 `Brewed from scratch, minus the coffee.`
- Two short serif-body columns (staggered `lift`): why build a café that doesn't exist — a complete, production-shaped playground: real database, real queue, real chain, imaginary espresso. Tone: warm, wry, confident.

### Section 3 — The Recipe · How it's built (cream, provenance-rail pattern)
Reuse the Provenance vertical rail (scroll-fill line + nodes) with five stations, mono labels + short serif copy:

| Station | Copy source (verified) |
|---|---|
| `01 · DOMAIN` | Entities & enums — the vocabulary of the café (`ThisCafeteria.Domain`) |
| `02 · APPLICATION` | Services, DTOs, validation — the recipes (`ThisCafeteria.Application`) |
| `03 · INFRASTRUCTURE` | EF Core + PostgreSQL, Azure storage/messaging/email (`ThisCafeteria.Infrastructure`) |
| `04 · WEB` | Blazor Web App, .NET 10, interactive server rendering — the counter (`ThisCafeteria.Web`) |
| `05 · WORKER` | Service Bus consumer processing `order-processing` — the back room (`ThisCafeteria.Worker`) |

- Side note (mono, `type`): `dotnet 10 · blazor server · clean architecture · xunit + testcontainers`

### Section 4 — The Yield · How staking works (full-bleed **charcoal** band, StakingBand's twin)
- Spinning hairline rings (reuse `.stakingband-ring`), centered content.
- Eyebrow `THE YIELD` (accent), H2 (`mask-line`): `Interest, ` / `<em>freshly ground.</em>`
- Body: "One Solidity contract, `CafeStakingPool`, runs the loyalty program on Sepolia. Stake CAFE, accrue COFFEE by the second, claim whenever you like."
- **Flow strip** (3 steps, hairline-bordered, staggered `lift`, mono headers):
  1. `stake()` — tokens move into the pool; `ReentrancyGuard` on the door.
  2. `rewardPerToken()` — an owner-set annual rate in basis points, accrued per second against total staked.
  3. `claimRewards()` — pending COFFEE transfers out; no lock-up, unstake anytime.
- Stat row with `data-count-to`: `100 % on-chain · Sepolia`, `0 days locked`, `1 contract, audited by tests` (adjust to real numbers).
- Honesty footnote (mono, small): `testnet only — the tokens are as real as the café.`
- CTA ghost button → `/staking`.

### Section 5 — The Pour · How it's deployed (cream, CI-log motif)
- Eyebrow `THE POUR`, H2 (`mask-line`): `Served from` / `<em>the cloud.</em>`
- **Deploy log** (stacked mono `type` lines, sequential delays — the signature animation of this page):
  ```
  $ git push origin main
  → GitHub Actions · docker build
  → push · Azure Container Registry
  → deploy · Azure Container Apps
  ✓ live at cafe.alexisreyna.dev
  ```
- Hairline card grid (2×3, `lift` stagger) naming the estate, all provisioned as Bicep IaC in `infra/`: Container Apps (Consumption), Postgres Flexible Server, Key Vault, Blob Storage, Service Bus, Log Analytics + Communication Email.
- Micro-stat: `11` bicep modules (`data-count-to`).

### Section 6 — Manifesto close (cream, centered)
- H2 (`mask-line`): `Fictional coffee.` / `<em>Real craft.</em>`
- Two CTAs: `button--dark` → "Shop the beans" (`/products`), `button--ghost` → "Read the source" (GitHub repo).

**Cleanup**: remove the Unsplash hero/collage image blocks, EB Garamond styles, `#6B4A38` navbar overrides, and `.editorial-hero--about` background override from the old page.

---

## Stage 2B — Mobile Nav Drawer (`NavMenu.razor` + `NavMenu.razor.css`)

Two problems from the screenshot: **Connect Wallet appears twice** (drawer header and footer) and the **pure-black `#0c0a09` full-screen sheet** is off-brand next to the cream homepage.

1. **De-duplicate Connect Wallet**
   - Header slot (`.nav-drawer__wallet`, NotAuthorized branch): replace the `Connect Wallet` button with the wordmark — `Artisanal Brew` in Playfair (the brand is currently hidden when the drawer opens; give it back). Authorized branch keeps the address chip + pulse dot.
   - Footer keeps the single primary `Connect Wallet` CTA (thumb-reachable, correct placement).

2. **Re-skin drawer to the warm editorial palette** (keep layout, tiles, stats, transitions):
   - Background `#0c0a09` → cream `#fbf9f4`; backdrop stays dark-glass.
   - Text `#f5f3ee` → ink `#211b17`; muted labels → `rgba(33,27,23,.62)`.
   - Tiles/stat cards: `background: #fffdf9` (or `rgba(33,27,23,.03)`), border `rgba(33,27,23,.13)`, radius unchanged; active state uses accent `#8d6f55`.
   - Footer primary CTA inverts: ink background, cream text; outline CTA becomes ink-outline.
   - Toggler-open bars and close × → ink; `.site-nav:has(.nav-drawer--open)` overrides updated accordingly.
   - `ToggleNav()` calls `themeColor.set("#0c0a09")` — change to `"#fbf9f4"` so the browser chrome matches the new drawer.
   - Optional polish: tiles get the `lift` stagger on open (respect reduced motion); hairline divider above footer stays.
   - Verify contrast (ink on cream ≥ AA) and the `crypto-dot` pulse color on light background.

---

## Stage 2C — `/journal` Redesign (`Pages/Journal.razor`)

Bring the standalone Journal page in line with the homepage's editorial index (the `Components/Home/Journal.razor` section is already on-language — it's the reference, not a target).

1. **Masthead replaces the autoplaying carousel** (minimalism: no auto-rotation).
   - Cream masthead: eyebrow `THE JOURNAL`, H1 `mask-line` (`Notes from` / `<em>the counter.</em>`), kicker, mono meta strip (`NN field notes · manual brew · slow design`), hairline rule that draws in.
   - The featured articles (`JournalCatalog.FeaturedArticles`) become a **featured stack** directly under the masthead: large numbered rows in the homepage `recruiter-journal-entry` style (image left, `01 | CATEGORY`, title, summary, read-time pill ↗), staggered blur-lift on scroll.
2. **Field Notes** (`HomeArticles`): reuse the same entry-row component/styles extracted from `Components/Home/Journal.razor` — sticky masthead column on desktop, single column of rows; collapse to stacked cards ≤980px, hover lift + image zoom.
3. **Short Stories** (`ShortStories`): compact 3-column hairline-card grid (cream cards, mono kicker badge, serif title), `lift` stagger; 1-column on mobile.
4. **Subscribe** → full-bleed **charcoal band** styled like StakingBand (rings optional, subtler): serif headline, single-field form with ink-on-cream button, mono note. This gives the page the same cream→charcoal→cream rhythm as Home.
5. Preserve all `JournalCatalog` / `JournalArticleContents` bindings and `ArticleUrl` links; keep semantics (`aria-labelledby`, list structure); drop carousel ARIA roledescriptions along with the carousel.
6. Factor the shared entry-row CSS out of `Components/Home/Journal.razor`'s inline `<style>` into `app.css` so both surfaces use one definition.

---

## Stage 3 — Integration & QA (orchestrator)

- `dotnet build` clean; no BL9992 or scoped-CSS regressions.
- Razor gotcha: every `@` in `<style>`/`<script>` inside `.razor` files must be `@@` (media queries, keyframes).
- Homepage unchanged after the Stage-1 extraction (visual diff).
- Wallet auth, cart badge, staking stats in drawer still function (InteractiveServer events).
- `prefers-reduced-motion` disables all new motion; count-ups snap to final values.
- Mobile pass at 360px/390px/760px: drawer, story rail, journal rows.
- No new external assets; type-only design (removes the old Story page's four Unsplash dependencies).

### Suggested agent split (parallel after Stage 1)

| Agent | Deliverable |
|---|---|
| Motion_Extractor (Stage 1, blocking) | `wwwroot/js/recruiterMotion.js`, shared CSS, Home.razor slimmed |
| Story_Designer | New `Pages/Story.razor` (+ styles) |
| Drawer_Designer | `NavMenu.razor` header slot + `NavMenu.razor.css` re-skin + themeColor fix |
| Journal_Designer | `Pages/Journal.razor` + shared entry-row CSS extraction |
| Integrator/QA | Build, responsive & reduced-motion audit, copy proofread |
