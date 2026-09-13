# ArtisanalBrew demo-readiness audit — Part 1 baseline
Date: 2026-09-09 (CST context; evidence timestamps UTC)
Auditor runtime: Cursor box (Linux), NOT the user Mac (ListMachines/machineId Shell unavailable to this executor)

## Mac access blocker (P0 for evidence path)
- Required path: `/Users/alexis/Documents/ArtisanalBrew`
- Expected machine: connected Mac (label like 192.168.1.10 / Alexiss-MacBook-Pro; historical machineId `c916fb59-eb8d-47e7-965a-8917c3abec81`)
- This subagent's Shell/Read have no usable `machineId` routing; Mac paths return not found; evidence written on the box mirror under this directory.
- Mac `git status` (dirty `.DS_Store` + untracked docs) was **NOT** captured. Preserve Mac tree; do not reset.

## Repo identity (box checkout used for analysis)
- Remote: `https://github.com/AlejoReyna/ArtisanalBrew.git`
- Requested branch/commit: `agent/atlantic-migration` @ `2dd68cc290b6ff66a990aecf75a4f60ee98bb756`
- Box HEAD used: `2dd68cc290b6ff66a990aecf75a4f60ee98bb756` (detached)
- Note: `origin/main` tip observed earlier as merge PR #108 (`cac35e7`) of atlantic-migration; analysis pinned to requested SHA.
- AGENTS.md: **absent** at repo root (only `.agents/` present)

## SDK / toolchain (box)
- `global.json`: SDK `10.0.300` rollForward `latestFeature`
- Installed for audit: .NET SDK **10.0.401**
- Node system: 20.19.2 (insufficient for Hardhat 3)
- Node for EVM tests: **22.14.0** (installed under `~/.local/node22`)
- PostgreSQL for isolated tests: **17.11** local service on `127.0.0.1:5432`, DB `thiscafeteria_test` / user `test_only` (box-local only; not shared/prod)
- Docker: **not available** on box (`NO_DOCKER`)
- Cargo: available (Solana program tests ran)

## Entry points
| Component | Path | Role |
|---|---|---|
| Web | `src/ThisCafeteria.Web` | Blazor storefront, checkout, wallet UI, `/api/chains`, liquid staking APIs |
| Worker | `src/ThisCafeteria.Worker` | Order processing / background jobs |
| AgentGateway | `src/ThisCafeteria.AgentGateway` | MCP/x402 agentic commerce (Express), proxied by Caddy |

## Env / config samples (names only; no secrets)
- `.env.example` — DB, Azure stubs, Sepolia `Blockchain__Network__*`, BSC deployer key names
- `src/ThisCafeteria.Web/appsettings.json` + `appsettings.Development.json`
- `src/ThisCafeteria.Worker/appsettings.json` + `appsettings.Development.json` (Dev points `LocalEvmManifest` → `deployments/evm-local.json` which **does not exist** in tree)
- `Properties/launchSettings.json` (Web + Worker)
- Atlantic production secrets live in `/opt/artisanalbrew/*.env` on VPS (not in repo); `deployments/atlantic/compose.yml` + `gateway.env` names

## Required vs optional deps (demo journeys)
| Dependency | Required for demo journeys? | Notes |
|---|---|---|
| PostgreSQL | **Required** | Web/Worker/orders/receipts; gateway idempotency in prod |
| Sepolia RPC | **Required** | Checkout currency/chain; `/api/chains` default `ethereum-sepolia` |
| Contract manifests under `deployments/` | **Required** | Runtime source of truth via `BlockchainManifestLoader` |
| Wallet (browser) | **Required** | Wallet + staking + Sepolia checkout |
| AgentGateway + x402 facilitator + Base Sepolia USDC | **Required for agentic journey** | Hardcoded `eip155:84532` (Base Sepolia), not Ethereum Sepolia |
| Redis | Not evidenced as required in samples | — |
| Email (Resend) | Optional for browse; **needed for receipt email** | atlantic-migration: Resend activation pending |
| Blob/Azure storage | Optional locally; receipts on disk in Atlantic | — |
| Service Bus | Azure leftover; Atlantic path differs | Worker still has Azure SB config keys |

## CI mapping (`.github/workflows/ci.yml`)
Equivalents executed in Part 1 where safe:
1. `dotnet restore` → ran
2. `dotnet build --configuration Release --no-restore` → ran
3. `dotnet test --configuration Release --no-build` → Unit + Architecture + Integration (isolated PG)
4. `node tools/verify_pixel_crew.mjs` → **not run** this phase (browser/homepage; defer)
5. `contracts/evm`: `npm ci && npm test` → ran (Node 22)
6. `src/ThisCafeteria.AgentGateway`: `npm ci && npm test && npm run build` → ran
7. `cargo test --manifest-path contracts/solana/Cargo.toml --locked` → ran
8. `contracts/solana`: `npm ci && npm run test:browser` → ran
9. `dotnet format --verify-no-changes` → **not run**
10. `crossstack-verification` / `run-acceptance.sh` → **SKIPPED** (starts Hardhat/Worker, can RESET_DB; isolation/risk)

## Hosting claims vs live
- README still markets **Azure Container Apps** + `cafe.alexisreyna.dev`
- `docs/atlantic-migration.md` + `deployments/atlantic/Caddyfile` use `cafe.alexisreyna.dev` on Atlantic.Net `209.23.11.117`
- Live demo URL (probed): **`https://cafe.alexisrs.dev`** (Caddy → Kestrel; agent card via Express)
- Live `/api/chains` default: `ethereum-sepolia` with marketplacePayment=true

## Tonight must-show journeys (bias)
1. Storefront `/products` — live GET 200 with catalog cards + Sepolia copy
2. Cart + Sepolia checkout — manifests/marketplacePayment enabled; Worker empty Network addresses rely on manifests
3. Orders/receipts — needs PG + Worker path; Resend pending per atlantic doc
4. Wallet + staking — liquid path uses manifest `liquidVault`; legacy `Blockchain:Network:StakingPoolContract` **mismatches** vault
5. Agentic — live agent-card network **Base Sepolia (`eip155:84532`)** while storefront is Ethereum Sepolia
