---
name: capability-gate
description: Add, enable, or audit a chain capability flag in ArtisanalBrew's manifest-gated registry — the rule that a feature stays invisible until a committed deployment manifest proves the contracts behind it exist. Use when the user wants to turn on a capability (marketplacePayment, legacyExit, agenticCommerce, faucet, liquidStaking, agenticSessionPayments), add a new capability flag, promote a chain from disabled to enabled, wire a new deployment manifest, or asks why a network or feature isn't showing up in the chain selector or /api/chains. Also use when reviewing a diff that touches BlockchainManifestLoader, ChainRegistry.Validate, ChainDefinition/ChainCapabilities, ChainsController, or anything under deployments/.
---

# Capability gating: a feature flag that cannot lie

Most feature-flag systems answer "is this on?" ArtisanalBrew's answers a harder
question: **"is this on, and is the thing it promises actually deployed?"**

The distinction matters because the flags here don't guard a UI variant, they
guard money. `marketplacePayment` turned on for a chain with no escrow contract
doesn't render a broken button — it renders a working button that takes a user's
funds to an address that is the empty string. The usual flag lifecycle (flip it
on, deploy the backend soon after) has a window where the claim is false. This
system removes the window by making the claim and the proof the same artifact.

The rule, stated once:

> A capability is a **claim about deployed infrastructure**. The claim lives in a
> committed deployment manifest. The manifest is rejected at construction time if
> the claim isn't backed by the addresses it requires. There is no code path that
> enables a capability without its deployment.

Everything below is how that rule is enforced, and how to work inside it.

## The path a capability travels

Five hops, in order. A capability that stops at any hop is invisible, and that is
the intended failure mode — **not** a bug to route around.

| # | Hop | File | What it does |
|---|---|---|---|
| 1 | Manifest | `deployments/*.json` | Declares `capabilities` + the addresses backing them |
| 2 | Loader | [BlockchainManifestLoader.cs](../../../src/ThisCafeteria.Application/Configuration/BlockchainManifestLoader.cs) | Reads the manifest, **replaces** the built-in chain definition |
| 3 | Validator | [ChainRegistry.Validate](../../../src/ThisCafeteria.Application/Configuration/IChainRegistry.cs#L33) | Throws if a flag is on without its deployment |
| 4 | API | [ChainsController.cs](../../../src/ThisCafeteria.Web/Controllers/ChainsController.cs) | Filters `.Where(chain => chain.Enabled)` |
| 5 | UI | `ChainSelector.razor`, login pill, staking sidebar | Renders only what the API returned |

Two properties fall out of this shape, and both are load-bearing:

**Replace, don't merge.** `Replace()` removes the built-in definition by key and
appends the manifest's, so a manifest is a *whole* chain definition, never a
patch. There is no way to inherit `Enabled = true` from a default while supplying
a partial deployment — you supply everything or you get the disabled built-in.
The registry ships nine chains disabled by default; three have manifests.

**Fail closed at construction.** `Validate` runs in the `ChainRegistry`
constructor, which runs at DI container build. An invalid manifest doesn't
degrade a feature, it refuses to start the application. Nothing half-configured
ever serves a request.

## The validation rules as they stand

Read these before adding a flag, because a new capability without a matching rule
is the one way to defeat the whole design.

```
LiquidStaking (EVM)      → requires Deployment.LiquidVault
LiquidStaking (Solana)   → requires Deployment.Program
LegacyExit               → requires Deployment.LegacyPool
MarketplacePayment (EVM) → requires AgenticEscrow OR LegacyPool
Faucet (Solana)          → requires Deployment.Cafe AND Deployment.Admin
```

Enabled Solana chains with `LiquidStaking` get a second tier of checks:
every deployment key must parse as a public key, `TokenProgram` /
`Token2022Program` must equal the canonical SPL program IDs, and
`StCafeDecimals` must equal `CafeDecimals` (both ≤ 9).

Note the shape of the `MarketplacePayment` rule. It's an **OR**, because there
are genuinely two settlement venues — the liquid rail settles through the
ERC-8183 escrow, the legacy Sepolia rail verifies a native transfer against the
legacy pool. This is the right way to relax a rule: name the alternative venue
explicitly. It is not the same as dropping the requirement, and a reviewer can
tell the difference at a glance. Prefer this over a boolean escape hatch.

Also note what is *not* manifest-driven: for EVM chains the loader hardcodes
`WalletLogin`, `LiquidStaking`, `Faucet`, and `RewardMinting` to `true`. Those
four are assumed of any EVM deployment. Only `agenticCommerce`,
`agenticSessionPayments`, `marketplacePayment`, and `legacyExit` are read from
the manifest. If you're adding a flag that isn't universally true of a chain
family, it belongs in the manifest-read set.

## Enabling an existing capability

Do these in order. Do not skip to step 3 to "see if it works" — a manifest edit
without the deployment is exactly the lie the design exists to prevent.

1. **Deploy the contract** the capability requires (see the table above) and get
   its address. No address, no flag. This step is not optional and not
   parallelizable with the others.
2. **Add the address** to the manifest's `addresses` object (EVM) or top level
   (Solana), using the key the loader reads — check `TryReadEvm` /
   `TryReadSolana` for the exact spelling rather than guessing.
3. **Set the flag** to `true` in the manifest's `capabilities` object.
4. **Verify it fails closed first.** Temporarily blank the address, start the
   app, and confirm you get the `InvalidOperationException` naming your chain and
   capability. Restore the address. This is a 30-second check that proves the
   gate is actually wired to your new flag rather than silently absent — do it
   every time, it has caught real gaps.
5. **Start the app and hit `/api/chains`.** The capability should appear in that
   chain's `capabilities` object. If the chain itself is missing, it's `Enabled
   == false` — a different problem, see below.
6. **Update the README capability table.** It has a per-chain "Enabled
   capabilities" / "Not yet enabled" split that goes stale silently. It is the
   first thing a reader trusts and the last thing anyone remembers to edit.

## Adding a new capability flag

1. Add the property to `ChainCapabilities`.
2. Read it in the loader with `ReadCapabilityFlag(root, "yourFlag")` — defaults
   to `false`, which is the correct default for a claim nobody has made yet.
3. **Add a `Validate` rule naming the deployment address it requires.** If you
   cannot name one, stop and reconsider: a capability with nothing deployable
   behind it is a UI preference, and it belongs in configuration, not here.
4. If the flag must reach the browser, confirm it flows through
   `ChainsController` — `capabilities = chain.Capabilities` serializes the whole
   object, so new properties ride along automatically. That convenience cuts both
   ways: see the secrets rule below.
5. Add a `ChainRegistryTests` case that asserts the flag **throws** without its
   deployment. Testing the happy path proves nothing here; the gate is the
   feature, so the negative test *is* the test.

## Secrets and the public surface

`capabilities` and `deployments` are serialized wholesale to `/api/chains`, which
is unauthenticated. Two consequences:

- **Manifests carry addresses and checksums only.** Never a private key, seed
  phrase, or API-keyed URL. Manifests are committed to git; anything in one is
  public forever, including after a later "removal" commit.
- **Endpoints with embedded credentials use env-var overrides instead.**
  `BundlerRpcUrl` is the worked example: hosted bundler URLs embed an API key, so
  it's sourced from `ARTISANALBREW_BUNDLER_RPC_URL__{CHAIN_KEY}` (upper-cased,
  hyphens → underscores) via `ApplyBundlerRpcUrlOverrides`, applied *after*
  manifest load so it wins, and deliberately excluded from the `ChainsController`
  projection. Copy this pattern for any new credentialed field, and copy the
  exclusion too — a field added to `ChainDefinition` is private only for as long
  as nobody adds it to the controller's anonymous object.

## Diagnosing "my chain/feature isn't showing up"

Walk the five hops in order and stop at the first that fails. Nine times in ten
it's hop 1 or 2, and the answer is that the system is working.

- **App won't start, `InvalidOperationException` about a chain** → hop 3. The
  message names the chain and the missing deployment. This is the gate doing its
  job; supply the address rather than relaxing the rule.
- **Chain absent from `/api/chains`** → hop 2 or 4. Either no manifest was loaded
  (the built-in definition is `Enabled = false`), or the path never reached the
  loader. Check `Blockchain:LocalEvmManifest` and
  `Blockchain:SolanaDeploymentManifest` in the Web **and** Worker `appsettings`.
  Both must be wired independently — they're separate processes with separate
  configuration, and a chain live in Web but absent in Worker means
  reconciliation silently skips it.
- **Chain present, capability `false`** → hop 1. The manifest doesn't declare it,
  or declares it under a different key than the loader reads.
- **Multiple EVM chains, only one loads** → `LocalEvmManifest` takes a
  **semicolon-separated** list of paths. One path loads one chain.
- **Everything's right and it still doesn't render** → hop 5, and now it's a UI
  bug rather than a gating one.

## What not to do

- **Don't add a bypass.** No `ignoreValidation`, no environment check that skips
  `Validate` in Development. The gate's whole value is that it has no exceptions;
  one exception and every reader has to check whether it applied.
- **Don't move the trust decision to the browser.** The server resolves addresses
  from the registry. A client-supplied address is not a shortcut, it's the
  vulnerability class this architecture is built against.
- **Don't enable a capability to unblock a demo.** It will be true in the demo and
  false in the README, and the README is what people read. If you need a
  demo path, deploy to a local chain and write a local manifest —
  `scripts/generate-solana-local-manifest.sh` exists for exactly this.
- **Don't mark a capability done in docs without running its proof.**
  `run-acceptance.sh` and the docs under `docs/` are the evidence trail; the plan
  documents have historically both over- and under-claimed status.

## Transferring this pattern

Outside this repo, the same shape applies wherever a flag promises something
external exists — a feature needing a migration, a region needing a provisioned
bucket, a tier needing a configured billing plan. The four moving parts:

1. The flag and the proof of backing live in **one committed artifact**.
2. Validation runs at **startup**, not at first use, so failure is loud and early.
3. The default is **off**, and turning it on is impossible without the proof.
4. There is **no bypass** — the value is entirely in the absence of exceptions.

The version of this most teams have is a config flag plus a runbook step saying
"make sure the bucket exists." That step is skipped exactly once, in a hurry, and
it's someone's bad afternoon. Making it a startup assertion costs an hour.
