#!/bin/sh
set -eu

: "${RUNDLER_NODE_HTTP:?RUNDLER_NODE_HTTP must be supplied through Key Vault}"
: "${RUNDLER_SIGNER_PRIVATE_KEY:?RUNDLER_SIGNER_PRIVATE_KEY must be supplied through Key Vault}"

exec /opt/rundler/rundler node \
  --chain_spec /etc/rundler/chain-spec.toml \
  --node_http "$RUNDLER_NODE_HTTP" \
  --signer.private_keys "$RUNDLER_SIGNER_PRIVATE_KEY" \
  --rpc.port 4338
