# ArtisanalBrew product audit and four-week delivery plan

**Audit date:** 2026-08-31  
**Delivery window:** 2026-08-31 through 2026-09-25  
**Repository state:** `agent/atlantic-migration` at `2dd68cc`  
**Overall status:** Red for public availability and storefront commerce; amber for agent/chain operations; green for the core visual system and unit-test baseline.

## Executive summary

ArtisanalBrew has a distinctive, polished product surface and a substantial technical foundation: the core containers are healthy, the catalog and product detail views render well, the mobile product detail is strong, and the .NET unit and architecture suites pass. The product is not release-ready today because two P0 failures break the public/customer path:

1. `cafe.alexisreyna.dev` resolves to `76.223.67.189` and `13.248.213.45`, not the active Atlantic.Net origin at `209.23.11.117`. A normal browser receives `ERR_SSL_UNRECOGNIZED_NAME_ALERT`; the correct origin returns HTTP 200 when resolved directly.
2. Adding an item to an anonymous cart succeeds, but the next render returns HTTP 500. Production logs confirm concurrent use of one EF Core `DbContext` from cart repricing and navigation initialization.

The four-week plan therefore restores public trust and commerce first, makes agent payments recoverable second, fixes responsive/content quality third, and closes multichain readiness plus release hardening fourth.

## Audit evidence

### Production and UX

- All five Atlantic.Net containers have been up for two weeks; `web`, `gateway`, and `postgres` report healthy.
- The active origin returns HTTP 200 and supports Blazor interactive rendering.
- The public DNS path fails before the application is reached because the A records point elsewhere.
- The desktop home, catalog, and product-detail experiences are visually cohesive and communicate the testnet positioning clearly.
- The home preview advertises Colombia Huila, Ethiopia Yirgacheffe, and Mexico Chiapas, while the live catalog contains House Espresso, Midnight Cold Brew, and Vanilla Cloud Latte.
- At approximately 940 px wide, Yield, Lab, and About clip primary headings/cards beyond the right edge.
- At 390 px, the catalog category strip truncates labels. The home hero remains visually empty during a long entrance sequence before its primary message appears.
- The wallet modal is visually clear, but its four provider buttons have no accessible names. Its safety copy is grammatically unclear.
- Procurement Lab renders a stray Razor/C# fragment as public page text from `ProcurementLab.razor`.
- About and README still describe Azure Container Apps as the live runtime even though the project has migrated to Atlantic.Net.

### Commerce failure

Reproduction:

1. Open `/products/house-espresso` anonymously.
2. Select **Add to Cart**.
3. The `POST /api/cart/items` call returns 200 and stores one line.
4. The following `/products` render returns 500; later pages can also return 500 while that cart line is present.

Confirmed production stack:

`NavMenu.OnInitializedAsync` → `RefreshCartCountAsync` → `ShoppingCartService.GetItemCountAsync` → `RepriceFromCatalogAsync` → `ProductService.GetProductBySlugAsync`.

EF Core throws: `A second operation was started on this context instance before a previous operation completed.` The error handler invokes the same navigation/cart path and also fails, leaving only a plain 500 page.

### Agent and chain operations

- The Worker did not advance its reconciliation cursor during four Ethereum Sepolia RPC failures in the last 24 hours. This is fail-safe, but there is no provider failover and reconciliation freshness is not visible as a release signal.
- x402 paid tools settle payment before executing the ASP.NET fulfillment call. If fulfillment fails after settlement, the atomic store releases the failed key and a retry can attempt settlement again without a durable resumable fulfillment record.
- The public agent resource routes remain demonstration stubs: product search always returns an empty array, provenance has no evidence, brew plans have no real plan, and wholesale pricing is a fixed multiplication.
- BSC marketplace checkout is correctly disabled. Pricing remains ETH-only and lacks a chain-keyed, timestamped native/USD quote and generalized receipt semantics.

### Test and dependency posture

- .NET unit tests: **373 passed**.
- Architecture tests: **11 passed**.
- Integration tests: **7 passed, 10 could not start** because `TEST_POSTGRES_CONNECTION` was not configured. These were fixture failures, not asserted product regressions.
- Agent Gateway: **20 passed, 1 skipped**. The skipped case is persistence across PostgreSQL store instances.
- Restore/build warnings include a Nethereum logging-version constraint mismatch and high-severity advisory `GHSA-2m69-gcr7-jv3q` through `SQLitePCLRaw.lib.e_sqlite3` 2.1.11.

## Prioritization rules

- **P0:** prevents normal public access, makes a core journey unusable, or risks charging without reliable fulfillment.
- **P1:** materially damages trust, operability, accessibility, or an advertised capability.
- **P2:** polish or workflow quality that should not block an otherwise safe release.
- Estimates are engineering effort: **S** up to two days, **M** two to four days, **L** four to seven days.

## Week 1 — Restore public access and storefront reliability

### AB-101 — Correct production DNS and validate TLS end to end

**Priority / estimate / owner:** P0 / S / Platform  
**Problem:** The public hostname does not route to the live Atlantic.Net origin and fails during TLS negotiation.

**Scope**

- Replace the stale/parked A records with the approved Atlantic.Net origin or the intended proxy/load-balancer records.
- Confirm Caddy obtains and serves the certificate for the public hostname.
- Remove stale Azure-era DNS/runbook references.
- Add an external HTTPS probe that resolves through public DNS.

**Acceptance criteria**

- `dig cafe.alexisreyna.dev` returns only the intended production path.
- A clean browser opens the homepage without a certificate warning or local DNS override.
- `/`, `/health/ready`, `/_framework/blazor.web.js`, and `/.well-known/agent-card.json` return the expected successful status through public DNS.
- Certificate SAN, issuer, and expiry are recorded in the production runbook.

### AB-102 — Eliminate cart-triggered `DbContext` concurrency failures

**Priority / estimate / owner:** P0 / M / Backend  
**Problem:** Any non-empty cart can turn the next server render into HTTP 500.

**Scope**

- Stop sharing one scoped EF Core context across parallel Blazor/component operations. Use a context factory or explicit unit-of-work boundary appropriate to the existing architecture.
- Reprice cart lines with one catalog query instead of serial per-line queries where practical.
- Make cart-count initialization single-flight and safe during simultaneous prerender/circuit initialization.
- Ensure the error page does not depend on the failing cart path.

**Acceptance criteria**

- Anonymous and authenticated users can add, update, remove, and reload a cart without a 5xx response.
- After adding a line, navigation to Shop, Yield, Lab, About, Cart, and Checkout remains successful.
- A regression test reproduces simultaneous navigation/cart initialization with at least one stored line.
- Twenty repeated add-and-navigate runs produce no EF Core concurrent-operation exception.
- Error rendering remains available when a downstream catalog lookup fails.

### AB-103 — Add a public synthetic checkout-smoke journey

**Priority / estimate / owner:** P0 / S / Platform + QA  
**Problem:** Container health remained green while public DNS and the cart journey were broken.

**Scope**

- Probe the real public hostname, not the container origin.
- Exercise anonymous catalog → product detail → add to cart → cart render.
- Keep the journey non-financial and clean up its isolated browser/session state.
- Alert separately on DNS/TLS, HTTP 5xx, and journey failure.

**Acceptance criteria**

- The check runs at least every five minutes from outside the host.
- Alert payload identifies the failing stage and includes a correlation ID.
- A forced failure is detected and routed to the documented owner.

### AB-104 — Remove the Procurement Lab source leak

**Priority / estimate / owner:** P1 / S / Frontend  
**Problem:** A trailing C# expression is rendered below the Lab experience.

**Scope**

- Remove the stray markup after the `@code` block.
- Add a render assertion that the source fragment is absent.
- Check generated Razor warnings for similar unreachable/trailing content.

**Acceptance criteria**

- The Lab contains no source-language fragments at any breakpoint.
- The page builds without the current leak and its render test passes.

### AB-105 — Resolve dependency security and compatibility warnings

**Priority / estimate / owner:** P1 / S / Backend  
**Problem:** The build reports a high-severity SQLite package advisory and an unsupported Nethereum logging dependency range.

**Acceptance criteria**

- The vulnerable transitive package is upgraded, replaced, or formally proven test-only and removed from shipped artifacts.
- The Nethereum/logging version constraint is resolved without suppressing the warning.
- CI publishes a dependency audit/SBOM result with no unowned high-severity item.

**Week 1 exit gate:** Public HTTPS works without overrides; the anonymous cart journey passes; there are no cart-related EF concurrency errors in a 24-hour observation window; the Lab source leak is gone.

## Week 2 — Make agent payments and reconciliation recoverable

### AB-201 — Introduce a durable x402 settlement/fulfillment state machine

**Priority / estimate / owner:** P0 / L / Agent Gateway + Backend  
**Problem:** Payment settlement can succeed before fulfillment fails, without a durable state that lets retries resume safely.

**Scope**

- Persist states such as `verified`, `settled`, `fulfilling`, `fulfilled`, and `failed-retryable`, keyed by the complete request/payment identity.
- Store settlement receipts before invoking fulfillment.
- On replay, resume fulfillment after a known settlement rather than settling again.
- Distinguish permanent fulfillment rejection from retryable infrastructure failure.

**Acceptance criteria**

- A forced backend failure after settlement creates exactly one settlement and a resumable record.
- Retrying returns or completes the original fulfillment without another charge.
- Concurrent identical requests converge on one state/result across multiple gateway instances.
- Operators can inspect and safely replay stuck fulfillment records.

### AB-202 — Replace paid-resource stubs with authoritative application services

**Priority / estimate / owner:** P1 / L / Backend + Product  
**Problem:** Paid tools currently return placeholders that do not justify payment or match the real catalog.

**Scope**

- Search the actual active catalog with bounded, deterministic results.
- Generate brew plans from real product metadata.
- Return persisted provenance evidence or an explicit unavailable response before charging.
- Produce wholesale quotes from authoritative pricing and inventory rules.

**Acceptance criteria**

- Every paid result references a real product and has a schema/version, generated timestamp, and deterministic request binding.
- Missing products/evidence fail before settlement when knowable before payment.
- Contract tests cover catalog updates, invalid IDs, stock boundaries, and quote expiry.

### AB-203 — Complete PostgreSQL replay and crash-recovery coverage

**Priority / estimate / owner:** P1 / M / Agent Gateway  
**Problem:** The cross-instance PostgreSQL idempotency test is skipped.

**Acceptance criteria**

- The skipped persistence test runs in CI.
- Coverage includes process restart between settlement and fulfillment, two live instances, and expired/stale records.
- Retention and cleanup preserve audit receipts while bounding table growth.

### AB-204 — Add RPC failover and reconciliation freshness SLOs

**Priority / estimate / owner:** P1 / M / Blockchain + Platform  
**Problem:** Sepolia reconciliation safely pauses on network failure but has no automatic provider fallback or clear freshness signal.

**Scope**

- Configure at least two independently operated RPC endpoints per production chain.
- Add bounded exponential backoff, circuit breaking, and provider rotation.
- Export last successful block/time, lag, cursor, and failure count by chain.

**Acceptance criteria**

- A simulated primary outage moves reads to the fallback without advancing the cursor incorrectly.
- Alerts fire when reconciliation age exceeds the agreed threshold.
- Recovery returns to normal without manual cursor edits or duplicate ledger events.

**Week 2 exit gate:** A settled x402 request is never charged twice and can resume after failure; paid resources return authoritative data; Worker reconciliation has failover and freshness alerts.

## Week 3 — Fix responsive UX, accessibility, and product truth

### AB-301 — Repair tablet and mobile layout regressions

**Priority / estimate / owner:** P1 / M / Frontend  
**Problem:** Key pages clip content at intermediate widths, and mobile catalog navigation truncates labels.

**Scope**

- Fix Yield heading overflow around 768–1024 px.
- Keep the Lab step card and About detail panel inside the viewport.
- Make the mobile catalog category row intentionally scrollable or fully collapsible, with a visible affordance.
- Prevent the product-detail media sheet from obscuring the mobile brand/header.

**Acceptance criteria**

- No horizontal page overflow or clipped primary content at 390, 768, 940, 1024, and 1440 px.
- Primary actions remain visible and keyboard reachable at every tested width.
- Visual regression snapshots cover Home, Products, Product Detail, Yield, Lab, and About.

### AB-302 — Make public content match production reality

**Priority / estimate / owner:** P1 / M / Product + Frontend + Platform  
**Problem:** The homepage, About page, README, and deployed catalog contradict one another.

**Scope**

- Populate the homepage preview from the same catalog source as Shop or intentionally seed the advertised products.
- Rewrite About/README infrastructure sections for Atlantic.Net, preserving Azure history only as migration history.
- Audit live chain/capability claims against committed manifests and deployed configuration.

**Acceptance criteria**

- Product names, counts, prices, and availability agree across Home, Shop, detail pages, and agent search.
- About names the actual host, persistence, queues/processes, secrets model, and disabled capabilities.
- A release checklist requires verification of all public technical claims.

### AB-303 — Make wallet selection accessible and safer to understand

**Priority / estimate / owner:** P1 / S / Frontend + Product  
**Problem:** Wallet provider buttons are unlabeled, and the safety copy is unclear.

**Scope**

- Add visible or accessible provider names, focus states, and announced connection status.
- Rewrite the recommendation as concise test-wallet guidance.
- Confirm Escape, close, focus trap, and return-focus behavior.

**Acceptance criteria**

- Screen readers announce each wallet provider distinctly.
- The modal passes keyboard-only navigation and automated accessibility checks.
- Copy clearly states that the app uses test funds and recommends a temporary test wallet.

### AB-304 — Add motion fallbacks and reduce time-to-message

**Priority / estimate / owner:** P2 / M / Frontend  
**Problem:** The mobile home can present an empty hero while the entrance animation runs.

**Acceptance criteria**

- The headline and primary CTA are readable within one second on a mid-tier mobile profile.
- `prefers-reduced-motion` removes nonessential movement and never hides content.
- Decorative robots cannot cover or block interactive text/controls.

**Week 3 exit gate:** Responsive matrix passes without clipping; wallet selection is accessible; public product/infrastructure claims match the deployed system; primary messages never depend on animation completion.

## Week 4 — Close multichain readiness and release hardening

### AB-401 — Implement chain-aware native asset quotes and receipt provenance

**Priority / estimate / owner:** P1 / L / Backend + Blockchain  
**Problem:** Checkout only understands a demo ETH/USD constant and cannot safely price tBNB.

**Scope**

- Add a server-owned quote service keyed by chain and native asset.
- Persist quote source, observed time, expiry, raw precision, chain ID, symbol, decimals, USD total, and native amount.
- Generalize `PaymentEthAmount` and ETH-only UI/DTO/receipt naming while preserving legacy rows.
- Bind quote identity, amount, recipient, chain, and expiry into verification.

**Acceptance criteria**

- Stale, wrong-chain, wrong-recipient, or rounded-wrong quotes fail closed.
- Existing Sepolia checkout remains unchanged from a user perspective.
- Receipts can reproduce the exact quote and settlement semantics for ETH and tBNB.

### AB-402 — Prove and conditionally enable BSC marketplace checkout

**Priority / estimate / owner:** P1 / L / Blockchain + QA  
**Dependency:** AB-401  
**Acceptance criteria**

- A funded BSC Testnet journey completes wallet switch → exact tBNB transfer → server confirmation → durable order/ledger/receipt → profile/orders display.
- The public transaction hash and test evidence are recorded.
- `marketplacePayment` remains `false` unless every acceptance criterion passes.

### AB-403 — Establish release SLOs and an operator dashboard

**Priority / estimate / owner:** P1 / M / Platform  
**Scope**

- Track public HTTPS availability, 5xx rate, cart-journey success, Blazor circuit failures, gateway fulfillment backlog, reconciliation freshness, queue depth, backup age, and certificate expiry.
- Assign alert owners and write one-page response runbooks.

**Acceptance criteria**

- Dashboard and alerts distinguish public edge, web, database, worker, gateway, and chain-provider failures.
- Every P0 signal has an owner, threshold, and tested response path.

### AB-404 — Run a release-candidate burn-in and close the audit

**Priority / estimate / owner:** P1 / M / QA + Product + Engineering  
**Scope**

- Run desktop/mobile smoke journeys, wallet-modal checks, anonymous/authenticated cart flows, Sepolia flows, agent paid-resource failure/replay tests, restore verification, and dependency scanning.
- Observe the release candidate for 48 hours before final sign-off.

**Acceptance criteria**

- No unresolved P0; every P1 is closed or explicitly accepted by Product with owner/date.
- No public 5xx from core journeys during burn-in.
- Backup restore, RPC failover, and one stuck-fulfillment recovery are demonstrated.
- Product, Engineering, and Operations sign the release checklist.

**Week 4 exit gate:** The release candidate survives 48 hours, core journeys and operational recovery are proven, and BSC is enabled only if its funded evidence is complete.

## Recommended weekly operating cadence

- **Monday:** confirm ticket owners, dependencies, and acceptance tests; no new scope after kickoff without trading out equivalent work.
- **Daily:** 15-minute risk review focused on P0/P1 evidence, not activity reporting.
- **Wednesday:** integrated demo in the production-like environment; update synthetic checks before merging.
- **Friday:** acceptance review against the exit gate, production telemetry review, and next-week reprioritization.

## Definition of done for every ticket

- Acceptance criteria are demonstrated with evidence, not only code review.
- Automated regression coverage exists at the lowest useful layer and at the user-journey layer for P0 paths.
- Logs/metrics contain correlation IDs without secrets or wallet private data.
- Documentation and public claims are updated in the same change.
- Deployment includes rollback instructions and post-deploy verification.
