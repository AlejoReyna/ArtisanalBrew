# Agentic commerce threat model

## Trust boundaries

The browser and MCP caller are untrusted. They may choose business inputs, but never trusted RPC
URLs, contract addresses, facilitator endpoints, solver addresses, payment recipients, or paymaster
policy. The TypeScript gateway owns x402 challenge/settlement binding and calls ASP.NET only with its
rotatable service credential. ASP.NET owns catalog, quotes, job rules, identity projections, and
trusted chain resolution. Workers treat decoded events plus the registry as authoritative and write
idempotent projections.

## Abuse controls

- Paid resources bind method, normalized route, canonical body hash, payment identity, network,
  asset, amount, recipient, nonce, and expiry; replay returns the original result.
- Escrow never accepts x402 as fulfillment funding. ERC-8183 terminal payout/refund is independent
  of reputation publication.
- Sponsorship policy is fail-closed: disabled policy, empty allowlists, missing grants, expiry,
  revocation, wrong target, wrong selector, and over-budget operations are denied.
- ERC-7683 source/destination chains, assets, resolver, settlement, output, fee, slippage, nonce,
  and deadlines are allowlisted. The solver is pre-funded test infrastructure, not a bridge.
- Metadata and deliverable URIs are untrusted and require SSRF, redirect, content-type, size, and
  timeout controls before any future fetcher is enabled.

## Failure and recovery

Each gateway, facilitator, worker reconciliation loop, solver, and reputation publisher fails
independently. Wrong-chain, wrong-contract, wrong-token, wrong-role, and mismatched-amount events
are rejected. Confirmed event rows are unique by chain, transaction, and log index. A deep reorg
that exceeds the configured confirmation window remains a manual recovery operation; the local
runbook documents the safe projection/checkpoint reset.

## Known limitations

The local Hardhat bundler proof runs Rundler in `--unsafe` mode because Hardhat cannot execute the
standard JavaScript storage-access tracer. ERC-8004/8183/7683 are draft standards and local
registries/resolvers are fixtures where documented. Static local token/native-USD prices are not
oracles. No public deployment or funded testnet transaction is performed automatically.
