# Agentic Commerce Remediation Walkthrough

This document outlines the remediation and hardening work performed on the Agentic Commerce features
in ArtisanalBrew to make reconciliation correct and independently verifiable.

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

## 6. Smart Account Scaffolding
- **Solution:** Maintained the smart account fail-closed implementation in `SmartAccountService`. Ensured `SmartAccountServiceTests.cs` correctly verified that the scaffolding throws `NotSupportedException` for all methods, successfully preventing spoofed transactions until true ERC-4337 dependencies exist.
  *Updated in Phase 4:* configuration discovery and counterfactual account derivation are now implemented against the pinned canonical v0.7.0 factory. Sponsorship and session operations remain fail-closed. See `docs/agentic-commerce-stack-plan.md` § Phase 4.

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
- Evidence captured to timestamped `acceptance-evidence-<timestamp>.log`.

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
| .NET unit tests | 179 | ✅ Passed |
| Gateway (TypeScript) | 11 | ✅ Passed |
| Gateway (`tsc` build) | — | ✅ Succeeded |
| EVM contracts (Hardhat) | 29 | ✅ Passed |
| Phase 3 acceptance harness | exit=0 | ✅ Passed — 2026-07-21 |

Evidence captured to `acceptance-evidence-20260721-051418.log`, with an immediate back-to-back
repeat run in `acceptance-evidence-20260721-051513.log` (untracked; logs are not committed).
Both exited 0 with identical per-run counts, demonstrating the harness is now repeatable.

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
| .NET unit tests | Applicator ordering, idempotency, deferral, concurrency, bytes32/NUL safety — in-memory and SQLite, not a live chain. |
| Hardhat contract tests | Solidity escrow/resolver logic in isolation. |
| Acceptance harness | Real EVM transactions → worker → PostgreSQL projections, driven by **Hardhat-controlled local wallets**. |
| Procurement Lab UI | Visualization of projections only; not exercised by any automated test. |

**Not covered by any test:** browser-wallet (MetaMask/WalletConnect) end-to-end flows, ERC-4337
smart-account or paymaster paths, deep-reorg rollback, and automatic re-application of deferred
events. No browser extension is involved at any point.

### Known Limitations

- **Reorg rollback:** The worker tracks a safe head (`MinimumConfirmations`) to prevent ephemeral
  indexing but does not automatically roll back projections if a deep reorg invalidates previously
  confirmed events. Manual database intervention is required in that scenario.
- **Deferred-event re-application:** Durably recorded deferred events are not yet automatically
  retried. Re-application is a Phase 4 concern.
- **Acceptance isolation is self-attested, not enforced.** The harness accepts `ACCEPTANCE_ISOLATED=1`
  or the `contracts/evm/.acceptance-isolated` marker as proof of isolation, and its safe-name list
  includes `this_cafeteria` — the ordinary development database. It does **not** verify that the
  PostgreSQL container, database, or connection is genuinely disposable. `ACCEPTANCE_ISOLATED=1
  RESET_DB=1` will therefore drop the normal dev database. Treat the isolation gate as a guard
  against accidents, not against misconfiguration. (Step 9b's purge is always scoped by contract
  address and is unaffected by this.)
- **Invalid-order policy is intentionally asymmetric.** `BudgetSet` and `JobFunded` arriving after a
  job leaves `Open` are marked applied and ignored rather than deferred, because the terminal
  projection already holds the authoritative budget. Every other out-of-order event is deferred.
  This is deliberate, but it is not a uniformly strict invalid-order policy.
- **Provider assignment coverage:** the main lifecycle proves `setProvider`. The rejection and
  expiry variants still create jobs with the provider supplied inline, which exercises the
  create-with-provider form rather than the two-step assignment.
