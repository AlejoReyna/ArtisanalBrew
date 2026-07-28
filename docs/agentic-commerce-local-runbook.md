# Agentic commerce local runbook

This runbook is intentionally local-only. It does not deploy to Base Sepolia or Arbitrum Sepolia
and it never funds a public transaction.

## What is implemented

The repository contains an ASP.NET application and worker, a pinned TypeScript x402/MCP gateway,
an ERC-8183 escrow, canonical ERC-4337 v0.7 local components, ERC-8004 fixture registries, and an
ERC-7683 two-node resolver prototype. Draft-standard fixtures and the local solver are experimental;
they are not production bridge, arbitration, reputation, or custody infrastructure.

## Start the local stack

1. Start PostgreSQL using the repository's existing development configuration.
2. Start two Hardhat nodes. Use chain IDs 31337 and 31338 and keep their RPC endpoints server-side.
3. From `contracts/evm`, deploy the deterministic local contracts and export the manifest.
4. Apply EF Core migrations, then start `ThisCafeteria.Web` and `ThisCafeteria.Worker`.
5. Start `src/ThisCafeteria.AgentGateway` with a local facilitator and server-only gateway secret.
6. Start Rundler against the local EntryPoint with `--unsafe`. This is required by the Hardhat
   tracer limitation documented in `docs/agentic-commerce-stack-plan.md`; it does not prove
   ERC-7562 storage-access enforcement.

The maintained smoke scripts are the executable source of truth for the exact ports and environment
variables: `run-acceptance.sh`, `contracts/evm/scripts/rundler-e2e-check.ts`,
`contracts/evm/scripts/two-node-crosschain-smoke.ts`, and
`contracts/evm/scripts/two-node-standing-solver-check.ts`.

## ERC-4337 sponsorship proofs

These run the real .NET sponsorship/submission classes against a live chain rather than stubs. Step 6
above (Rundler) is only needed for the first one:

| Script | Proves | Needs a bundler? |
|--------|--------|------------------|
| `crossstack-sponsor-check.ts` | the real sponsor's signature is accepted by the on-chain paymaster | no |
| `crossstack-bundler-submit-check.ts` | real .NET code submits a sponsored operation through a real bundler and independently verifies it mined | yes |
| `crossstack-bundler-submit-denied-check.ts` | the submitter refuses an unapproved sponsorship without contacting the bundler | no (and no chain) |
| `crossstack-bundler-submit-negative-check.ts` | all five Phase 4 gate denials — over-budget, wrong-target, wrong-selector, expired, revoked — provoked from the real policy engine and refused by the real submitter | no |
| `metamask-session-key-e2e.ts` | the MetaMask v1.3.0 delegation path: install, redeem, revoke, and eleven on-chain rejections (user-paid) | yes |
| `crossstack-sponsored-delegation-check.ts` | delegation + sponsorship together: an agent with no gas money spends only what it was delegated, and identical sponsorship buys an out-of-scope payment nothing | yes |

## Demo sequence

Discover the seeded supplier, call `request_wholesale_quote` and observe the 402 challenge, settle
the local test USDC payment exactly once, create an escrow job from the returned commitment, submit
the cross-chain intent, verify destination settlement, fund the escrow, submit provider evidence,
complete or reject as evaluator, and let the worker index the terminal feedback.

Keep intent settlement and escrow funding as separate observable stages. A failed, duplicated,
expired, or policy-denied intent must leave the escrow Open and unfunded.

## Recovery and shutdown

Stop worker, gateway, bundler, and Hardhat processes before restarting deterministic nodes. If a
local node is redeployed, clear only the local database checkpoints and projections created for that
chain, then rerun migrations and deployment. Never reuse local deployment manifests against a public
chain. The current worker uses confirmation depth and idempotent event keys but does not automatically
roll back projections after a deep reorg; manual recovery is required as documented in the plan.

## Public testnet dry run

Before any public action, replace local manifests with reviewed Base Sepolia and Arbitrum Sepolia
manifests, validate every capability and contract bytecode, configure secret RPC/facilitator/bundler
endpoints through the server environment, and run the smoke flow with funding disabled. A public
deployment or funded testnet transaction requires explicit user approval and is intentionally not
performed by this repository task.
