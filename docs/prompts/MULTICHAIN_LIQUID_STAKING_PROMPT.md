# ArtisanalBrew Multichain Liquid Staking — Implementation Prompt

You are a senior full-stack and smart-contract engineer working in the existing ArtisanalBrew repository. Implement the multichain liquid-staking program described below. Read [`docs/multichain-liquid-staking-plan.md`](../multichain-liquid-staking-plan.md) completely before editing code; it contains the audited current-state map, architecture decisions, risks, and orchestration gates.

## Mission

Retain Ethereum Sepolia and add these testnets:

- Hedera Testnet;
- Avalanche Fuji;
- Linea Sepolia;
- Base Sepolia;
- BNB Smart Chain Testnet;
- Monad Testnet;
- Arbitrum Sepolia;
- Solana Testnet.

Replace the current non-liquid CAFE staking experience with this behavior:

1. A user deposits CAFE.
2. The protocol mints transferable stCAFE receipt tokens.
3. The current holder of stCAFE accrues separately funded COFFEE rewards.
4. The holder can claim COFFEE.
5. The holder can redeem stCAFE for CAFE.

This is liquid staking of CAFE, not validator staking of each network's native gas asset.

Add one shared multichain selector component in both required placements:

- the login pill/account control in `NavMenu.razor`;
- the staking dashboard's `yield-sidebar` in `YieldPanel.razor`, with an equivalent usable presentation on mobile.

The selected chain must remain synchronized across both placements, persist through navigation/reload, and drive wallet connection, dashboard data, transaction submission, verification, explorer links, and reconciliation.

## Non-negotiable legacy context

The current Ethereum Sepolia deployments are:

| Role | Address |
|---|---|
| CAFE | `0x15DbED39271D2788a9Be63ffB34C8E2DdED8754A` |
| COFFEE | `0x4056E7F5FD1584C3db6223c9483761Dcb30Bf21C` |
| Legacy `CafeStakingPool` | `0x932d50E20F917B9BbBe2C40F30D43BCef0e93F90` |

All three explorer pages currently report unverified source. The tracked `contracts/ThisCafeteriaTokens.sol` file is empty. Do not claim byte-for-byte source reproducibility for those deployments, do not attempt an in-place/proxy upgrade, and do not strand legacy stakers.

Deploy the new Sepolia liquid vault against the existing CAFE and COFFEE addresses. Keep the old pool available only for balance display, reward claim, unstake/withdraw, reconciliation, and a guided two-step migration into the new vault. Disable new legacy deposits in the UI.

The repository already has unrelated uncommitted changes. Inspect `git status` before work, preserve all user changes, and do not rewrite or discard unrelated files. Do not commit, push, open a PR, or broadcast any public testnet deployment unless the user explicitly authorizes that action. Local contract deployment is required and authorized.

## Current code you must inspect before editing

- `src/ThisCafeteria.Application/Configuration/BlockchainNetworkOptions.cs`
- `src/ThisCafeteria.Web/Program.cs`
- `src/ThisCafeteria.Web/Controllers/WalletAuthController.cs`
- `src/ThisCafeteria.Web/Controllers/StakingController.cs`
- `src/ThisCafeteria.Web/Services/Wallet/WalletChallengeService.cs`
- `src/ThisCafeteria.Web/Services/Blockchain/CoffeeWeb3Service.cs`
- `src/ThisCafeteria.Web/Services/Blockchain/ContractAbis.cs`
- `src/ThisCafeteria.Web/Services/Blockchain/WalletDashboardState.cs`
- `src/ThisCafeteria.Web/Components/Layout/NavMenu.razor` and CSS
- `src/ThisCafeteria.Web/Components/Shared/YieldPanel.razor` and CSS
- `src/ThisCafeteria.Web/wwwroot/js/walletAuth.js`
- `src/ThisCafeteria.Web/wwwroot/js/coffeeStaking.js`
- `src/ThisCafeteria.Domain/Entities/StakingLedgerEntry.cs`
- `src/ThisCafeteria.Domain/Entities/StakingReconciliationCheckpoint.cs`
- their EF configurations and migrations
- `src/ThisCafeteria.Worker/StakingLedgerReconciliationWorker.cs`
- `contracts/CafeStakingPool.sol`, `contracts/CafeFaucet.sol`, and the empty token file
- current staking, wallet, controller, decoder, and integration tests
- checkout and reward-minting paths that share the current blockchain service

## Required chain registry

Replace the singleton `Blockchain:Network` design with an immutable validated registry. Use stable string keys; do not force Solana into an EVM `ChainId` field.

| Key | Family | Chain ID/cluster | Hex | Currency | Public RPC default | Explorer |
|---|---|---|---|---|---|---|
| `ethereum-sepolia` | EVM | `11155111` | `0xaa36a7` | ETH | `https://ethereum-sepolia-rpc.publicnode.com` | `https://sepolia.etherscan.io` |
| `hedera-testnet` | EVM relay | `296` | `0x128` | HBAR | `https://296.rpc.thirdweb.com` | `https://hashscan.io/testnet` |
| `avalanche-fuji` | EVM | `43113` | `0xa869` | AVAX | `https://43113.rpc.thirdweb.com` | `https://testnet.snowtrace.io` |
| `linea-sepolia` | EVM | `59141` | `0xe705` | ETH | `https://59141.rpc.thirdweb.com` | `https://sepolia.lineascan.build` |
| `base-sepolia` | EVM | `84532` | `0x14a34` | ETH | `https://84532.rpc.thirdweb.com` | `https://sepolia.basescan.org` |
| `bsc-testnet` | EVM | `97` | `0x61` | tBNB | `https://97.rpc.thirdweb.com` | `https://testnet.bscscan.com` |
| `monad-testnet` | EVM | `10143` | `0x279f` | MON | `https://10143.rpc.thirdweb.com` | `https://monad-testnet.socialscan.io` |
| `arbitrum-sepolia` | EVM | `421614` | `0x66eee` | ETH | `https://421614.rpc.thirdweb.com` | `https://sepolia.arbiscan.io` |
| `solana-testnet` | Solana | `testnet` | n/a | SOL | `https://api.testnet.solana.com` | `https://explorer.solana.com/?cluster=testnet` |

Use `https://solana-testnet.drpc.org` as a configurable Solana fallback. Add development-only `evm-local` and `solana-localnet` entries after local manifests exist.

Each entry needs public wallet RPC, independently overridable server RPC, explorer templates, finality policy, native currency metadata, icons, contract/program identifiers, deployment start block/slot, and capabilities. Never return a credentialed server RPC to the browser. Validate duplicate keys/chain IDs, malformed URLs, missing family fields, duplicate deployments, and inconsistent capabilities at startup.

At minimum define these capabilities independently: wallet login, liquid staking, legacy exit, faucet, marketplace payment, and reward minting. A visible network may have staking disabled until a valid deployment manifest is configured; render an honest unavailable state instead of using zero addresses or silently falling back to another chain.

## Implementation order and gates

Work in the following order. Keep the solution buildable at each gate. Do not perform public deployments as part of these steps.

### 0. Protect and characterize the baseline

- Record the dirty-worktree state and avoid unrelated edits.
- Add regression tests around existing Sepolia wallet authentication, checkout/payment verification, staking transaction verification, session matching, and ledger uniqueness.
- Correct stale documentation that claims token source exists if touched by this work.
- Record legacy deployment addresses and configurable deployment start blocks.

Gate: the current .NET suite passes and existing Sepolia checkout behavior is characterized.

### 1. Create reproducible local contract projects

#### EVM

Create `contracts/evm` as a pinned Hardhat 3 TypeScript project with its own lockfile. Use a current pinned OpenZeppelin Contracts release compatible with the selected compiler. Do not use floating dependency versions.

Implement:

- `CafeLiquidStakingVault`, an ERC-4626 CAFE vault whose ERC-20 shares are stCAFE;
- testnet/local CAFE and COFFEE token implementations with explicit roles/caps or fixed supply, used only where the legacy tokens do not exist;
- a faucet suitable for testnets/local development;
- deployment, seeding, ABI export, and manifest generation scripts.

`CafeLiquidStakingVault` requirements:

- standard ERC-4626 `deposit`, `mint`, `withdraw`, `redeem`, preview/conversion/limit behavior;
- transferable stCAFE shares;
- OpenZeppelin inflation/donation mitigations;
- COFFEE reward funding over a bounded schedule;
- global reward-per-share plus per-holder checkpoints;
- checkpoint sender and receiver before every share transfer, mint, and burn so already accrued COFFEE stays with the prior holder and future rewards follow the transferred stCAFE;
- `earned`, `claimRewards`, reward-rate and period-finish views/events;
- safe transfers, reentrancy protection, pause, two-step ownership or least-privilege roles;
- deposits may pause while withdrawals and funded reward claims remain available;
- no administrative method can rescue user CAFE principal or committed COFFEE;
- reject zero amounts, invalid duration, unsupported fee-on-transfer/rebasing asset behavior, and underfunded schedules.

Write unit, property/fuzz, and invariant tests for:

- first deposit and inflation/donation attacks;
- deposit/mint/withdraw/redeem preview parity and rounding direction;
- stCAFE transfer before, during, and after reward accrual;
- no double claim after transfer;
- reward conservation and schedule rollover;
- pause/emergency behavior;
- malicious/reentrant token behavior;
- role and rescue restrictions.

Provide a local Hardhat node using chain ID 31337 and an idempotent command that deploys and funds CAFE, COFFEE, the vault, and faucet. Generate a JSON manifest with chain key/ID, addresses, deploy block, compiler/settings, source commit, ABI checksum, and timestamp. Export canonical ABI JSON for the app; remove duplicated handwritten liquid-vault ABI fragments.

#### Solana

Create `contracts/solana` as a pinned Rust/Anchor workspace.

Implement:

- local/testnet SPL CAFE and COFFEE mints;
- Token-2022 stCAFE mint;
- vault authority PDA and CAFE custody account;
- deposit, redeem, reward funding, reward checkpoint, reward claim, pause, and admin instructions;
- checked transfers and strict mint/decimal/account constraints;
- emitted events and deterministic PDA/version seeds;
- a Token-2022 transfer hook, or an equivalently secure reviewed mechanism, that checkpoints COFFEE rights whenever liquid stCAFE moves.

Do not implement plain transferable SPL stCAFE with address-local reward debt and no transfer checkpoint; that can lose or duplicate rewards.

Add local validator integration tests covering deposit, stCAFE transfer, accrual split between sender/recipient, claim, redeem, wrong mint/program/account rejection, pause, and replay/duplicate protection. Generate the IDL and a local deployment manifest with program ID, mint addresses, deployment slot, source commit, and checksums.

Gate: both protocol families complete a local deposit → transfer stCAFE → accrue/claim COFFEE → redeem CAFE smoke test without the web app.

### 2. Build the registry, selection state, and wallet identity model

Add:

- `BlockchainOptions` with `DefaultChainKey` and chain definitions;
- immutable `IChainRegistry` with startup validation;
- scoped `ISelectedChainAccessor`;
- sanitized `GET /api/chains`;
- antiforgery-protected `POST /api/chains/select`;
- a protected same-site selected-chain cookie/session representation.

Every state-changing blockchain API request must carry `chainKey`. The server must resolve RPC and deployment identifiers only from its registry. Reject unknown/disabled chains and any family, wallet, transaction, contract/program, or selected-chain mismatch.

Introduce a `WalletIdentity` entity related to `ApplicationUser` with:

- family (`Evm`, `Solana`);
- normalized address/public key and display form;
- wallet provider;
- verification timestamp;
- unique `(Family, NormalizedAddress)` index.

Migrate existing EVM users safely from `ApplicationUser.WalletAddress` and `WalletChainId`. Preserve a one-release compatibility read path and plan a later removal migration. Expand address/identifier fields for Solana instead of retaining 42-character assumptions.

Refactor challenges behind family-specific verification:

- EVM: single-use expiring domain/URI/nonce challenge, selected chain binding, and Nethereum signer recovery;
- Solana: Solana Wallet Standard `signMessage`, base58 public key validation, and server Ed25519 verification;
- rate limit challenge creation and verification;
- bind cached challenges to session, family, normalized identity, chain key, issue time, and expiration;
- never allow one family/verifier to consume another family's challenge.

The same EVM address across EVM chains maps to one EVM wallet identity. Solana is a separate identity unless a future explicit linking flow proves both keys; do not silently merge identities.

Gate: forward migration tests, registry validation tests, EVM/Solana signature tests, expired/replayed/cross-family challenge tests, and selection persistence tests pass.

### 3. Decompose server blockchain services

Break the single `CoffeeWeb3Service` responsibility into chain-safe interfaces, including:

- blockchain gateway resolver by family;
- liquid-staking gateway;
- marketplace payment gateway;
- COFFEE/reward-token gateway;
- EVM client factory keyed by chain key;
- Solana client factory keyed by chain key.

Preserve the existing Ethereum Sepolia marketplace checkout and loyalty behavior. Do not make checkout follow a newly selected chain unless that chain explicitly has marketplace-payment and reward-minting deployments configured. Never fall back from an unsupported selected chain to Sepolia without telling the user.

Generalize dashboard state and responses to include `(chainKey, family, normalized wallet)`. Ignore stale async responses from a previously selected chain.

Gate: existing checkout tests and new gateway-resolution/wrong-chain tests pass.

### 4. Implement EVM liquid staking end to end

Replace `stake/unstake` as the primary experience with:

- approve only when allowance is insufficient;
- `deposit(CAFE, receiver)` and previewed stCAFE shares;
- `redeem(stCAFE, receiver, owner)` and previewed CAFE assets;
- `claimRewards()` for COFFEE;
- configured confirmation waiting;
- server-side receipt, sender, target, calldata, event, amount, share, reward-token, and chain verification;
- idempotent ledger recording.

Use contract events as ground truth. Do not trust client-submitted amounts, addresses, share output, RPC URLs, or contract addresses without matching them against decoded transaction/receipt data and server registry configuration.

Keep a Sepolia legacy panel that can:

- read old staked CAFE and pending COFFEE;
- claim rewards;
- unstake/withdraw;
- guide the user into a subsequent new-vault deposit;
- record and reconcile legacy operations separately.

Gate: local EVM browser-level end-to-end flow passes, including account/chain change, insufficient allowance, rejection, confirmation wait, refresh, duplicate record, legacy exit, and stale selected-chain cases.

### 5. Add the shared selector and liquid-staking UI

Create one `ChainSelector.razor` and reuse it in both locations. Do not fork selector logic or maintain two independent selected-chain values.

Login pill requirements:

- selected network icon and short label;
- shortened authenticated address when connected;
- chain can be selected before wallet login;
- wallet choices adapt to EVM vs Solana;
- EVM selection performs `wallet_switchEthereumChain`, then `wallet_addEthereumChain` only for error 4902;
- Solana uses Solana Wallet Standard, with Phantom supported;
- switching families clears incompatible transient state and requires the matching authenticated identity.

Staking sidebar requirements:

- selector near Account Overview;
- full chain name and capability/status;
- responsive/mobile equivalent in the existing drawer/sidebar UX;
- accessible keyboard behavior, focus states, labels, expanded state, and readable error messages.

Dashboard requirements:

- CAFE wallet balance;
- stCAFE balance;
- redeemable CAFE value;
- share exchange rate;
- pending COFFEE and COFFEE wallet balance;
- deposit preview and action;
- redeem preview and action;
- claim action;
- chain-specific native gas balance and faucet/help;
- honest unsupported/unconfigured/RPC-unavailable states;
- recent activity scoped to the selected chain and wallet;
- legacy Sepolia migration card only where configured.

Listen for EVM `accountsChanged`/`chainChanged` and Solana account/disconnect events. Never submit a transaction when active wallet, authenticated identity, selected family, or selected chain disagrees.

Gate: component tests and browser tests prove both selector placements stay synchronized and accessible across desktop/mobile states.

### 6. Implement Solana end to end

Add separate browser and server adapters; do not put Solana conditionals throughout EVM code.

Browser flow:

- discover/connect through Solana Wallet Standard;
- sign the authentication challenge;
- derive/create required associated token accounts explicitly;
- build only instructions for the registry-configured program/mints;
- wallet sign/send;
- wait for configured commitment;
- post chain key and signature for verification.

Server verification must fetch the transaction from the trusted Solana RPC and validate:

- successful status and configured commitment;
- signer matches authenticated public key;
- configured program ID;
- expected instruction discriminator;
- vault PDA, mint, token accounts, owner, and amount/share/reward deltas;
- no duplicate `(chainKey, signature, instruction index)` record.

Gate: localnet browser end-to-end parity passes. Keep Solana Testnet liquid staking capability disabled until a separately authorized deploy and smoke test succeeds.

### 7. Migrate persistence and reconciliation

Update staking ledger data to store:

- chain key and family;
- wallet up to at least 64 characters;
- transaction/signature up to at least 128 characters;
- operation/event/instruction index;
- action type;
- asset, share, and reward amounts separately with safe precision;
- asset, receipt, reward, and vault/program identifiers up to at least 64 characters;
- block/slot, timestamp, explorer URL, and verification state.

Replace global transaction-hash uniqueness with `(ChainKey, TransactionId, OperationIndex)`. Make reconciliation checkpoints unique by `(ChainKey, SourceIdentifier)` and able to hold EVM block cursors or Solana slot/signature cursors.

Run one independently supervised loop per enabled deployment. Use bounded ranges, independent backoff, cancellation, structured chain labels, and health reporting. One failing RPC must not stop all chains. Continue the legacy Sepolia reconciler until explicit retirement.

Gate: replay, duplicate, reorg/finality, missing-record recovery, cursor resume, and one-chain-failure-isolation tests pass.

### 8. Local orchestration, CI, docs, and rollout controls

Provide documented, repeatable commands to:

1. start PostgreSQL;
2. start the local Hardhat node and Solana validator;
3. build/deploy/seed both protocol families;
4. generate and load deployment manifests without editing committed production configuration;
5. apply EF migrations;
6. start web and worker;
7. execute the full local smoke test.

Extend CI to run:

- formatting/static checks;
- .NET unit and integration tests;
- EF migration validation;
- EVM contract tests and local deployment smoke test;
- Solana/Anchor tests and local deployment smoke test where the runner supports the pinned toolchain;
- browser tests for selector synchronization and core wallet states using deterministic mocked providers or local nodes.

Document environment variables, manifest schema, adding a chain, local reset/redeploy, legacy migration, capability rollout, RPC failure handling, and rollback. Never print or commit deployer keys. Public deployment scripts must require an explicit network and confirmation flag and must refuse unknown chain IDs.

Gate: clean-checkout local setup succeeds and all available suites pass.

## API and security rules

- All state-changing endpoints require antiforgery protection and authenticated wallet ownership.
- Add rate limits to wallet challenge and transaction-record endpoints.
- Validate chain key before address/hash/signature parsing.
- Use family-specific normalization; never pass Solana identifiers through EVM checksum utilities.
- Use integer base units internally for blockchain amounts. Convert to decimal/display strings only at boundaries; do not use JavaScript `Number` for token base units.
- Bind verification to configured chain, deployment, function/instruction, signer, asset, and observed events/balance deltas.
- Keep private RPC endpoints and all private keys server-only.
- Apply RPC timeout, retry/backoff, cancellation, and response-size limits.
- Do not call an operation successful until the server verifies it at the configured finality depth.
- Use checks-effects-interactions and least privilege in contracts/programs.
- Do not introduce an admin path that can seize user principal.

## Testing minimums

Add focused tests for at least:

- registry validation and public/private RPC separation;
- selected-chain persistence and two-selector synchronization;
- EVM/Solana address normalization and signature verification;
- challenge expiry, replay, family swap, chain swap, and session swap;
- EVM calldata/event verification for deposit, redeem, claim, legacy exit/claim;
- Solana instruction/account/balance verification;
- wrong chain, wrong contract/program, wrong signer, wrong token/mint, wrong amount, failed transaction, insufficient finality;
- chain-scoped ledger uniqueness and reconciliation cursors;
- stale dashboard response after chain switch;
- checkout regression on Ethereum Sepolia;
- accessible desktop/mobile selector interaction;
- every contract/program property and invariant listed in Phase 1.

## Acceptance criteria

The work is complete only when:

- all nine public networks render with accurate family-specific metadata;
- the login pill and staking sidebar use one synchronized selector state;
- EVM and Solana login prove key ownership correctly;
- a clean local setup deploys both protocol families reproducibly;
- local EVM and Solana flows deposit CAFE, mint and transfer stCAFE, split COFFEE accrual correctly, claim COFFEE, and redeem CAFE;
- every recorded operation is verified on its actual chain and namespaced in storage;
- legacy Sepolia users can still claim and exit, then migrate;
- unsupported chains show explicit capability state;
- existing Sepolia checkout and loyalty behavior is not regressed;
- no secret is exposed in client configuration, logs, manifests, or source;
- .NET, migration, EVM, Solana, integration, and browser tests pass;
- no public testnet transaction was broadcast without explicit user authorization.

## Handoff format

At the end, report:

- implementation summary by phase;
- exact files and migrations added/changed;
- new configuration keys and environment variables;
- generated local deployment addresses/program IDs and manifest locations;
- commands run and test results;
- legacy migration behavior;
- chains still capability-disabled and the precise reason;
- public deployments or funding still requiring explicit authorization;
- known risks or follow-up work.
