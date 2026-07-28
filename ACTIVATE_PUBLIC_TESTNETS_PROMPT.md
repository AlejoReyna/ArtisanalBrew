# ArtisanalBrew — Really Activate the Remaining Public Testnets

You are a senior full-stack and smart-contract engineer working in the existing ArtisanalBrew repository. Read [`docs/multichain-liquid-staking-plan.md`](docs/multichain-liquid-staking-plan.md) and [`docs/multichain-liquid-staking-operations.md`](docs/multichain-liquid-staking-operations.md) completely before editing anything. The operations doc documents the exact working pattern this task must repeat; do not invent a different one.

## Mission

Seven of the nine chains this app advertises are placeholders. Take each of the following from "defined but disabled" to "really live" the same way BSC Testnet already was:

- Hedera Testnet (`hedera-testnet`, chain ID 296)
- Avalanche Fuji (`avalanche-fuji`, chain ID 43113)
- Linea Sepolia (`linea-sepolia`, chain ID 59141)
- Base Sepolia (`base-sepolia`, chain ID 84532)
- Monad Testnet (`monad-testnet`, chain ID 10143)
- Arbitrum Sepolia (`arbitrum-sepolia`, chain ID 421614)
- Solana Testnet (`solana-testnet`)

"Really live" means: real contracts deployed and reviewed on the real public network, a validated deployment manifest committed under `contracts/*/deployments/`, the chain flips to `Enabled = true` in the running app, and the local deposit → transfer/accrue → claim → redeem smoke flow passes against the real deployment before it is ever advertised to a user.

## Non-negotiable current-state context (verified this session — do not re-derive, trust this)

- `src/ThisCafeteria.Application/Configuration/BlockchainOptions.cs:18-40` (`ChainDefinitionDefaults.PublicChains`) is the code-backed fallback chain list used whenever `Blockchain:Chains` is absent from configuration (`ChainRegistry` constructor, `IChainRegistry.cs:19`). Every chain above is present there with `enabled: false` and an empty `Deployment` (`new()`); only `ethereum-sepolia` and `bsc-testnet` are meaningfully configured, and only `ethereum-sepolia` is `enabled: true`.
- `contracts/evm/hardhat.config.ts:13-39` defines exactly one real public network target: `bscTestnet` (RPC from `BSC_TESTNET_RPC_URL`, key from `BSC_DEPLOYER_PRIVATE_KEY`). Hedera, Fuji, Linea, Base Sepolia, Monad, and Arbitrum Sepolia have **no Hardhat network entry at all** — this must be added per chain before anything can deploy.
- `contracts/evm/scripts/deploy.ts:13-14` already refuses any non-local chain ID unless `CONFIRM_PUBLIC_DEPLOYMENT=I_UNDERSTAND_THIS_BROADCASTS` is set. Keep and reuse this guard for every new network; do not weaken or bypass it.
- `src/ThisCafeteria.Application/Configuration/BlockchainManifestLoader.cs` is the single hard-limiting bottleneck: `TryReadEvm` (line 32-37) only recognizes chain ID `31337 → "evm-local"` and `97 → "bsc-testnet"` and throws `InvalidDataException` for anything else; `TryReadSolana` (line 116-119) only recognizes `cluster: "localnet"` or `"testnet"`, one slot each. `Program.cs` in both `ThisCafeteria.Web` and `ThisCafeteria.Worker` only ever pass a **single** EVM manifest path (`ARTISANALBREW_EVM_MANIFEST` / `Blockchain:LocalEvmManifest`) and a **single** Solana manifest path into `LoadDeploymentManifests`. This means deploying real contracts to a new chain is not sufficient by itself — **the loader must be generalized to accept an arbitrary chain ID against an allowlist of the six new EVM chain IDs, and to load multiple simultaneous EVM manifests** (e.g. one manifest path per configured chain, or a directory of manifests), before a second and third public EVM chain can be enabled at the same time as `bsc-testnet`. Do this generalization once, early, rather than special-casing each new chain the way `bsc-testnet` currently is (see the `chainId == 97` conditionals throughout `TryReadEvm`).
- `contracts/evm/deployments/bsc-testnet.json` is the reference manifest shape and the exact JSON schema `TryReadEvm` expects (`addresses.cafe/coffee/liquidVault/faucet`, `capabilities`, `deployBlock`, etc.). Reuse this shape for every new EVM chain manifest, extended only for the new optional fields already read by the loader (`entryPoint`, `accountFactory`, `modularSimpleFactory`, `delegationManager`, etc. — leave these blank unless you are also doing the ERC-4337 activation work described in the companion prompt `ACTIVATE_SESSION_KEY_SMART_ACCOUNTS_PROMPT.md`; do not conflate the two efforts in one PR).
- `IChainRegistry.cs:31,32,34` (`Validate`) will reject startup if an enabled chain claims `LiquidStaking`/`LegacyExit` capability without the matching deployment fields, and (for Solana) enforces canonical `TokenProgram`/`Token2022Program` addresses and matching decimals across CAFE/stCAFE. Read this validation before writing manifests; a manifest that doesn't satisfy it will crash the app at boot, not fail gracefully.
- `contracts/solana` already has a working Anchor workspace and localnet test suite (`docs/multichain-liquid-staking-operations.md`, "Local Solana" section) that can persist a fixture manifest via `SOLANA_FIXTURE_OUTPUT`. Solana Testnet activation reuses this, pointed at a funded Testnet deployer instead of the local validator.
- The repository has unrelated uncommitted changes and untracked scratch files (`git status`). Inspect it first, preserve everything, and do not touch files outside this task's scope.

## Non-negotiable safety rules

1. Never broadcast a funded public-testnet transaction without the user's explicit, in-the-moment authorization for that specific chain — the existing `CONFIRM_PUBLIC_DEPLOYMENT` gate is necessary but not sufficient; ask before setting it.
2. Never request, log, print, or persist a deployer private key. It is supplied only through the user's own shell environment for the duration of one deployment command.
3. Never flip a chain's `Enabled` flag to `true` (whether by manifest or by editing `ChainDefinitionDefaults.PublicChains`) until its full local smoke flow (deposit → transfer/mint stCAFE → accrue → claim COFFEE → redeem CAFE, or the Solana equivalent) has passed against that chain's real deployment.
4. Do not commit, push, or open a PR unless explicitly asked.
5. If a chain's public RPC (the `thirdweb.com` placeholder URLs currently in `PublicChains`) turns out to be unusable (rate-limited, wrong chain, dead), say so and propose an alternative rather than silently degrading the manifest.

## Implementation order and gates

### 0. Generalize the manifest loader (do this once, first)

- Extend `BlockchainManifestLoader.TryReadEvm` to accept any chain ID from an explicit allowlist covering all seven target chains (296, 43113, 59141, 84532, 10143, 421614), each mapped to its correct `chainKey`, `displayName`, native currency, and explorer templates — mirroring exactly what `ChainDefinitionDefaults.PublicChains` already declares for the disabled entries, so enabling a chain doesn't change its advertised metadata, only its `Enabled`/`Deployment` state.
- Change the manifest-loading call sites (`src/ThisCafeteria.Web/Program.cs`, `src/ThisCafeteria.Worker/Program.cs`) to accept and load multiple EVM manifest paths (one env var per chain, or a single directory-glob env var — pick whichever fits the existing `ARTISANALBREW_EVM_MANIFEST` convention with least surprise) instead of exactly one.
- Add regression tests proving: an unrecognized chain ID still throws, a manifest with a mismatched `chainKey` for its `chainId` still throws, loading manifests for two different chains at once enables both without disturbing `ethereum-sepolia` or any still-disabled chain, and existing `bsc-testnet`/`evm-local` loading behavior is unchanged.

Gate: existing .NET suite passes; a synthetic two-chain manifest-loading test passes.

### 1. Add Hardhat network targets

Add one network entry per new EVM chain to `contracts/evm/hardhat.config.ts`, following the exact `bscTestnet` pattern (chain-specific RPC URL env var default, chain-specific deployer key env var, correct chain ID). Confirm compiler settings (`transactionGasCap`, `viaIR`, optimizer runs) are compatible with each target — some public RPCs and chains (Hedera's EVM relay in particular) have documented quirks; investigate before assuming Hardhat's default HTTP network type works unmodified.

Gate: `npx hardhat compile` succeeds; each new network resolves via `npx hardhat run --network <name>` against a dry-run/no-op script (do not deploy yet).

### 2. Deploy and validate each chain, one at a time, with explicit authorization per chain

For each chain, in order (suggest starting with the ones most likely to have straightforward, well-documented public RPCs — Base Sepolia and Arbitrum Sepolia are Superchain/Arbitrum-standard and lowest-risk; Hedera's EVM relay and Monad are newer and likely to need extra investigation):

1. Ask the user to confirm they want to proceed with this specific chain and that they will supply a funded deployer key for it.
2. Set the chain's RPC URL and deployer key env vars locally (never in a committed file).
3. `npm run build && npm run test` (must pass before any public broadcast).
4. Run the deploy script with `CONFIRM_PUBLIC_DEPLOYMENT=I_UNDERSTAND_THIS_BROADCASTS` against that network, producing `contracts/evm/deployments/<chain-key>.json`.
5. Review the emitted addresses and manifest by hand before proceeding — no automated step should treat a manifest as trusted without a human review checkpoint.
6. Run the local deposit → mint stCAFE → transfer → accrue → claim COFFEE → redeem CAFE smoke flow against the real deployed contracts (reuse whatever script/harness the earlier liquid-staking work built for `evm-local`/`bsc-testnet`; do not write a new one per chain).
7. Only after the smoke flow passes, load the manifest and confirm the chain now appears `Enabled = true` via `GET /api/chains` and in both `ChainSelector` placements (login pill, staking sidebar).

Repeat for Solana Testnet using the Anchor workspace's existing deploy/test pattern instead of Hardhat, pointed at a funded Testnet keypair; same authorization-per-chain, review-before-enable, smoke-flow-before-enable discipline. Recall `IChainRegistry` requires exact canonical `TokenProgram`/`Token2022Program` addresses and matching CAFE/stCAFE decimals for any enabled Solana chain with liquid staking — verify these before generating the manifest, not after a failed startup.

Gate per chain: manifest committed, reviewed, smoke flow passed, chain visibly enabled and functioning in both selector placements, existing chains (especially `ethereum-sepolia` and any already-enabled chain) unaffected.

### 3. Regression and documentation

- Run the full existing .NET, EVM, and Solana test suites; nothing pre-existing may regress.
- Update `docs/multichain-liquid-staking-operations.md` with the exact commands used for each newly activated chain, following its existing "BSC Testnet EVM" section as the template.
- Update `docs/multichain-liquid-staking-plan.md` if any chain required an architectural deviation from what it originally specified (e.g. Hedera relay quirks, Monad-specific gas behavior).

Gate: clean-checkout instructions for each newly activated chain are documented and reproducible by someone who wasn't in this conversation.

## Testing minimums

- Manifest loader: unrecognized chain ID rejection, chain-key/chain-id mismatch rejection, multi-manifest simultaneous loading, no regression to `evm-local`/`bsc-testnet`/`ethereum-sepolia` behavior.
- Per newly enabled chain: contract unit/fuzz/invariant suite (reuse from Phase 1 of the original multichain plan — do not re-derive), local smoke flow against the real deployment, `ChainRegistry` startup validation with the real manifest.
- Selector/UI: newly enabled chains appear correctly in both `ChainSelector` placements with correct name/icon/capability state; still-disabled chains remain absent; switching into and out of a newly enabled chain doesn't corrupt selection state for other chains.
- No public deployment step runs in CI; CI continues to exercise only `evm-local`/local Solana validator paths unless the user explicitly asks otherwise.

## Acceptance criteria

- Every chain the user explicitly authorizes for this pass is really deployed, reviewed, smoke-tested, and visibly enabled — not just marked `enabled: true` on faith.
- Any chain the user does not authorize (e.g. still-unresolved RPC/tooling issues) stays honestly disabled with the precise blocking reason recorded, not silently skipped.
- `ethereum-sepolia`'s existing checkout/staking/wallet-login behavior is unaffected.
- No deployer key, seed phrase, or credential appears in source, logs, manifests, or chat transcript.
- Existing test suites (.NET, EVM, Solana) all pass.

## Handoff format

Report, per chain: authorized or not (and why if not), deployment addresses and manifest path, smoke-test result, and current `Enabled` state — plus overall loader/config changes made, commands run, and any chain-specific quirks discovered that future maintainers need to know.
