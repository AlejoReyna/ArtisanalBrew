# Agentic commerce protocol revisions

This local-first slice records the references used during implementation. Draft
interfaces are isolated behind adapters and are not presented as production
standards.

- x402 v2: Coinbase Developer Documentation migration guide and official
  TypeScript package family `@x402/core`, `@x402/express`, and `@x402/evm`.
  The v2 network identifier is CAIP-2, for example `eip155:84532`; the gateway
  will pin package versions in its own lockfile.
- ERC-8183: Ethereum Improvement Proposal 8183, current published draft page
  dated February 2026. The non-upgradeable local escrow intentionally uses the
  reference lifecycle and the current `bytes calldata optParams` signatures,
  accepts a client or provider budget setter, stores the draft's string
  description commitment, and omits hooks so `claimRefund` remains unhooked and
  recoverable. Funding verifies the exact token balance delta, rejecting
  fee-on-transfer or rebasing behavior for escrow safety.
- ERC-8004: Ethereum Improvement Proposal 8004, current published draft page
  dated August 2025. Local identity/reputation fixtures must be labelled as
  fixtures unless the canonical reference registries are deployed.
- ERC-7683: Ethereum Improvement Proposal 7683, current published draft page
  dated April 2024. The resolver-facing model follows the current
  `ResolvedCrossChainOrder` direction; the local solver is pre-funded and is
  not a bridge or production liquidity network.
- ERC-4337: EIP-4337 plus the pinned account-abstraction/bundler stack selected
  for local deployment. EntryPoint and bundler code will not be reimplemented
  in this repository.

Source references:

- https://docs.cdp.coinbase.com/x402/migration-guide
- https://eips.ethereum.org/EIPS/eip-8183
- https://eips.ethereum.org/EIPS/eip-8004
- https://eips.ethereum.org/EIPS/eip-7683
- https://eips.ethereum.org/EIPS/eip-4337
