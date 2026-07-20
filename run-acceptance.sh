#!/bin/bash
set -e

# Cleanup trap to ensure background processes are stopped on failure or exit
trap 'kill $(jobs -p) 2>/dev/null || true' EXIT

echo "Building projects..."
dotnet build

if [ "$RESET_DB" = "1" ]; then
    echo "Dropping database to ensure clean state..."
    dotnet ef database drop -f --project src/ThisCafeteria.Infrastructure --startup-project src/ThisCafeteria.Web
fi

echo "Applying latest database migrations..."
dotnet ef database update --project src/ThisCafeteria.Infrastructure --startup-project src/ThisCafeteria.Web

echo "Waiting for PostgreSQL database health..."
until pg_isready -h localhost -p 5433 -U postgres; do
    echo "Waiting for postgres..."
    sleep 2
done

echo "Starting Hardhat node in background..."
cd contracts/evm
npm run deploy:local:node > node.log 2>&1 &
NODE_PID=$!
cd ../..

echo "Waiting for Hardhat node RPC health..."
until curl -s -X POST -H "Content-Type: application/json" --data '{"jsonrpc":"2.0","method":"eth_blockNumber","params":[],"id":1}' http://127.0.0.1:8545 > /dev/null; do
    echo "Waiting for Hardhat node..."
    sleep 2
done

echo "Deploying contracts..."
cd contracts/evm
npm run deploy:local
cd ../..

export Blockchain__LocalEvmManifest="$(pwd)/contracts/evm/deployments/evm-local.json"
echo "Starting Worker in background..."
dotnet run --project src/ThisCafeteria.Worker --environment Development &> worker.log &
WORKER_PID=$!

echo "Waiting for Worker to start..."
sleep 5

echo "Running acceptance test..."
cd contracts/evm
HARDHAT_NETWORK=localhost npx tsx scripts/acceptance-test.ts
TEST_EXIT=$?
cd ../..

if [ $TEST_EXIT -ne 0 ]; then
    echo "Acceptance test failed with exit code $TEST_EXIT."
else
    echo "Acceptance test completed successfully."
fi

exit $TEST_EXIT
