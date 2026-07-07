# Staking System Upgrade — Implementation Prompt

You are working in **ArtisanalBrew** (ThisCafeteria), a .NET Blazor Server coffee-shop app with a crypto staking feature on **Ethereum Sepolia**. Your task is to implement the upgrade backlog below. Work through the items **in order** — they are sorted by priority. Create a feature branch (e.g. `feature/staking-hardening`) off the current branch before you start. **Push the branch to origin after every committed item** so no work is ever stranded locally.

## How the staking system works today (verified against the code)

Three contracts are involved: an ERC-20 payment token ("CAFE"), the CoffeeCoin reward ERC-20 ("COFFEE"), and a StakingPool contract (source in `contracts/CafeStakingPool.sol` and `contracts/ThisCafeteriaTokens.sol` — read them before changing any ABI). Contract addresses live under `Blockchain:Network` in `src/ThisCafeteria.Web/appsettings.json` (note: `StakingPoolContract` is empty there and injected via environment config in deployment; see `docs/sepolia-staking-pool-deploy.md`). The backend uses hand-written minimal ABIs in `src/ThisCafeteria.Web/Services/Blockchain/ContractAbis.cs` (pool ABI: `balanceOf`, `earned`, `stake`, `unstake`, `withdraw`).

The flow:

1. **Connect** — `src/ThisCafeteria.Web/wwwroot/js/coffeeStaking.js` (`connectWalletForStaking`) picks the MetaMask provider, forces the Sepolia chain, then POSTs the address to `POST /staking/save-wallet-session` (`StakingController.SaveWalletSession`), which stores the checksummed address in the ASP.NET session — **with no proof of ownership**.
2. **Stake** — JS sends two MetaMask txs: exact-amount `approve` on the payment token, then `stake(amount)` on the pool. Step-by-step status streams into Blazor via `DotNetObjectReference` → `YieldPanel.OnTxStatusChanged` → `TxStepper` UI.
3. **Record** — JS POSTs `{walletAddress, amount, transactionHash}` to `POST /staking/api/record-stake`. `StakingController.RecordStakingTransactionAsync` checks: session wallet matches, hash not already in DB, then `CoffeeWeb3Service.VerifyStakingTransactionAsync` verifies on-chain (receipt succeeded, tx targeted the pool, `from` == wallet, calldata selector + amount decode, matching ERC-20 `Transfer` event in logs). On success it inserts a `StakingLedgerEntry` (off-chain history mirror; unique index on `TransactionHash`).
4. **Unstake** — same shape; accepts `unstake(uint256)` or `withdraw(uint256)` selectors, Transfer event must flow pool → wallet.
5. **Dashboard** — `CoffeeWeb3Service.GetDashboardDataAsync` fans out parallel read-only RPC calls (native balance, token balance, pool `balanceOf`, pool `earned`, COFFEE balance). `YieldPanel.razor` renders stat tiles, stake/unstake cards, and the last 8 ledger entries.

**Crucial context:** the app already has signature-based wallet authentication in `src/ThisCafeteria.Web/Controllers/WalletAuthController.cs` — `POST /api/wallet-auth/challenge` issues a nonce (cached, expiring), and `POST /api/wallet-auth/verify` recovers the signer via `EthereumMessageSigner.EncodeUTF8AndEcRecover` and signs the user in. The staking session endpoint simply doesn't use it. **Reuse this flow — do not build a new signature scheme.**

Key files:

| File | Role |
|---|---|
| `src/ThisCafeteria.Web/Controllers/StakingController.cs` | Session + record/verify endpoints |
| `src/ThisCafeteria.Web/Controllers/WalletAuthController.cs` | Existing nonce-challenge signature auth |
| `src/ThisCafeteria.Web/Services/Blockchain/CoffeeWeb3Service.cs` | Nethereum reads + on-chain verification |
| `src/ThisCafeteria.Web/Services/Blockchain/ContractAbis.cs` | Minimal ABIs |
| `src/ThisCafeteria.Web/wwwroot/js/coffeeStaking.js` | MetaMask/web3.js client flow |
| `src/ThisCafeteria.Web/Components/Shared/YieldPanel.razor` | Main staking UI + ledger history |
| `src/ThisCafeteria.Web/Components/Shared/TxStepper.razor` | Multi-step tx status UI |
| `src/ThisCafeteria.Application/Services/Blockchain/` | `ICoffeeWeb3Service`, `StakingAmountRules`, `StakingTransactionType`, `CoffeeDashboardModel` |
| `src/ThisCafeteria.Domain/Entities/StakingLedgerEntry.cs` + `src/ThisCafeteria.Infrastructure/Persistence/Configurations/StakingLedgerEntryConfiguration.cs` | Ledger entity + EF config |
| `contracts/CafeStakingPool.sol` | Actual pool contract source — the ground truth for ABIs |
| `src/ThisCafeteria.Worker/` | Background worker project |
| `tests/ThisCafeteria.UnitTests/StakingDashboardTests.cs` | Only 3 staking tests exist today |

## Upgrade backlog (implement in this order)

### 1. Require proof of wallet ownership for staking sessions (security, highest priority)
`StakingController.SaveWalletSession` accepts any syntactically valid address — anyone can claim any wallet. Rework the staking connect flow to reuse the existing challenge/verify machinery from `WalletAuthController`: after MetaMask connect, `coffeeStaking.js` must request a challenge, have the user sign it (`personal_sign`), and submit the signature; only a signature-verified address may be stored in the staking session. Keep the UX to a single extra MetaMask signature prompt (no gas). Extract the shared nonce/recover logic into a service both controllers use rather than duplicating it. Users already signed in via wallet auth should not need to re-sign.

### 2. Fix CSRF exposure on staking endpoints
`StakingController` is annotated `[IgnoreAntiforgeryToken]`. Remove it and make the JS `fetch` calls send the antiforgery token (ASP.NET Core can expose it via a cookie/header pattern — wire it the idiomatic way for this app). All state-changing endpoints (`save-wallet-session`, `clear-wallet-session`, `record-stake`, `record-unstake`) must be covered. Apply the same treatment to `RewardsController`'s `mint-loyalty` endpoint if it has the same issue.

### 3. Add a rewards claim flow
`earned()` is displayed as "Pending Rewards" but nothing can claim it. Check `contracts/CafeStakingPool.sol` for the actual claim function name/signature first. Add end-to-end support: extend the pool ABI, add a claim button + TxStepper flow in `YieldPanel.razor`, a `claim` handler in `coffeeStaking.js`, a `POST /staking/api/record-claim` endpoint, server-side verification in `CoffeeWeb3Service.VerifyStakingTransactionAsync` (a claim emits a COFFEE-token `Transfer` from the pool to the wallet; the amount is not known client-side beforehand, so verify against the event value rather than a user-supplied amount), a new `StakingTransactionType.Claim`, and a `"claim"` ledger `ActionType` rendered in the activity list. If the deployed pool does not expose a claim function, degrade gracefully: hide the button when a static `eth_call` probe fails, and note this in your summary.

### 4. Add confirmation-depth check to on-chain verification
`VerifyStakingTransactionAsync` (and `VerifyPaymentTransactionAsync`) accept a receipt the instant it lands — a reorg can orphan a recorded tx. Require a configurable minimum confirmation depth (add `MinimumConfirmations` to `BlockchainNetworkOptions`, default 2–3): compare current block number to the receipt's block number, and have the client wait/retry the record call briefly (with TxStepper feedback like "Waiting for confirmations…") instead of failing hard.

### 5. Ledger reconciliation via the Worker
The DB ledger diverges from the chain whenever the record step is skipped (user closes tab between tx confirm and record). Add a background reconciliation job in `src/ThisCafeteria.Worker`: periodically scan the pool contract's event logs (`eth_getLogs` over a bounded, checkpointed block range) for stake/unstake (and claim) events involving wallets known to the app, and insert any missing ledger rows (using the block timestamp for `RecordedAtUtc`; the unique tx-hash index makes inserts idempotent). Keep RPC load modest — checkpoint the last scanned block in the DB.

### 6. Stop lying about APR
`StakingAprPercent` is a hardcoded config value (5.2) shown as "Contract Annual Yield" while actual rewards come from the contract. Check `CafeStakingPool.sol` for a reward-rate view function; if one exists, derive the displayed APR from the contract; if not, keep the config value but change the UI label to make clear it is an indicative/configured rate, not contract-enforced.

### 7. Surface RPC failures instead of returning 0
`CoffeeWeb3Service.GetPendingStakingRewardsAsync` catches all exceptions and returns `0m` — an RPC outage silently renders "0 rewards". Log the failure and propagate an "unavailable" state (e.g. nullable decimal or a result wrapper in `CoffeeDashboardModel`) so `YieldPanel` can show "—" with a tooltip/retry rather than a false zero.

### 8. Skip redundant approvals
The stake flow always sends an `approve` tx. Read `allowance(owner, spender)` first in `coffeeStaking.js` and skip the approve step (and its TxStepper step) when the existing allowance covers the amount. Keep exact-amount approvals when one is needed — do not switch to infinite approvals.

### 9. Correct server-side unstake validation
`RecordStakingTransactionAsync` validates all amounts with `StakingAmountRules.IsValidStakeAmount`. For unstakes it should use `IsValidUnstakeAmount` against the wallet's staked balance (fetch via `GetStakedPaymentTokenBalanceAsync`; remember the on-chain balance is *post*-unstake at verification time, so validate accordingly or drop the balance check and document why on-chain verification suffices — your call, but be deliberate).

### 10. Remove the missing-table band-aids and consolidate duplicated helpers
`IsMissingStakingLedgerTable` (string-matching `"42P01"` / `"StakingLedgerEntries"` in exception messages) appears in both `StakingController` and `YieldPanel`. Migrations are applied at deploy time now — delete these catch blocks and let a real error surface. While there, consolidate the three copies of `IsConfiguredAddress` / `TryNormalizeWallet` / tx-hash validation scattered across `StakingController`, `CoffeeWeb3Service`, and `YieldPanel` into one shared static helper class in `ThisCafeteria.Application` (e.g. `WalletAddressRules` next to `StakingAmountRules`).

### 11. Fix DI anti-patterns in the Blazor layer
`YieldPanel.razor` resolves `AppDbContext` via `Services.GetService<AppDbContext>()` and `StakingController` resolves `UserManager` via `IServiceProvider`. In Blazor Server, injected scoped `DbContext` instances live as long as the circuit — switch `YieldPanel`'s ledger query to `IDbContextFactory<AppDbContext>` (register with `AddDbContextFactory` if not already), and constructor-inject `UserManager<ApplicationUser>` in `StakingController`.

### 12. Test coverage
`StakingDashboardTests.cs` has 3 tests; the verification logic has none. Add unit tests for at least: calldata decoding (`TryDecodeStakingAmount` — stake/unstake/withdraw/claim selectors, malformed input), `StakingAmountRules` (both methods), wallet/hash normalization helpers, the new confirmation-depth logic, and controller behaviors (session mismatch → 400, duplicate hash → 409, unverified tx → 400, unauthenticated → 401) using the existing test patterns in the repo. If the verification internals are hard to test through `ICoffeeWeb3Service`, refactor the pure decoding/validation parts into testable static/internal methods rather than mocking Nethereum RPC.

## Constraints and working rules

- **Do not break the existing purchase/mint flow** (`initCoffeePurchases`, `RewardsController.mint-loyalty`) — it shares `coffeeStaking.js` and the session-wallet plumbing.
- Preserve the existing UX patterns: TxStepper step naming (`approve`/`stake`/`record`, `pending`/`confirmed`/`error`), `notify()`/`notifyCompleted()` contract with `YieldPanel`.
- Database changes go through EF Core migrations in `src/ThisCafeteria.Infrastructure/Persistence/Migrations` (Postgres). Any new entity needs an `IEntityTypeConfiguration` next to the existing ones.
- This is Sepolia testnet, but write the code as if real value were at stake — that's the point of this upgrade.
- Match the existing code style: file-scoped namespaces, `sealed` classes, expression-bodied members where the codebase uses them, no comment noise.
- After each backlog item: `dotnet build` and `dotnet test` must pass before moving to the next. Commit per item with a message describing the change, **and push to origin** — unpushed work has already been lost once on this machine.
- If an item turns out to be infeasible, implement the graceful-degradation path described in the item and record the limitation in your final summary — don't silently skip.
- Port 5286 may already be in use on this machine; use a different port if you need to run the app locally.

## Definition of done

All 12 items addressed (or explicitly reported as degraded with reasons), solution builds clean, all tests pass, branch pushed, and a final summary lists: what changed per item, new endpoints/config keys, any new migrations, and anything requiring a contract redeploy or config change (e.g. `MinimumConfirmations`, `StakingPoolContract`).
