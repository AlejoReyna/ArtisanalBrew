#!/usr/bin/env bash
set -euo pipefail

# Public Devnet deployment. The wallet path is passed at execution time and is never written to
# a manifest or repository file. The pre-existing program keypair remains under target/ (ignored)
# so the same authority can perform future upgrades.
: "${SOLANA_WALLET:?Set SOLANA_WALLET to the existing local keypair path.}"
: "${SOLANA_PUBLIC_DEPLOYMENT_CONFIRMED:?Set SOLANA_PUBLIC_DEPLOYMENT_CONFIRMED=true to broadcast to Devnet.}"
[[ "${SOLANA_PUBLIC_DEPLOYMENT_CONFIRMED}" == "true" ]] || { echo "SOLANA_PUBLIC_DEPLOYMENT_CONFIRMED must be true." >&2; exit 2; }

export SOLANA_CLUSTER=devnet
export SOLANA_RPC_URL="${SOLANA_RPC_URL:-https://api.devnet.solana.com}"
[[ "${SOLANA_RPC_URL}" == https://* ]] || { echo "SOLANA_RPC_URL must be a complete HTTPS Devnet RPC URL (for example https://api.devnet.solana.com)." >&2; exit 2; }
buffer_signer="${SOLANA_BUFFER_SIGNER:-target/deploy/cafe_liquid_staking-devnet-buffer-keypair.json}"

# The default CLI buffer is ephemeral. If a public RPC loses blockhashes during a large upload,
# it prints a recovery mnemonic to stdout and leaves the upload impossible to resume safely.
# Keep a dedicated buffer signer under target/ (which is gitignored) instead. Re-running this
# script then resumes the same buffer without displaying seed material.
if [[ ! -f "${buffer_signer}" ]]; then
  solana-keygen new --silent --no-bip39-passphrase --outfile "${buffer_signer}"
fi

anchor build
solana program deploy target/deploy/cafe_liquid_staking.so \
  --program-id target/deploy/cafe_liquid_staking-keypair.json \
  --buffer "${buffer_signer}" \
  --keypair "${SOLANA_WALLET}" \
  --url "${SOLANA_RPC_URL}" \
  --use-rpc \
  --max-sign-attempts 15 \
  --with-compute-unit-price 10000
npx tsx scripts/deploy-public.ts
