# ArtisanalBrew — Really Implement ERC-4337 Session-Key Smart Accounts on a Public Testnet

You are a senior full-stack and smart-contract engineer working in the existing ArtisanalBrew repository. Read [`docs/erc4337-session-key-provenance.md`](../erc4337-session-key-provenance.md) completely before editing anything — it is the audited-provenance source of truth for every contract/SDK address and version this task must reuse, not redeploy or substitute. If the larger agentic-commerce work is also in scope for this codebase, also read the ERC-4337 integration section of [`AGENTIC_COMMERCE_STACK_PROMPT.md`](AGENTIC_COMMERCE_STACK_PROMPT.md) (lines "### ERC-4337 integration") for the paymaster/policy rules that apply here too; this prompt is a narrower, standalone slice of that work — implement it even if the rest of the agentic-commerce stack is not being built.

## Mission

Everything proven in `contracts/evm/scripts/metamask-session-key-e2e.ts` — owner deploys a HybridDeleGator account, signs two scoped EIP-712 delegations, activates them on-chain, an agent redeems them autonomously to fund an escrow, and the owner can revoke — currently only runs as a Hardhat script against a local node in CI. Make the **entire flow** clickable in the live Blazor app against a real public testnet (Ethereum Sepolia, using the app's existing default chain, unless the user asks for a different target).

Only step 1 of this flow exists in the browser today: registering/deriving a counterfactual modular-account address. Build the rest: delegation creation and owner signing, on-chain epoch activation, agent-side redemption, and revocation — all against real deployed contracts on Sepolia, not a simulated node.

## Non-negotiable current-state context (verified this session — do not re-derive, trust this)

**What already works and must not be reimplemented:**

- `src/ThisCafeteria.Infrastructure/Services/SmartAccountService.cs` (`ISmartAccountService`) already fully implements, and independently verifies against live on-chain state, the *server* half of this flow: `RegisterModularAccountAsync` (verifies the ERC-1967 implementation slot before persisting), `RecordPermissionEpochInstalledAsync` (verifies via `NonceEnforcer.currentNonce` that the claimed epoch is actually active on-chain before persisting anything — throws `InvalidOperationException` otherwise), `GetActivePermissionEpochAsync` (re-verifies the same way on every read, so a stale local record never reports as active), and `RevokeSessionPermissionsAsync` (only revokes locally after independently confirming the owner already advanced the on-chain nonce). Do not rebuild or bypass this trust model — it is fail-closed by design and the whole point of the security posture. Your job is wiring a UI onto it, not changing it, unless you find an actual bug.
- `src/ThisCafeteria.Web/Components/Shared/SmartAccountPanel.razor` already renders account discovery/selection and has a partially-built epoch display (`_activeEpoch` block, lines 69-88, shows agent/expiry/grant-count once an epoch exists) and a "no active permission" empty state (line 89-92) — extend this component, don't fork a new one. The one interactive control that exists is `RegisterModularAccountAsync` (line 228) calling `smartAccountRegistration.js`'s `deriveModularAccountAddress()` — pure client-side address computation via `@metamask/delegation-toolkit`, no signature, no transaction.
- `contracts/evm/scripts/metamask-session-key-e2e.ts` is the exact reference sequence to replicate in the browser: owner's first UserOperation deploys the account (~line 146); owner signs two off-chain EIP-712 delegations for `approve` and `escrow.fund`, each scoped with `NonceEnforcer`/`TimestampEnforcer`/`LimitedCallsEnforcer(1)` caveats (~lines 174-201); owner sends a second UserOperation calling `NonceEnforcer.incrementNonce` to activate the epoch (~line 222) — this is what `RecordPermissionEpochInstalledAsync` expects to have already happened on-chain before it's called; the agent submits its own UserOperation redeeming both delegations through `DelegationManager` (~line 268) — this is the actual autonomous payment and never needs the owner's key; a later owner `incrementNonce` UserOperation revokes the epoch (~line 324) — this is what `RevokeSessionPermissionsAsync` expects to already be true on-chain before it updates local state.
- `docs/erc4337-session-key-provenance.md` lists the exact audited artifact, revision, and canonical/deployed address for every piece (`HybridDeleGator`, `SimpleFactory`, `DelegationManager`, the four enforcers used, plus `NonceEnforcer`/`TimestampEnforcer`), the pinned `@metamask/delegation-toolkit@0.13.0` SDK dependency chain, and the exact audit coverage/limitations. These are framework-revision canonical addresses (not chain-specific redeployments) per that table — **check whether they are already live on public Ethereum Sepolia before deploying anything**; the SDK (`toMetaMaskSmartAccount`, delegation signing/redemption helpers) is what must be reused in the browser, not reimplemented from raw EIP-712 typed-data construction.
- **The chain-deployment gap you must close carefully:** `ethereum-sepolia`'s entry in `ChainDefinitionDefaults.PublicChains` (`src/ThisCafeteria.Application/Configuration/BlockchainOptions.cs`, the `Evm(..., legacy: true)` call) only sets `Deployment.Cafe/Coffee/LegacyPool/Faucet` — none of `EntryPoint`, `ModularAccountFactory`, `DelegationManager`, `HybridDeleGatorImplementation`, or the six enforcer addresses are populated for any real chain today. `SmartAccountPanel.IsModularConfigured` (razor:129-145) requires every one of those fields to be non-blank before it will even show the "Register modular account" button — this is exactly why the panel shows "not configured yet" on Sepolia right now.
- **Trap to avoid:** `BlockchainManifestLoader.Replace()` (`src/ThisCafeteria.Application/Configuration/BlockchainManifestLoader.cs:16-20`) does a full remove-and-replace of a chain's entire `ChainDefinition` when a manifest loads for its key — it does not merge fields. `TryReadEvm` currently only recognizes chain ID `31337` and `97`, and would need `11155111` (Sepolia) added to light up the AA fields via a manifest — but if you do that, the generated Sepolia manifest **must also carry forward `Cafe`, `Coffee`, `LegacyPool`, `Faucet`, and every legacy capability flag**, or loading it will silently break existing checkout/staking/exit on Sepolia. Decide deliberately between (a) extending the loader to merge onto the existing static definition instead of replacing it, or (b) generating a Sepolia manifest that carries every existing field forward unchanged plus the new AA fields — and prove via a regression test that existing Sepolia checkout/staking/wallet-login is unaffected either way. If the companion prompt `ACTIVATE_PUBLIC_TESTNETS_PROMPT.md` is also being worked in this codebase, coordinate on the loader generalization rather than duplicating it — that prompt already tasks the loader's multi-chain-ID generalization.
- No bundler currently runs against any public chain. Locally, `docs/erc4337-session-key-provenance.md` (bottom) notes Rundler must run `--unsafe` because Hardhat EDR cannot run the ERC-7562 JS validation tracer — that limitation does not apply on a real chain, where a hosted safe-mode bundler (Pimlico, Alchemy, Biconomy, or a self-run Rundler/Alto against real Sepolia) is expected and required for a production-honest demo. Do not ship the `--unsafe` bundler flag against a public network.
- The repository has unrelated uncommitted changes and untracked scratch files (`git status`). Inspect it first, preserve everything, and do not touch files outside this task's scope.

## Non-negotiable safety rules

1. This flow moves real (test) funds autonomously once activated. Never activate a permission epoch, submit a UserOperation, or broadcast any public transaction without the user's explicit authorization for that specific action.
2. Never store, log, or transmit the owner's private key. The owner signs delegations and UserOperations client-side, via their own connected wallet — exactly as `smartAccountRegistration.js` already does for address derivation.
3. The agent's own key (whatever redeems delegations) is a scoped, low-value key by design (the existing e2e script uses Hardhat's published well-known test key locally — a real deployment needs a real, minimally-funded, rotate-able agent key, generated and held server-side, never exposed to the browser).
4. Do not weaken `ISmartAccountService`'s fail-closed on-chain verification anywhere (`RecordPermissionEpochInstalledAsync`'s nonce check, `RevokeSessionPermissionsAsync`'s pre-revoke confirmation, `RegisterModularAccountAsync`'s implementation-slot check). If the UI needs a new capability, add a new verified method; do not add a way to assert state the chain doesn't agree with.
5. Do not commit, push, or open a PR unless explicitly asked.
6. Every new UserOperation path (activation, redemption, revocation) must simulate before submission and must surface failure reasons to the user, matching the existing e2e script's live-rejection coverage (uninstalled permission, wrong target/token/selector/amount, non-default/batch/delegatecall mode, exhausted quota, expiry, revocation) — do not silently swallow a rejected simulation.

## Implementation order and gates

### 0. Confirm what's actually live on Sepolia

Before deploying anything, check whether the canonical `EntryPoint` (`0x0000000071727De22E5E9d8BAf0edAc6f37da032`) and the MetaMask Delegation Framework v1.3.0 contracts listed in `docs/erc4337-session-key-provenance.md` are already deployed at their documented addresses on public Ethereum Sepolia (bytecode/exact-match check, same method the provenance doc used for its Etherscan verification links). If they are, this phase is "point at them," not "deploy them." What you almost certainly do need to deploy fresh to Sepolia: this repo's own `SimpleFactory`/reference account factory instance (or confirm the repo already has a canonical Sepolia deployment of its own factory — check `contracts/evm` deployment history) and the `AgenticCommerceEscrow` (or equivalent) contract the delegations ultimately call into, since that's app-specific, not a MetaMask framework contract.

Gate: a written determination, with addresses and verification evidence, of what's reusable canonical infrastructure vs. what this task must deploy.

### 1. Populate the chain manifest correctly

Resolve the `Replace()`-vs-merge trap described above. Add whichever loader change you decided on, plus a Sepolia AA manifest (or static config extension) carrying every required `Deployment` field (`EntryPoint`, `ModularAccountFactory`, `DelegationManager`, `HybridDeleGatorImplementation`, `AllowedTargetsEnforcer`, `AllowedMethodsEnforcer`, `ExactCalldataEnforcer`, `LimitedCallsEnforcer`, `NonceEnforcer`, `TimestampEnforcer`) alongside the existing legacy fields, untouched.

Gate: `SmartAccountPanel.IsModularConfigured` evaluates `true` for `ethereum-sepolia`; existing Sepolia checkout/staking/wallet-login regression tests still pass unmodified.

### 2. Browser: delegation creation and owner signing

Add a JS module (sibling to `smartAccountRegistration.js`, same IIFE/SDK-bundle pattern) that, using the connected owner wallet and `@metamask/delegation-toolkit`, builds and signs the two scoped delegations (exact `approve(escrow, amount)` and exact `escrow.fund(jobId, amount, 0x)`) with the correct caveats, mirroring `metamask-session-key-e2e.ts` lines ~174-201 exactly — same enforcer set, same one-time `LimitedCallsEnforcer(1)` semantics, same epoch/nonce binding. Surface this as a new step in `SmartAccountPanel.razor`, gated behind having a registered modular account with no active epoch (reuse the existing `_modularAccountRegistered` / `_activeEpoch is null` branch, razor:89-92).

Gate: owner can sign both delegations in-browser against a real Sepolia wallet; signed delegation payloads are inspectable/loggable client-side for review before submission (nothing broadcast yet).

### 3. Browser + server: on-chain activation

Add the UI action that submits the owner's `NonceEnforcer.incrementNonce` UserOperation (via a real hosted bundler — see Phase 0's bundler decision) to activate the signed epoch, then calls `RecordPermissionEpochInstalledAsync` with the resulting tx hash and the signed grants. Trust the server method's existing on-chain nonce re-verification; do not add a client-trusted "it worked" shortcut.

Gate: activating an epoch in the browser produces a server-verified `AgentPermissionEpochInfo` that the panel's existing epoch-display block (razor:69-88) renders correctly; a tampered/wrong/stale nonce claim is rejected by the server exactly as `RecordPermissionEpochInstalledAsync`'s doc comment promises.

### 4. Agent-side redemption

Wire whatever server-side agent process is authoritative in this codebase (the agent gateway/worker pattern from the broader agentic-commerce work, if present; otherwise a minimal scoped background service) to detect an activated epoch and redeem it through `DelegationManager`, exactly matching `metamask-session-key-e2e.ts` line ~268 — one bundled UserOperation executing both the approve and the escrow fund. This step never touches the owner's key.

Gate: after activation, the agent autonomously executes the batched approve+fund against the real Sepolia escrow within a reasonable window, without any further owner action.

### 5. Revocation

Add the missing UI control: a "Revoke agent permission" button (there is currently no caller of `RevokeSessionPermissionsAsync` anywhere in `src/ThisCafeteria.Web` — confirmed by grep this session) that submits the owner's `incrementNonce` UserOperation (mirroring the e2e script's revocation, ~line 324) and then calls `RevokeSessionPermissionsAsync`, trusting its existing pre-revoke on-chain confirmation.

Gate: revoking in the browser leaves the epoch `Revoked` per `GetActivePermissionEpochAsync`, and a subsequent agent redemption attempt against the revoked delegation fails on-chain exactly as the e2e script's live-rejection coverage proves it should.

### 6. Regression, security review, and documentation

- Run the full existing .NET, EVM contract, and browser test suites.
- Add browser-level (or scripted, if no browser harness exists yet) tests for: registration → sign → activate → agent redeem → confirm balance change → revoke → confirm subsequent redemption attempt fails; wrong-network wallet; owner rejects a signature; simulation failure surfaces a clear error; activation attempted with no registered account; double-activation attempt.
- Update `docs/erc4337-session-key-provenance.md`'s "Live acceptance evidence" section (or add a new doc) recording that the flow now runs against real Sepolia, with the bundler used and its safe/unsafe mode, and any deviation from the local e2e script's exact sequence.

Gate: clean-checkout instructions for running this flow against Sepolia are documented and reproducible.

## Testing minimums

- Manifest/config: Sepolia AA fields populate correctly without disturbing legacy fields (explicit regression test).
- Delegation signing: correct caveat set, correct one-time/exact-amount semantics, rejection of a tampered/wrong-target/wrong-selector delegation by `DelegationManager` on submission.
- Activation: server rejects a claimed epoch that doesn't match live `NonceEnforcer.currentNonce`; rejects a replayed/duplicate activation claim.
- Redemption: agent batch execution matches the exact amounts/targets from the signed delegations; a delegation whose quota is already spent cannot be redeemed twice (`LimitedCallsEnforcer(1)`).
- Revocation: server rejects a revoke claim that the chain doesn't yet agree happened; a redemption attempt after real on-chain revocation fails.
- All existing checkout/staking/wallet-login/chain-selector regression suites still pass.

## Acceptance criteria

- A real user, on the live app, connected to Ethereum Sepolia with a real wallet, can register a modular account, sign and activate a scoped session-key delegation, watch an agent autonomously redeem it to fund a real escrow, and revoke it — with every step backed by real on-chain transactions, not a simulated node.
- Every server-side trust boundary already built into `ISmartAccountService` remains fail-closed and unmodified in its verification logic.
- No private key (owner's or agent's) is ever exposed to the browser, logged, or committed.
- Existing legacy Sepolia behavior (checkout, staking, wallet login, `SimpleAccount` path) is unaffected.
- The bundler used against Sepolia runs in safe mode, or the gap is explicitly and honestly documented if it cannot be resolved in this pass.

## Handoff format

Report: what was already live vs. newly deployed on Sepolia (with addresses and verification evidence), the exact loader/config change made and why (merge vs. replace decision), files/components added or changed grouped by contracts/server/browser/tests/docs, the bundler and its mode, commands run and results, a walkthrough of one full register → sign → activate → redeem → revoke cycle actually executed end-to-end with tx hashes, remaining security review findings, and any step that could not be completed against a real public network with the precise blocker.
