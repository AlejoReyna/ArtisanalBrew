# AgentGateway production runbook

This runbook defines the production contract for
`src/ThisCafeteria.AgentGateway`. It does not declare either public testnet's
session-payment capability released; that remains gated by a compatible live
bundler and a funded end-to-end proof.

## Readiness gates

| Gate | Code state | Operational requirement |
|---|---|---|
| Safe-mode bundler | Enforced. Public-chain configuration accepts only `bundlerMode: "safe"` and startup preflight requires the configured v0.7 EntryPoint in `eth_supportedEntryPoints`. | Run Rundler without `--unsafe` against a tracer-capable RPC. Restrict bundler ingress. The existing public deployment does not yet advertise the canonical modular EntryPoint, so session payments remain disabled. |
| Durable account/permission/receipt persistence | Implemented with `PostgresIdempotencyStore`. x402 fulfillment records and complete signed agent-permission requests plus UserOperation/transaction receipts are stored in PostgreSQL under separate namespaces. PostgreSQL advisory locks serialize a key across replicas. | Supply `AGENT_GATEWAY_DATABASE_URL`. The database principal needs create-table/index permission for first boot and read/write permission afterward. Back up and monitor `agent_gateway_atomic_results`. |
| Key custody | Implemented as an external signer adapter. Production rejects `AGENT_SESSION_PRIVATE_KEY`; the gateway sends only sign-message/sign-typed-data requests to the signer and cannot request raw transaction signatures. | Put the agent EOA in a KMS/HSM/Vault-backed signer reachable over private networking and HTTPS/mTLS. Inject signer credentials through Key Vault. Apply signer-side chain/domain/account policy, rate limits, audit logs, rotation, and emergency disable. |
| Chain bytecode verification | Enforced during production startup. The EntryPoint, SimpleFactory, HybridDeleGator implementation, DelegationManager, and target/method/exact-calldata/limited-call/nonce/timestamp enforcers are all checked against trusted runtime code hashes. | Record reviewed `keccak256(runtimeBytecode)` values in the server-only chain configuration. A missing deployment or mismatch stops startup. Hashes must come from the reviewed release artifact, not be learned from the same RPC at runtime. |
| Dependency audit | Remediated. MCP and affected transitive packages are pinned/overridden to patched versions. | CI must run `npm audit`; a new high/critical finding blocks release. Current verification is zero findings at all severities. |

## Required environment

Always:

- `AGENT_GATEWAY_SERVICE_SECRET`
- `X402_PAY_TO`
- `X402_USDC_ADDRESS`
- `X402_FACILITATOR_URL`
- `ASPNET_INTERNAL_URL`
- `AGENT_GATEWAY_DATABASE_URL` in production

For agent redemption:

- `AGENTIC_PAYMENT_CHAINS_JSON`
- `AGENT_SMART_ACCOUNT_SALT`
- `AGENT_REDEMPTION_API_TOKEN`
- `AGENT_SIGNER_URL`
- `AGENT_SIGNER_ADDRESS`
- `AGENT_SIGNER_TOKEN`

`AGENT_SESSION_PRIVATE_KEY` is a local-test-only escape hatch. The production
process exits if it is present.

Each entry in `AGENTIC_PAYMENT_CHAINS_JSON` contains a fixed `chainKey`,
`chainId`, `rpcUrl`, `bundlerUrl`, `bundlerMode`, the complete trusted
`DeleGatorEnvironment`, and `expectedCodeHashes`. Only `ethereum-sepolia`
(11155111) and `bsc-testnet` (97) are accepted by the redemption request schema.
Do not expose this configuration to the browser or accept overrides in a request.

## Remote signer contract

The gateway calls `POST /v1/signatures` on `AGENT_SIGNER_URL` with:

```json
{
  "address": "0x...",
  "operation": "signTypedData",
  "payload": {}
}
```

The signer returns `{"signature":"0x..."}`. The other permitted operation is
`signMessage`; raw transaction signing is denied in the gateway adapter. The
signer must independently enforce the configured address, EIP-712 chain/domain,
request authentication, rate limit, and audit trail. The bearer token is an
application check, not a substitute for private networking and mTLS.

## Release procedure

1. Apply the database permission and start one gateway replica with agent
   redemption disabled. Confirm `/health/live` reports PostgreSQL persistence.
2. Configure the external signer and confirm its address matches the
   deterministic agent HybridDeleGator for `AGENT_SMART_ACCOUNT_SALT`.
3. Populate reviewed runtime code hashes and a safe-mode bundler URL.
4. Start with agent redemption enabled. Production startup preflight must pass
   bytecode and `eth_supportedEntryPoints` checks.
5. Submit a funded testnet one-shot permission with a unique
   `Idempotency-Key`. Confirm the same request replays the stored receipt and a
   different request with that key returns HTTP 409.
6. Confirm the UserOperation receipt, transaction success, `JobFunded` event,
   application reconciliation row, and Procurement Lab UI state.
7. Only after Step 6 passes on a chain may its `agenticSessionPayments`
   capability become `true`.

## Failure and recovery

- A bytecode or EntryPoint preflight failure is a release stop, never a warning.
- If the signer is unavailable, redemption fails without falling back to an
  in-process key.
- PostgreSQL is in the spend-critical path. Do not fall back to memory in
  production.
- A crash after broadcast but before receipt persistence may leave an unknown
  result. Query the bundler/EntryPoint by the logged UserOperation context and
  reconcile before retrying. Exact calldata, the active epoch, the account
  nonce, and `LimitedCalls(1)` remain the on-chain double-spend barriers.
- Revoke an epoch through the owner UserOperation flow; do not delete database
  rows to simulate revocation.
