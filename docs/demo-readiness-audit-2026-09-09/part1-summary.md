# Part 1 summary — ArtisanalBrew demo-readiness (2026-09-09)

## Execution context
- Intended: user Mac `/Users/alexis/Documents/ArtisanalBrew` @ `2dd68cc`
- Actual: box mirror checkout at same commit; **Mac Shell/Read blocked** (no machineId). Evidence on box only:
  - `/workspace/ArtisanalBrew/docs/demo-readiness-audit-2026-09-09/`
  - Parent should copy to Mac path when machineId available.

## Commands / exits / durations (high level)
| Command | Exit | Duration |
|---|---|---|
| dotnet restore | 0 | ~7s |
| dotnet build Release | 0 | ~16s |
| unit tests | 0 | ~5s (373 pass) |
| architecture tests | 0 | ~1s (11 pass) |
| integration tests (isolated PG) | 0 | ~11s (17 pass) |
| AgentGateway npm ci/test/build | 0 | ~16s total (20 pass / 1 skip) |
| EVM hardhat test (Node 22) | 0 | ~25s (29 pass) |
| Solana cargo test | 0 | ~25s (4 pass) |
| Solana browser mocha | 0 | ~4s (3 pass) |
| EVM hardhat (Node 20) | 1 | blocked by Node version |
| run-acceptance / crossstack / Mac git | skip | see test-results.md |
| Live curl home/products/ready/agent-card/chains | 0 | useful; no deploy-version header |

## Pass/fail/skip totals (automated this phase)
- .NET: **401 passed / 0 failed / 0 skipped** (373+11+17)
- AgentGateway: **20 passed / 0 failed / 1 skipped**
- EVM: **29 passed** (after Node 22)
- Solana: **7 passed** (4 cargo + 3 browser)
- Skipped suites: acceptance harness, crossstack CI job, format check, pixel crew verify, Mac-local dirty-tree capture

## Top blockers / P0–P1 candidates (this phase only; demo journeys)
### P0
1. **Mac audit path unreachable** from this executor — cannot record Mac dirty tree or write evidence to required Mac docs path without parent machineId rebind.
2. **Agentic journey chain ≠ storefront Sepolia checkout**: AgentGateway + live agent-card use `eip155:84532` (Base Sepolia); storefront default is Ethereum Sepolia `11155111`. Blocks coherent “Sepolia checkout + agentic” story unless demo scripts intentionally switch networks / wallets.

### P1
3. **`Blockchain:Network:StakingPoolContract` wrong** (`appsettings.json:19` = BSC `erc8004Registry` address) vs runtime `liquidVault` (`deployments/ethereum-sepolia.json:24`). Live liquid staking API looks correct; legacy `StakingController`/`CoffeeWeb3Service` path is poisoned if used.
4. **Root vs proof EVM manifests diverge** (esp. liquidVault); Story showcase hardcodes proof address (`StoryTechShowcase.razor:133`).
5. **Hosting/docs domain drift**: README/Azure + `cafe.alexisreyna.dev` vs live `cafe.alexisrs.dev` / Atlantic; Caddyfile sample still old hostname.
6. **Worker Dev missing `deployments/evm-local.json`** — local Worker misconfig risk for order-processing demos.
7. **Receipts email**: atlantic-migration notes Resend activation pending — may soft-fail order receipt demo.
8. **Hardhat/CI needs Node ≥ 22.13** — Node 20 fails EVM tests (environment footgun on Mac if still on 20).

### Journey smoke (non-destructive live)
- `/products` 200 with product cards + Sepolia messaging — OK for storefront journey
- `/health/ready` 200 — OK
- `/staking` 200 — page serves
- `/api/chains` exposes Sepolia+BSC+Solana with liquidVault matching root Sepolia manifest — good for wallet/staking liquid path
- Agent card ready on Base Sepolia — agentic path distinct from Sepolia checkout

## Artifacts
- `baseline.md`, `commands.log`, `manifest-diff.md`, `test-results.md`, `part1-summary.md`
- logs: `restore.log`, `build.log`, `unit-test.log`, `arch-test.log`, `integration-test.log`, `agentgateway-*.log`, `evm-*.log`, `solana-*.log`
- trx: `test-results/*.trx`
