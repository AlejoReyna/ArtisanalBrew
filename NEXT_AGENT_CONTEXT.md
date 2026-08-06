This file was intentionally requested by Alexis to facilitate agentic development and was humanly reviewed.

# ArtisanalBrew Agentic Development Context

Updated: 2026-08-06
Integration branch: `agent/docs-three-gap-refresh`

## Mission and release rule

ArtisanalBrew is a recruiter-facing multichain and agent-commerce project. Preserve the working storefront and Ethereum Sepolia checkout while completing public-testnet releases honestly.

An address or capability flag is not proof of a working integration. Enable a public feature only after its deployment, wallet or agent flow, server verification, reconciliation, smoke test, and manifest are complete. Never invent transaction hashes or call a local run public evidence.

## Read first

1. [`README.md`](README.md) — current product state and commands.
2. [`docs/agentic-commerce-stack-plan.md`](docs/agentic-commerce-stack-plan.md) — agent-commerce architecture.
3. [`docs/agent-gateway-production-runbook.md`](docs/agent-gateway-production-runbook.md) — gateway trust boundaries and production configuration.
4. [`docs/erc4337-session-key-provenance.md`](docs/erc4337-session-key-provenance.md) — ERC-4337 deployment and acceptance evidence.
5. [`docs/bsc-testnet-marketplace-follow-up.md`](docs/bsc-testnet-marketplace-follow-up.md) — exact BSC checkout blocker.
6. [`docs/multichain-liquid-staking-plan.md`](docs/multichain-liquid-staking-plan.md) and [`docs/multichain-liquid-staking-operations.md`](docs/multichain-liquid-staking-operations.md).

Then inspect the code and tests; documentation is context, not proof.

## Repository boundaries

- `src/ThisCafeteria.Domain`: persistence entities and domain types.
- `src/ThisCafeteria.Application`: chain registry, manifest validation, models, and application contracts.
- `src/ThisCafeteria.Infrastructure`: EF Core, Identity, migrations, and external infrastructure.
- `src/ThisCafeteria.Web`: Blazor UI, wallet authentication, chain APIs, verification, and dashboards.
- `src/ThisCafeteria.Worker`: EVM/Solana and agent-commerce reconciliation loops.
- `src/ThisCafeteria.AgentGateway`: TypeScript x402/MCP and ERC-4337 redemption boundary.
- `contracts/evm`: Hardhat contracts, tests, deployment scripts, ABIs, and evidence artifacts.
- `contracts/solana`: Anchor program, IDL/build output, and smoke/browser tests.
- `tests`: .NET unit and PostgreSQL integration coverage.

Use the existing chain registry, wallet identity model, ledger, selected-chain state, gateways, and reconciliation patterns. Do not create parallel versions.

## Manifest truth

Web and Worker load the root runtime manifests:

- `deployments/ethereum-sepolia.json`
- `deployments/bsc-testnet.json`
- `deployments/solana-devnet.json`

The files under `contracts/evm/deployments/` are contract-deployment/provenance artifacts and contain a different historical deployment set. Do not copy their addresses into runtime configuration or documentation without reconciling and verifying the intended deployment.

Current capability gates:

- Ethereum Sepolia: marketplace checkout enabled; agent commerce enabled; session-key payments disabled.
- BSC Testnet: liquid staking/agent commerce available as configured; marketplace checkout disabled; session-key payments disabled.
- Solana Devnet: enabled by its validated runtime manifest; it does not provide the EVM marketplace or ERC-4337 flow.

## Implemented agent-commerce surface

- Pinned x402 v2 gateway and request binding.
- ERC-8004-compatible identity, validation, and reputation contracts/adapters plus PostgreSQL projections.
- ERC-8183 escrow lifecycle, application funding/evidence/completion/refund flows, and reconciliation.
- Deterministic two-node ERC-7683-style solver/rebalance demonstration and projections.
- ERC-4337 v0.7 owner flow and an authenticated one-shot delegated session-payment redemption route.
- Procurement Lab UI and worker reconciliation.

ERC-8004, ERC-8183, and ERC-7683 remain draft/revision-sensitive surfaces. Keep revisions pinned behind adapters and label experimental fixtures honestly.

## Three confirmed gap closures

### Gap 1 — session-key redemption

Implemented on local branch `agent/gap1-session-redemption`, commit `d36f423`:

- `POST /agentic-payments/redeem` requires gateway bearer authentication and an idempotency key.
- The gateway reconstructs and exactly compares the one-shot function-call scope, nonce, timestamps, LimitedCalls(1) caveat, root authority, and delegation signature.
- It reads the live NonceEnforcer nonce, constructs the deterministic delegated account, submits a v0.7 UserOperation, waits for the receipt, and verifies the sender and transaction status.
- The Hardhat/Rundler end-to-end script now drives the HTTP route and records the UserOperation and transaction hashes.

Acceptance is **not closed on a public testnet**. This environment has no testnet signer, RPC, compatible safe bundler, or funded wallet. The observed deployed Rundler advertises a legacy EntryPoint rather than the canonical modular v0.7 EntryPoint required by this stack. No new public UserOperation hash was produced. Keep `agenticSessionPayments=false`.

### Gap 2 — gateway production readiness

Implemented on local branch `agent/gap2-gateway-production-readiness`, commit `f6d0a47`:

- Public ERC-4337 configuration requires `bundlerMode: "safe"` and preflights chain ID, supported EntryPoint, and trusted runtime bytecode hashes for the full modular account/delegation stack.
- PostgreSQL-backed atomic idempotency stores make x402 responses and complete session-redemption receipts restart-safe. In-memory storage remains development-only.
- Production rejects raw private-key custody and uses an HTTPS external signing service. The gateway never receives the signer secret.
- Production startup requires PostgreSQL and fails closed on incomplete agent configuration.
- The gateway dependency audit is clean.

Operational limits remain: the gateway cannot inspect how a remote bundler process was launched, so operators must independently confirm its tracer/safe-mode configuration. This environment has no PostgreSQL service, external signer, or compatible public bundler, so the PostgreSQL restart test and public bytecode/bundler preflight were not executed here.

### Gap 3 — BSC marketplace checkout

The flag remains correctly disabled. The actual blocker is in the checkout/receipt domain, not the settlement verifier:

- `Checkout.razor` prices with a static ETH/USD rate and deliberately rejects non-ETH native assets.
- Browser bindings and UI copy are ETH-specific.
- Persistence, DTOs, receipts, profile, and admin views store/format `PaymentEthAmount`.
- There is no chain-keyed BNB quote source or quote provenance.

The existing chain-aware gateway can verify a BSC native transfer and the BSC manifest has a destination address, but enabling the flag now would misprice orders and mislabel receipts. Implement and pass every gate in [`docs/bsc-testnet-marketplace-follow-up.md`](docs/bsc-testnet-marketplace-follow-up.md), including a real funded BSC Testnet checkout with a recorded transaction hash, before setting `marketplacePayment=true`.

## Verification state

Fresh 2026-08-06 release-gate results:

- `dotnet restore`: completed with existing NU1608 constraint warnings and NU1903 high-severity advisories for `SQLitePCLRaw.lib.e_sqlite3`/`System.Security.Cryptography.Xml`.
- Release build: succeeded with 0 errors and 21 warnings.
- .NET unit tests: 352 passed.
- .NET integration command: 17 discovered; 7 database-independent tests passed and 10 fixture tests were blocked because `TEST_POSTGRES_CONNECTION` was absent. Apple Container is not installed on this host.
- Agent gateway: 20 passed, 1 PostgreSQL restart test skipped because `AGENT_GATEWAY_TEST_DATABASE_URL` was absent; TypeScript build passed.
- Gateway `npm audit`: 0 findings across 206 dependencies.
- EVM Hardhat tests: 29 passed.
- `dotnet format --verify-no-changes`: passed.
- BSC manifest regression test: included in the 352-test unit pass.

The release gate is therefore not completely green: both PostgreSQL-backed suites still need a host with the configured databases, and the existing .NET package advisories require separate dependency remediation. Run the complete gate again after any further changes; do not reuse these counts as new evidence.

## Remaining work, in order

1. Provision a canonical modular v0.7 EntryPoint-compatible public bundler in documented safe mode.
2. Provision the HTTPS external signer, PostgreSQL database, trusted chain configuration, expected bytecode hashes, public RPC, and funded delegated account.
3. Run the PostgreSQL restart/replay test with `AGENT_GATEWAY_TEST_DATABASE_URL`.
4. Run gateway redemption → `JobFunded` → Worker reconciliation → Procurement Lab against a public testnet; record the real UserOperation and transaction hashes.
5. Only then enable `agenticSessionPayments`.
6. Generalize marketplace pricing and receipt persistence to a native-asset/chain-keyed model.
7. Complete the BSC Testnet checkout acceptance and record its real transaction hash.
8. Only then enable BSC `marketplacePayment`.

## Local verification

```bash
dotnet restore
dotnet build ThisCafeteria.sln --configuration Release --no-restore
dotnet test tests/ThisCafeteria.UnitTests --configuration Release --no-build
dotnet test tests/ThisCafeteria.IntegrationTests --configuration Release --no-build
npm --prefix contracts/evm test
npm --prefix src/ThisCafeteria.AgentGateway test
npm --prefix src/ThisCafeteria.AgentGateway run build
npm --prefix src/ThisCafeteria.AgentGateway audit
dotnet format ThisCafeteria.sln --verify-no-changes --no-restore
git diff --check
```

PostgreSQL integration tests require `TEST_POSTGRES_CONNECTION`. The optional gateway restart test requires `AGENT_GATEWAY_TEST_DATABASE_URL`. `scripts/apple-container-postgres.sh` can provision PostgreSQL only on a host with Apple Container installed.

## Non-negotiable rules

- Preserve unrelated dirty-worktree files. In particular, `docs/agent-missing-features-prompt.md` and `docs/agent-swarm-brief.md` predate this work and are not part of these commits.
- Never commit private keys, seed phrases, keypair JSON, `.env`, private RPC URLs, validator ledgers, `node_modules`, or build output.
- Never broadcast to a public chain without explicit authorization for the intended network and wallet expenditure.
- Treat browser input, wallet/RPC responses, agent metadata, deliverable URIs, and transaction identifiers as untrusted.
- Use the server registry and trusted runtime bytecode hashes as trust roots; verify successful on-chain execution before authoritative writes.
- Keep reconciliation transactional, idempotent, bounded, restart-safe, and independently supervised per chain.
- A clean build is not deployment evidence.

## Git state

The three gap commits exist locally. The documentation integration branch stacks the Gap 1 and Gap 2 commits and cherry-picks Gap 3. GitHub CLI was not authenticated in this environment, so no branch was pushed and no pull request was opened. Authenticate with `gh auth login`, then publish each gap as a reviewable branch/PR without staging the unrelated prompt documents.

The completion report must separate implementation, test evidence, public deployment evidence, remaining blockers, capability flags, and branch/commit/push/PR state.
