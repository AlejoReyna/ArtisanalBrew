# ArtisanalBrew — Make ERC-4337 Sponsored Submission Actually Work — Implementation Prompt

You are a senior full-stack, smart-contract, and infrastructure engineer working in the existing ArtisanalBrew repository.

## Mission

Everything needed to *decide* whether a UserOperation should be gas-sponsored, and to *produce a valid paymaster signature* for it, already exists and is proven against a live chain. What does **not** exist anywhere in `main` is code that actually **submits** a sponsored UserOperation to a bundler and gets it mined — and no bundler is deployed anywhere this app can reach in production. Close that gap: wire real `.NET` submission through a bundler, and get a bundler this app can actually talk to running against Ethereum Sepolia (not just local Hardhat).

Read [`docs/agentic-commerce-stack-plan.md`](../agentic-commerce-stack-plan.md) in full before touching anything — especially the "Session handoff (2026-07-21)", "Bundler investigation", and "Rundler investigation" sections. That document is the authoritative record of what was tried, what failed, and why. Do not re-discover any of it from scratch.

## Verified ground truth (checked directly, not taken on faith from docs)

**Already implemented and proven, on `main`, against a live chain:**
- Canonical, unmodified ERC-4337 v0.7 contracts pinned and deployed to Sepolia: `EntryPoint`, `SimpleAccountFactory`, `VerifyingPaymaster` (addresses in `deployments/ethereum-sepolia.json`: `entryPoint`, `accountFactory`, `verifyingPaymaster`).
- `UserOperationSponsor` (`src/ThisCafeteria.Infrastructure/Services/UserOperationSponsor.cs`) produces canonical paymaster signatures by asking the paymaster contract itself for the hash to sign — not a reimplementation.
- `SponsorshipPolicyService` gates who gets sponsored and how much.
- `IUserOperationSimulator` gets real gas costs via the canonical EntryPoint's own `eth_call` simulation recipe.
- All of the above is proven cross-stack against a real Hardhat node by `contracts/evm/scripts/crossstack-sponsor-check.ts`, which calls the actual C# classes via `tools/ThisCafeteria.CrossStackHarness` — not stubs, not a scratch app.
- Modular/session-key smart accounts (HybridDeleGator + DelegationManager + caveat enforcers) are implemented in code and unit-tested (`ISmartAccountService.RegisterModularAccountAsync`, `GetActivePermissionEpochAsync`, etc.), with a browser derivation flow (`smartAccountRegistration.js`).

**Confirmed missing / not wired, right now, on `main`:**
- **No `.NET` code submits a UserOperation to a bundler at all.** Confirmed: `grep -rn "SendUserOperationAsync" src/` on `main` returns nothing. The cross-stack proof scripts submit sponsored ops via `EntryPoint.handleOps` directly — that is not how a production wallet-less sponsored flow is supposed to work; a real bundler must receive `eth_sendUserOperation`.
- **`ChainDefinition.BundlerRpcUrl` exists as a field only in an unmerged local branch, not in `main` at all** — confirmed via `git show origin/main:src/.../IBundlerClient.cs` (path does not exist). It is never set by `BlockchainManifestLoader`, never present in any `deployments/*.json` manifest, and never referenced by any appsettings file. Even once submission code exists, there is currently no config path to give any real chain a bundler endpoint.
- **No bundler process runs anywhere in this app's infrastructure.** `infra/` (all Bicep modules) has zero mention of a bundler. Rundler/Alto only ever run as ephemeral CI/local processes (`nohup ... &`), never as a deployed service. Azure Container Apps (`thiscafeteria-prod-rg`) has exactly two apps: `thiscafeteria-prod-web` and `thiscafeteria-prod-worker` — no bundler.
- **The only bundler proven to work at all (Rundler v0.11.0) was only proven in `--unsafe` mode locally**, which skips ERC-4337 storage-access-rule (anti-DoS) validation. Hardhat's EDR engine cannot run the standard JS tracer bundlers need for safe-mode — this is a local-testing limitation specifically, not a Rundler limitation. Alto (the other bundler tried) failed for a different, deeper reason: it calls a proprietary, undocumented Pimlico simulation contract (selector `0xd6383f94`, not the canonical `simulateHandleOp`) that isn't calibrated for a non-canonically-addressed EntryPoint redeployment. Full diagnosis in the plan doc — don't re-litigate it.
- **The Sepolia deployment manifest has no modular-account addresses** (`modularSimpleFactory`, `delegationManager`, `hybridDeleGatorImplementation`, the six caveat enforcers) — only `deployments/ethereum-sepolia.json`'s `addresses` keys exist: `cafe, coffee, liquidVault, faucet, entryPoint, accountFactory, verifyingPaymaster, erc8004Registry, erc7683Resolver, erc8183Escrow`. So: legacy/simple sponsored accounts are deployable on Sepolia today; modular/session-key accounts are code-complete but **not deployed** there yet.
- `NativeCurrencyUsdRate` is still a static config number, not a live oracle (unchanged from the plan doc).

**A concrete head start exists and should not be redone from scratch:** a full `.NET` Rundler JSON-RPC transport (`IBundlerClient` / `RundlerBundlerClient`, handling the v0.7 `factory`/`factoryData` JSON-RPC split, `eth_sendUserOperation`, `eth_getUserOperationReceipt`) was already written and works — it just sits in an old, unpushed local branch (`agent/enable-solana-multichain`, commit `7553618`, message `feat(bundler): add .NET Rundler transport for ERC-4337 UserOperation submission`). Recover it with `git show 7553618 --stat` / `git cherry-pick 7553618` rather than re-implementing. It is not wired into `Program.cs`'s DI beyond registering the `HttpClient`, and nothing calls it yet.

## Non-negotiable rules (carried over from this repo's other agentic-commerce work)

- Never implement EntryPoint, a bundler, signature cryptography, or private-key custody from scratch. Use the pinned canonical contracts and a real bundler binary/service — the investigation above already tells you which one works and why.
- Never accept RPC/bundler URLs, EntryPoint, or paymaster addresses from the client. Server registry (`IChainRegistry`) is the trust root.
- Do not broadcast to a public chain or spend real funds without Alexis's explicit authorization for the specific network and wallet.
- Do not mark this "done" because a local Hardhat proof passes. The gate is: a sponsored UserOperation submitted through a real, reachable bundler, against Ethereum Sepolia, mined and confirmed, with the app's own server-side verification (not just `receipt.success`) checking it.
- Preserve the existing local-Hardhat proof scripts and their honest caveats (`bundler-e2e-check.ts`'s `BUNDLER_E2E_RESULT=KNOWN_FAILURE` marker) — don't delete evidence of what doesn't work.

## Recommended sequence, in order of leverage

1. **Recover commit `7553618`** (the unmerged `IBundlerClient`/`RundlerBundlerClient`) onto a fresh branch off current `main` — don't rewrite it blind; read it, verify it still builds/tests against current `main` (things have moved since it was written), and adapt as needed.
2. **Add `BundlerRpcUrl` to the manifest schema and loader.** `BlockchainManifestLoader.TryReadEvm` needs to read an optional `bundlerRpcUrl` field from `deployments/ethereum-sepolia.json`'s root (or a new `addresses`-adjacent section — your call, but keep it out of `addresses` since it's not a contract address). Decide: self-hosted Rundler, or a third-party hosted bundler API that supports Sepolia in safe mode (Pimlico/Alchemy/StackUp all offer one) — a hosted option sidesteps needing to run and secure a bundler process in `thiscafeteria-prod-rg` yourself, and gets you real safe-mode storage-access validation, which no local setup here has ever proven. This is a real decision with cost/ops tradeoffs; don't default silently — flag it back to Alexis if it's not obvious which to pick.
3. **Wire actual submission**: find where a sponsored UserOperation currently gets built + signed (trace from `UserOperationSponsor`) and make it call `IBundlerClient.SendUserOperationAsync` instead of stopping at "signature produced" or calling `EntryPoint.handleOps` directly. Poll `GetUserOperationReceiptAsync` and verify the mined result server-side the same way this repo already verifies liquid-staking and escrow transactions (event decode + exact account/amount match, not just a success flag) — see `EvmLiquidStakingGateway` for the existing pattern to follow.
4. **Prove it against Sepolia, not just Hardhat.** The whole point of this task is that the local-only proof is known to have a real gap (safe-mode never exercised). Write the Sepolia-facing proof, run it once with explicit authorization, and record the public UserOperation hash / transaction hash — never place bundler API keys or RPC secrets in client-visible config or manifests.
5. Only after 1-4: modular/session-key account deployment to Sepolia (currently missing from the manifest, see above), then multi-pair solver support and the quote-endpoint UI wiring — both explicitly still open per the plan doc and lower leverage than getting basic sponsored submission working at all.

## Completion report expected

Same shape as this repo's other agent handoffs:
1. What was implemented, with exact files.
2. Commands/tests run and their results (`dotnet build`, `dotnet test`, the cross-stack scripts, and — if you got there — the live Sepolia proof).
3. Which bundler option was used (self-hosted vs. hosted) and why, plus what it costs/requires operationally going forward.
4. Any public Sepolia transaction/UserOperation hashes produced, and under whose explicit authorization.
5. What's still not done, ranked, and why — don't claim this is finished because a signature was produced; the gate is a real bundler mining a real sponsored operation.
