#!/usr/bin/env bash
set -euo pipefail

# Generates public deployment metadata. Every address is supplied explicitly so an
# ephemeral validator run cannot silently become a public configuration. Public
# cluster output additionally requires an explicit release acknowledgement.
output="${1:-contracts/solana/local-deployment-manifest.json}"
idl="${SOLANA_IDL_PATH:-contracts/solana/target/idl/cafe_liquid_staking.json}"
binary="${SOLANA_PROGRAM_BINARY:-contracts/solana/target/deploy/cafe_liquid_staking.so}"

required=(SOLANA_CHAIN_KEY SOLANA_RPC_URL SOLANA_CLUSTER SOLANA_PROGRAM_ID SOLANA_DEPLOYMENT_SLOT SOLANA_STATE_PDA SOLANA_AUTHORITY_PDA SOLANA_CAFE_MINT SOLANA_STCAFE_MINT SOLANA_COFFEE_MINT SOLANA_CAFE_CUSTODY SOLANA_COFFEE_CUSTODY SOLANA_ADMIN SOLANA_TOKEN_PROGRAM SOLANA_TOKEN_2022_PROGRAM)
for name in "${required[@]}"; do
  if [[ -z "${!name:-}" ]]; then
    echo "Missing required public manifest variable: ${name}" >&2
    exit 2
  fi
done
[[ "${SOLANA_CLUSTER}" == "localnet" || "${SOLANA_CLUSTER}" == "devnet" || "${SOLANA_CLUSTER}" == "testnet" ]] || { echo "Only localnet, devnet, and testnet manifests are supported." >&2; exit 2; }
expected_chain_key="solana-${SOLANA_CLUSTER}"
[[ "${SOLANA_CHAIN_KEY}" == "${expected_chain_key}" ]] || { echo "Expected SOLANA_CHAIN_KEY=${expected_chain_key}." >&2; exit 2; }
if [[ "${SOLANA_CLUSTER}" != "localnet" ]]; then
  [[ "${SOLANA_PUBLIC_DEPLOYMENT_CONFIRMED:-}" == "true" ]] || { echo "Set SOLANA_PUBLIC_DEPLOYMENT_CONFIRMED=true after completing the public release gate." >&2; exit 2; }
  [[ "${SOLANA_RPC_URL}" == https://* ]] || { echo "Public Solana RPC must use HTTPS." >&2; exit 2; }
fi
[[ "${SOLANA_DEPLOYMENT_SLOT}" =~ ^[0-9]+$ ]] || { echo "SOLANA_DEPLOYMENT_SLOT must be a non-negative integer." >&2; exit 2; }
if [[ "${SOLANA_CLUSTER}" != "localnet" && "${SOLANA_DEPLOYMENT_SLOT}" -eq 0 ]]; then
  echo "Public Solana deployment slot must be positive." >&2
  exit 2
fi
[[ -f "${idl}" && -f "${binary}" ]] || { echo "IDL or program binary is missing." >&2; exit 2; }

public_keys=(SOLANA_PROGRAM_ID SOLANA_STATE_PDA SOLANA_AUTHORITY_PDA SOLANA_CAFE_MINT SOLANA_STCAFE_MINT SOLANA_COFFEE_MINT SOLANA_CAFE_CUSTODY SOLANA_COFFEE_CUSTODY SOLANA_ADMIN)
for name in "${public_keys[@]}"; do
  [[ "${!name}" =~ ^[1-9A-HJ-NP-Za-km-z]{32,44}$ ]] || { echo "Invalid Solana public key in ${name}." >&2; exit 2; }
done
[[ "${SOLANA_STATE_PDA}" == "${SOLANA_AUTHORITY_PDA}" ]] || { echo "The current program uses the vault state PDA as its token authority PDA." >&2; exit 2; }

readonly token_program_id="TokenkegQfeZyiNwAJbNbGKPFXCWuBvf9Ss623VQ5DA"
readonly token_2022_program_id="TokenzQdBNbLqP5VEhdkAS6EPFLC1PHnBqCXEpPxuEb"
[[ "${SOLANA_TOKEN_PROGRAM}" == "${token_program_id}" ]] || { echo "Unexpected SPL Token program ID." >&2; exit 2; }
[[ "${SOLANA_TOKEN_2022_PROGRAM}" == "${token_2022_program_id}" ]] || { echo "Unexpected Token-2022 program ID." >&2; exit 2; }

cafe_decimals="${SOLANA_CAFE_DECIMALS:-9}"
st_cafe_decimals="${SOLANA_STCAFE_DECIMALS:-9}"
coffee_decimals="${SOLANA_COFFEE_DECIMALS:-9}"
for decimals in "${cafe_decimals}" "${st_cafe_decimals}" "${coffee_decimals}"; do
  [[ "${decimals}" =~ ^[0-9]+$ && "${decimals}" -le 9 ]] || { echo "Solana token decimals must be integers from 0 through 9." >&2; exit 2; }
done
[[ "${cafe_decimals}" == "${st_cafe_decimals}" ]] || { echo "CAFE and stCAFE decimals must match." >&2; exit 2; }

mkdir -p "$(dirname "${output}")"
idl_sha256="$(shasum -a 256 "${idl}" | awk '{print $1}')"
binary_sha256="$(shasum -a 256 "${binary}" | awk '{print $1}')"
source_commit="${SOLANA_SOURCE_COMMIT:-$(git rev-parse HEAD 2>/dev/null || true)}"
created_at="${SOLANA_MANIFEST_CREATED_AT:-$(date -u +%Y-%m-%dT%H:%M:%SZ)}"
rust_toolchain="${SOLANA_RUST_TOOLCHAIN:-unknown}"
if [[ "${rust_toolchain}" == "unknown" ]] && command -v rustc >/dev/null 2>&1; then
  rust_toolchain="$(rustc --version)"
fi

jq -n \
  --arg schemaVersion "1" \
  --arg chainKey "${SOLANA_CHAIN_KEY}" --arg rpcUrl "${SOLANA_RPC_URL}" --arg cluster "${SOLANA_CLUSTER}" \
  --arg programId "${SOLANA_PROGRAM_ID}" --arg statePda "${SOLANA_STATE_PDA}" --arg authorityPda "${SOLANA_AUTHORITY_PDA}" \
  --arg cafeMint "${SOLANA_CAFE_MINT}" --arg stCafeMint "${SOLANA_STCAFE_MINT}" --arg coffeeMint "${SOLANA_COFFEE_MINT}" \
  --arg cafeCustody "${SOLANA_CAFE_CUSTODY}" --arg coffeeCustody "${SOLANA_COFFEE_CUSTODY}" --arg admin "${SOLANA_ADMIN}" \
    --arg tokenProgram "${SOLANA_TOKEN_PROGRAM}" --arg token2022Program "${SOLANA_TOKEN_2022_PROGRAM}" \
    --arg cafeDecimals "${cafe_decimals}" --arg stCafeDecimals "${st_cafe_decimals}" --arg coffeeDecimals "${coffee_decimals}" \
  --arg idlPath "${idl}" --arg idlSha256 "${idl_sha256}" --arg binarySha256 "${binary_sha256}" \
  --arg anchorVersion "0.31.1" --arg solanaCliVersion "2.2.1" --arg rustToolchain "${rust_toolchain}" \
  --arg deploymentSlot "${SOLANA_DEPLOYMENT_SLOT}" --arg sourceCommit "${source_commit}" --arg createdAt "${created_at}" \
  '{schemaVersion:$schemaVersion,chainKey:$chainKey,rpcUrl:$rpcUrl,cluster:$cluster,programId:$programId,deploymentSlot:($deploymentSlot|tonumber),statePda:$statePda,authorityPda:$authorityPda,cafeMint:$cafeMint,stCafeMint:$stCafeMint,coffeeMint:$coffeeMint,cafeCustody:$cafeCustody,coffeeCustody:$coffeeCustody,administrator:$admin,tokenProgram:$tokenProgram,token2022Program:$token2022Program,cafeDecimals:($cafeDecimals|tonumber),stCafeDecimals:($stCafeDecimals|tonumber),coffeeDecimals:($coffeeDecimals|tonumber),anchorVersion:$anchorVersion,solanaCliVersion:$solanaCliVersion,rustToolchain:$rustToolchain,idlPath:$idlPath,idlSha256:$idlSha256,programBinarySha256:$binarySha256,sourceCommit:$sourceCommit,createdAtUtc:$createdAt,capabilities:{walletLogin:true,liquidStaking:true,rewardFunding:true,reconciliation:true}}' > "${output}"

echo "Wrote verified Solana ${SOLANA_CLUSTER} manifest: ${output}"
