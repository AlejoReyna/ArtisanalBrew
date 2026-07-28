# ArtisanalBrew — Finish the ERC-4337 Sepolia Sponsored-Submission Proof — Implementation Prompt

You are a senior full-stack, smart-contract, and infrastructure engineer working in the existing
ArtisanalBrew repository. A prior session did essentially all of the work already. Your job is the
last, small, concrete step: run it.

## Mission

Everything needed to submit a sponsored UserOperation through a real bundler and independently
verify it on-chain is built, tested, and proven **against local Hardhat + a local Rundler bundler**.
What's missing is the same proof **against Ethereum Sepolia** — the actual gate this whole effort
was for, because only a real network exercises real bundler safe-mode validation, which nothing
local can. The code is done. Two credentials were the only blocker, and one has already arrived.

**Check out branch `agent/erc4337-sponsored-submission` first** (currently local-only, 9 commits
ahead of `origin/main`, never pushed). Do not start from `main` and do not redo any of this work —
read the commits (`git log origin/main..agent/erc4337-sponsored-submission`) and
[`docs/agentic-commerce-stack-plan.md`](docs/agentic-commerce-stack-plan.md)'s top section
("Session handoff (2026-07-22) — ERC-4337 sponsored submission") before touching anything.

## Verified ground truth (checked directly, not taken on faith)

**Already implemented and proven live, on `agent/erc4337-sponsored-submission`:**
- `IUserOperationSubmitter` / `UserOperationSubmitter`
  (`src/ThisCafeteria.Infrastructure/Services/UserOperationSubmitter.cs`) submits a signed,
  sponsored UserOperation through `IBundlerClient` (never `EntryPoint.handleOps` directly), polls
  for a receipt, and independently verifies the mined result by decoding the EntryPoint's own
  `UserOperationEvent` — not just trusting `receipt.success`.
- `RundlerBundlerClient` (`src/ThisCafeteria.Infrastructure/Services/RundlerBundlerClient.cs`) — a
  correct v0.7 JSON-RPC transport. Three real RPC-shape bugs were found and fixed by running it
  against a live bundler for the first time (packed-vs-unpacked gas fields, non-minimal hex
  quantities, truncated `paymasterData`); each has a regression test.
- **Local proof**: `contracts/evm/scripts/crossstack-bundler-submit-check.ts` — real `.NET` code
  submits a sponsored UserOperation through a real, locally-running Rundler v0.11.0 bundler, gets it
  mined, and independently verifies it. Re-run and passing as of the last session.
- **Negative-path proof**: `contracts/evm/scripts/crossstack-bundler-submit-denied-check.ts` — proves
  a denied sponsorship never reaches the bundler at all, through the real production code (not
  mocked tests).
- 240 unit tests pass (`dotnet test tests/ThisCafeteria.UnitTests`).
- `BlockchainManifestLoader.ApplyBundlerRpcUrlOverrides` — reads a per-chain bundler URL from
  `ARTISANALBREW_BUNDLER_RPC_URL__{CHAIN_KEY}`, environment-only, never a committed manifest (a
  hosted bundler's URL embeds a live API key).
- `ThisCafeteria.CrossStackHarness` no longer hardcodes the Hardhat dev owner/signer/chain-id —
  parametrized via `CROSSSTACK_OWNER_ADDRESS` / `CROSSSTACK_VERIFYING_SIGNER_KEY` /
  `CROSSSTACK_EVM_CHAIN_ID`, defaulting to the old local values (no regression).

**Written and ready, never executed:**
- `contracts/evm/scripts/sepolia-bundler-submit-check.ts` — the actual Sepolia proof, same code
  path as the local one. It has a hard authorization gate
  (`SEPOLIA_BROADCAST_AUTHORIZED=yes`) — without it, it only runs read-only checks and exits 0
  without broadcasting anything. Read the file's own header comment before running it; it documents
  every required environment variable.

**Read-only reconnaissance already performed against live Sepolia** (no signing, no broadcast — safe
to re-run or trust as still current, but re-verify if much time has passed):
- Chain ID confirmed `11155111`.
- `EntryPoint` (`deployments/ethereum-sepolia.json`'s `entryPoint`) has real deployed bytecode.
- The paymaster's EntryPoint deposit was **`0`** — it needs a small funding transaction
  (`entryPoint.depositTo(paymaster)`) before any sponsorship can succeed. The Sepolia proof script
  does this automatically when authorized, only if the deposit is still below a small threshold.
- The deployer address (`deployments/ethereum-sepolia.json`'s `admin`, `0x9d5305a9621aafb5b5f8ba7a9977e3d96ea7eceb`)
  held **~0.082 Sepolia ETH** — comfortably enough for the deposit and gas.
- Confirmed by reading `contracts/evm/scripts/deploy.ts` directly (not assumed): the deployed
  paymaster's trusted verifying signer **is that same deployer account** —
  `verifyingPaymaster = await viem.deployContract("CanonicalVerifyingPaymaster", [entryPoint.address, admin])`
  where `admin = deployer.account.address`. So exactly one wallet does triple duty: smart-account
  owner, deposit funder, and paymaster verifying signer. There is no separate hidden credential.

## What Alexis has already provided this session — use it, don't re-ask

- **Pimlico API key**: `pim_HJLbBg1H5x7H5WtKCaZmyh`. Alexis explicitly said exposure doesn't matter
  ("it's just sepolia tokens... handle it for me"). The bundler URL is
  `https://api.pimlico.io/v2/sepolia/rpc?apikey=pim_HJLbBg1H5x7H5WtKCaZmyh`.
  **Do not commit this key anywhere** (not `deployments/ethereum-sepolia.json`, not any tracked
  file) — set it as `SEPOLIA_BUNDLER_RPC_URL` (for the proof script) and, if you also want the
  running Web app to pick it up, `ARTISANALBREW_BUNDLER_RPC_URL__ETHEREUM_SEPOLIA` (both
  environment-variable-only). This prompt file itself is untracked/local-only by this repo's own
  convention (see the other loose `*_PROMPT.md` files at the repo root) — keep it that way, don't
  `git add` it either.
- **Broadcast authorization**: Alexis said, verbatim, "handle it for me pls" after being told
  exactly what broadcasting would do (real, if valueless, Sepolia transactions from the existing
  deployer wallet). Treat that as the explicit authorization this project's rules require — you do
  not need to ask again for a general "may I broadcast" — **but the specific irreversible actions
  the script performs (funding the paymaster deposit, submitting the UserOperation) are still real,
  so don't be reckless: run it once, read its output, don't loop it, and if anything about the
  on-chain state has changed since the read-only reconnaissance above, re-check before spending.**

**Still missing, and this really is the only remaining blocker**:
`ETHEREUM_SEPOLIA_DEPLOYER_PRIVATE_KEY`. It has never been present anywhere in this environment —
checked the shell environment and both `.env` files (by grepping for the key name only, never
printing values) and confirmed it's absent. Alexis has not sent it yet as of this handoff. **Ask for
it directly and concretely** (the prior session's phrasing that worked: ask for exactly the one
variable name, offer the safer path of `export`ing it directly in Alexis's own terminal via the `!`
prefix so it never touches chat, but accept a pasted value too if offered — Alexis has shown
willingness to just paste secrets to move faster and get visibly frustrated by over-explaining or
repeating the same request multiple times without new information). **Do not fabricate, guess, or
reuse an unrelated key for this.**

## Non-negotiable rules (unchanged from the original task)

- Never implement EntryPoint, a bundler, signature cryptography, or private-key custody from
  scratch. Everything needed already exists — use it.
- Never accept RPC/bundler URLs, EntryPoint, or paymaster addresses from the client.
- Never commit a bundler API key or RPC secret to any tracked file.
- The gate is a real bundler mining a real sponsored operation on Sepolia, verified server-side by
  this app's own code (not `receipt.success` alone) — a local Hardhat pass is not sufficient and was
  already achieved last session; don't stop there again.

## Recommended sequence

1. `git checkout agent/erc4337-sponsored-submission`. Confirm `dotnet build` and
   `dotnet test tests/ThisCafeteria.UnitTests` are still clean (240 passing) before doing anything
   else — re-verify, don't assume a prior session's claims are still true.
2. Get `ETHEREUM_SEPOLIA_DEPLOYER_PRIVATE_KEY` from Alexis (see above — this is the one real gap).
3. Run:
   ```
   ETHEREUM_SEPOLIA_DEPLOYER_PRIVATE_KEY=<from Alexis> \
   SEPOLIA_BUNDLER_RPC_URL=https://api.pimlico.io/v2/sepolia/rpc?apikey=pim_HJLbBg1H5x7H5WtKCaZmyh \
   SEPOLIA_BROADCAST_AUTHORIZED=yes \
   HARDHAT_NETWORK=ethereumSepolia npx tsx scripts/sepolia-bundler-submit-check.ts
   ```
   from `contracts/evm/`. Read its output as it runs — it prints read-only checks first, then what
   it's about to do, before broadcasting anything.
4. On success, it prints `SEPOLIA_BUNDLER_SUBMIT_RESULT=PASS`, a `userOpHash`, a transaction hash,
   and an Etherscan link. Record all three in your completion report.
5. On failure, do not retry blindly — read the actual error. If it's an `AA3x`/`AA9x` paymaster or
   EntryPoint validation error, that's the real contract telling you something specific; if it's a
   bundler RPC schema error, compare against what `RundlerBundlerClient` already learned the hard
   way for Rundler (Pimlico's schema is the same ERC-4337 bundler-spec shape, but don't assume every
   field-level detail transfers without checking).

## Completion report expected

Same shape as this repo's other agent handoffs:
1. What was run and its exact result.
2. The public Sepolia UserOperation hash and transaction hash (or, if it failed, the exact error and
   your diagnosis — not a guess).
3. Whether the paymaster needed funding, and how much was actually sent.
4. Anything about Pimlico's bundler behavior that differed from Rundler's, worth recording for
   next time.
5. Update `docs/agentic-commerce-stack-plan.md`'s top handoff section with the real outcome — this
   repo's own convention, and the next agent after you will trust it the same way you're trusting
   this one.
