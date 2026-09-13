# Must-show project-specific risks — ArtisanalBrew demo readiness
Date: 2026-09-09  
Scope: MUST-SHOW journeys only (storefront `/products`, cart + Sepolia checkout, orders + receipts, wallet + staking Sepolia, agentic features).  
Code basis: GitHub `AlejoReyna/ArtisanalBrew` @ `2dd68cc` (Mac `ListMachines`/`machineId` unavailable in this executor; live probes against `https://cafe.alexisrs.dev`).  
Constraint: no product source edits; secrets not printed.

## Ranked findings

### MSR-01 — Checkout Pay CTA stays usable on non-ETH / non-marketplace chains
- **Severity:** P0
- **Journey:** Cart + Sepolia checkout (chain selector trap)
- **Expected:** Disabled capabilities must not look usable; BSC/Solana checkout should be clearly unavailable before click.
- **Actual:** `ChainSelector` lists every `Enabled` chain with no capability filter. Checkout still renders **Pay with {NativeCurrencySymbol}** enabled; non-ETH conversion is swallowed to `_ethAmount = 0` so Amount to Pay can show `0 ETH` while the CTA still says e.g. Pay with tBNB; failure only on click via capability/symbol guards.
- **Evidence:**
  - `src/ThisCafeteria.Web/Components/Shared/ChainSelector.razor:26-32`
  - `src/ThisCafeteria.Web/Components/Pages/Checkout.razor:455-475`
  - `src/ThisCafeteria.Web/Components/Pages/Checkout.razor:639-646`
  - `src/ThisCafeteria.Web/Components/Pages/Checkout.razor:850-866`
  - Live `/api/chains`: `bsc-testnet.marketplacePayment=false`, `solana-devnet.marketplacePayment=false`
- **Smallest fix/hide workaround:** Tonight: hide ChainSelector on `/checkout` (force Sepolia) or disable Pay when `!MarketplacePayment` or symbol≠ETH and show an inline banner. Proper: filter selector by journey capability.
- **Effort:** 15–45 min hide; 2–4h proper filter
- **Retest:** On `/checkout` with cart, select BSC → Pay disabled + clear message; Sepolia → Pay enabled with non-zero ETH amount.

### MSR-02 — Email receipts fail closed if Resend not activated (Atlantic)
- **Severity:** P0
- **Journey:** Orders + receipts
- **Expected:** Must-show receipt email succeeds or UI offers an honest offline PDF path.
- **Actual:** Atlantic runbook documents Resend as pending / fail-closed when `Resend__ApiKey` empty — no fake success. Checkout still presents "Send receipt" after pay. PDF is generated then email send throws.
- **Evidence:**
  - `docs/atlantic-migration.md:5` (Resend pending)
  - `docs/atlantic-migration.md:170-194` (empty key → explicit failure)
  - `src/ThisCafeteria.Web/Components/Pages/Checkout.razor:94-129`, `986-1038`
  - `src/ThisCafeteria.Infrastructure/Services/ReceiptService.cs:15-38`
- **Smallest fix/hide workaround:** Pre-demo: confirm `resend.env` key + domain; or hide email form and show "PDF saved / download only" if key unset. Do not soft-succeed.
- **Effort:** 30–90 min ops verify; 1–2h UI hide/download
- **Retest:** Complete Sepolia checkout → Send receipt to controlled inbox → accept in Resend; or confirm form hidden when unconfigured.

### MSR-03 — Agentic MCP resources return empty / stub payloads (look like success)
- **Severity:** P0
- **Journey:** Agentic features
- **Expected:** Paid agent tools return real catalog/provenance or a hard "unavailable" error.
- **Actual:** Internal ASP.NET agent resource surface is `[AllowAnonymous]` behind a shared secret but returns deterministic stubs: empty product search, empty provenance (`evidenceStatus: "not-seeded"`), wholesale quote `quantity * 200000`, brew-plan `planVersion: "local-1"`. Gateway/agent-card are live on Base Sepolia (`eip155:84532`).
- **Evidence:**
  - `src/ThisCafeteria.Web/Controllers/AgentResourceController.cs:16-43`
  - Live `/.well-known/agent-card.json` + `/bazaar` 200 on `cafe.alexisrs.dev`
  - `docs/atlantic-migration.md:196-209`
- **Smallest fix/hide workaround:** Tonight: demo only x402 challenge/payment, not post-pay content; or return HTTP 501 with clear message instead of empty OK. Hide bazaar tools that are stubs from the script.
- **Effort:** 30–60 min hide/script; 4–8h wire real catalog
- **Retest:** After paying for `get_provenance_report` / `search_products`, response is either real data or explicit unavailable — never empty success.

### MSR-04 — Procurement Lab submit uses placeholder evidence hash
- **Severity:** P0
- **Journey:** Agentic features (`/procurement-lab`)
- **Expected:** Deliverable submission uses a real hash or blocks with "not ready".
- **Actual:** `SubmitJob` always sends `"IPFS_HASH_HERE"` to the escrow. Buttons still look operational (Fund / Deliver / Pay / Refund).
- **Evidence:** `src/ThisCafeteria.Web/Components/AgenticCommerce/ProcurementLab.razor:915` (also Create/Fund/Complete paths ~897–923)
- **Smallest fix/hide workaround:** Disable Deliver button with tooltip "evidence CID required"; or hardcode a documented demo CID and label it as demo-only in UI.
- **Effort:** 15–30 min hide; 1–2h real CID input
- **Retest:** Deliver disabled or requires CID; on-chain submit never uses literal `IPFS_HASH_HERE`.

### MSR-05 — Public hostname / runbook skew (`alexisrs` vs `alexisreyna`)
- **Severity:** P1
- **Journey:** All must-shows / health readiness vs deploy path
- **Expected:** Demo URL, TLS, and Atlantic/pixel runbooks agree.
- **Actual:** Live site answering health + chains is `https://cafe.alexisrs.dev` (200). `https://cafe.alexisreyna.dev` fails TLS (`TLSV1_UNRECOGNIZED_NAME`). Docs still instruct curls against `cafe.alexisreyna.dev`.
- **Evidence:**
  - Live probes 2026-09-09: `cafe.alexisrs.dev/health/ready` → Healthy; `cafe.alexisreyna.dev` TLS fail
  - `docs/atlantic-migration.md:16,33,134-136`
  - `docs/pixel-home-production-runbook.md:50,76`
- **Smallest fix/hide workaround:** Use only `cafe.alexisrs.dev` in tonight's script; sticky-note runbook URLs. Defer DNS/cert cleanup.
- **Effort:** 0 min script; 1–3h DNS/TLS later
- **Retest:** Scripted curls to the hostname used live; TLS OK; `/health/ready` Healthy.

### MSR-06 — ETH-only pricing + fixed demo rate vs multi-chain claims
- **Severity:** P1
- **Journey:** Cart + Sepolia checkout (honesty) / BSC claims
- **Expected:** Pricing matches selected native asset or checkout refuses non-ETH visibly.
- **Actual:** Fixed `DemoEthUsdRate = 3750` (not CoinGecko). `ToNativePaymentAmount` refuses any symbol ≠ `ETH` (tests cover BNB refusal). UI still shows "1 ETH = …" and `FormatEth` always suffixes `ETH`. BSC manifest keeps `marketplacePayment: false` by design (follow-up doc). Live BSC still appears in `/api/chains` and selector.
- **Evidence:**
  - `src/ThisCafeteria.Application/Services/OrderPricingService.cs:10-50`
  - `src/ThisCafeteria.Application/Services/IOrderPricingService.cs:10-14`
  - `src/ThisCafeteria.Web/Components/Pages/Checkout.razor:1052`
  - `docs/bsc-testnet-marketplace-follow-up.md:1-40`
  - Test: `tests/ThisCafeteria.UnitTests/OrderPricingServiceTests.cs:31-36`
- **Smallest fix/hide workaround:** Keep Sepolia-only checkout (MSR-01); verbally call out fixed demo rate (Terms already state it).
- **Effort:** covered by MSR-01; docs already OK
- **Retest:** Sepolia checkout amount = `round_to_zero(usdTotal/3750, 18)`; BSC cannot complete pay.

### MSR-07 — Server tx acceptance is strong on Sepolia rail; pending/reject OK; client amount unused
- **Severity:** P2 (positive control with residual race UX)
- **Journey:** Cart + Sepolia checkout
- **Expected:** Accept only matching chain/sender/recipient/asset/amount; pending vs reject; idempotent.
- **Actual:** `EvmMarketplacePaymentGateway` requires enabled EVM + `MarketplacePayment` + `LegacyPool`, valid hash/from/amount, receipt success, `to==LegacyPool`, confirmations, `from` match, exact wei via `Web3.Convert.ToWei`. Pending returns `Pending`; OrderService maps pending → retry message; failed → reject. Replay blocked by `ExistsByPaymentHashAsync` **and** unique filtered index on `PaymentTransactionHash`. Client `PaymentEthAmount` is validated but **not** used for verification (server requotes catalog). Residual: concurrent double-submit may surface DB uniqueness error instead of friendly replay message.
- **Evidence:**
  - `src/ThisCafeteria.Infrastructure/Services/Blockchain/EvmMarketplacePaymentGateway.cs:15-79`
  - `src/ThisCafeteria.Application/Services/OrderService.cs:32-58`, `131-144`, `146-168`
  - `src/ThisCafeteria.Infrastructure/Persistence/Configurations/OrderConfiguration.cs:29-31`
  - Checkout poll: `Checkout.razor:522-523`, `WaitForPaymentConfirmationAsync` ~952+
- **Smallest fix/hide workaround:** None required for demo if single click; optionally catch unique-violation → "payment already used".
- **Effort:** 1–2h polish
- **Retest:** Wrong recipient/amount → reject; under-confirmed → pending message; replay hash → "already been used".

### MSR-08 — BSC `legacyPool` address equals Sepolia marketplace wallet (poisoned manifest field)
- **Severity:** P1
- **Journey:** Checkout / chains honesty
- **Expected:** Disabled marketplace chain either omits settlement address or carries a real BSC pool.
- **Actual:** Live + repo `bsc-testnet` has `marketplacePayment:false` but `legacyPool` identical to Sepolia `0x9d5305a962…eceb`. If someone flips the flag for demo, payments would target a Sepolia address on chain 97.
- **Evidence:**
  - `deployments/bsc-testnet.json` capabilities + addresses
  - Live `/api/chains` bsc-testnet deployments.legacyPool
  - Sepolia legacyPool same value
- **Smallest fix/hide workaround:** Do not enable BSC marketplace tonight; optionally blank `legacyPool` in manifest when capability false (config-only).
- **Effort:** 10–20 min config; avoid product code
- **Retest:** `/api/chains` BSC either no legacyPool or distinct BSC address; marketplacePayment remains false.

### MSR-09 — Web vs Worker manifest skew (Atlantic)
- **Severity:** P1
- **Journey:** Agentic features + staking reconciliation; "what Web and Worker load"
- **Expected:** Web and Worker load the same settlement universe for demo chains, or Worker disablement is mirrored in UI.
- **Actual:** Both Dockerfiles `COPY deployments/ ./deployments/`. Web `appsettings` loads `ethereum-sepolia.json;bsc-testnet.json` + Solana. Atlantic **Worker** env forces `ARTISANALBREW_EVM_MANIFEST=deployments/ethereum-sepolia.json` and empty Solana — intentional fail-closed. Live Web `/api/chains` exposes Sepolia+BSC+Solana. AgenticCommerce reconciliation on Worker therefore covers Sepolia only.
- **Evidence:**
  - `src/ThisCafeteria.Web/appsettings.json:2-4`
  - `src/ThisCafeteria.Worker/appsettings.json:5-7`
  - `src/ThisCafeteria.Web/Dockerfile:51-56`
  - `src/ThisCafeteria.Worker/Dockerfile:38-43`
  - `deployments/atlantic/compose.yml:47-51`
  - `src/ThisCafeteria.Web/Program.cs:38-44` / `src/ThisCafeteria.Worker/Program.cs:30-33`
- **Smallest fix/hide workaround:** Demo agentic escrow + staking only on Ethereum Sepolia; never switch ChainSelector to BSC/Solana during agentic/reconciling demos.
- **Effort:** script discipline 0; align env later 1–2h
- **Retest:** After Sepolia agentic job lifecycle, Worker projection updates; BSC job does not appear reconciled.

### MSR-10 — `agenticSessionPayments` disabled but Smart Account grant UI still offered
- **Severity:** P1
- **Journey:** Agentic features / wallet
- **Expected:** Disabled session capability must not look grantable.
- **Actual:** Live caps `agenticSessionPayments:false` (Sepolia+BSC). Atlantic notes ERC-4337 session redemption disabled. `SmartAccountPanel` still shows "Sign delegations & activation" when modular account registered; `PrepareActivationAsync` does not check `Capabilities.AgenticSessionPayments`.
- **Evidence:**
  - Live `/api/chains` capabilities
  - `docs/atlantic-migration.md:209+` (sessions disabled)
  - `src/ThisCafeteria.Web/Components/Shared/SmartAccountPanel.razor:135-168`, `476-528`
  - Registry validate requires DelegationManager/HybridDeleGator/ModularEntryPoint when flag true (`IChainRegistry.cs:54-61`)
- **Smallest fix/hide workaround:** Hide grant/activate section unless capability true; demo Procurement Lab escrow path only.
- **Effort:** 30–60 min hide
- **Retest:** With Sepolia selected, no activation CTA; or CTA explains "sessions disabled in this deploy".

### MSR-11 — YieldPanel `preview=connected` injects fake balances
- **Severity:** P1
- **Journey:** Wallet + staking (Sepolia)
- **Expected:** No fake-success dashboard on must-show path.
- **Actual:** If URL contains `preview=connected`, panel short-circuits with hardcoded CAFE/stCAFE/coffee/gas balances and a fake wallet — looks fully funded without chain reads.
- **Evidence:** `src/ThisCafeteria.Web/Components/Shared/YieldPanel.razor:577-597`
- **Smallest fix/hide workaround:** Never open staking with that query tonight; strip query from bookmarks; or gate behind Development env (config later).
- **Effort:** 0 min discipline; 20 min env gate later
- **Retest:** `/staking` without query shows connect/real balances only.

### MSR-12 — Auth/ownership on orders, receipts, profile (mostly OK; public ledger intentional)
- **Severity:** P2
- **Journey:** Orders + receipts; profile mutations
- **Expected:** Mutations scoped to authenticated profile; no IDOR.
- **Actual:**
  - `POST /api/orders` + `GET /api/orders/me` `[Authorize]`; create uses `EnsureProfileLinkedAsync` auth id, not client-supplied ownership for placement (`OrderService` uses `authenticatedUserProfileId`). `CreateOrderRequest.UserProfileId` is validated but unused for authz (dead field).
  - Delete order admin-only.
  - Profile PATCH/PUT/DELETE avatar `[Authorize]` + resolved profile id.
  - `/orders` Commerce Ledger is **public** and loads **all** `GetCommerceTransactionsAsync` (wallets truncated) — intentional transparency, not private order history (private list is Profile / `api/profile/me/orders`).
  - Receipt send uses in-circuit `_completedOrder` + `_completedUserProfileId` (no cross-user API); any email may be entered (expected for guest receipt).
- **Evidence:**
  - `OrdersController.cs:18-66`
  - `OrderService.cs:20-31`, `107-111`
  - `ProfileController.cs:11-95`
  - `Orders.razor:1-21`, `109-113`
  - `Checkout.razor:1005-1031`
- **Smallest fix/hide workaround:** Script uses Profile for "my orders"; treat `/orders` as public register. Optional: stop accepting `UserProfileId` in create DTO later.
- **Effort:** script note; 1h DTO cleanup later
- **Retest:** Unauth create → 401; user A cannot mutate user B profile; public ledger loads without login.

### MSR-13 — Health/readiness vs Atlantic deploy path
- **Severity:** P2
- **Journey:** Deploy readiness (supports all must-shows)
- **Expected:** `/health/ready` reflects DB init; compose depends on it; cron checks match live host.
- **Actual:** Web maps `/health/live` (self) and `/health/ready` + `/health` → `MigrationReadinessHealthCheck`. Dockerfile HEALTHCHECK curls ready. Atlantic compose: postgres+gateway healthchecks; web relies on image HEALTHCHECK; `artisanalbrew-healthcheck` curls web ready + gateway ready. Pixel runbook still Azure-oriented but shares ready checks. Live `cafe.alexisrs.dev/health/ready` Healthy.
- **Evidence:**
  - `src/ThisCafeteria.Web/Program.cs:67-69`, `354-365`
  - `src/ThisCafeteria.Web/HealthChecks/MigrationReadinessHealthCheck.cs:1-20`
  - `src/ThisCafeteria.Web/Dockerfile:58-59`
  - `deployments/atlantic/compose.yml:9-13,73-78,93-97`
  - `deployments/atlantic/artisanalbrew-healthcheck:1-14`
- **Smallest fix/hide workaround:** Pre-demo curl ready on `cafe.alexisrs.dev`; ignore alexisreyna host.
- **Effort:** 5 min
- **Retest:** `curl -f https://cafe.alexisrs.dev/health/ready` → Healthy before guests arrive.

### MSR-14 — Hardcoded homepage training metrics (adjacent demo surface)
- **Severity:** P3
- **Journey:** Agentic/home adjacent (pixel hero)
- **Expected:** Metrics labeled simulated or live.
- **Actual:** Pixel home generation chips hardcode "GEN 300 — Fully trained — 27.0 coins, 6.8 mugs per run".
- **Evidence:** `src/ThisCafeteria.Web/Components/Home/PixelHome.razor:184`
- **Smallest fix/hide workaround:** Verbally "simulated training scores"; skip deep claim.
- **Effort:** 0
- **Retest:** N/A for must-show checkout; optional label check.

### MSR-15 — `/api/chains` filters disabled chains; capabilities exposed; selectors ignore capabilities
- **Severity:** P2 (design gap; compounds MSR-01/10)
- **Journey:** All chain-gated must-shows
- **Expected:** Disabled capabilities not presented as actionable.
- **Actual:** API returns only `chain.Enabled` and includes full `capabilities` object (good for clients). Select endpoint rejects unknown/disabled keys. UI selectors/CTAs do not consistently honor capability bits (see MSR-01/10). Bundler RPCs correctly omitted from public API.
- **Evidence:**
  - `ChainsController.cs:11-72`
  - `SelectedChainAccessor.cs:27-38`
  - `BlockchainManifestLoader.cs:105-107`
- **Smallest fix/hide workaround:** Same as MSR-01/10.
- **Effort:** bundled
- **Retest:** Disabled chain keys absent from GET; select disabled → 400.

## Manifest load summary (Web vs Worker)

| Surface | Manifest source | Live observation |
|---|---|---|
| Web container | `COPY deployments/` + appsettings `LocalEvmManifest` sepolia;bsc + solana-devnet; env override wins | `/api/chains` returns ethereum-sepolia, bsc-testnet, solana-devnet |
| Worker (Atlantic) | same image copy, but env `ARTISANALBREW_EVM_MANIFEST=deployments/ethereum-sepolia.json`, Solana `""` | Sepolia-only reconciliation by design |
| Trusted checkout recipient | Sepolia `Deployment.LegacyPool` / marketplace wallet | Live Sepolia legacyPool `0x9d5305a962…eceb` |

## Tonight script hygiene (no code)

1. Stay on **Ethereum Sepolia** for checkout, staking, faucet, agentic escrow.
2. Use hostname **`cafe.alexisrs.dev`** only.
3. Do not open `/staking?preview=connected`.
4. Demo agentic: x402 challenge and/or Procurement Lab fund/complete — skip Deliver or pre-agree CID; do not rely on product-search/provenance content.
5. Verify Resend before promising email receipts; otherwise show on-page receipt only.
