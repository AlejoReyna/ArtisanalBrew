# ArtisanalBrew Agent Gateway

This service is the TypeScript boundary for x402 v2 and MCP. It does not access
PostgreSQL, hold blockchain keys, or accept trusted infrastructure addresses
from callers. It calls ASP.NET only through the authenticated internal routes.

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

This package still deliberately refuses production mode because its existing
gateway idempotency store is process-local. Production also requires a
safe-mode bundler, durable account/permission/receipt persistence, key custody,
chain bytecode verification, and remediation or explicit acceptance of all
`npm audit` findings.
