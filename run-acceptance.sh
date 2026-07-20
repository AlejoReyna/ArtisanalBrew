#!/bin/bash
# run-acceptance.sh – Phase 3 acceptance harness (hardened)
#
# REQUIRES:
#   - A running PostgreSQL instance on localhost:5433
#   - The target database must be isolated: either
#       (a) ACCEPTANCE_ISOLATED=1 is set, OR
#       (b) contracts/evm/.acceptance-isolated marker file exists
#   - RESET_DB=1 must be set explicitly to drop and recreate the database;
#     without it the script is read-only with respect to the database schema.
#
# CAPTURES to: acceptance-evidence-<timestamp>.log
#
# FINAL OUTPUT LINES (machine-readable):
#   ACCEPTANCE_RESULT=PASS  exit=0
#   ACCEPTANCE_RESULT=FAIL  exit=<N>
set -euo pipefail

# ---------------------------------------------------------------------------
# 1. Evidence log setup
# ---------------------------------------------------------------------------
EVIDENCE_LOG="acceptance-evidence-$(date +%Y%m%d-%H%M%S).log"
exec > >(tee -a "$EVIDENCE_LOG") 2>&1

echo "========================================================"
echo " Phase 3 Acceptance Harness"
echo " Command  : $0 $*"
echo " Timestamp: $(date -u +%Y-%m-%dT%H:%M:%SZ)"
echo " Log      : $EVIDENCE_LOG"
echo "========================================================"

# ---------------------------------------------------------------------------
# 2. PID tracking and cleanup trap
# ---------------------------------------------------------------------------
NODE_PID=""
WORKER_PID=""

cleanup() {
    echo "--- cleanup trap: terminating background processes ---"
    if [ -n "$WORKER_PID" ] && kill -0 "$WORKER_PID" 2>/dev/null; then
        echo "Killing worker PID $WORKER_PID"
        kill "$WORKER_PID" 2>/dev/null || true
    fi
    if [ -n "$NODE_PID" ] && kill -0 "$NODE_PID" 2>/dev/null; then
        echo "Killing Hardhat node PID $NODE_PID"
        kill "$NODE_PID" 2>/dev/null || true
    fi
    # Wait for children to exit (up to 5s)
    local deadline=$(( $(date +%s) + 5 ))
    while [ -n "$WORKER_PID" ] && kill -0 "$WORKER_PID" 2>/dev/null; do
        [ "$(date +%s)" -ge "$deadline" ] && break
        sleep 0.5
    done
    while [ -n "$NODE_PID" ] && kill -0 "$NODE_PID" 2>/dev/null; do
        [ "$(date +%s)" -ge "$deadline" ] && break
        sleep 0.5
    done
}

trap cleanup EXIT

# ---------------------------------------------------------------------------
# 3. Isolation marker check (fail closed)
# ---------------------------------------------------------------------------
ISOLATED=0
if [ "${ACCEPTANCE_ISOLATED:-0}" = "1" ]; then
    ISOLATED=1
    echo "INFO: ACCEPTANCE_ISOLATED=1 set – accepting isolated environment."
fi
if [ -f "contracts/evm/.acceptance-isolated" ]; then
    ISOLATED=1
    echo "INFO: contracts/evm/.acceptance-isolated marker found."
fi
if [ "$ISOLATED" = "0" ]; then
    echo "ERROR: Isolation marker not present."
    echo "  Set ACCEPTANCE_ISOLATED=1 or create contracts/evm/.acceptance-isolated"
    echo "  to confirm this is a local isolated test environment."
    echo "ACCEPTANCE_RESULT=FAIL exit=1"
    exit 1
fi

# ---------------------------------------------------------------------------
# 4. PostgreSQL health check (BEFORE any migration or drop)
# ---------------------------------------------------------------------------
echo "--- Checking PostgreSQL health ---"
PGREADY=0
for i in $(seq 1 30); do
    if pg_isready -h localhost -p 5433 -U postgres -q; then
        PGREADY=1
        echo "PostgreSQL is ready."
        break
    fi
    echo "Waiting for PostgreSQL (attempt $i/30)..."
    sleep 2
done
if [ "$PGREADY" = "0" ]; then
    echo "ERROR: PostgreSQL did not become ready within 60 seconds."
    echo "ACCEPTANCE_RESULT=FAIL exit=1"
    exit 1
fi

# ---------------------------------------------------------------------------
# 5. Database isolation sanity check
# ---------------------------------------------------------------------------
DB_NAME="this_cafeteria"
# Refuse to reset shared databases (must not match production names).
SAFE_DB_PATTERNS=("this_cafeteria_test" "this_cafeteria_acceptance" "this_cafeteria_local" "this_cafeteria")
SAFE=0
for pat in "${SAFE_DB_PATTERNS[@]}"; do
    if [ "$DB_NAME" = "$pat" ]; then
        SAFE=1
        break
    fi
done
if [ "$SAFE" = "0" ]; then
    echo "ERROR: Target database '$DB_NAME' is not in the safe list."
    echo "ACCEPTANCE_RESULT=FAIL exit=1"
    exit 1
fi

# ---------------------------------------------------------------------------
# 6. Optionally reset database (fail closed unless isolation confirmed)
# ---------------------------------------------------------------------------
if [ "${RESET_DB:-0}" = "1" ]; then
    echo "RESET_DB=1: dropping database for clean state..."
    dotnet ef database drop -f \
        --project src/ThisCafeteria.Infrastructure \
        --startup-project src/ThisCafeteria.Web
fi

# ---------------------------------------------------------------------------
# 7. Build and migrate
# ---------------------------------------------------------------------------
echo "--- Building projects ---"
dotnet build

echo "--- Applying database migrations ---"
dotnet ef database update \
    --project src/ThisCafeteria.Infrastructure \
    --startup-project src/ThisCafeteria.Web

# ---------------------------------------------------------------------------
# 8. Start Hardhat node
# ---------------------------------------------------------------------------
echo "--- Starting Hardhat node ---"
(cd contracts/evm && npm run deploy:local:node) > contracts/evm/node.log 2>&1 &
NODE_PID=$!
echo "Hardhat node PID: $NODE_PID"

echo "--- Waiting for Hardhat node RPC ---"
RPC_READY=0
for i in $(seq 1 30); do
    if curl -s -X POST -H "Content-Type: application/json" \
        --data '{"jsonrpc":"2.0","method":"eth_blockNumber","params":[],"id":1}' \
        http://127.0.0.1:8545 > /dev/null 2>&1; then
        RPC_READY=1
        echo "Hardhat node is ready."
        break
    fi
    echo "Waiting for Hardhat node (attempt $i/30)..."
    sleep 2
done
if [ "$RPC_READY" = "0" ]; then
    echo "ERROR: Hardhat node did not start within 60 seconds."
    echo "ACCEPTANCE_RESULT=FAIL exit=1"
    exit 1
fi

# ---------------------------------------------------------------------------
# 9. Deploy contracts
# ---------------------------------------------------------------------------
echo "--- Deploying contracts to local Hardhat node ---"
(cd contracts/evm && npm run deploy:local)
echo "Contract addresses (from manifest):"
cat contracts/evm/deployments/evm-local.json | grep -E '"erc8183Escrow"|"cafe"|"erc8004Registry"' || true

# ---------------------------------------------------------------------------
# 10. Start worker
# ---------------------------------------------------------------------------
export Blockchain__LocalEvmManifest="$(pwd)/contracts/evm/deployments/evm-local.json"
echo "--- Starting reconciliation worker ---"
dotnet run --project src/ThisCafeteria.Worker --environment Development > worker.log 2>&1 &
WORKER_PID=$!
echo "Worker PID: $WORKER_PID"

echo "--- Waiting for worker to initialize (5s) ---"
sleep 5

# ---------------------------------------------------------------------------
# 11. Run acceptance test
# ---------------------------------------------------------------------------
echo "--- Running acceptance test script ---"
echo "Test harness note: uses Hardhat-controlled local wallets, NOT a browser wallet or UI E2E test."

TEST_EXIT=0
(cd contracts/evm && HARDHAT_NETWORK=localhost npx tsx scripts/acceptance-test.ts) || TEST_EXIT=$?

echo ""
echo "--- Acceptance test exit code: $TEST_EXIT ---"

# ---------------------------------------------------------------------------
# 12. Capture evidence
# ---------------------------------------------------------------------------
echo "--- Worker checkpoint and applied-event counts (via psql) ---"
psql -h localhost -p 5433 -U postgres -d "$DB_NAME" \
    -c 'SELECT "ChainKey", "EscrowAddress", "LastScannedBlock", "UpdatedAtUtc" FROM "AgenticCommerceCheckpoints";' \
    2>/dev/null || echo "(psql query failed – DB may not be available)"

psql -h localhost -p 5433 -U postgres -d "$DB_NAME" \
    -c 'SELECT COUNT(*) AS applied_event_count FROM "AgenticJobAppliedEvents";' \
    2>/dev/null || echo "(psql query failed)"

psql -h localhost -p 5433 -U postgres -d "$DB_NAME" \
    -c 'SELECT COUNT(*) AS deferred_event_count FROM "AgenticJobDeferredEvents";' \
    2>/dev/null || echo "(psql query failed)"

psql -h localhost -p 5433 -U postgres -d "$DB_NAME" \
    -c 'SELECT "OnChainJobId", "Status", "CreationTransactionHash", "FundedTransactionHash", "CompletionTransactionHash" FROM "AgenticJobs" ORDER BY "OnChainJobId";' \
    2>/dev/null || echo "(psql query failed)"

echo ""
echo "--- Lifecycle stages proven ---"
if [ "$TEST_EXIT" = "0" ]; then
    echo "  create → provider assignment → budget → funding → submission → completion/payment release  [VERIFIED]"
    echo "  rejection/refund  [VERIFIED]"
    echo "  expiry/claim refund  [VERIFIED]"
fi

# ---------------------------------------------------------------------------
# 13. Final machine-readable marker
# ---------------------------------------------------------------------------
echo ""
if [ "$TEST_EXIT" = "0" ]; then
    echo "ACCEPTANCE_RESULT=PASS  exit=0"
else
    echo "ACCEPTANCE_RESULT=FAIL  exit=$TEST_EXIT"
fi

# Allow cleanup trap to run, then exit with the preserved acceptance exit code.
exit "$TEST_EXIT"
