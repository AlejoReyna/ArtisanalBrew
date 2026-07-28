# ArtisanalBrew — Prepare the Self-Hosted Sepolia ERC-4337 Proof Runner

Work in `/Users/alexis/Desktop/tcde/ArtisanalBrew` on branch
`agent/erc4337-sponsored-submission`. Do not switch branches, reset changes, commit secrets, or
touch unrelated untracked files.

## Goal

Prepare a secure, VM-local way to run the repository-approved sponsored-submission proof as soon
as the self-hosted Sepolia node reports `eth_syncing: false`. This task is preparation only.
**Do not broadcast, fund the paymaster, submit a UserOperation, deploy contracts, or retry any
public-chain operation.**

The final operation must remain the repository-approved command path rooted in:

```text
contracts/evm/scripts/sepolia-bundler-submit-check.ts
```

The script has a hard authorization gate and its read-only ABI lookup was deliberately corrected to
load compiled artifacts rather than calling `deployContract` before authorization. Preserve that
fix exactly.

## Current public Sepolia deployment

The original stack was replaced after Pimlico rejected its custom EntryPoint. Use only this current
manifest state:

- EntryPoint: `0xdd9a61064ef9e2d9612da1f1307e168b85fe43a6`
- Factory: `0x03e558b6af3e871f1884b670bd10d785b414e3fb`
- Verifying paymaster: `0x35409fae884605c1ab9a1dcd561d3cb39da6619f`
- Legacy CAFE: `0x15DbED39271D2788a9Be63ffB34C8E2DdED8754A`
- Legacy COFFEE: `0x4056E7F5FD1584C3db6223c9483761Dcb30Bf21C`

The authoritative local record is
`contracts/evm/deployments/ethereum-sepolia.json`.

## Azure runtime already exists

Azure resource group: `thiscafeteria-prod-rg`.

VM: `thiscafeteria-sepolia-aa` (West US). It has a 1 TB persistent disk and these running
containers:

- Geth Sepolia execution client, RPC bound to VM localhost `127.0.0.1:8545` with `debug` API.
- Lighthouse Sepolia beacon node connected to Geth via authenticated Engine API.
- Rundler v0.11.0 in safe mode on port `4338`, configured for the current EntryPoint above.

The VM's public bundler port is locked to Alexis's current public IP. Do not open it broadly or
expose Geth RPC publicly. Rundler already returns the current EntryPoint from
`eth_supportedEntryPoints`.

Geth is still syncing. Treat any non-false `eth_syncing` response as a hard stop.

## Secrets and security

- Secrets live in Azure Key Vault `tc-kv-3m7beebrmubaa`.
- A previous Azure Run Command invocation accidentally logged a testnet signer in diagnostic output.
  Do not print, inspect, echo, trace (`set -x`), place in command arguments, or copy any secret.
- Do not call `az keyvault secret show --query value`.
- Do not write a private key to a repository file, VM log, Docker command line, or final report.
- A Key Vault-backed runner may use an environment file with permissions `0600`, but must remove it
  on both success and failure. Prefer secret injection mechanisms that cannot appear in process
  listings or command diagnostics.

## Deliverable

Implement the smallest safe runner/deployment helper or documented procedure needed to execute the
existing proof script on the VM after sync completes. It must:

1. Refuse to continue while `eth_syncing` is not `false`.
2. Perform the script's normal read-only checks before authorization.
3. Keep `SEPOLIA_BROADCAST_AUTHORIZED` unset during this preparation task.
4. Use the self-hosted Rundler endpoint, not Pimlico.
5. Make no public-chain writes during this task.
6. Include a verification command that confirms the remote bundler advertises the expected EntryPoint.

Run only safe validation appropriate to your edits. Update only relevant handoff documentation if
you make material changes, and report exact files changed plus the command that should be run once
an operator explicitly authorizes the final broadcast.
