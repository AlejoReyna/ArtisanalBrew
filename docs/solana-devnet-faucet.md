# Solana Devnet CAFE Faucet

The Solana faucet is a **server-side authorized mint**: the application mints CAFE to the claimant's
associated token account under the CAFE mint authority. Unlike the EVM faucet (a contract the wallet
calls client-side), the wallet never signs — the server does — so the mint-authority secret and the
cooldown live on the server.

On-chain facts (Solana Devnet, `deployments/solana-devnet.json`):

- CAFE mint `C7g7g34QzvmAiP4HMmdjWLgfV9Y8FSF4GcAXK97HLQEg` is a **Token-2022** mint.
- Its mint authority is the manifest `administrator` `D5iN8pAhbzXN9Duq75GsKo6VPHwWzBbtYc1s5KH2CJGA`.

## Components

- `SolanaTransactionBuilder` — hand-rolled ATA derivation, Token-2022 `MintToChecked` + ATA
  create-idempotent instructions, legacy message compilation, and Ed25519 signing (no Solnet
  dependency). Validated by unit tests against the live custody ATAs and a read-only devnet
  `simulateTransaction` round-trip.
- `SolanaFaucetSecret` — loads and validates the mint-authority key (fail-closed).
- `SolanaFaucetService` / `SolanaFaucetController` (`/faucet/api/solana`) — status + claim, cooldown
  enforced via the `SolanaFaucetClaim` table.
- `YieldPanel.razor` — un-gated Solana faucet UI (a server `@onclick` claim, not a client contract call).

## Configuration

Policy (non-secret) in `appsettings.json`:

```json
"Blockchain": { "SolanaFaucet": { "ClaimAmount": 100, "CooldownSeconds": 86400 } }
```

Secret (never in a manifest or appsettings) — the CAFE mint-authority keypair, in the Solana CLI
keypair format (a JSON array of 64 bytes) or base58, in an environment variable:

```
ARTISANALBREW_SOLANA_ADMIN_KEY=[12,34,...]   # the D5iN8… administrator secret key
```

The service refuses to operate unless the key's derived public key equals the manifest administrator.

## Enablement runbook (operator-gated — requires a funded devnet broadcast)

The capability is **off** until every step below is done, per the safety rule that no capability flag is
`true` without a working, verified flow behind it.

### 1. Provision the mint-authority secret

You need the secret key for the administrator `D5iN8pAhbzXN9Duq75GsKo6VPHwWzBbtYc1s5KH2CJGA` — the same
keypair used as `payer`/`SOLANA_WALLET` in `contracts/solana/scripts/deploy-public.ts`. Confirm it:

```bash
solana-keygen pubkey /path/to/admin-keypair.json
# must print: D5iN8pAhbzXN9Duq75GsKo6VPHwWzBbtYc1s5KH2CJGA
```

Expose it to the Web process (the keypair file is already a JSON byte array — exactly the accepted
format). Either export it, or add the line to a repo-root `.env` (loaded by `LocalDotEnvLoader`):

```bash
export ARTISANALBREW_SOLANA_ADMIN_KEY="$(cat /path/to/admin-keypair.json)"
```

### 2. Fund the administrator with devnet SOL (for ATA rent + fees)

```bash
solana balance   D5iN8pAhbzXN9Duq75GsKo6VPHwWzBbtYc1s5KH2CJGA --url devnet
solana airdrop 2 D5iN8pAhbzXN9Duq75GsKo6VPHwWzBbtYc1s5KH2CJGA --url devnet   # if low
```

### 3. Turn the flag on locally to test

The faucet UI/API only appear when the chain reports the capability, so flip it to test (keep it if the
claim works). In `deployments/solana-devnet.json`, add to `capabilities`:

```json
"capabilities": { "walletLogin": true, "liquidStaking": true, "rewardFunding": true, "reconciliation": true, "faucet": true }
```

If the manifest `rpcUrl` (`https://devnet.helius-rpc.com/`) needs an API key, either add one or point the
server at the keyless public endpoint by setting the chain's `ServerRpcUrl` / manifest `rpcUrl` to
`https://api.devnet.solana.com`.

### 4. Run the app and claim

Postgres must be running (the faucet service and its `SolanaFaucetClaim` table need the DB; the
`AddSolanaFaucetClaims` migration is applied automatically on startup).

```bash
dotnet run --project src/ThisCafeteria.Web
```

In the browser: connect a Solana wallet set to **Devnet** (e.g. Phantom) → select **Solana Devnet** →
open **Staking** → **Claim CAFE**. Confirm the CAFE balance credits and the explorer link resolves to a
finalized transaction.

### 5. Lock it in

Keep the `faucet: true` flag committed, and update the README network table to move "CAFE faucet" out of
Solana Devnet's "Not yet enabled" column.

### Fast pre-check (no wallet, no DB, no secret)

To confirm the mint still works on-chain before wiring the wallet, the read-only devnet simulation used
during development can be re-run: build the create-ATA + `MintToChecked` message with
`SolanaTransactionBuilder` and POST `simulateTransaction` with `sigVerify:false,
replaceRecentBlockhash:true` to `https://api.devnet.solana.com`. `err: null` and a `postTokenBalances`
credit means the serializer + mint authority are still valid.
