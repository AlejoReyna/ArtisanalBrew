#!/usr/bin/env bash
set -euo pipefail

# Local-only orchestration entry point. It intentionally does not deploy to or fund a public chain.
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

if [[ "${AGENTIC_COMMERCE_LOCAL_ONLY:-1}" != "1" ]]; then
  echo "Refusing to run: AGENTIC_COMMERCE_LOCAL_ONLY must remain 1 for this script." >&2
  exit 2
fi

echo "[agentic-commerce] local-only smoke"
echo "[agentic-commerce] contract tests"
(cd "$repo_root/contracts/evm" && npm test)

echo "[agentic-commerce] gateway tests and build"
(cd "$repo_root/src/ThisCafeteria.AgentGateway" && npm test && npm run build)

echo "[agentic-commerce] escrow/indexer acceptance"
(cd "$repo_root" && ACCEPTANCE_ISOLATED=1 ./run-acceptance.sh)

echo "[agentic-commerce] completed; start the two-node intent and Rundler checks separately"
echo "[agentic-commerce] no public deployment or funded testnet transaction was attempted"
