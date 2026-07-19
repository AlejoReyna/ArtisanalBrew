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
