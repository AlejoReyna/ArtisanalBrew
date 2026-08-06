# ArtisanalBrew Agent Gateway

This service is the TypeScript boundary for x402 v2, MCP, and agent-side
delegation redemption. It calls ASP.NET only through authenticated internal
routes and never accepts trusted RPC, bundler, contract, facilitator, or signer
addresses from callers.

The initial implementation pins the official x402 v2 package family at 2.19.0
and uses Base Sepolia (`eip155:84532`) with a configured test-USDC address.
`AGENT_GATEWAY_SERVICE_SECRET`, `X402_PAY_TO`, and `X402_USDC_ADDRESS` are
server-only environment variables. No values are sent to browser metadata.

The facilitator and payment recipient are intentionally configuration-only. A
local facilitator can be used for deterministic tests; public facilitator use
is not enabled by this service automatically.

## ERC-4337 agent permissions

`src/agenticPayments.ts` is the additive modular-account integration boundary.
It is pinned to MetaMask Delegation Framework v1.3.0 and only composes the
official SDK's exact function-call, nonce, timestamp and limited-call caveats.
It never treats x402 settlement or paymaster sponsorship as asset authority.
Existing reference SimpleAccounts remain a separate account type and are not
migrated automatically.

Callers must load the deployed environment from a trusted deployment manifest,
verify its account type, framework revision, EntryPoint version/address and
bytecode, then call `requireCompatibleBundler` before building operations.
Activation and revocation are owner UserOperations containing the call returned
by `encodePermissionEpochChange`; the same audited on-chain nonce transition is
used for each action. Payment authority is issued by
`signExactOneShotPermission`, and the agent submits the result returned by
`encodeRedemption` through a v0.7 bundler.

`POST /agentic-payments/redeem` is the authenticated agent route. It requires an
idempotency key, reconstructs and compares every signed delegation field against
the exact target/selector/calldata/epoch/time/call-limit policy, confirms the
epoch live through `NonceEnforcer.currentNonce`, and only then sends the agent's
UserOperation. PostgreSQL stores the signed permission request and resulting
UserOperation/transaction receipt atomically for replay across replicas and
restarts.

## Production mode

The gateway can run with `NODE_ENV=production`, but it fails startup unless the
spend-critical controls are present:

- `AGENT_GATEWAY_DATABASE_URL` provides PostgreSQL-backed atomic x402
  fulfillments and agent permission/receipt records.
- Agent redemption uses `AGENT_SIGNER_URL`, `AGENT_SIGNER_ADDRESS`, and
  `AGENT_SIGNER_TOKEN`; `AGENT_SESSION_PRIVATE_KEY` is rejected in production.
- Every configured chain must declare `bundlerMode: "safe"` and the bundler
  must advertise the canonical v0.7 EntryPoint during startup preflight.
- Every configured modular contract must have a trusted expected runtime
  bytecode hash; startup preflight reads and compares each deployed contract.
- The pinned dependency tree currently passes `npm audit` with zero findings.

The currently deployed public Rundler still does not advertise the canonical
modular EntryPoint, so public `agenticSessionPayments` capability flags remain
off. Production x402/MCP can run with agent redemption unconfigured; enabling
public session payments additionally requires the live bundler/testnet gate.
See [`docs/agent-gateway-production-runbook.md`](../../docs/agent-gateway-production-runbook.md).
