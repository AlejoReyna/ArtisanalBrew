# ArtisanalBrew — Build the Browser Activation/Revocation Flow for Session-Key Permissions

## Mission

Everything needed to activate and redeem a session-key delegation exists as either a proven
contract deployment, a verified server-side trust boundary, or a working reference script — except
the browser UI that ties them together. Build that UI: delegation signing, on-chain activation, and
revocation, wired into the existing `SmartAccountPanel.razor`. Agent-side autonomous redemption
(wiring `agenticPayments.ts` into `server.ts`) is explicitly **out of scope** for this prompt — it
depends on this work existing first and is tracked separately.

## Non-negotiable current-state context (verified this session — do not re-derive, trust this)

**What already works and must not be reimplemented or weakened:**

- `src/ThisCafeteria.Infrastructure/Services/SmartAccountService.cs` (`ISmartAccountService`) fully
  implements the *server* half of this flow, all fail-closed against live on-chain reads:
  `RegisterModularAccountAsync` (verifies the ERC-1967 implementation slot), `GetActivePermissionEpochAsync`
  / `RecordPermissionEpochInstalledAsync` (verify via `NonceEnforcer.currentNonce` that a claimed
  epoch is actually active on-chain before ever trusting it), `RevokeSessionPermissionsAsync` (only
  revokes locally after independently confirming the owner already advanced the on-chain nonce).
  **Do not modify the verification logic in these methods.** Your job is wiring a UI onto them, not
  changing their trust model.
- `SubmitOwnerUserOperationAsync(chainKey, ownerAddress, operation, cancellationToken)` on the same
  interface is the bundler relay, added and tested this session. It is a dumb forward: takes an
  already browser-signed `BundlerUserOperation` (see `ThisCafeteria.Application.Services.IBundlerClient`),
  checks the modular stack is configured and that `operation.Sender` matches the modular account
  already registered to `ownerAddress` (fails with `InvalidOperationException` otherwise — this
  prevents relaying an operation for an account that isn't the caller's), then calls
  `IBundlerClient.SendUserOperationAsync(chainKey, operation, entryPointOverride: chain.Deployment.ModularEntryPoint)`.
  It never signs anything and never asserts an operation succeeded — `RecordPermissionEpochInstalledAsync`
  / `RevokeSessionPermissionsAsync`'s on-chain re-verification remain the actual trust boundary. Call
  this from Razor exactly like every other `ISmartAccountService` method (direct DI injection — this
  is Blazor Server, there is no separate REST controller for smart-account operations and there
  should not be one for this either).
- **A real, non-obvious architecture bug was found and fixed this session — do not reintroduce it.**
  The `HybridDeleGator` implementation's immutable EntryPoint is the canonical ERC-4337 v0.7 singleton
  `0x0000000071727De22E5E9d8BAf0edAc6f37da032` — confirmed by grepping the deployed bytecode on both
  Sepolia and BSC Testnet (the canonical address appears 21 times; the chain's own legacy EntryPoint
  address appears zero times). This is **not** the same as `ChainDeployment.EntryPoint`, which is a
  *different*, custom-deployed EntryPoint this project uses only for the legacy SimpleAccount/Phase-4
  sponsorship stack. A new field, `ChainDeployment.ModularEntryPoint`, carries the canonical address
  and is what every modular/HybridDeleGator operation must resolve — never `ChainDeployment.EntryPoint`.
  This is already wired through `BlockchainManifestLoader` (reads `modularEntryPoint` from the
  manifest `addresses` object), `ChainRegistry.Validate` (requires it whenever
  `Capabilities.AgenticSessionPayments` is true, alongside `DelegationManager` and
  `HybridDeleGatorImplementation`), `SmartAccountService.TryGetConfiguredModularChain`, and
  `SmartAccountPanel.razor`'s `IsModularConfigured` check and its one existing JS interop call
  (`smartAccountRegistration.js`'s `entryPoint` parameter). Both `deployments/ethereum-sepolia.json`
  and `deployments/bsc-testnet.json` already carry the correct `modularEntryPoint` address, along
  with the full modular stack (`modularSimpleFactory`, `delegationManager`,
  `hybridDeleGatorImplementation`, all six enforcers) — verified live on both chains, no contract
  deployment is needed on either chain.
- `SmartAccountPanel.razor` today has exactly one wired control: "Register modular account", which
  imports `/js/smartAccountRegistration.js` and calls `deriveModularAccountAddress` (pure client-side
  address computation via the vendored `@metamask/delegation-toolkit` + viem browser bundle — no
  signature, no transaction). The active-epoch display block (`_activeEpoch`, showing agent/expiry/grant-count)
  and the "no active permission" empty state already render correctly once an epoch exists — you are
  adding the controls that create/end that state, not the display itself.
- **The browser never has the bundler URL.** `ChainDefinition.BundlerRpcUrl` is deliberately excluded
  from `/api/chains` and every public projection. This is why the relay method above exists, and it
  constrains how you get gas figures (see the gas-estimation section below) — there is no path where
  the browser calls a bundler RPC endpoint directly, for anything.
- **A self-hosted Sepolia bundler (Rundler) is running** on Azure VM `thiscafeteria-sepolia-aa`
  (resource group `thiscafeteria-prod-rg`), backed by a self-hosted geth+lighthouse Sepolia node. As
  of this session it was recovering from a stale-database crash (`lighthouse` needed
  `--purge-db-force` to checkpoint-resync) and `geth` was actively re-syncing, ETA a few hours from
  when this was written. Check current sync status before relying on it for live testing — do not
  assume it is caught up. It is not currently confirmed to support the canonical modular EntryPoint
  (only confirmed for the legacy one, per `docs/sepolia-rundler-deployment.md`) — check
  `eth_supportedEntryPoints` against it and be prepared to report if the canonical EntryPoint isn't
  advertised, rather than silently assuming it works.

## The gas-estimation design decision (resolved this session — implement it this way)

No code in this repo has ever needed a browser-initiated, non-sponsored, real-gas-estimated
UserOperation before. Everything today either goes through the sponsorship policy engine (which has
its own gas logic in `UserOperationSimulator`/`SponsorshipPolicyService`) or through Hardhat scripts
(which use `viem`'s `createBundlerClient` server-side against a bundler URL that script has direct
access to). Do not reuse `IUserOperationSimulator` for this — it validates a fully-specified gas
guess via `eth_call` simulation, it does not derive one, and it is wired into the sponsorship trust
path, not this one.

The resolved approach:

1. **Add gas estimation to `IBundlerClient`/`RundlerBundlerClient`**: a new method wrapping the
   standard ERC-4337 bundler RPC method `eth_estimateUserOperationGas`, mirroring the existing
   `SendUserOperationAsync`/`GetUserOperationReceiptAsync` pattern in that file exactly (same
   `CallAsync<T>` JSON-RPC helper, same `entryPointOverride` parameter shape, same v0.7
   packed/unpacked field translation already implemented in `ToRpc`). Per the ERC-4337 bundler spec,
   this method accepts a partially-formed operation (placeholder signature is fine — bundlers do not
   validate the signature for this call) and returns `preVerificationGas`, `verificationGasLimit`,
   `callGasLimit` (and for bundlers that estimate it, `paymasterVerificationGasLimit`/
   `paymasterPostOpGasLimit`, not relevant here since this is unsponsored).
2. **Add a thin passthrough on `ISmartAccountService`** (or a small new interface if that reads
   cleaner — your call, but keep it in the same DI-injected-into-Razor style as everything else) that
   calls this new bundler method with `entryPointOverride: chain.Deployment.ModularEntryPoint`. This
   is not a trust boundary — it is a convenience call to get real numbers instead of guessing, same
   spirit as `IUserOperationSimulator`'s own doc comment ("a number this codebase computed by itself
   would agree with itself and nothing else").
3. **Everything else happens in the browser, with no new server surface**: the owner's UserOperation
   nonce (`EntryPoint.getNonce(sender, key)`), whether the account needs `initCode` (i.e., is it
   deployed yet — `eth_getCode`), the encoded `execute()` calldata, and current gas prices
   (`eth_gasPrice`/`eth_maxPriorityFeePerGas`, same pattern `checkoutEth.js` already uses) are all
   derivable via the public RPC through the account object the toolkit SDK already gives you. Do not
   invent a server endpoint for any of these — if you find yourself wanting one, stop and reconsider,
   the SDK's `MetaMaskSmartAccount` object (a viem `SmartAccount`) already exposes what you need.

## Extending the vendored browser bundle

`contracts/evm/scripts/browser-delegation-bundle-entry.ts` currently re-exports only
`toMetaMaskSmartAccount`, `Implementation`, `createPublicClient`, `createWalletClient`, `custom`,
`toHex` — exactly what `smartAccountRegistration.js` needs and nothing more. Extend it to also
export what this task needs from `@metamask/delegation-toolkit` (`createDelegation`, `createExecution`,
`ExecutionMode`, `contracts` — for `NonceEnforcer.incrementNonce`/`DelegationManager.redeemDelegations`
encoding, mirroring `src/ThisCafeteria.AgentGateway/src/agenticPayments.ts`'s exact same imports) and
from `viem` whatever additional primitives you need (e.g. `parseAbi`/gas-price reads — check what's
already used in `checkoutEth.js` and `agenticPayments.ts` before assuming you need something new).
Rebuild with `npm run build:browser-delegation-bundle --prefix contracts/evm` (defined in
`contracts/evm/package.json`, invokes `build-browser-delegation-bundle.ts`, an esbuild IIFE build —
do not hand-edit `wwwroot/js/delegation-toolkit.iife.min.js` or its `.LICENSE` file, both are
generated). Confirm the rebuilt bundle is committed alongside the entry-file change.

## Non-negotiable safety rules

1. This flow moves real (test) funds and revokes real permissions once activated. **Never activate a
   permission epoch, submit a UserOperation, or broadcast any transaction without the user's explicit,
   in-the-moment authorization for that specific action** — this includes your own testing. Ask before
   every real broadcast, every time, even if a similar one was just approved.
2. Never store, log, or transmit the owner's private key. The owner signs delegations and
   UserOperations client-side, via their own connected wallet, exactly as
   `smartAccountRegistration.js` already does for address derivation — you are extending that
   pattern, not inventing a new trust model.
3. Do not weaken any of `ISmartAccountService`'s existing fail-closed on-chain verification
   (`RecordPermissionEpochInstalledAsync`'s nonce check, `RevokeSessionPermissionsAsync`'s pre-revoke
   confirmation, `RegisterModularAccountAsync`'s implementation-slot check, `SubmitOwnerUserOperationAsync`'s
   sender-match check). If the UI needs a new server capability, add a new method with its own
   verification; do not add a way to assert state the chain does not agree with.
4. Every new UserOperation path (activation, revocation) must surface failure reasons to the user
   clearly — do not silently swallow a rejected simulation, a bundler rejection, or a signature the
   owner declined. `checkoutEth.js`'s `normalizeProviderError` helper (added this session, fixed a
   real `"[object Object]"` bug from unwrapped wallet-provider rejections) is the pattern to copy for
   any new JS module that talks to `window.ethereum`.
5. Do not commit, push, or open a PR unless explicitly asked.
6. The repository has other uncommitted/in-progress work possibly present depending on which worktree
   you're in — inspect `git status` first, preserve everything, and do not touch files outside this
   task's scope.

## Implementation order and gates

### 0. Confirm the bundler actually supports the canonical EntryPoint

Before writing any browser code, check the Rundler bundler's `eth_supportedEntryPoints` response
(directly, or via the existing `RundlerBundlerClient` pattern) includes
`0x0000000071727De22E5E9d8BAf0edAc6f37da032`. If it doesn't, this is a real blocker to report, not
something to route around — the fix (adding the canonical EntryPoint to Rundler's own config) is
infrastructure, not application code.

Gate: a written determination, with the actual RPC response, of whether the canonical EntryPoint is
supported.

### 1. Gas estimation

Add `EstimateUserOperationGasAsync` to `IBundlerClient`/`RundlerBundlerClient` and its thin
`ISmartAccountService` (or sibling) passthrough, per the design above.

Gate: a unit test (mirroring the existing `RundlerBundlerClientTests.cs` patterns) proving the
method sends the correctly-shaped `eth_estimateUserOperationGas` request and parses a realistic
Rundler-shaped response.

### 2. Browser: delegation creation and owner signing, and UserOperation building

Add a JS module (sibling to `smartAccountRegistration.js`, same IIFE-bundle-import pattern) that:
- builds and signs the two scoped delegations (exact `approve(escrow, amount)` and exact
  `escrow.fund(jobId, amount, 0x)` calldata) with the correct caveats, mirroring
  `contracts/evm/scripts/metamask-session-key-e2e.ts` (`unsignedPermission`/`signPermission`
  functions, roughly lines 173–201) exactly — same enforcer set, same one-time
  `LimitedCallsEnforcer(1)` semantics, same epoch/nonce binding read live via
  `NonceEnforcer.currentNonce` through the public RPC;
- builds and signs the owner's `NonceEnforcer.incrementNonce(DelegationManager)` UserOperation
  (mirroring `agenticPayments.ts`'s `encodePermissionEpochChange`, now available in the browser
  bundle) using the account's nonce/gas-estimate/gas-price as described above, then calls
  `ISmartAccountService.SubmitOwnerUserOperationAsync` (via the C# Razor code-behind — the JS module
  returns the signed `BundlerUserOperation`-shaped object, C# submits it, same division of labor as
  `deriveModularAccountAddress` returning data for C# to act on).

Surface this as a new step in `SmartAccountPanel.razor`, gated behind having a registered modular
account with no active epoch (reuse the existing `_modularAccountRegistered` / `_activeEpoch is null`
branch).

Gate: owner can sign both delegations and the activation UserOperation in-browser against a real
Sepolia wallet; the signed payloads are inspectable/loggable client-side before submission (nothing
broadcasts without explicit user action in the UI, matching safety rule #1).

### 3. Activation wiring

Wire the "Activate" button: submit the signed activation UserOperation via
`SubmitOwnerUserOperationAsync`, then call `RecordPermissionEpochInstalledAsync` with the resulting
tx hash and the signed grants. Trust the server method's existing on-chain nonce re-verification — do
not add a client-trusted "it worked" shortcut.

Gate: activating an epoch in the browser (with explicit user authorization for the broadcast)
produces a server-verified `AgentPermissionEpochInfo` that the panel's existing epoch-display block
renders correctly; a tampered/wrong/stale nonce claim is rejected by the server exactly as
`RecordPermissionEpochInstalledAsync`'s doc comment promises.

### 4. Revocation

Add the missing UI control: a "Revoke agent permission" button (there is currently no caller of
`RevokeSessionPermissionsAsync` anywhere in `src/ThisCafeteria.Web`) that builds/signs the owner's
`incrementNonce` UserOperation the same way as activation, submits it via
`SubmitOwnerUserOperationAsync`, then calls `RevokeSessionPermissionsAsync`, trusting its existing
pre-revoke on-chain confirmation.

Gate: revoking in the browser (with explicit user authorization) leaves the epoch `Revoked` per
`GetActivePermissionEpochAsync`.

### 5. Regression, security review, and documentation

- Run the full existing .NET test suite. Add unit tests for the new `IBundlerClient` method and any
  new `ISmartAccountService` method, following the existing test-double conventions in
  `SmartAccountServiceTests.cs`/`RundlerBundlerClientTests.cs` (small per-file stubs, not shared
  mocks).
- Update `docs/erc4337-session-key-provenance.md`'s "Live acceptance evidence" section (or add a new
  doc) recording whether the flow ran against real Sepolia, the bundler used, and any deviation from
  the reference script's exact sequence.
- Update the README capability table if `agenticSessionPayments` moves from `false` to `true` on any
  chain — only after a real end-to-end proof, per this project's existing rule that no capability
  flag is `true` without a working, verified flow behind it.

## Testing minimums

- Gas estimation: unit test proving the request/response shape against a realistic bundler response.
- Delegation signing: correct caveat set, correct one-time/exact-amount semantics.
- Activation: server rejects a claimed epoch that doesn't match live `NonceEnforcer.currentNonce`;
  rejects a replayed/duplicate activation claim (existing server behavior — write a test if one
  doesn't already cover it for this exact path).
- Revocation: server rejects a revoke claim the chain doesn't yet agree happened.
- All existing checkout/staking/wallet-login/chain-selector regression suites still pass.

## Acceptance criteria

- A real user, on the live app, connected to Ethereum Sepolia with a real wallet, can register a
  modular account (already works), sign and activate a scoped session-key delegation, and revoke it —
  with every step backed by real on-chain transactions the user explicitly authorized, not a
  simulated node.
- Every server-side trust boundary already built into `ISmartAccountService` remains fail-closed and
  unmodified in its verification logic.
- No private key (owner's) is ever exposed to the browser, logged, or committed.
- `SubmitOwnerUserOperationAsync` correctly targets `ChainDeployment.ModularEntryPoint`, never
  `ChainDeployment.EntryPoint`, for every modular operation.
- Existing legacy Sepolia behavior (checkout, staking, wallet login, SimpleAccount path) is
  unaffected.

## Handoff format (for audit)

Report, precisely: what was checked vs. assumed about the bundler's EntryPoint support (with the raw
RPC response); every file added or changed, grouped by contracts/server/browser/tests/docs; the exact
gas-estimation request/response shapes used; a walkthrough of one full register → sign → activate →
revoke cycle, including whether it was actually executed against real Sepolia or only built/unit-tested
(say so plainly either way — do not imply a live run happened if it didn't); test results; and any
step that could not be completed, with the precise blocker. Do not mark anything "done" that wasn't
independently verified against live on-chain state — this project's own history includes claims that
"turned out to be false and had to be re-verified from scratch," and the whole point of the
fail-closed design this session extended is that a claim and its proof are the same artifact.
