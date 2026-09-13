# ArtisanalBrew demo-readiness audit — FINAL REPORT

**Date:** 2026-09-09  
**Live:** https://cafe.alexisrs.dev  
**Code analyzed:** `agent/atlantic-migration` @ `2dd68cc290b6ff66a990aecf75a4f60ee98bb756`  
**Auditor surface:** box clone `/workspace/ArtisanalBrew` (Mac write path blocked — no ListMachines / machineId)  
**Constraint:** AUDIT ONLY — no product source changes  
**Evidence:** `docs/demo-readiness-audit-2026-09-09/` (baseline, risks, screenshots, logs, `browser-live.md`)

---

## 1. Decision

### Primary decision: **NO-GO** for the stated must-show live demo

Full must-show scope (storefront interactive cart, Sepolia checkout, orders/receipts, wallet + staking, agentic features) is **not demo-ready tonight** on live.

**Three top reasons:**

1. **Cart → checkout journey broke in live browser.** After Add to Cart, `/products` returned HTTP 500 with a Blazor unhandled error; checkout was not completed and no payment was attempted. Post-session cold curls returned 200 again — treat as intermittent Blazor-circuit failure, not permanent SSR death — but the interactive pay path remains **Broken / Unverified** for guests tonight.
2. **Orders and staking interactive surfaces failed in the same session** (`/orders` 500, `/staking` 500; Login showed no wallet modal + Blazor error). Cold 200s later do not verify ledger browsing under load or wallet connect / liquid stake actions.
3. **Agentic must-shows are only partially safe:** Procurement Lab intro renders, but wallet CTA hits Blazor error; Deliver submits literal `IPFS_HASH_HERE` (MSR-04); MCP agent resources return empty/stub 200 payloads (MSR-03); AgentGateway card is Base Sepolia (`eip155:84532`) while storefront checkout is Ethereum Sepolia (`11155111`).

**Automated tests (.NET 401, AG 20/1 skip, EVM 29, Solana 7) passed on the box in isolation — they are not proof of live journeys.**

### Optional reduced-scope fallback (separate from primary decision)

**CONDITIONAL GO — reduced scope only** if the operator **explicitly accepts a scope cut** to:

- Cold/browse storefront (homepage + `/products` + product detail) **without** Add to Cart  
- Procurement Lab **intro / landing copy only** (no wallet CTA, no Fund/Deliver/Pay)  
- Verbal pointing at `/api/chains` + Sepolia messaging  

**Gates for even that reduced scope (every remaining gate):**

| # | Gate | Clear-before criterion |
|---|---|---|
| R1 | Confirm cold `/`, `/products`, `/health/ready` = 200 | Curl immediately before guests |
| R2 | Script forbids Add to Cart, checkout, Login, staking actions, Deliver | Operator rehearsal pass |
| R3 | Use hostname `cafe.alexisrs.dev` only (not alexisreyna) | Script sticky note |
| R4 | Never open `/staking?preview=connected` | Bookmark hygiene |
| R5 | Do not claim email receipts, MCP paid tool content, or Base+ETH Sepolia as one chain | Talking points |

If any interactive must-show is restored to the script, primary remains **NO-GO** until cart/checkout, orders, and wallet/staking are re-verified live without Blazor 500s.

---

## 2. Feature matrix

| Feature | Status | Environment | Evidence | Limitation | Safe to demonstrate? |
|---|---|---|---|---|---|
| Storefront browse (home / `/products` / detail) | **Partially working** | Live | Screenshots `01`–`03`; cold curl 200 post-audit; session OK until cart | After Add to Cart, `/products` 500 + Blazor error (`04`–`05`); session failures intermittent | **Yes — browse only**; no cart mutation |
| Cart + Sepolia checkout | **Broken** | Live | Browser: add → products 500; checkout unverified; no payment | MSR-01 Pay CTA usable on non-marketplace chains; MSR-06 fixed 3750 rate | **No** |
| Orders / receipts | **Broken** / **Unverified** | Live | `/orders` 500 in session (`09`); cold curl later 200; Resend pending (MSR-02) | Public ledger by design (MSR-12); email fail-closed if Resend unset | **No** for must-show; do not promise email |
| Wallet connect | **Broken** / **Unverified** | Live | Login → no wallet modal + Blazor error | Circuit fragility | **No** |
| Staking (liquid / Sepolia) | **Partially working** (SSR) / **Broken** (interactive) | Live | Page cold 200; session `/staking` 500 (`07`); `/api/chains` liquidVault OK | MSR-11 preview fake balances; legacy StakingPoolContract mismatch vs vault (manifest-diff A) | **Browse/API only if cold**; no stake actions |
| Agentic — Procurement Lab intro | **Partially working** | Live | Screenshot `06`; intro OK | Wallet CTA → Blazor error; Deliver = `IPFS_HASH_HERE` (MSR-04) | **Intro only** |
| Agentic — MCP / x402 content | **Partially working** | Live + code | Agent-card 200 Base Sepolia; stubs empty 200 (MSR-03) | Cross-chain vs storefront Sepolia; session payments cap false but grant UI still offered (MSR-10) | **x402 challenge narrative only**; no post-pay content |
| `/api/chains` | **Verified working** | Live | Screenshot `10`; cold 200 | UI selectors ignore capabilities (MSR-15/01) | **Yes** (API honesty) |
| `/health/ready` | **Verified working** | Live | Cold curl Healthy | Use alexisrs host (MSR-05/13) | **Yes** (ops) |
| Admin | **Intentionally disabled** / out of scope | — | Confirmed demo scope | N/A | N/A — not required |
| Local automated suites | **Verified working** | Box only | `test-results.md`; .NET 401; AG 20/1s; EVM 29; Solana 7 | Not live journey proof | Cite as local confidence only |

Status vocab used: Verified working | Partially working | Broken | Intentionally disabled | Unverified.

---

## 3. Ranked findings

### Tonight blockers (P0 / interactive demo killers)

| Rank | ID | Sev | Finding | Evidence |
|---:|---|---|---|---|
| 1 | LIVE-CART | P0 | Add to Cart triggers Blazor unhandled error and `/products` HTTP 500 in live session | Screenshots `04`–`05`; `browser-live.md` |
| 2 | LIVE-ORDERS | P0 | `/orders` HTTP 500 during interactive session — orders/receipts must-show unverified | Screenshot `09`; cold 200 later ≠ interactive proof |
| 3 | LIVE-STAKING-WALLET | P0 | `/staking` 500 in session; Login no wallet modal + Blazor error | Screenshot `07`; `browser-live.md` |
| 4 | MSR-01 | P0 | Checkout Pay CTA stays usable on BSC/Solana; amount can show 0 ETH | `ChainSelector.razor:26-32`; `Checkout.razor:455-475`, `639-646`, `850-866` |
| 5 | MSR-02 | P0 | Email receipts fail closed if Resend unset (Atlantic pending) | `docs/atlantic-migration.md:5`, `170-194`; `ReceiptService.cs:15-38` |
| 6 | MSR-03 | P0 | Agentic MCP resources return empty/stub OK payloads | `AgentResourceController.cs:16-43` |
| 7 | MSR-04 | P0 | Procurement Lab Deliver submits `IPFS_HASH_HERE` | `ProcurementLab.razor:915` |
| 8 | LIVE-CIRCUIT | P0 | Intermittent Blazor-circuit 500s vs cold SSR 200 — demo risk under interaction | Post-audit cold curl recovery; Development Mode error pages |

### Tonight P1 (hide / script-discipline if any surface is shown)

| Rank | ID | Sev | Finding | Evidence |
|---:|---|---|---|---|
| 9 | MSR-05 | P1 | Runbooks/docs host `cafe.alexisreyna.dev` (TLS fail); live is `cafe.alexisrs.dev` | Live probes; `atlantic-migration.md:16,33` |
| 10 | MSR-08 | P1 | BSC `legacyPool` cloned from Sepolia marketplace wallet | `deployments/bsc-testnet.json` vs Sepolia |
| 11 | MSR-09 | P1 | Worker Atlantic Sepolia-only; Web exposes 3 chains | `compose.yml:47-51`; Web appsettings manifests |
| 12 | MSR-10 | P1 | `agenticSessionPayments:false` but Smart Account grant UI still offered | `SmartAccountPanel.razor:135-168`, `476-528` |
| 13 | MSR-11 | P1 | `preview=connected` injects fake staking balances | `YieldPanel.razor:577-597` |
| 14 | MSR-06 | P1 | Fixed DemoEthUsdRate 3750; non-ETH refused in pricing | `OrderPricingService.cs:10-50` |
| 15 | CHAIN-SPLIT | P1 | Storefront Ethereum Sepolia vs AgentGateway Base Sepolia `eip155:84532` | `server.ts:62,125,282+`; live agent-card |

### Later improvements (P2–P3 — not tonight blockers alone)

| Rank | ID | Sev | Finding |
|---:|---|---|---|
| 16 | MSR-07 | P2 | Strong Sepolia payment verify + unique tx hash; residual double-submit UX |
| 17 | MSR-12 | P2 | Auth on private orders OK; `/orders` public ledger intentional |
| 18 | MSR-13 | P2 | Health/ready sound; keep alexisrs in cron/runbooks |
| 19 | MSR-15 | P2 | `/api/chains` filters Enabled; UI ignores capability bits |
| 20 | MANIFEST-A | P1→later | Web `StakingPoolContract` = BSC registry addr; liquid path OK via manifest |
| 21 | MANIFEST-B | P1→later | Root vs proof EVM liquidVault diverge; Story showcase hardcodes proof |
| 22 | MSR-14 | P3 | Pixel home hardcoded GEN 300 training metrics |
| 23 | OPS-DEV | P2 | Error pages show Development Mode; `/robots.txt` 500 |
| 24 | TOOLCHAIN | P2 | Hardhat needs Node ≥ 22.13 (Node 20 fails EVM tests) |

---

## 4. Tonight's action list

### Fix now (only if insisting on full must-show — still likely misses “later tonight”)

| Action | Effort | Retest criteria |
|---|---|---|
| Stabilize Blazor circuit after cart/login (live repro: add-to-cart → products 500; login → wallet modal) | **Unknown / likely hours** — root cause not isolated in this audit | Fresh browser: Add to Cart → stay on cart; open checkout; Login opens wallet modal; `/orders` + `/staking` render without 500 for 10 min continuous session |
| Confirm Resend key + domain on Atlantic **or** hide Send-receipt UI | 30–90 min ops / 1–2h UI hide | Receipt email arrives **or** form absent with PDF/download-only copy |
| Disable Deliver (or require real CID) on Procurement Lab | 15–30 min | Deliver disabled or never submits `IPFS_HASH_HERE` |
| Force Sepolia-only on checkout (hide ChainSelector / disable Pay when `!MarketplacePayment`) | 15–45 min hide | BSC/Solana selected → Pay disabled + banner |

**Candid assessment:** Interactive live failures are the binding constraint. Code hides alone do not clear LIVE-CART / LIVE-ORDERS / LIVE-STAKING-WALLET. Prefer **NO-GO full scope** over rushed production patches tonight.

### Hide / avoid (required for any reduced demo)

| Action | Effort | Retest criteria |
|---|---|---|
| Script: **no** Add to Cart, checkout, pay, Login, stake, Fund/Deliver/Pay/Refund | 0–15 min | Rehearsal completes without those clicks |
| Hostname only `https://cafe.alexisrs.dev` | 0 min | No alexisreyna URLs in slides/script |
| Never `/staking?preview=connected` | 0 min | URL bar clean |
| Do not demo MCP post-pay catalog/provenance content | 0–15 min | Script stops at challenge/agent-card |
| Do not enable BSC marketplace / flip capabilities | 0 min | `/api/chains` still `marketplacePayment:false` for BSC |
| Skip email receipt promise | 0 min | Talking point: on-page / PDF only if shown |

### Defer (post-demo)

| Item | Effort (order-of-magnitude) |
|---|---|
| Wire real MCP resources / fail with 501 instead of empty 200 | 4–8h |
| Align docs DNS/TLS (`alexisrs` vs `alexisreyna`); Caddyfile sample | 1–3h |
| Filter ChainSelector by capability; hide session grant when cap false | 2–4h + 30–60 min |
| Fix legacy `StakingPoolContract` / proof vs root vault / Story hardcode | 1–3h |
| Worker Dev missing `deployments/evm-local.json` | 30–60 min |
| Gate `preview=connected` behind Development | 20 min |
| Turn off Development Mode error pages in prod presentation | ops config |
| Node 22+ for local Hardhat on presenter machine | env |

---

## 5. Rehearsal script (≈5–10 min) — verified behavior only

**Label:** Steps marked **[VERIFIED]** were observed working (browse/cold or API). Steps marked **[UNVERIFIED / DO NOT]** failed or were not proven interactively.

1. **[VERIFIED]** Open `https://cafe.alexisrs.dev` — homepage / pixel surface loads.  
2. **[VERIFIED]** Navigate `/products` — catalog cards + Sepolia messaging (cold and initial session). **[DO NOT]** click Add to Cart.  
3. **[VERIFIED]** Open one product detail (e.g. House Espresso) — detail OK. Back to products **without** cart mutation.  
4. **[VERIFIED]** Optional: show `/api/chains` (Sepolia default, liquidVault present, BSC/Solana `marketplacePayment:false`). Narrate: checkout rail is Ethereum Sepolia; agent card is Base Sepolia — **do not** conflate.  
5. **[VERIFIED]** Optional cold open `/procurement-lab` — intro/copy only. **[DO NOT]** wallet CTA, Fund, Deliver, Pay, Refund.  
6. **[DO NOT]** `/checkout`, pay, `/orders` interactive claims, Login, `/staking` actions, email receipt, MCP paid tool bodies.  
7. **[VERIFIED]** Pre-show ops: `curl -f https://cafe.alexisrs.dev/health/ready` → Healthy.

**If a 500 / Blazor error appears:** stop interactive demo; fall back to homepage + products browse only; do not refresh-spam into Development Mode error theater.

**Timing:** ~5 min browse+API; +2–3 min procurement intro if stable; hard stop before any wallet or cart click.

---

## 6. Verification ledger

| Check | Result | Where |
|---|---|---|
| Repo SHA | `2dd68cc290b6ff66a990aecf75a4f60ee98bb756` | Box detached HEAD; `baseline.md` |
| `dotnet restore` / Release build | Pass (0 errors) | `commands.log`, `build.log` |
| Unit + Architecture + Integration | 373 + 11 + 17 = **401 pass** | `test-results.md` (isolated local PG) |
| AgentGateway npm test/build | **20 pass / 1 skip** | `agentgateway-test.log` |
| EVM Hardhat (Node 22) | **29 pass** | `evm-test.log` (Node 20 failed version gate) |
| Solana cargo + browser | **4 + 3 = 7 pass** | `solana-*-test.log` |
| Acceptance / crossstack / format / pixel verify | **Skipped** | Isolation / risk (`test-results.md`) |
| Live cold curl `/`, `/products`, `/orders`, `/staking`, `/procurement-lab`, `/api/chains`, `/health/ready` | **200** (post-session) | Parent cold curl; `browser-live.md` |
| Live interactive cart/checkout | **Failed / unverified** | Screenshots `04`–`05` |
| Live interactive orders | **500 in session** | Screenshot `09` |
| Live interactive staking / wallet modal | **500 / Blazor error** | Screenshot `07` |
| Live agent-card network | Base Sepolia `eip155:84532` | Part1 curls / `commands.log` |
| Live storefront default chain | Ethereum Sepolia / marketplacePayment true | `/api/chains` screenshot `10` |
| Code risks MSR-01..15 | Documented | `must-show-risks.md`, `part-risks-summary.md` |
| Mac evidence sync | **Blocked** | No MCP ListMachines; Shell machineId unavailable |
| Payment on live | **Not attempted** | Audit constraint / broken cart path |
| Admin | Out of scope | Confirmed |

---

## Appendix — evidence index

- `docs/demo-readiness-audit-2026-09-09/part1-summary.md`
- `baseline.md`, `test-results.md`, `manifest-diff.md`, `commands.log`
- `must-show-risks.md`, `part-risks-summary.md`
- `browser-live.md` (live session vs cold curl)
- `screenshots/01-homepage.png` … `11-robots-500.png`
- Logs / TRX under same evidence directory

**Mac sync note for parent:** Finished report lives at box  
`/workspace/ArtisanalBrew/docs/demo-readiness-audit-2026-09-09.md`  
and evidence under  
`/workspace/ArtisanalBrew/docs/demo-readiness-audit-2026-09-09/`.  
Copy to `/Users/alexis/Documents/ArtisanalBrew/docs/` when machineId / ListMachines is available — this executor could not.
