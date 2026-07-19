# Local Solana protocol

This is the separate Anchor implementation of the CAFE liquid vault. `stCAFE`
movement must use `transfer_st_cafe`, which checkpoints both identities before
the Token-2022 transfer. The vault PDA is both mint and freeze authority;
stCAFE accounts remain frozen between vault instructions, so direct raw
Token-2022 transfers fail on-chain instead of bypassing reward checkpoints.

The pinned Anchor/Rust toolchain is declared in `Anchor.toml` and `Cargo.toml`.
Run `solana-test-validator`, deploy with the loader-v4 command below, and then
run the configured test suite with `anchor test --skip-build --skip-deploy
--skip-local-validator`. Public Solana Testnet staking remains disabled until
a separately authorized deployment and smoke test.

For Solana CLI 2.2.1 local validators, deploy with the loader-v4 command when
loader-v3 reports that new programs are disabled:

```text
solana program-v4 deploy target/deploy/cafe_liquid_staking.so \
  --program-keypair target/deploy/cafe_liquid_staking-keypair.json \
  --keypair "$ANCHOR_WALLET" --url localhost --use-rpc
```

The JavaScript smoke test uses `npm test` with `ANCHOR_PROVIDER_URL` and
`ANCHOR_WALLET` set to the local validator and local test keypair.

The vault uses Token-2022-compatible interfaces and requires the stCAFE mint
authority to be the vault PDA. Create that mint before `initialize`; the
program will not take custody of an arbitrary mint. CAFE and COFFEE custody
accounts must be owned by the vault PDA. Each wallet receives a position PDA
that stores shares, reward debt, and claimable COFFEE.

Local CAFE, stCAFE, and COFFEE mints use nine decimals. SPL token quantities
are `u64`, so EVM-style 18-decimal fixtures would cap the representable whole-
token supply at roughly 18.44 tokens. Application code reads and validates mint
decimals from RPC and the shared deployment manifest instead of assuming EVM
precision.

On macOS the installed Rust, Anchor, and Solana binaries are loaded by the login
shell. If a non-login runner cannot find them, invoke verification with
`zsh -lic 'cargo test'` or source `$HOME/.cargo/env`; do not report the toolchain
as unavailable before checking the login-shell PATH.

The lockfile pins older transitive crates where necessary because the Solana
2.2.1 SBF compiler ships Rust 1.79/1.84-era tooling. Do not regenerate it
without reviewing the SBF compiler compatibility.
