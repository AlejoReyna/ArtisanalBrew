# Self-hosted Sepolia Rundler

This runs a private Rundler v0.11.0 instance for the existing ArtisanalBrew Sepolia EntryPoint
`0x7d75859d1e2be07b0c18c0ef3dd062b69bcc4217`. It does not deploy or replace any contracts.

## Required secrets

- `SEPOLIA_BUNDLER_NODE_RPC_URL`: a Sepolia endpoint supporting `debug_traceCall` with custom
  JavaScript tracers. Rundler safe mode needs this; a standard public RPC endpoint is insufficient.
- `SEPOLIA_BUNDLER_SIGNER_PRIVATE_KEY`: a new, dedicated Sepolia-funded beneficiary key. Do not
  reuse `ETHEREUM_SEPOLIA_DEPLOYER_PRIVATE_KEY`.

## Deployment

Build `contracts/evm/rundler/Dockerfile` with `contracts/evm` as its context and push it as
`thiscafeteria-rundler:latest` to the existing ACR. Then deploy Bicep with
`enableSepoliaBundler=true`. The Container App has internal-only ingress, one always-on replica,
and reads its RPC URL and signer from Key Vault.

Run the final proof from a one-off workload inside the same Container Apps environment using the
internal Rundler FQDN. Do not make this unauthenticated RPC public merely to run the proof.
