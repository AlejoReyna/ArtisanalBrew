# Solana deployment manifest

Generate a manifest only for a local validator after the program and public fixture accounts have
been created:

```bash
SOLANA_CHAIN_KEY=solana-localnet \
SOLANA_RPC_URL=http://127.0.0.1:8899 \
SOLANA_CLUSTER=localnet \
SOLANA_PROGRAM_ID=<program-id> \
SOLANA_DEPLOYMENT_SLOT=<slot> \
SOLANA_STATE_PDA=<state-pda> \
SOLANA_AUTHORITY_PDA=<authority-pda> \
SOLANA_CAFE_MINT=<cafe-mint> \
SOLANA_STCAFE_MINT=<stcafe-mint> \
SOLANA_COFFEE_MINT=<coffee-mint> \
SOLANA_CAFE_CUSTODY=<cafe-custody> \
SOLANA_COFFEE_CUSTODY=<coffee-custody> \
SOLANA_ADMIN=<administrator> \
SOLANA_TOKEN_PROGRAM=TokenkegQfeZyiNwAJbNbGKPFXCWuBvf9Ss623VQ5DA \
SOLANA_TOKEN_2022_PROGRAM=TokenzQdBNbLqP5VEhdkAS6EPFLC1PHnBqCXEpPxuEb \
scripts/generate-solana-local-manifest.sh
```

The generator supports `localnet` and `testnet`, requires all public addresses, and records SHA-256
checksums for the IDL and program binary. It never accepts or writes a private key. Testnet output
requires `SOLANA_PUBLIC_DEPLOYMENT_CONFIRMED=true`, an HTTPS RPC URL, `solana-testnet` as the chain
key, and a positive deployment slot (local validators may legitimately report slot zero). This acknowledgement creates metadata only; it never deploys
or broadcasts a transaction.

Set `ARTISANALBREW_SOLANA_MANIFEST` to the generated file before starting both Web and Worker.
The shared configuration adapter loads the same addresses into both processes. A validated
manifest replaces the disabled registry placeholder and enables Wallet Standard login, liquid
staking, reward funding, reconciliation, and both shared selector placements. CAFE,
stCAFE, and COFFEE default to nine decimals; the generator rejects values above nine and requires
CAFE and stCAFE to use the same precision.

For Testnet, use `SOLANA_CLUSTER=testnet`, `SOLANA_CHAIN_KEY=solana-testnet`, an HTTPS Testnet RPC,
and set `SOLANA_PUBLIC_DEPLOYMENT_CONFIRMED=true` only after the program and fixtures have been
deployed and the same deposit → fund → claim → redeem smoke scenario has passed against them.

The repair command does not mutate the live cursor unless explicitly requested:

```bash
scripts/solana-repair-backfill.sh \
  --chain solana-localnet \
  --start-slot 100 \
  --end-slot 500 \
  --dry-run
```

Ranges above 100,000 slots additionally require `--allow-large-range`. That acknowledgement is
independent from the more dangerous `--advance-live-cursor` option.
