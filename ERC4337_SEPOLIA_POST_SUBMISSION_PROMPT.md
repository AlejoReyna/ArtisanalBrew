# ArtisanalBrew — Continue ERC-4337 Sepolia After the Confirmed Sponsored Submission

Work in `/Users/alexis/Desktop/tcde/ArtisanalBrew` on branch
`agent/erc4337-sponsored-submission`. Preserve the existing dirty worktree and unrelated
untracked files. Do not reset, clean, switch branches, commit, push, or expose secrets.

## Read first

Before changing anything, inspect:

- `docs/agentic-commerce-stack-plan.md` — its top handoff is the authoritative current record.
- `ERC4337_SEPOLIA_BROADCAST_PROMPT.md` and `SEPOLIA_SELF_HOSTED_PROOF_RUNNER_PROMPT.md` — they
  contain historical context only; do not follow their stale instructions to broadcast again.
- `README.md`, `contracts/evm/package.json`, deployment manifest, relevant source, and tests.

The safe validation baseline is:

```bash
dotnet build -m:1
dotnet test tests/ThisCafeteria.UnitTests -m:1
cd contracts/evm && npm run build
```

## Confirmed public Sepolia outcome — do not replay it

The ERC-4337 sponsored operation is already mined and successful. It used:

- EntryPoint: `0xdd9a61064ef9e2d9612da1f1307e168b85fe43a6`
- Factory: `0x03e558b6af3e871f1884b670bd10d785b414e3fb`
- Verifying paymaster: `0x35409fae884605c1ab9a1dcd561d3cb39da6619f`
- Deployed counterfactual account (salt `1`): `0x8BfC1139736B4b070a8DF903412Beb33C2E6c00c`
- UserOperation hash:
  `0x87d8f80711508c7be740ee136e7909c4449276486321f21dbd221f4efb96c5c0`
- Mined transaction:
  `0xb945492fc894b7a2d9defa7245120fe9b7bf2a9fb83b09de3cf49a4c79dbf5bb`

The canonical EntryPoint emitted a matching `UserOperationEvent` with `success=true` in block
`11340974`. The paymaster received two `0.005 ETH` deposits (a total of `0.01 ETH`) before the
operation; the transaction spent `1642518000000000 wei` for `821259` gas.

**Do not re-run the Sepolia submission script with salt `1`, do not submit another UserOperation,
do not fund the paymaster, and do not deploy replacement contracts.** This task is diagnostic and
implementation work only unless Alexis gives new, explicit authorization for a distinct public
chain transaction.

## Remaining problem to resolve

The operation was accepted and mined by self-hosted Rundler v0.11.0 in safe mode, but after mining
its `eth_getUserOperationReceipt` method returned:

```text
-32603 internal error: rpc provider error
```

The real `UserOperationSubmitter` had successfully sent the UserOperation, but the harness's
receipt poll then ended with a three-second `HttpClient.Timeout` / `ResponseEnded` exception before
it could return `Confirmed`. A direct, read-only call to the same receipt RPC later yielded the
Rundler internal error, while an independent Sepolia `eth_getLogs` query proved the EntryPoint event
above. The harness timeout has already been increased to 20 seconds, and the Sepolia script now
prints the calculated hash before it submits. Those changes improve diagnosability but do not by
themselves explain or repair Rundler's receipt endpoint.

The operational objective is to identify and fix the self-hosted Rundler / upstream-node receipt
failure, then add proportionate regression coverage or a safe fallback in the application code so a
successfully mined UserOperation cannot be misreported as an unhandled transport failure. Do this
without creating another public-chain operation.

## Infrastructure boundaries

The self-hosted runtime is in Azure resource group `thiscafeteria-prod-rg`, VM
`thiscafeteria-sepolia-aa` (West US):

- Geth Sepolia RPC is bound only to `127.0.0.1:8545` on the VM.
- Lighthouse is paired with Geth.
- Rundler v0.11.0 safe mode listens on port `4338` and advertises the current EntryPoint.
- The VM holds valuable synced Sepolia state. Do not delete, redeploy, recreate, resize, or expose
  services publicly without explicit user authorization.

Read-only Azure and VM diagnostics are in scope. You may inspect service status and non-secret
logs. Never use shell tracing, never print or retrieve a secret value, never put a secret in a
command argument or source file, and never commit credentials. Do not open Geth RPC to the public
internet.

## Suggested approach

1. Reproduce only the **read-only** `eth_getUserOperationReceipt` failure for the public hash above.
   Also check its standard upstream RPC dependencies directly from the VM (for example, the mined
   transaction receipt and required logs) to distinguish a Rundler defect from a node/RPC problem.
2. Inspect Rundler configuration and non-secret logs. Confirm it targets the current EntryPoint and
   that Geth/Lighthouse are synchronized and healthy. Prefer a minimal configuration or service fix
   over new infrastructure.
3. Inspect `RundlerBundlerClient` and `UserOperationSubmitter`. If a robust application fallback is
   appropriate, preserve the security rule: confirmation must come from the canonical EntryPoint's
   matching `UserOperationEvent` and the actual transaction receipt, never from the bundler's
   `success` field alone. Do not silently mark uncertain operations as confirmed.
4. Add or update focused unit tests for any production-code behavior you change. Avoid mocking away
   the condition the new logic is meant to handle.
5. Run the validation baseline above and `git diff --check` scoped to your files. Update the top
   handoff in `docs/agentic-commerce-stack-plan.md` with evidence, conclusions, exact commands, and
   any residual limitation.

## Relevant current edits

The current worktree intentionally includes changes to:

- `contracts/evm/deployments/ethereum-sepolia.json` — current redeployed addresses.
- `contracts/evm/scripts/sepolia-bundler-submit-check.ts` — artifact-based read-only ABI lookup,
  `0.01 ETH` paymaster threshold, and calculated UserOperation hash output.
- `tools/ThisCafeteria.CrossStackHarness/Program.cs` — remote-bundler timeout raised from 3 to 20
  seconds.
- `docs/agentic-commerce-stack-plan.md` — confirmed public transaction outcome and known receipt
  endpoint behavior.

These changes are uncommitted. Keep them unless an evidence-based correction is necessary.

## Completion report

Report:

1. Root cause of the receipt failure, with evidence.
2. Exact files changed and why.
3. Whether the public receipt RPC now returns the known receipt; include no secrets.
4. Validation results.
5. Whether any action would require a new authorization before proceeding.
