This file was intentionally requested by Alexis to facilitate agentic development, so it's not AI Slop as it was humanly reviewed

# ArtisanalBrew Agentic Development Context

Updated: 2026-07-19
Active feature branch: `agent/enable-solana-multichain`
Draft pull request: [#50](https://github.com/AlejoReyna/ArtisanalBrew/pull/50)
Stacked base: `perf/faster-loading`, which is draft PR #48

## Mission

Continue ArtisanalBrew as a recruiter-facing, technically honest multichain and agent-commerce project. Preserve the working storefront and Ethereum Sepolia checkout while finishing controlled public testnet releases and the remaining agent-commerce standards.

Do not equate an entry in the registry with a working integration. A network is user-visible only after its contracts/program, wallet flow, server verification, reconciliation, smoke tests, and deployment manifest are complete.

## Read first

1. [`README.md`](README.md) — product overview, implemented state, availability table, and local commands.
2. [`docs/multichain-liquid-staking-plan.md`](docs/multichain-liquid-staking-plan.md) — target architecture and phased rollout.
3. [`docs/multichain-liquid-staking-operations.md`](docs/multichain-liquid-staking-operations.md) — operational controls.
4. [`docs/solana-local-manifest.md`](docs/solana-local-manifest.md) — Solana release-manifest contract.
5. [`docs/agentic-commerce-stack-plan.md`](docs/agentic-commerce-stack-plan.md) — x402/ERC-4337/ERC-8004/ERC-8183/ERC-7683 plan.
6. [`docs/agentic-commerce-protocol-revisions.md`](docs/agentic-commerce-protocol-revisions.md) — pinned/draft-standard limitations.

Then inspect the code and tests; documentation is context, not proof.

## Repository boundaries

- `src/ThisCafeteria.Domain`: persistence entities and domain types.
- `src/ThisCafeteria.Application`: chain registry, manifest validation, shared codecs/models, application contracts.
- `src/ThisCafeteria.Infrastructure`: EF Core, Identity, migrations, and external infrastructure.
- `src/ThisCafeteria.Web`: Blazor UI, wallet authentication, chain APIs, transaction verification, and dashboard reads.
- `src/ThisCafeteria.Worker`: isolated EVM/Solana reconciliation loops and repair execution.
- `src/ThisCafeteria.AgentGateway`: pinned TypeScript x402/MCP gateway boundary.
- `contracts/evm`: Hardhat EVM contracts, fixtures, tests, deployment scripts, and ABIs.
- `contracts/solana`: Anchor program, IDL/build output, and smoke/browser tests.
- `tests`: .NET unit and PostgreSQL integration coverage.

Use the existing registry, wallet identity model, ledger, selected-chain state, gateways, and reconciliation patterns. Do not create parallel versions of those systems.

## Current implementation

### Chain registry and UI

- Nine requested public networks exist in `ChainDefinitionDefaults`.
- Ethereum Sepolia is the only baseline-enabled public entry.
- Hedera, Avalanche Fuji, Linea Sepolia, Base Sepolia, BSC Testnet, Monad Testnet, Arbitrum Sepolia, and undeployed Solana Testnet are disabled and hidden.
- `ChainSelector.razor` is shared by the login/navigation placements and the liquid-staking dashboard. It renders enabled entries only.
- `/api/chains` returns enabled, sanitized public metadata only.
- Selection is persisted and crossing EVM/Solana families cannot reuse the previous identity.

### EVM liquid staking

- `CafeLiquidStakingVault.sol` is a non-upgradeable local EVM liquid vault.
- Depositing CAFE mints transferable stCAFE; redeeming burns stCAFE and returns CAFE.
- COFFEE reward accounting checkpoints share changes.
- The contract has ERC-4626-style previews, pause controls, reentrancy protection, exact accounting, and tests.
- Test CAFE/COFFEE and faucet fixtures plus a local deployment manifest are available.
- The existing Sepolia CAFE, COFFEE, pool, and faucet remain legacy references. New legacy pool deposits are disabled; exit/claim/migration guidance remains.
- The seven added EVM testnets do not yet have public vault deployments and must stay hidden.

### Solana liquid staking

- The Anchor program uses a vault/state PDA, CAFE and COFFEE custody, Token-2022 stCAFE, position PDAs, checked transfers, reward funding, claims, redeem, pause/admin controls, and events.
- stCAFE mint and freeze authority belong to the vault PDA. Receipt accounts remain frozen outside program operations.
- `transfer_st_cafe` is the supported receipt-transfer path; it checkpoints both wallets, thaws, transfers, and refreezes. A raw SPL transfer is intentionally rejected.
- Wallet Standard authentication persists hashed challenges in PostgreSQL and binds nonce, message, expiry, origin, chain, address, and Ed25519 signature.
- The browser can connect, deposit, redeem, and claim through Wallet Standard.
- The dashboard reads actual RPC balances, custody, supply, exchange rate, and pending rewards using raw integer quantities.
- Server verification checks finality, transaction success, signer, program/instruction/event discriminators, invocation stack, exact accounts, PDAs, mint decimals/authorities, token-account owners/mints, and canonical token programs.
- The worker uses persistent slot/signature cursors, isolated loops, bounded pagination, idempotent operation indexes, and failure-safe cursor transactions.
- `scripts/solana-repair-backfill.sh` supports bounded dry-run/backfill and explicit live-cursor advancement.
- `scripts/generate-solana-local-manifest.sh` emits public metadata and IDL/program checksums; it never accepts a private key.

### Persistence and application safety

- Wallet identities are family- and chain-aware, with reassignment protection.
- Ledger quantities have raw integer fields; display conversion uses deployment-specific token decimals.
- Ledger uniqueness is namespaced by chain, transaction/signature, and server-derived operation index.
- Wallet challenges and reconciliation cursors are PostgreSQL-backed and transactionally consumed/advanced.
- EVM and Solana failures are isolated so one RPC cannot stop every reconciliation loop.

### Agent commerce

- Implemented: local non-upgradeable ERC-8183 escrow, lifecycle tests, authenticated internal resource endpoints, and pinned x402 v2 TypeScript gateway/request binding.
- Not implemented: ERC-4337 infrastructure, ERC-8004 registry/reputation adapter, ERC-7683 two-node solver, related EF projections/worker loops/UI, and the complete integrated agent-commerce demo.
- ERC-8004, ERC-8183, and ERC-7683 are drafts. Keep exact revisions pinned behind adapters and label fixtures/experimental behavior honestly.

## Last verified release gate

The feature commit preceding this context file passed:

- .NET solution build: 0 errors.
- .NET unit tests: 95 passed.
- PostgreSQL integration tests: 6 passed using Apple Container PostgreSQL 16.
- EVM Hardhat tests: 5 passed.
- Agent gateway tests: 2 passed; TypeScript build passed.
- Solana Rust tests: 4 passed.
- Solana browser adapter tests: 3 passed.
- Anchor build passed.
- Anchor smoke passed on three consecutive fresh validators.
- EF migrations passed on a fresh PostgreSQL database.
- `dotnet format --verify-no-changes`, `cargo fmt`, shell syntax checks, and `git diff --check` passed.

Re-run checks relevant to any changed surface. Do not repeat these numbers as current evidence after modifying behavior without running the corresponding suites.

## Solana enablement truth

Solana localnet is implementation-complete and enabled only when Web and Worker receive the same validated manifest through `ARTISANALBREW_SOLANA_MANIFEST`, `Blockchain:SolanaDeploymentManifest`, or the legacy local-manifest key.

Solana Testnet is not publicly deployed and must remain disabled/hidden. The configured deployer had zero Testnet SOL and the public faucet was rate-limited. To enable it:

1. Obtain Testnet SOL for an explicitly authorized deployment wallet without committing or logging its secret material.
2. Build the pinned program and record source/IDL/binary checksums.
3. Deploy the program and create CAFE, stCAFE, COFFEE, custody, PDA, and administrator fixtures.
4. Confirm mint/token ownership, decimals, authorities, program data, and deployment slot through RPC.
5. Execute deposit → reward funding → vault-mediated stCAFE transfer → claim → redeem against Testnet.
6. Confirm Web transaction verification and Worker reconciliation against those public signatures.
7. Generate a `solana-testnet` manifest with an HTTPS RPC, positive deployment slot, and `SOLANA_PUBLIC_DEPLOYMENT_CONFIRMED=true`.
8. Supply the same manifest to Web and Worker, rerun application tests, and only then allow the selector/API to expose Testnet.

Never enable Testnet by flipping `Enabled=true` on the placeholder or inserting guessed addresses.

## Recommended next coding sequence

### Priority 1 — stabilize and merge the current stack

- Review draft PR #50 against its stacked base and respond to CI/review findings.
- Decide whether to keep it stacked on PR #48 or retarget after #48 merges.
- Keep unrelated local files out of commits.

### Priority 2 — controlled Solana Testnet rehearsal

- Proceed only when Alexis explicitly authorizes the broadcast and funds/selects the deployment wallet.
- Add a rehearsal checklist/artifact containing public transaction signatures, addresses, checksums, and smoke evidence.
- Do not place private RPC credentials in `/api/chains`, manifests, browser configuration, logs, or Git history.

### Priority 3 — first additional EVM network

- Prefer Base Sepolia because it is also the planned agent-commerce execution hub.
- Parameterize the existing deployment script and deploy a new vault/faucet around configured token fixtures.
- Verify source, manifest, wallet switching, dashboard actions, server verification, reconciliation, and rollback before enabling it.
- Roll out one chain at a time. A shared EVM bytecode artifact does not prove a deployment or RPC integration works.

### Priority 4 — complete the agent-commerce demo locally

- Pin a known ERC-4337 EntryPoint/account/bundler/paymaster stack; do not implement EntryPoint or cryptography from scratch.
- Add ERC-8004 identity/reputation fixtures behind a revisioned adapter.
- Add a deterministic two-node ERC-7683-compatible solver demonstration.
- Project on-chain state into PostgreSQL through idempotent workers.
- Build the recruiter-facing flow: discover → x402 quote → identity signals → cross-chain budget → smart-account escrow funding → evidence → completion/refund → reputation.
- Keep x402 for immediate digital resources and ERC-8183 for escrowed work; do not collapse their trust models.

## Local commands

Start/stop PostgreSQL with Apple Container, not Docker Desktop:

```bash
scripts/apple-container-postgres.sh start
scripts/apple-container-postgres.sh stop
```

The script reports the `TEST_POSTGRES_CONNECTION` value. Export it before integration tests.

Core verification:

```bash
dotnet restore
dotnet build ThisCafeteria.sln --configuration Release --no-restore
dotnet test tests/ThisCafeteria.UnitTests --configuration Release --no-build
dotnet test tests/ThisCafeteria.IntegrationTests --configuration Release --no-build
npm --prefix contracts/evm test
npm --prefix src/ThisCafeteria.AgentGateway test
npm --prefix src/ThisCafeteria.AgentGateway run build
cargo test --manifest-path contracts/solana/Cargo.toml --locked
npm --prefix contracts/solana run test:browser
dotnet format ThisCafeteria.sln --verify-no-changes --no-restore
cargo fmt --manifest-path contracts/solana/Cargo.toml --all -- --check
git diff --check
```

Anchor end-to-end verification requires the pinned Solana/Anchor toolchain and a clean local validator. Follow `contracts/solana/README.md`; do not infer success from Rust unit tests alone.

## Non-negotiable engineering rules

- Preserve unrelated dirty-worktree files. At the time this context was created, `.DS_Store` files, `.claude/`, two earlier prompt documents, a résumé, and generated `infra/main.json` were intentionally not part of PR #50.
- Never commit private keys, seed phrases, keypair JSON, `.env`, private RPC URLs, validator ledgers, `node_modules`, or `target` output.
- Never broadcast to a public chain merely because a deployment script exists. Require Alexis's explicit authorization for the intended network and wallet expenditure.
- Treat the browser, wallet responses, RPC responses, agent metadata, deliverable URIs, and transaction identifiers as untrusted input.
- Use server registry data as the trust root and verify successful on-chain execution before writing authoritative ledger state.
- Preserve legacy Sepolia claim/exit behavior and the storefront checkout while refactoring staking.
- Use raw integers for chain amounts; convert for display only with manifest-validated decimals.
- Keep per-chain reconciliation transactional, idempotent, bounded, restart-safe, and independently supervised.
- Do not mark a network complete because it compiles. Deployment, wallet interaction, server verification, reconciliation, and user-facing state must all pass.

## Completion report expected from the next agent

Report separately:

1. What was implemented.
2. Exact files and migrations changed.
3. Commands/tests run and their counts/results.
4. Local versus public deployments performed, including public signatures/addresses only.
5. Remaining blockers and whether each selector entry is enabled or hidden.
6. Git branch, commit, push, and PR state.

If a release gate fails, leave the affected network disabled and explain the evidence. That is a correct result, not a reason to bypass the gate.
