# ArtisanalBrew — Really Implement the Remaining Capabilities on the Three Live Chains

## Mission

Three chains are already enabled and selectable in the running app via committed deployment
manifests: **Ethereum Sepolia (11155111)**, **BNB Smart Chain Testnet (97)**, and **Solana Devnet**.
Each is missing capabilities the others have. Bring the three to a coherent, verified capability
parity — real contracts, real manifests, real end-to-end flows, no stubs and no capability flag set
`true` without a working feature and verification path behind it.

## Non-negotiable current-state context (verified this session — do not re-derive, trust this)

- The registry ships nine chain definitions in `src/ThisCafeteria.Application/Configuration/BlockchainOptions.cs`
  (`ChainDefinitionDefaults.PublicChains`). All are `Enabled = false` except `ethereum-sepolia`.
- Chains become visible only when a manifest replaces the default with an `Enabled = true` definition
  (`BlockchainManifestLoader.LoadDeploymentManifests`), and `ChainsController.cs:19` filters the public
  API on `chain.Enabled`. Both Web and Worker `appsettings.json` point at
  `deployments/ethereum-sepolia.json;deployments/bsc-testnet.json` (EVM) and
  `deployments/solana-devnet.json` (Solana). Those three files exist and carry real on-chain addresses.
- **Capabilities as they resolve at runtime today:**
  - Ethereum Sepolia: `WalletLogin, LiquidStaking, Faucet, RewardMinting, AgenticCommerce`.
    Missing: `AgenticSessionPayments`, `MarketplacePayment`, `LegacyExit`.
  - BSC Testnet: `WalletLogin, LiquidStaking, Faucet, RewardMinting, AgenticCommerce`.
    Missing: `AgenticSessionPayments`, `MarketplacePayment`.
  - Solana Devnet: `WalletLogin, LiquidStaking, RewardMinting` (reward funding + reconciliation).
    Missing: `Faucet`, `AgenticCommerce` (EVM-only stack — see scope note below).
- **Loader capability nuance — this is the crux.** In `BlockchainManifestLoader.TryReadEvm`
  (lines ~129–137) the EVM capability block is **hardcoded** to `WalletLogin/LiquidStaking/Faucet/
  RewardMinting = true`, and only `AgenticCommerce` and `AgenticSessionPayments` are read from the
  manifest's `capabilities` object. `MarketplacePayment` and `LegacyExit` are **never** read from an
  EVM manifest — they are only ever set on the compile-time legacy Sepolia default. So a manifest
  declaring `marketplacePayment: true` is silently ignored today.
- `AgenticSessionPayments` is already wired end to end EXCEPT for deployment: the loader reads
  `modularSimpleFactory` + the enforcer/delegation addresses (lines ~116–126) and the
  `agenticSessionPayments` flag (line ~136); `SmartAccountService` (`src/ThisCafeteria.Infrastructure/
  Services/SmartAccountService.cs:~590`) **fails closed** unless every modular address is present.
  The current `ethereum-sepolia.json` / `bsc-testnet.json` manifests do not carry those addresses, so
  the capability is false. Provenance rules: `docs/erc4337-session-key-provenance.md`. A focused prompt
  for just this capability already exists: `ACTIVATE_SESSION_KEY_SMART_ACCOUNTS_PROMPT.md` — reuse it.
- Capability consumers to wire against, not around:
  - `src/ThisCafeteria.Web/Components/Shared/YieldPanel.razor` gates faucet and legacy-exit UI on
    `SelectedChain.Capabilities.Faucet` / `.LegacyExit`.
  - `SmartAccountService` gates session-key smart accounts on the modular deployment.
- Manifests contain public addresses and checksums only — never a private key or seed phrase.

## Scope

**In scope — bring these three capabilities up:**

1. `AgenticSessionPayments` on **Ethereum Sepolia** and **BSC Testnet** — deploy the MetaMask
   Delegation Framework modular stack on each chain, populate the manifest, capability lights up.
2. `MarketplacePayment` on **Ethereum Sepolia** and **BSC Testnet** — this requires a loader change
   (read the flag from the manifest instead of hardcoding it) plus a working, verified checkout
   payment path on the liquid chains.
3. `Faucet` on **Solana Devnet** — a real devnet CAFE faucet flow so onboarding matches the EVM chains.

**Out of scope unless the operator explicitly authorizes it, with reasons:**

- `LegacyExit` on any liquid chain. The legacy Sepolia pool is unverified and must not be reused or
  upgraded (existing README safety rule). Do not enable it to reach "parity."
- Full `AgenticCommerce` on Solana. The ERC-8004/7683/8183 stack is EVM-only; a Solana equivalent is a
  separate, large workstream. If requested, treat it as its own prompt, not part of this one.

## Non-negotiable safety rules

- Never set a capability flag `true` unless the contracts are deployed, addressed in a validated
  manifest, and a real end-to-end flow plus verification passes on that chain. No demo-only flags.
- Deploy and verify one chain at a time, and require explicit operator authorization before any public
  broadcast or funded transaction. Unattended tests and builds stay local-only.
- Public manifests carry addresses/checksums only. Hosted-bundler URLs and signer keys stay in
  environment variables (`ARTISANALBREW_BUNDLER_RPC_URL__*`, `Sponsorship__VerifyingSignerPrivateKey`),
  never in a committed manifest.
- Web and Worker must load identical manifests. A capability enabled for the app but invisible to the
  reconciler (or vice versa) is a bug, not a partial win.
- The browser never chooses trusted addresses; the server resolves them from the registry/manifest.

## Implementation order and gates

### 0. Fix the loader so manifest capabilities are honored (do this once, first)

- In `BlockchainManifestLoader.TryReadEvm`, read `marketplacePayment` (and, behind the out-of-scope
  gate, `legacyExit`) from the manifest `capabilities` object instead of hardcoding them off. Keep the
  `IChainRegistry.Validate` guard that a capability requiring a deployment (e.g. `LegacyExit` needs
  `LegacyPool`) fails validation when the address is missing — extend the same pattern for any new
  deployment a capability requires.
- Add/extend unit tests in `tests/ThisCafeteria.UnitTests/BlockchainManifestLoaderTests.cs` proving a
  manifest can turn `marketplacePayment` on and off, and that an inconsistent manifest (flag on,
  required address missing) throws.

### 1. AgenticSessionPayments — Ethereum Sepolia, then BSC Testnet

- Follow `ACTIVATE_SESSION_KEY_SMART_ACCOUNTS_PROMPT.md` and the provenance matrix in
  `docs/erc4337-session-key-provenance.md`. Deploy the unmodified MetaMask Delegation Framework
  (v1.3.0) modular stack — factory, DelegationManager, HybridDeleGator implementation, and the
  enforcers the loader expects — per chain.
- Add the addresses and `capabilities.agenticSessionPayments: true` to that chain's manifest under
  `deployments/`. Confirm `SmartAccountService` stops failing closed and a session-key delegation can
  be created, installed on chain, redeemed by the agent, and revoked.

### 2. MarketplacePayment — Ethereum Sepolia, then BSC Testnet

- Decide and document the payment rail (x402 gateway vs. direct escrow settlement) and wire the
  checkout path to it on the liquid chains. Reuse the agentic-commerce escrow already deployed
  (`erc8183Escrow` in the manifests) where appropriate; see `AGENTIC_COMMERCE_STACK_PROMPT.md`.
- Turn the capability on via the manifest (now honored after step 0) and un-gate the checkout UI.
- Prove a real payment settles and reconciles on both chains.

### 3. Faucet — Solana Devnet

- Provide a real CAFE faucet on devnet (program instruction or an authorized server-side mint from the
  manifest `administrator`, matching how the EVM faucet behaves). Add `walletLogin`-parity onboarding.
- Extend `TryReadSolana` to read a `faucet` capability from the Solana manifest and set
  `ChainCapabilities.Faucet` accordingly; add the flag to `deployments/solana-devnet.json`.
- Un-gate the Solana faucet UI in `YieldPanel.razor` and prove a devnet claim credits CAFE.

### 4. Regression, security review, and documentation

- Update the README network table (capabilities columns) and
  `docs/multichain-liquid-staking-plan.md` / `-operations.md` to match the new reality.
- Run the full test suite plus `/security-review`. Update `ChainVisibilityTests` and
  `ChainRegistryTests` for any capability assertions that change.

## Testing minimums

- Unit: loader reads `marketplacePayment` (and Solana `faucet`) from manifests; validation rejects a
  capability whose required deployment address is absent.
- Integration: `/api/chains` reports the new capabilities for each enabled chain; `ChainVisibility`
  and `ChainRegistry` tests updated.
- End-to-end, per chain, on the real network (with authorization): session-key create → install →
  redeem → revoke (EVM); checkout payment settle + reconcile (EVM); CAFE faucet claim (Solana devnet).
- Worker reconciliation processes any new on-chain events the capabilities emit.

## Acceptance criteria

- Ethereum Sepolia and BSC Testnet each report `AgenticSessionPayments` and `MarketplacePayment`
  `true`, backed by deployed contracts in their manifests and passing end-to-end flows.
- Solana Devnet reports `Faucet` `true`, backed by a working devnet claim.
- No capability flag is `true` anywhere without a deployed address and a green end-to-end path.
- The loader no longer hardcodes `marketplacePayment`; manifests are the single source of truth.
- Full test suite and security review pass; README and docs match the running app.

## Handoff format

Report, per chain and per capability: deployed addresses, the manifest diff, the end-to-end evidence
(tx hashes / UserOperation hashes / devnet signatures), test results, and anything left gated and why.
