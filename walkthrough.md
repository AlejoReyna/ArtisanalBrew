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
- Explicit `NODE_PID`/`WORKER_PID` variables with a cleanup trap that kills each by PID.
- `wait` on child processes before final exit.
- Machine-readable final marker: `ACCEPTANCE_RESULT=PASS` or `ACCEPTANCE_RESULT=FAIL exit=N`.
- Evidence captured to timestamped `acceptance-evidence-<timestamp>.log`.
- DB error output is no longer swallowed.

**Harness boundary:** The acceptance test uses Hardhat-controlled local wallets via a TypeScript
script (`contracts/evm/scripts/acceptance-test.ts`). It is **not** a browser wallet/UI E2E test
and does not require MetaMask or any browser extension.

### 8.4 New Tests Added

| Test | File | Purpose |
|------|------|---------|
| `ApplyEvent_JobCompleted_BeforeJobCreated_RecordsDeferredEvent` | Applicator | Missing prerequisite → deferred |
| `ApplyEvent_JobFunded_BeforeJobCreated_RecordsDeferredEvent` | Applicator | Missing prerequisite → deferred |
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
| .NET unit tests | 154 | ✅ Passed |
| Gateway (TypeScript) | 11 | ✅ Passed |
| EVM contracts (Hardhat) | 24 | ✅ Passed |
| Phase 3 acceptance harness | exit=0 | ✅ Passed — 2026-07-20 |

Evidence captured to: `acceptance-evidence-20260720-143038.log`

All lifecycle stages verified:
- Agent identity registration → DB `AgentDirectoryEntries` row confirmed.
- JobCreated (on-chain ID 1) → `AgenticJobs` row, Status: Open, CreationTx: `0xc489c0a1...` confirmed.
- JobFunded → Status: Funded, DB row confirmed.
- JobSubmitted → Status: Submitted, DB row confirmed.
- JobCompleted (evaluator approval) → Status: Completed, provider payout verified on-chain.
- Rejection variant → Status: Rejected, DB row confirmed.
- Expiry variant → Status: Expired, DB row confirmed.

### Known Limitations

- **Reorg rollback:** The worker tracks a safe head (`MinimumConfirmations`) to prevent ephemeral
  indexing but does not automatically roll back projections if a deep reorg invalidates previously
  confirmed events. Manual database intervention is required in that scenario.
- **Deferred-event re-application:** Durably recorded deferred events are not yet automatically
  retried. Re-application is a Phase 4 concern.
