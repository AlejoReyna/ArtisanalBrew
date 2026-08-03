# Agentic Commerce Remediation Walkthrough

This document outlines the remediation and hardening work performed on the Agentic Commerce features
in ArtisanalBrew to make reconciliation correct and independently verifiable.

## Current Repository State (2026-07-28)

- **Enabled public chains:** Ethereum Sepolia, BSC Testnet, and Solana Devnet are loaded from
  committed manifests. Sepolia and BSC enable wallet login, liquid staking, faucet, reward
  minting, and agentic commerce. Their manifests keep session-key payments, marketplace payments,
  and legacy exit disabled. Solana Devnet enables wallet login, liquid staking, reward funding,
  and reconciliation.
- **Implemented but gated features:** The MetaMask Delegation Framework session-account flow,
  ERC-4337 bundler transport, canonical EntryPoint confirmation, and the server-side Solana faucet
  are implemented. Public session payments remain disabled because the EVM manifests do not contain
  the complete modular-account deployment or enable the capability. The Solana faucet remains
  disabled because the Devnet manifest does not enable `capabilities.faucet`; operation also
  requires the server-side administrator secret.
- **Sponsored submission proof:** A sponsored UserOperation was mined successfully on Sepolia
  through self-hosted Rundler on 2026-07-24. `UserOperationSubmitter` now confirms from the canonical
  EntryPoint event and mined transaction receipt, including a bounded-log fallback when Rundler's
  receipt endpoint fails.
- **Manifest caveat:** That public proof used EntryPoint
  `0xdd9a61064ef9e2d9612da1f1307e168b85fe43a6`, while the currently committed
  `deployments/ethereum-sepolia.json` still records EntryPoint
  `0x7d75859d1e2be07b0c18c0ef3dd062b69bcc4217` and its companion deployment. Treat the public
  transaction as historical proof of the submission path, not proof that the current committed
  Sepolia manifest is synchronized. Reconcile and verify the manifest before any new public
  enablement or broadcast.
- **Repository hygiene:** Implementation prompts now live under `docs/prompts/`. Acceptance logs,
  runtime logs, and the local isolation marker are ignored and are not retained in source control.

## 1. Concurrent Gateway Idempotency
The Agent Gateway (`ThisCafeteria.AgentGateway`) handles x402 payments and tool integration. It was
vulnerable to double-settlement of concurrent identical requests.
- **Solution:** Refactored `IdempotencyStore` to use a promise-based `executeAtomic` lock to reserve an idempotency key during processing.
- **Result:** Two identical concurrent requests now wait for the same promise. The first one executes and caches the result; the second request returns the cached result without double-settling the payment.

## 2. Payment-Binding Security
The metadata sent by the client during an x402 payment must uniquely identify the intent to prevent replay attacks and metadata tampering.
- **Solution:** Strengthened `requestBinding.test.ts` to ensure that altering the network, asset, amount, payTo, or payload fields correctly produces a different binding hash.

## 3. Worker Decoupling & Testability
`AgenticCommerceReconciliationWorker` directly invoked `Nethereum` RPC clients to fetch logs, which made testing difficult and coupled it to EVM-specific implementations.
- **Solution:** Extracted a new `IEscrowEventProvider` interface. Implemented `EvmEscrowEventProvider` for EVM compatibility and utilized dependency injection to inject it into the worker.
- **Result:** We successfully introduced `AgenticCommerceReconciliationWorkerTests.cs` using an in-memory SQLite database and Moq to simulate log syncing in the test environment.

## 4. True Optimistic-Concurrency Check
The `AgenticJobProjection` previously did not have a robust optimistic concurrency token enforcement at the EF Core level.
- **Solution:** Added `[ConcurrencyCheck]` to `AgenticJobProjection.ConcurrencyToken`. Implemented `AgenticJobProjectionConcurrencyTests.cs` using two separate `DbContext` instances to verify that a `DbUpdateConcurrencyException` is thrown when concurrent updates occur.

## 5. Constraint & Migrations Hardening
- **Solution:** Configured `AgenticJobProjection` to use `(ChainKey, ContractAddress, OnChainJobId)` as its unique key. Added and modified the `AgenticJobProjectionMigrationTests.cs` to test EF Core's database model generation.
- **SQLite vs Postgres Migrations:** Refactored previous `DO $$` raw SQL blocks inside the `Migrations/` directory by explicitly wrapping them in `migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL"` checks. This allowed full migration testing using SQLite in-memory without syntax errors.

## 6. Smart Account and Bundler Implementation
- **Current implementation:** `SmartAccountService` supports configuration discovery,
  counterfactual account derivation and deployment, modular-account registration, permission epoch
  installation, redemption, and revocation. `RundlerBundlerClient` and `UserOperationSubmitter`
  submit sponsored operations through a bundler and independently confirm the canonical
  EntryPoint event before recording usage.
- **Fail-closed boundary:** Unconfigured or partially configured chains still throw
  `NotSupportedException`. This is now a deployment/capability guard, not a placeholder for every
  smart-account method. The local EVM manifest enables the full session flow; the committed Sepolia
  and BSC public manifests do not.
- **Receipt resilience:** `EntryPointConfirmationReader` can verify a mined operation from a bounded
  EntryPoint log query when the bundler receipt endpoint is unavailable. See
  `docs/agentic-commerce-stack-plan.md` for the public Sepolia proof and receipt-endpoint diagnosis.

## 7. Reorg Limitations Documentation
- **Solution:** Explicitly documented the missing full reorg-rollback functionality in `docs/agentic-commerce-stack-plan.md`, clarifying that while confirmation depth prevents ephemeral indexing, manual intervention is needed if a deep reorg occurs until block-header validation and inverse applicators are implemented.

## 8. Phase 3 Hardening — Consistent Event Policy (2026-07-20)

This pass addresses correctness gaps in the reconciliation event-handling policy, bytes32 persistence
safety, and the `run-acceptance.sh` harness.

### 8.1 Consistent Event-Handling Policy

**Problem:** `ProviderSet` threw `InvalidOperationException` when the job was not found; all other
missing-job events silently `return`ed — inconsistent policy that could crash the worker or silently
skip permanently lost events when the checkpoint advanced.

**Solution:**
- Introduced `AgenticJobDeferredEvent` domain entity to durably record events that arrive out-of-order
  (before their prerequisites) or in invalid state.
- New policy in `AgenticCommerceReconciliationApplicator`:
  - **Missing job:** record a durable `AgenticJobDeferredEvent` row; do NOT mark as applied.
  - **Invalid state transition:** record deferred (same mechanism).
  - **Idempotent duplicate terminal events:** silently skip, record as applied.
  - **Wrong escrow address:** naturally returns null (address is part of the query key) → deferred.
  - **Checkpoint atomicity:** the worker wraps all events in a single DB transaction; if SaveChanges fails, the checkpoint stays unchanged.
- Added EF Core migration `AddAgenticJobDeferredEvents` with unique index on log identity.

### 8.2 Bytes32 Persistence Safety

**Problem:** `Deliverable`/`Reason` bytes32 fields were stored via `ToHex(true)` which was
correct but undocumented and untested.

**Solution:**
- Introduced `NormalizeBytes32(byte[]?)` in `EvmEscrowEventProvider`:
  1. Strip trailing zero bytes.
  2. Try strict UTF-8 decoding (`DecoderExceptionFallback`).
  3. Fall back to `0x`-prefixed lowercase hex — always pure ASCII, never NUL bytes.
- Added 4 unit tests proving no NUL bytes reach PostgreSQL text columns.

### 8.3 Acceptance Harness Hardening

**Changes to `run-acceptance.sh`:**
- PostgreSQL health check now runs **before** migrations (previously was after).
- `RESET_DB=1` now requires an isolation marker (`ACCEPTANCE_ISOLATED=1` or `contracts/evm/.acceptance-isolated`); fails closed without it.
- Explicit `NODE_PID`/`WORKER_PID` variables with a cleanup trap, now trapping `EXIT INT TERM`.
- Machine-readable final marker: `ACCEPTANCE_RESULT=PASS` or `ACCEPTANCE_RESULT=FAIL exit=N`.
- Evidence captured locally to timestamped `acceptance-evidence-<timestamp>.log`; these generated
  logs are ignored and are not committed.

**Two defects found on re-verification (2026-07-21) and fixed:**

1. *Evidence capture hung, so the final marker was never reached.* The evidence queries invoked
   host `psql -h localhost -p 5433 -U postgres` with no `PGPASSWORD` and with `2>/dev/null`. psql
   prompted for a password on the tty with the prompt suppressed, and blocked indefinitely; the run
   had to be killed, which also meant the cleanup trap never fired. Evidence queries now `exec`
   into the Apple `container` PostgreSQL instance (`$PG_CONTAINER`, default
   `this-cafeteria-postgres`), so no password prompt is possible.

2. *Database errors were still being swallowed, and orphaned workers accumulated.* The `2>/dev/null`
   redirects are removed — a failed evidence query now prints its error and forces
   `ACCEPTANCE_RESULT=FAIL` even when the lifecycle assertions passed. Separately, the trap killed
   only the `dotnet run` / `npm run` **wrapper** PID, orphaning the real Worker binary each run; 28
   orphaned workers had accumulated, all concurrently polling the acceptance database. Cleanup now
   walks the full descendant tree (`descendant_pids`/`kill_tree`), sends `SIGTERM`, then escalates
   to `SIGKILL` after 5s. Verified: the run leaves zero orphaned worker or Hardhat processes.

Because these defects invalidated the previous evidence capture, the 2026-07-20 acceptance log was
truncated before any checkpoint value, applied-event count, or success marker was recorded.

**Two further defects found under audit and fixed:**

3. *Provider assignment was never actually exercised.* The acceptance script passed the provider
   directly into `createJob`, so `setProvider` was never called and the `ProviderSet` decode →
   apply path was never proven end to end — yet the harness still printed
   `create → provider assignment → … [VERIFIED]`. The contract requires `job.provider ==
   address(0)` for `setProvider`, so the script now creates the job with the zero address, calls
   `setProvider`, and asserts the projection's `ProviderAddress` before budgeting. The proven path
   is now genuinely `create → provider assignment → budget → funding → submission → completion`.

4. *The harness was not repeatable, and could silently produce a stale pass.* Hardhat deploys
   deterministically, so a fresh node redeploys the escrow to the **same address** every run. A
   checkpoint left from a previous run (e.g. block 62) refers to a chain that no longer exists;
   because the new chain restarts near block 0, the worker treated the stale higher checkpoint as
   "already scanned" and skipped every event of the new run. The earlier 05:03 run only passed
   because that escrow address had never been used before. Step 9b now purges reconciliation state
   scoped strictly by the freshly deployed `ContractAddress`/`RegistryAddress` — never a blanket
   delete — and evidence queries report **per-run** counts alongside cumulative ones. Verified by
   two back-to-back runs, both exit 0 with identical per-run counts.

The results in this document come from those clean, repeatable 2026-07-21 runs.

**Harness boundary:** The acceptance test uses Hardhat-controlled local wallets via a TypeScript
script (`contracts/evm/scripts/acceptance-test.ts`). It is **not** a browser wallet/UI E2E test
and does not require MetaMask or any browser extension.

### 8.4 New Tests Added

| Test | File | Purpose |
|------|------|---------|
| `ApplyEvent_JobCompleted_BeforeJobCreated_RecordsDeferredEvent` | Applicator | Missing prerequisite → deferred |
| `ApplyEvent_JobFunded_BeforeJobCreated_RecordsDeferredEvent` | Applicator | Missing prerequisite → deferred |
| `ApplyEvent_JobSubmitted_BeforeFunding_RecordsDeferredEventAndLeavesStatusOpen` | Applicator | Submission before funding → deferred, stays unapplied |
| `ApplyEvent_DuplicateJobCreated_DifferentLogIdentity_IsIdempotent` | Applicator | Same job, two log identities |
| `ApplyEvent_DelayedPrerequisite_DeferralIsRetainable` | Applicator | Deferred record survives subsequent Create |
| `ApplyEvent_WrongEscrowAddress_IsIgnoredSafely` | Applicator | Cross-escrow events deferred |
| `ApplyEvent_DuplicateTerminalEvent_IsIdempotent` | Applicator | Duplicate Completed is idempotent |
| `ApplyEvent_DeferredEvent_DuplicateLogIdentity_DoesNotThrow` | Applicator | No double-deferred row |
| `NormalizeBytes32_AsciiText_ReturnsStrippedText` | Applicator | bytes32 UTF-8 decode |
| `NormalizeBytes32_AllZeroBytes_ReturnsEmptyString` | Applicator | bytes32 all-zero |
| `NormalizeBytes32_BinaryBytes_ReturnsPureAsciiHex` | Applicator | bytes32 binary → hex |
| `NormalizeBytes32_NoNulBytesInAnyOutput` | Applicator | NUL-byte safety |
| `ReconcileOnceAsync_PersistenceFailure_CheckpointRemainsAtPreviousBlock` | Worker | Checkpoint rollback |

## Verification Status

| Suite | Count | Status |
|-------|-------|--------|
| .NET unit tests | 268 | ✅ Passed — 2026-07-28 |
| Gateway (TypeScript) | 14 | ✅ Passed — 2026-07-28 |
| Gateway (`tsc` build) | — | ✅ Succeeded |
| EVM contracts (Hardhat) | 29 | ✅ Passed — 2026-07-28 |
| Phase 3 acceptance harness | exit=0 | ✅ Passed — 2026-07-21 |

Two immediate back-to-back local runs on 2026-07-21 exited 0 with identical per-run counts,
demonstrating that the harness is repeatable. Their generated evidence logs were intentionally
removed from source control; future logs match `.gitignore` and remain local.

Harness final marker: `ACCEPTANCE_RESULT=PASS  exit=0`.

Escrow deployed for this run: `0xa51c1fc2f0d1a1b8494ed1fe312d7c3a78ed91c0`
(chain `evm-local`, chainId 31337).

Post-run database evidence, scoped to the contracts deployed by this run:

| Metric | Value |
|--------|-------|
| Checkpoint `LastScannedBlock` (run escrow) | 68 |
| `AgenticJobAppliedEvents` — **this run** | 19 |
| `AgenticJobAppliedEvents` — cumulative, all contracts | 102 |
| `AgenticJobDeferredEvents` — **this run** | 0 |
| `AgenticJobs` rows for this escrow | 3 (ids 1/2/3) |

Lifecycle stages verified against on-chain transaction hashes:
- Agent identity registration → DB `AgentDirectoryEntries` row confirmed.
- JobCreated with an **unset provider** → Status: Open, CreationTx `0x84ea9fa3d0026822d8f22ce4d525a30a3f1307a0ab7191093dc3a0e88dc33b1f`.
- **ProviderSet (`setProvider`)** → `ProviderAddress` reconciled to `0x70997970C51812dc3A010C7d01b50e0d17dc79C8`, tx `0x95f2d9383635ddca12287ebba02a0a09bac0ed487d234a3f75694a3c86bd4277`, status still Open.
- BudgetSet + JobFunded → Status: Funded, FundedTx `0x85266a28116135a087bed35f1ad92bfa9e28535a86cbac138009a96265f1688a`.
- JobSubmitted → Status: Submitted, DB row confirmed.
- JobCompleted (evaluator approval) → Status: Completed, CompletionTx `0x480799e06539c98c859a08e34f427fe0bc8b6dc36b663355500852d8d79b84ec`; provider payout asserted on-chain.
- Rejection variant (job 2) → Status: Rejected, refund path confirmed.
- Expiry variant (job 3) → Status: Expired, refund path confirmed.

Job identity is `ChainKey + ContractAddress + OnChainJobId`, so each redeployed escrow starts its
own job-id sequence; rows under earlier escrow addresses are retained history, not duplicates.

### What these results do and do not prove

| Verified by | Scope |
|-------------|-------|
| .NET unit tests | Applicator ordering, idempotency, deferral, concurrency, bytes32/NUL safety, sponsorship policy/signer/simulator fail-closed gating, cross-chain solver policy fail-closed gating — in-memory, SQLite, and stubbed chains; not a live chain. |
| Hardhat contract tests | Solidity escrow/resolver/ERC-4337 logic in isolation. |
| Acceptance harness | Real EVM transactions → worker → PostgreSQL projections, driven by **Hardhat-controlled local wallets**. |
| Cross-stack ERC-4337 scripts | `crossstack-sponsor-check.ts` and `simulation-recipe-check.ts` prove the real C# simulator/sponsor against live Hardhat. `crossstack-bundler-submit-check.ts` additionally drives the real C# `UserOperationSubmitter` through Rundler and independently verifies the canonical EntryPoint event. These live scripts are manual gates, not part of the automated test suite. |
| Bundler (Rundler) e2e script | `rundler-e2e-check.ts`: a real bundler (Rundler, `--unsafe` mode) accepts a UserOperation via `eth_sendUserOperation`, bundles it, and gets it mined — confirmed via `eth_getUserOperationReceipt`, on-chain account bytecode, and recipient balance change. Proves the bundler path itself works against this repo's pinned canonical EntryPoint; does not involve `ThisCafeteria.*` .NET code and does not exercise storage-access-rule validation (Hardhat can't run the tracer that enforces it — see `docs/agentic-commerce-stack-plan.md`'s "Rundler investigation"). Not part of the automated test suite — run manually against a live node plus a separately-started Rundler process. |
| Public Sepolia sponsored proof | A real sponsored UserOperation was accepted by self-hosted Rundler in safe mode and mined successfully on 2026-07-24. The app-side confirmation fallback was subsequently added after Rundler's unbounded receipt log scan timed out. This proves the submission/confirmation design, but the committed Sepolia manifest currently points at a different deployment and must be reconciled before reuse. |
| Two-node cross-chain smoke test | `two-node-crosschain-smoke.ts`: proves the Phase 5 gate — asset moves from a genuinely separate source node to a real (deployed, not counterfactual) smart account on a separate destination node; job funding happens only after that move is verified; an unfilled intent leaves the job Open and the source asset refundable. Re-run twice against fresh nodes for reproducibility. Not part of the automated test suite; the "solver" here is inline script logic. |
| Standing cross-chain solver | `two-node-standing-solver-check.ts` + a real `ThisCafeteria.Worker` process running `CrossChainSolverWorker`: the script deploys and submits intents but never fills — a separately-started, genuinely long-running .NET background service watches the source chain, decodes intents from real transaction calldata, evaluates a fail-closed policy, and fills approved ones. Confirmed at every layer: worker logs, `CrossChainSolverFills` DB rows with real fill tx hashes, and on-chain `isResolved`/balance checks. Also proved the denial path: a disallowed token pair was left correctly unfilled. Not part of the automated test suite. |
| Quote-preview vs. real fill | `GET /api/intents/quote` was queried against a live `ThisCafeteria.Web` process holding **no private key**, then the identical route was submitted as a real intent and autonomously filled by the separately-running solver worker. The previewed `amountOut` and the actually-paid amount were byte-for-byte identical (`9700000000000000000` both times, a 9700 bps / 3% spread). Not part of the automated test suite; the endpoint itself has no UI consumer yet. |
| Procurement Lab UI | Visualization of projections only; not exercised by any automated test. |

**Not covered by any automated test:** browser-wallet (MetaMask/WalletConnect) end-to-end flows,
public-manifest-enabled session payments, bundler-enforced storage-access-rule validation on local
Hardhat, deep-reorg rollback, and automatic re-application of deferred events. The real .NET
bundler submission path is covered by a manual cross-stack proof, and the public Sepolia submission
is historical live evidence; neither is an unattended test.

### Known Limitations

- **Reorg rollback:** The worker tracks a safe head (`MinimumConfirmations`) to prevent ephemeral
  indexing but does not automatically roll back projections if a deep reorg invalidates previously
  confirmed events. Manual database intervention is required in that scenario.
- **Deferred-event re-application:** Durably recorded deferred events are not yet automatically
  retried. Re-application is a Phase 4 concern.
- **Acceptance isolation now fails closed against the dev database.** `RESET_DB=1` only drops
  a database whose name matches `this_cafeteria_test`/`this_cafeteria_acceptance` — the ordinary
  `this_cafeteria` dev database is refused even with `ACCEPTANCE_ISOLATED=1` set, since an
  operator-set flag is not evidence that a *shared* database is disposable. Verified: the
  destructive path exits 1 and refuses when pointed at `this_cafeteria`. (Step 9b's own contract
  purge is always scoped by address regardless.)
- **Invalid-order policy is intentionally asymmetric.** `BudgetSet` and `JobFunded` arriving after a
  job leaves `Open` are marked applied and ignored rather than deferred, because the terminal
  projection already holds the authoritative budget. Every other out-of-order event is deferred.
  This is deliberate, but it is not a uniformly strict invalid-order policy.
- **Provider assignment coverage:** the main lifecycle proves `setProvider`. The rejection and
  expiry variants still create jobs with the provider supplied inline, which exercises the
  create-with-provider form rather than the two-step assignment.
- **Bundler submission is implemented, but public enablement is incomplete.**
  `UserOperationSubmitter` sends through `IBundlerClient`, verifies the canonical EntryPoint event,
  and records sponsorship usage only after confirmation. The flow has passed locally through
  Rundler and once on public Sepolia. However, the committed public manifests still disable
  `agenticSessionPayments`, do not contain the full modular-account deployment, and the Sepolia
  manifest does not match the deployment used by the public proof. Browser-driven public session
  payments must remain gated until those deployment records are reconciled and verified.
- **`NativeCurrencyUsdRate` is a static configured number, not a live price oracle.** The USD
  budget is therefore only as accurate as that configured value; on a real chain with a moving gas
  price and asset price, the effective USD cost of sponsorship will drift from what the grant
  assumes.

## 9. Documentation and Generated-Artifacts Policy (2026-07-28)

- Reusable implementation prompts are organized under `docs/prompts/`.
- Acceptance evidence and runtime logs are generated locally and ignored by Git.
- The local acceptance-isolation marker is not source-controlled; use
  `ACCEPTANCE_ISOLATED=1` or create the ignored marker deliberately for a local run.
- Office/PDF exports are not canonical project documentation. The repository walkthrough and
  operational records are maintained as Markdown.
