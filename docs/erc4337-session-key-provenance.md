# ERC-4337 agent-payment provenance matrix

Status: **sufficient for the narrowly scoped MetaMask Delegation Framework
v1.3.0 path described below**. Existing SimpleAccount users remain on the
unchanged reference-account path.

## Selected stack

| Component | Selected artifact | Authoritative revision / deployment | Build and bytecode evidence | Audit evidence and scope | Compatibility |
| --- | --- | --- | --- | --- | --- |
| Account implementation | `HybridDeleGator` | [`MetaMask/delegation-framework` v1.3.0](https://github.com/MetaMask/delegation-framework/tree/bfbdf9795a976833ed2fa000baf42fbb83958b03), commit `bfbdf9795a976833ed2fa000baf42fbb83958b03`; canonical address `0x48dBe696A4D990079e039489bA2053B36E8FFEC4` | solc 0.8.23, optimizer 200, London. [Exact-match verified source](https://sepolia.etherscan.io/address/0x48dBe696A4D990079e039489bA2053B36E8FFEC4#code) includes the immutable manager and EntryPoint constructor arguments | Consensys Diligence Aug 2024 covers the account/core files by file hash; Cyfrin Feb 2025 covers the complete core/signature/enforcer system and verifies the EntryPoint replay fix | Its typed UserOperation domain includes chain ID, account, and the immutable EntryPoint; ERC-4337 v0.7 |
| Factory | `SimpleFactory` | same revision; canonical address `0x69Aa2f9fe1572F1B640E1bbc512f5c3a734fc77c` | Published deployment package and exact SDK deployment bytecode; deterministic CREATE2 proxy deployment | Consensys Diligence factory scope plus Cyfrin core review | Produces v0.7 `factory`/`factoryData`; preserves the repository's separate SimpleAccountFactory |
| EntryPoint | account-abstraction v0.7 | canonical `0x0000000071727De22E5E9d8BAf0edAc6f37da032`; local tests reuse the repository's pinned `@account-abstraction/contracts@0.7.0` deployment | Existing repository reproducible Hardhat deployment | Official account-abstraction release evidence; not treated as delegation-policy evidence | Rundler v0.11.0 accepts v0.7 operations. Paymasters remain independent |
| Validator / signer | Hybrid ECDSA owner validation and ERC-1271 delegation signing in `HybridDeleGator` | same framework revision and deployed account bytecode | Unmodified account implementation; SDK produces framework EIP-712 data | Consensys account/signature file hashes; Cyfrin Feb 2025 core/signature scope | Root owner remains usable; delegation signatures are checked on-chain by `DelegationManager` |
| Delegation authorization | `DelegationManager` | same revision; canonical address `0xdb9B1e94B5b69Df7e401DDbedE43491141047dB3` | Published deployment and exact local SDK bytecode | Consensys manager file hash; Cyfrin Feb 2025 full manager/delegation execution scope | Validates delegate, authority chain, signature, disabled state, caveats, and execution mode on-chain |
| Target / selector / token / amount | SDK `functionCall` scope using unmodified `AllowedTargetsEnforcer`, `AllowedMethodsEnforcer`, and `ExactCalldataEnforcer` | `0x7F20f61b1f09b08D970938F6fa563634d65c4EeB`, `0x2c21fD0Cb9DC8445CB3fb0DC5E7Bb0Aca01842B5`, `0x99F2e9bF15ce5eC84685604836F71aB835DBBdED` | Exact addresses from `@metamask/delegation-deployments@0.12.0`; local deployment uses exact ABI-package bytecode | Consensys covers target/method enforcers; Cyfrin Feb and Mar 2025 cover the enforcer system and exact-calldata/execution additions | Selected scope accepts only `SingleDefault`; arbitrary targets, selectors, calldata, batch and delegatecall are rejected |
| Per-operation and cumulative quota | Exact calldata fixes each payment amount; `LimitedCallsEnforcer(limit=1)` makes that exact amount the cumulative delegation quota | `0x04658B29F6b82ed55274221a06Fc97D318E25416` | same published deployment package and exact local bytecode | Consensys file-hash scope and Cyfrin Feb 2025 enforcer scope | One exact approval and one exact escrow funding call are issued as separate, one-use delegations in one redemption |
| Expiry | `TimestampEnforcer` | `0x1046bb45C8d673d4ea75321280DB34899413c069` | same published deployment package and exact local bytecode | Consensys file-hash scope and Cyfrin Feb 2025 enforcer scope | Enforced in the manager hook from block timestamp |
| Install / revoke / replay epoch | `NonceEnforcer` | `0xDE4f2FAC4B3D87A1d9953Ca5FC09FCa7F366254f` | same published deployment package and exact local bytecode | Consensys file-hash scope; Cyfrin Feb 2025 covers the EntryPoint-bound replay fix and nonce enforcer | A delegation is signed for the next epoch and is unusable until the owner's account calls `incrementNonce(manager)` on-chain. The next owner increment revokes the whole epoch. UserOperation nonce plus delegation hash/call-limit prevent replay |
| SDK | `@metamask/delegation-toolkit@0.13.0` | `MetaMask/smart-accounts-kit` commit `2a8c19737f52abdb9249efd8ca52579f33d0281d`; npm integrity `sha512-SVc2gcnKQ8KQodugfuXMs49pHO1x9AYw3Wmdg4yBEsgm5TF5zPpWFmAj+bOL8q+Fn+mHJAibFIrJBtS970G9Dw==` | Exact dependencies: `@metamask/delegation-abis@0.11.0`, `@metamask/delegation-core@0.2.0`, `@metamask/delegation-deployments@0.12.0` | SDK is not substituted for contract audit evidence; it only builds the audited contracts' standard data structures | Builds deterministic accounts, delegations, caveats, redemptions, and v0.7 UserOperations |

## Audit reports and limitations

- [Consensys Diligence, August 2024](https://diligence.security/audits/2024/08/metamask-delegation-framework/)
  records per-file hashes for the account, manager, factory, signer path, and
  the target/method/spend/time/nonce/call-limit enforcers.
- [Cyfrin, February 2025](https://github.com/MetaMask/delegation-framework/blob/v1.3.0/audits/cyfrin/cyfrin-3-25.pdf)
  reviews the core, signatures, manager and enforcer system and verifies the
  EntryPoint-domain replay and transfer/execution-mode fixes at commits
  `1f91637e...` and `cdd39c62...`.
- [Cyfrin, March 2025](https://github.com/MetaMask/delegation-framework/blob/v1.3.0/audits/cyfrin/cyfrin-4-25.pdf)
  covers the exact-execution/calldata family. The later `b2807a9d...`
  default-mode correction is included in the reviewed/final v1.3.0 lineage.
- Audit coverage reduces risk; it does not prove absence of defects. Production
  rollout still requires bytecode checks on each target chain, a safe-mode
  bundler, protected owner/agent key custody, monitoring, and incident response.

## Exact permission architecture

The owner creates two signed, epoch-bound delegations: exact token
`approve(escrow, amount)` and exact `escrow.fund(jobId, amount, 0x)`. Each uses
the SDK's `functionCall` scope, `NonceEnforcer`, `TimestampEnforcer`, and
`LimitedCallsEnforcer(1)`. The owner's v0.7 UserOperation activates the epoch
on-chain. The agent's own deterministic HybridDeleGator then redeems both
through `DelegationManager` in one bundled operation. A later owner
`incrementNonce` transaction revokes all permissions in that epoch.

Gas sponsorship is deliberately outside this authority. A paymaster may fund
gas only after successful EntryPoint simulation; if unavailable, the operation
may use the agent account's prefund. Neither path grants asset-payment rights.

## Live acceptance evidence

`scripts/metamask-session-key-e2e.ts` deploys the real v1.3.0 environment and
submits operations through pinned Rundler v0.11.0. It proves deterministic owner
and agent deployment, on-chain activation/revocation, escrow settlement and
`JobFunded` reconciliation, root-owner continuity, and live rejection of:
uninstalled permission, wrong target, wrong token, wrong selector, wrong amount,
non-default mode, batch mode, delegatecall mode, exhausted quota, expiry, and
revocation. `scripts/rundler-e2e-check.ts` separately
proves the original SimpleAccount factory path still deploys and settles.

The local Rundler must use `--unsafe` because Hardhat EDR cannot run the ERC-7562
JavaScript validation tracer. EntryPoint signature, nonce, caveat, execution and
receipt validation are still live. This is sufficient for local functional
acceptance, but production requires a safe-mode bundler backed by a tracer-capable
node. The acceptance used the explicitly allowed user-paid fallback; it did not
claim a production paymaster integration.
