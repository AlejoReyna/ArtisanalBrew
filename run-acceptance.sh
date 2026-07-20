#!/bin/bash
set -e

echo "Building projects..."
dotnet build

echo "Skipping docker (Postgres already running locally)..."

echo "Waiting for DB to be ready..."
sleep 5
dotnet ef database update --project src/ThisCafeteria.Infrastructure --startup-project src/ThisCafeteria.Web

echo "Starting Hardhat node in background..."
cd contracts/evm
npm run deploy:local:node > node.log 2>&1 &
NODE_PID=$!
cd ../..

echo "Waiting for Hardhat node..."
sleep 5

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
HARDHAT_NETWORK=localhost npx ts-node scripts/acceptance-test.ts
TEST_EXIT=$?
cd ../..

echo "Cleaning up..."
kill $WORKER_PID
kill $NODE_PID

exit $TEST_EXIT
