# ArtisanalBrew Agentic Commerce Stack — Implementation Prompt

You are a senior full-stack, smart-contract, protocol-integration, and application-security engineer working in the existing ArtisanalBrew repository.

Implement the agent-commerce stack described below. Before editing anything, read [`docs/agentic-commerce-stack-plan.md`](../agentic-commerce-stack-plan.md) and [`docs/multichain-liquid-staking-plan.md`](../multichain-liquid-staking-plan.md) completely. The first document is the authoritative plan for this task; the second defines the shared multichain foundation this work must reuse.

## Mission

Build a reproducible local-first demonstration that connects:

- x402 v2 for immediate paid HTTP/MCP resources;
- ERC-4337 for smart accounts, batched UserOperations, and quota-limited gas sponsorship;
- ERC-8004 for agent identity, endpoints, reputation, and validation references;
- ERC-8183 for asynchronous job escrow with provider submission and evaluator completion/rejection;
- ERC-7683 for a solver-facing cross-chain intent prototype when the user's funds start on another EVM chain.

The coherent demo is:

1. discover an ArtisanalBrew supplier agent;
2. pay test USDC through x402 for a structured wholesale quote;
3. inspect ERC-8004 identity and trust signals;
4. create an ERC-8183 procurement job from the quote commitment;
5. if required, express an ERC-7683-compatible intent to deliver the budget from an Arbitrum-like source chain to a Base-like destination chain;
6. batch approval and escrow funding through an ERC-4337 smart account, with policy-checked sponsorship and a user-paid fallback;
7. have the provider submit a coffee-lot deliverable commitment;
8. have the evaluator complete or reject the job;
9. index the terminal result and publish an ERC-8004-compatible reputation signal without coupling reputation success to escrow payout.

Use Base Sepolia as the testnet integration hub and Arbitrum Sepolia as the first cross-chain source. Build and verify the entire flow on two deterministic local EVM nodes first. Do not broadcast any public deployment or funded testnet transaction without explicit user approval.

## Non-negotiable repository context

- The app is .NET 10 ASP.NET Core/Blazor with PostgreSQL, EF Core, Nethereum, and a separate worker.
- The repository already contains checkout, rewards, transparency records, wallet authentication, staking reconciliation, and an in-progress multichain/liquid-staking implementation.
- The in-progress chain registry and selected-chain accessor are under `src/ThisCafeteria.Application/Configuration` and `src/ThisCafeteria.Web/Services/Blockchain`.
- The in-progress pinned Hardhat workspace is `contracts/evm`; the Anchor workspace is `contracts/solana`.
- Existing and in-progress user changes are uncommitted. Inspect `git status`, preserve them, and do not reset, discard, overwrite, or reformat unrelated work.
- Reuse `IChainRegistry`, wallet identity work, trusted RPC resolution, transaction verification, explorer templates, ledger uniqueness conventions, and worker reconciliation patterns. Do not build parallel versions.
- Preserve existing marketplace checkout, reward claims, wallet login, chain selection, staking, and local contract behavior.
- Official x402 v2 reference SDKs currently support TypeScript, Go, and Python, not .NET. Add a small TypeScript gateway rather than writing an unreviewed x402 implementation in ASP.NET.
- ERC-8004, ERC-8183, and ERC-7683 are drafts. Verify their current official specifications at implementation time, record the exact revisions used, isolate them behind adapters, and label the UI/demo experimental.

## Working rules

1. Start with a read-only repository audit and baseline test run.
2. Report discovered overlap with the multichain work before changing overlapping files.
3. Make small, reviewable phases with a working gate after each phase.
4. Use exact dependency versions and lockfiles; do not use floating versions.
5. Prefer canonical/reference packages and contracts over hand-rolled protocol infrastructure.
6. Never implement EntryPoint, a bundler, signature cryptography, or private-key custody from scratch.
7. Never store user/agent private keys or unrestricted session authority.
8. Never accept RPC URLs, contract addresses, facilitator URLs, EntryPoint/paymaster addresses, or solver contracts from the client.
9. Never use x402 as escrow for physical fulfillment.
10. Never describe the local intent solver as a production bridge.
11. Do not commit, push, open a PR, delete data, or deploy publicly unless explicitly requested.

## Standards responsibilities

Keep these boundaries explicit in code and documentation:

- x402: immediate payment for a digital HTTP response;
- ERC-4337: account execution, batching, sponsorship, and constrained permissions;
- ERC-8004: identity and reputation signals, not trust guarantees;
- ERC-8183: job escrow and evaluator decision, not complete arbitration;
- ERC-7683: solver-facing intent representation, not bridge liquidity or guaranteed execution.

## Required architecture

### TypeScript x402/MCP gateway

Create `src/ThisCafeteria.AgentGateway` unless repository conventions strongly justify another path. It must have an exact-version lockfile and own only:

- official x402 v2 server middleware and facilitator integration;
- MCP tools and Bazaar-compatible discovery metadata;
- request/output schemas;
- payment-to-request binding;
- idempotent fulfillment;
- authenticated internal calls to ASP.NET;
- health/readiness and structured correlation logs.

Initial resources:

- `search_products` — free;
- `create_brew_plan` — 0.01 test USDC;
- `get_provenance_report` — 0.02 test USDC;
- `request_wholesale_quote` — 0.02 test USDC.

ASP.NET remains authoritative for catalog, provenance, quotes, users, job projections, and business rules. The gateway must not connect directly to PostgreSQL and must not contain blockchain private keys.

Bind each paid fulfillment to HTTP method, normalized route, canonical request-body hash, payment identity, network, asset, amount, recipient, nonce, and expiry. A replay returns the original idempotent result or a clear conflict; it never repeats a side effect.

Use test USDC on the Base-like local chain and Base Sepolia. Do not make CAFE/Permit2 a prerequisite for the first vertical slice. Keep Solana x402 out of scope for this EVM stack because the public x402 test path uses Solana Devnet while the staking plan requested Solana Testnet.

### ASP.NET bounded services and APIs

Add focused interfaces and implementations rather than expanding `CoffeeWeb3Service`:

- `IAgentDirectoryService`;
- `IAgentResourceService`;
- `IAgenticJobService`;
- `ISmartAccountService`;
- `ICrossChainIntentService`;
- `IOnchainCommerceVerifier`.

Add authenticated internal resource endpoints for the gateway and user-facing APIs for directory, job preparation, transaction verification, intent preview/submission, and projections. All state-changing requests require `chainKey` and resolve trusted configuration through the shared registry.

Internal gateway authentication must be explicit, rotatable, constant-time validated, rate-limited, and excluded from browser-delivered configuration. Prefer a scoped service credential for local/development and document how production would use network identity or mTLS. Do not rely only on obscurity or an unprotected localhost assumption.

### Chain registry extensions

Extend existing chain definition/capability/deployment models only as necessary for:

- x402 public payment network/asset/recipient metadata;
- ERC-4337 EntryPoint, account factory/implementation, bundler, and paymaster;
- ERC-8004 identity/reputation/validation registries;
- ERC-8183 escrow and payment token;
- ERC-7683 resolver, settlement contracts, and solver endpoint.

Separate public metadata from secret server endpoints. Validate capabilities at startup: an enabled capability must have every required family-specific deployment value. Base owns the destination execution stack; Arbitrum owns only configured source-intent capability unless other features are explicitly deployed.

### ERC-8183 contract

Implement a minimal, non-upgradeable ERC-8183-compatible escrow under `contracts/evm`, against the current official draft revision. Required behavior:

- Open, Funded, Submitted, Completed, Rejected, Expired states;
- client, optional provider, evaluator, description commitment, budget, expiry, status;
- one configured ERC-20 payment token per deployment unless the current draft mandates otherwise;
- `createJob`, `setProvider`, `setBudget`, `fund(expectedBudget)`, `submit(deliverable)`, `complete(reason)`, `reject(reason)`, and `claimRefund` semantics;
- exact-budget/front-running guard;
- safe ERC-20 transfers, reentrancy protection, checks-effects-interactions;
- optional disclosed platform fee with a conservative immutable/configured maximum;
- terminal-state immutability and escrow-conservation invariants;
- no owner seizure of escrow and no participant/budget mutation after funding.

Start without per-job hooks. Publish reputation from the worker after confirmed completion/rejection/expiry. If hooks are later added, `claimRefund` must remain unhooked and recoverable, and only audited/allowlisted immutable hook implementations may be used.

Write exhaustive unit, fuzz/property, and invariant tests for roles, transitions, deadlines, payouts/refunds, fee behavior, reentrancy, false-return tokens, fee-on-transfer/rebasing behavior, duplicate actions, and conservation.

### ERC-8004 integration

Use canonical deployed registries or a pinned official reference implementation. For local development, deploy the exact pinned reference contracts if feasible. If a reduced test fixture is unavoidable, name and label it as a fixture and do not claim standards compliance.

Register seeded buyer, supplier, and evaluator agents with metadata that advertises MCP/HTTPS endpoints and x402 support. Index:

- identity/registry coordinates;
- verified agent wallet;
- metadata URI and safe cached hash;
- endpoint-domain verification;
- feedback/revocation events;
- raw reputation/validation signals by reviewer.

Treat every URI and metadata field as untrusted. Implement SSRF protection, response-size/content-type/time limits, redirect restrictions, and private/link-local network denial. Never convert arbitrary feedback into one trusted score. Show issuer, reviewer set, sample size, and Sybil warning.

After an ERC-8183 terminal event has enough confirmations, publish one idempotent feedback record referencing the job and relevant x402 proof. Failure to publish reputation must never revert or delay escrow settlement.

### ERC-4337 integration

Verify and pin the current canonical EntryPoint/account-abstraction release. Use an established local bundler, smart-account implementation/factory, and paymaster stack. Do not hand-roll EntryPoint or a bundler.

Required flow:

- discover/create the user's smart account;
- prepare and simulate UserOperations;
- batch exact token approval plus `fund(jobId, expectedBudget)`;
- submit through the bundler;
- verify UserOperation and transaction receipts server-side;
- support a user-paid fallback when sponsorship is disabled/rejected;
- surface status and failure reason in the UI.

Paymaster policy must verify authenticated identity/account, chain, nonce, target contract, selector, token, value, job budget, gas bounds, expiration, per-operation and daily quotas, and server-side simulation. It must reject arbitrary calls.

Only add agent session permissions if an audited compatible module is available. Permissions must constrain agent key, chains, targets, selectors, tokens, per-job/daily amounts, solver limits, expiry, and revocation. Otherwise retain explicit user confirmation. Never invent a custom permission cryptosystem and never ask the app user to sign raw EIP-7702 delegation authorizations.

### ERC-7683 intent prototype

Verify the current official ERC-7683 draft and implement behind `ICrossChainIntentService`. Build two local EVM nodes representing Arbitrum source and Base destination.

The intent must express and enforce:

- permitted source chain/asset and maximum input;
- exact destination chain/asset/recipient and minimum output;
- solver fee/slippage limits;
- nonce, replay domain, fill/settlement deadlines;
- allowlisted resolver and settlement contracts;
- current canonical solver-facing resolution output.

Add a local solver that can fulfill the deterministic test order. The application independently verifies the destination settlement and resulting smart-account balance before enabling the ERC-8183 funding operation.

Keep intent settlement and escrow funding as two observable stages. Do not initially hide them inside an ERC-8183 hook. A failed, partial, tampered, duplicated, or expired intent must leave the job Open and unfunded.

Document exactly what is simulated or pre-funded in the local solver. Never imply that the prototype supplies production bridge security or liquidity.

### Persistence

Add EF Core entities/configurations/migrations for projections equivalent to:

- agent directory entry;
- x402 resource fulfillment and payment receipt;
- agentic job and job event;
- cross-chain intent and settlement/fill event;
- agent feedback/revocation;
- smart-account profile without keys.

Use precision-safe numeric representations and chain-aware identifiers. Onchain log uniqueness must include at least `(ChainKey, TransactionId, LogIndex)`. Add idempotency keys for paid resources and reputation publication. Large quote/evidence documents remain offchain; persist their hashes and storage references.

Migrations must be forward-safe against current data and must not rewrite unrelated migrations. Add migration and repository tests.

### Worker and reconciliation

Add independent supervised loops for ERC-8183, ERC-8004, intents, pending x402 settlements if applicable, and reputation publication. Use separate checkpoints, bounded ranges, confirmation/finality policy, reorg handling, cancellation, exponential backoff, health/lag metrics, and idempotent inserts.

One failing RPC, registry, facilitator, solver, or publication loop must not stop the others. Treat decoded events plus trusted registry configuration as authoritative; reject wrong-chain, wrong-contract, wrong-token, wrong-role, and mismatched-amount records.

### UI

Add an authenticated `Agent Commerce` / `Procurement Lab` experience consistent with the existing Blazor design:

- agent directory and raw trust signals;
- paid-resource playground that visibly shows 402 challenge, payment, settlement, and response;
- procurement job creation and provider assignment;
- state stepper for Open/Funded/Submitted/Completed/Rejected/Expired;
- provider evidence submission;
- evaluator completion/rejection and expiry refund;
- cross-chain route preview with source amount, destination amount, fees, slippage, and deadline;
- smart-account, sponsorship, permission, and revocation status;
- explorer links derived only from `IChainRegistry`;
- optional Protocol Inspector correlating x402 receipt, proposal hash, intent/order, UserOperation, transaction, job events, and reputation feedback.

Keep protocol names secondary to user actions. Label draft standards, local fixtures, mock/pre-funded solver behavior, and reputation limitations honestly. Make all new flows keyboard accessible and responsive.

## Required implementation sequence and gates

### 0. Audit and baseline

- read both plans completely;
- inspect `git status` and all overlapping files;
- run existing .NET, Hardhat, and available Anchor tests;
- record baseline failures and the current state of multichain work;
- identify exact files owned by this task.

Gate: report baseline and overlap before editing.

### 1. Local contracts and protocol infrastructure

- pin official specification revisions and dependency versions;
- implement/test ERC-8183;
- deploy canonical ERC-4337 local components;
- deploy pinned ERC-8004 registries or clearly labeled fixtures;
- implement/test the ERC-7683 resolver/order and local solver;
- export ABIs and reproducible manifests.

Gate: direct scripts pass identity registration, sponsored UserOperation, full escrow success/reject/expiry paths, reputation write/read, and two-node intent fulfillment.

### 2. x402/MCP vertical slice

- build TypeScript gateway;
- build internal ASP.NET resource endpoints;
- implement schemas, payment binding, idempotency, receipts, MCP tools, and discovery metadata;
- test 402, success, replay, tampering, wrong payment, and facilitator failure.

Gate: one external agent can discover a paid tool, settle exactly once, and receive deterministic structured output.

### 3. Directory, job projections, and normal-wallet workflow

- add persistence and migrations;
- index ERC-8004 and ERC-8183;
- implement job APIs and Procurement Lab UI;
- complete the full local job lifecycle with a normal wallet;
- publish/index terminal feedback asynchronously.

Gate: normal-wallet success, rejection, and expiry/refund E2E tests pass before account abstraction is introduced.

### 4. Smart-account and paymaster workflow

- integrate factory/account/bundler/paymaster;
- batch approval plus funding;
- enforce sponsorship policy and fallback;
- add permission module only if audited and compatible;
- add browser E2E and abuse tests.

Gate: sponsored and user-paid flows pass; arbitrary, over-budget, expired, revoked, wrong-target, and wrong-selector UserOperations fail.

### 5. Cross-chain intent workflow

- add intent preview, creation, local solver, destination verification, projections, and UI;
- connect verified destination funding to the existing smart-account escrow flow;
- test partial/failing/expired/tampered/duplicate fills.

Gate: source funds arrive at the destination smart account and subsequently fund the job; any intent failure leaves the job Open and recoverable.

### 6. Orchestration and hardening

- add an isolated Docker Compose profile/file for the two nodes, gateway, facilitator/reference service, bundler/paymaster, solver, web, worker, and PostgreSQL;
- add idempotent deploy/seed/start/smoke/stop commands;
- add correlation and metrics;
- run full regression, security, accessibility, and recovery tests;
- document local runbook, threat model, demo script, limitations, and testnet dry run.

Gate: a clean local environment can run the entire recruiter demo reproducibly with one documented command sequence.

## Mandatory tests

At minimum, cover:

- current repository regression suite;
- ERC-8183 roles, all transitions, invalid transitions, payout/refund, deadlines, fee ceiling, reentrancy, malicious token behavior, and conservation invariants;
- x402 challenge, success, request/payment binding, body tampering, wrong chain/token/amount/payee, replay, timeout, settlement failure, and idempotent response;
- gateway-to-ASP.NET authentication, authorization, and rate limits;
- ERC-8004 metadata validation, SSRF defenses, feedback filtering, revocation, and idempotent publication;
- ERC-4337 simulation, bundler receipts, paymaster quotas, arbitrary-call denial, fallback, and permission revocation;
- ERC-7683 resolver validation, minimum output, maximum input, slippage/fee/deadline, replay, partial/failing fill, wrong destination, and settlement verification;
- event decoding, wrong-chain/contract/token rejection, duplicate logs, confirmations, reorg rollback, and checkpoint recovery;
- EF migrations and uniqueness;
- browser/MCP E2E success, rejection, and expiry paths.

## Required smoke scenario

Automate and document this exact scenario:

1. start PostgreSQL, two local EVM nodes, web, worker, gateway, x402 facilitator/reference setup, bundler/paymaster, and solver;
2. deploy/seed contracts and manifests;
3. seed buyer, provider, evaluator, product, coffee lot, provenance evidence, and test balances;
4. discover the provider through ERC-8004-indexed data;
5. invoke `request_wholesale_quote` and receive 402;
6. settle x402 test USDC and receive the quote once;
7. create the ERC-8183 job using the quote commitment;
8. fulfill an intent from the Arbitrum-like chain to the Base-like smart account;
9. submit a sponsored batched UserOperation approving USDC and funding the escrow;
10. provider submits deliverable evidence;
11. evaluator completes the job;
12. verify provider payout and job event projection;
13. publish/index the ERC-8004 feedback;
14. show correlated identifiers in the Protocol Inspector.

Also automate rejection and expiry/refund variants.

## Security and honesty requirements

- No private keys, seed phrases, unrestricted session authority, API credentials, or secret RPC URLs in source, logs, manifests, screenshots, or browser configuration.
- No client-supplied trusted infrastructure addresses.
- No hidden fees or chain/asset substitutions.
- No x402 fulfillment before verified payment and no duplicate fulfillment on replay.
- No escrow release by anyone except the configured evaluator under the required state.
- No paymaster sponsorship for arbitrary calls.
- No server fetch of arbitrary metadata/deliverable URLs without SSRF controls.
- No reputation aggregation that hides reviewer identity or Sybil limitations.
- No escrow funding until cross-chain destination settlement is independently verified.
- No claim that draft ERCs, local fixtures, or the local solver are production-ready.
- Every protocol has an independent kill switch and safe fallback where applicable.

## Acceptance criteria

- The local smoke scenario and its rejection/refund variants pass.
- Existing checkout, rewards, wallet login, chain selector, staking, and reconciliation behavior still passes.
- x402/MCP uses maintained official middleware with pinned versions.
- ERC-8183 contract tests and conservation invariants pass.
- ERC-4337 uses canonical/pinned infrastructure, safely sponsors the approved batch, and supports user-paid fallback.
- ERC-8004 signals are indexed and displayed with reviewer/evidence context.
- ERC-7683 prototype fulfills the deterministic two-node outcome and is visibly labeled experimental.
- Shared configuration and chain resolution are used throughout; no duplicate chain/wallet abstractions exist.
- Database projections are idempotent, chain-aware, and reorg/reconciliation capable.
- Correlation links x402 payment, proposal, intent, UserOperation, job, and reputation feedback.
- Local deployment is reproducible from pinned dependencies and generated manifests.
- Documentation includes architecture, exact standards/package revisions, local runbook, threat model, demo script, limitations, and recovery.
- No public testnet deployment is broadcast without explicit user authorization.

## Handoff format

When finished, report:

1. concise outcome and demo behavior;
2. files/components added or changed, grouped by gateway, contracts, .NET, worker, persistence, UI, tests, and docs;
3. exact standards, packages, and source revisions pinned;
4. commands run and results, including pre-existing failures;
5. local deployment addresses/manifests and smoke-test output;
6. security decisions, threat-model findings, and remaining risks;
7. draft-standard deviations or fixtures used;
8. public-testnet steps that remain intentionally unexecuted;
9. any blockers with concrete evidence and the safest next action.
