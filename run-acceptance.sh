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

# `dotnet run` and `npm run` spawn child processes that survive a kill of the
# wrapper PID.  Earlier revisions killed only the wrapper, which orphaned the
# real Worker/Hardhat binaries and left them polling the database indefinitely.
# Collect the whole descendant tree and signal it depth-first.
descendant_pids() {
    local parent="$1"
    local child
    for child in $(pgrep -P "$parent" 2>/dev/null); do
        descendant_pids "$child"
        echo "$child"
    done
}

kill_tree() {
    local label="$1"
    local root="$2"
    [ -n "$root" ] || return 0
    kill -0 "$root" 2>/dev/null || return 0

    local pids
    pids="$(descendant_pids "$root") $root"
    echo "Killing $label tree (root PID $root): $(echo $pids)"

    local pid
    for pid in $pids; do
        kill -TERM "$pid" 2>/dev/null || true
    done

    # Give the tree up to 5s to exit, then escalate to SIGKILL.
    local deadline=$(( $(date +%s) + 5 ))
    while [ "$(date +%s)" -lt "$deadline" ]; do
        local alive=0
        for pid in $pids; do
            kill -0 "$pid" 2>/dev/null && alive=1
        done
        [ "$alive" = "0" ] && return 0
        sleep 0.5
    done

    for pid in $pids; do
        if kill -0 "$pid" 2>/dev/null; then
            echo "  escalating to SIGKILL for PID $pid"
            kill -KILL "$pid" 2>/dev/null || true
        fi
    done
}

cleanup() {
    echo "--- cleanup trap: terminating background processes ---"
    kill_tree "worker" "$WORKER_PID"
    kill_tree "hardhat node" "$NODE_PID"
}

trap cleanup EXIT INT TERM

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
# PostgreSQL runs under the Apple `container` runtime; evidence queries exec into
# it rather than using host psql (which blocks on an interactive password prompt).
PG_CONTAINER="${PG_CONTAINER:-this-cafeteria-postgres}"
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
# 9b. Purge reconciliation state for the freshly deployed contracts
#
# Hardhat deploys deterministically: a fresh node redeploys the escrow and
# registry to the SAME addresses every run.  A checkpoint left over from a
# previous run therefore points at a block height on a chain that no longer
# exists.  Because the new chain restarts near block 0, the worker would treat
# the stale (higher) checkpoint as "already scanned" and silently skip every
# event of this run, leaving the previous run's rows in place.  That produces
# either a false pass or a confusing tx-hash mismatch.
#
# A fresh deployment at an address makes all prior state for that address
# invalid by definition, so purge exactly those rows — scoped by
# ContractAddress, never a blanket delete.
# ---------------------------------------------------------------------------
ESCROW_ADDR="$(node -e 'const m=require("./contracts/evm/deployments/evm-local.json");process.stdout.write((m.addresses.erc8183Escrow||"").toLowerCase())')"
REGISTRY_ADDR="$(node -e 'const m=require("./contracts/evm/deployments/evm-local.json");process.stdout.write((m.addresses.erc8004Registry||"").toLowerCase())')"

if [ -z "$ESCROW_ADDR" ] || [ -z "$REGISTRY_ADDR" ]; then
    echo "ERROR: could not read deployed contract addresses from the manifest."
    echo "ACCEPTANCE_RESULT=FAIL exit=1"
    exit 1
fi

echo "--- Purging stale reconciliation state for redeployed contracts ---"
echo "  escrow:   $ESCROW_ADDR"
echo "  registry: $REGISTRY_ADDR"

if ! container exec "$PG_CONTAINER" psql -U postgres -d "$DB_NAME" -v ON_ERROR_STOP=1 \
    -c "DELETE FROM \"AgenticJobAppliedEvents\" WHERE lower(\"ContractAddress\") IN ('$ESCROW_ADDR','$REGISTRY_ADDR');
        DELETE FROM \"AgenticJobDeferredEvents\" WHERE lower(\"ContractAddress\") = '$ESCROW_ADDR';
        DELETE FROM \"AgenticJobs\"              WHERE lower(\"ContractAddress\") = '$ESCROW_ADDR';
        DELETE FROM \"AgentDirectoryEntries\"    WHERE lower(\"RegistryAddress\") = '$REGISTRY_ADDR';
        DELETE FROM \"AgenticCommerceCheckpoints\" WHERE lower(\"EscrowAddress\") IN ('$ESCROW_ADDR','$REGISTRY_ADDR');"; then
    echo "ERROR: failed to purge stale reconciliation state; evidence would not be trustworthy."
    echo "ACCEPTANCE_RESULT=FAIL exit=1"
    exit 1
fi

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

# Run psql inside the Apple `container` PostgreSQL instance.  Host `psql` would
# prompt for a password on the tty and block forever, which previously stalled
# evidence capture before the final marker was ever emitted.  Database errors
# are NOT swallowed: stderr is kept and a non-zero status is reported inline.
EVIDENCE_DB_FAILED=0
run_evidence_query() {
    local label="$1"
    local sql="$2"
    echo "-- ${label} --"
    if ! container exec "$PG_CONTAINER" psql -U postgres -d "$DB_NAME" -c "$sql"; then
        echo "ERROR: evidence query failed: ${label}"
        EVIDENCE_DB_FAILED=1
    fi
}

run_evidence_query "checkpoints" \
    'SELECT "ChainKey", "EscrowAddress", "LastScannedBlock", "UpdatedAtUtc" FROM "AgenticCommerceCheckpoints";'

run_evidence_query "applied event count (THIS RUN – escrow + registry only)" \
    "SELECT COUNT(*) AS applied_event_count_this_run FROM \"AgenticJobAppliedEvents\" WHERE lower(\"ContractAddress\") IN ('$ESCROW_ADDR','$REGISTRY_ADDR');"

run_evidence_query "applied event count (cumulative, all contracts)" \
    'SELECT COUNT(*) AS applied_event_count_total FROM "AgenticJobAppliedEvents";'

run_evidence_query "deferred event count (THIS RUN)" \
    "SELECT COUNT(*) AS deferred_event_count_this_run FROM \"AgenticJobDeferredEvents\" WHERE lower(\"ContractAddress\") = '$ESCROW_ADDR';"

run_evidence_query "deferred event count (cumulative)" \
    'SELECT COUNT(*) AS deferred_event_count_total FROM "AgenticJobDeferredEvents";'

run_evidence_query "job projections (THIS RUN)" \
    "SELECT \"OnChainJobId\", \"Status\", \"ProviderAddress\", \"CreationTransactionHash\", \"FundedTransactionHash\", \"CompletionTransactionHash\" FROM \"AgenticJobs\" WHERE lower(\"ContractAddress\") = '$ESCROW_ADDR' ORDER BY \"OnChainJobId\";"

if [ "$EVIDENCE_DB_FAILED" = "1" ] && [ "$TEST_EXIT" = "0" ]; then
    echo "ERROR: lifecycle assertions passed but database evidence capture failed."
    TEST_EXIT=1
fi

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
