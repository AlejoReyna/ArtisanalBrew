# Multichain liquid-staking operations

## Local EVM

From `contracts/evm`:

```sh
npm ci
npm run build
npm run test
XDG_CONFIG_HOME=/private/tmp/artisanalbrew-hardhat-config npm run deploy:ephemeral
npm run export:abi
```

The ephemeral deployment uses chain ID `31337` and writes
`contracts/evm/deployments/evm-local.json`. It prints only public contract
addresses. `deploy:local` targets a separately started local node; local node
output must not be captured in logs that expose its development keys.

The deployment script refuses non-local networks unless
`CONFIRM_PUBLIC_DEPLOYMENT=I_UNDERSTAND_THIS_BROADCASTS` is explicitly set.
Sepolia CAFE and COFFEE can be supplied with `CAFE_ADDRESS` and
`COFFEE_ADDRESS`; the script never replaces those legacy tokens.

## BSC Testnet EVM

BSC Testnet uses chain ID `97` and the `bscTestnet` Hardhat target. The deployer
must hold tBNB and its private key is supplied only through the local shell
environment; it must never be committed or pasted into chat:

```sh
cd contracts/evm
export BSC_TESTNET_RPC_URL='https://97.rpc.thirdweb.com'
export BSC_DEPLOYER_PRIVATE_KEY='0x...'
export CONFIRM_PUBLIC_DEPLOYMENT=I_UNDERSTAND_THIS_BROADCASTS
export PUBLIC_RPC_URL="$BSC_TESTNET_RPC_URL"
npm run build
npm run test
npm run deploy:bsc-testnet
```

The command deploys fresh CAFE, COFFEE, liquid-vault, and faucet contracts and
writes `contracts/evm/deployments/bsc-testnet.json`. Review the addresses and
run the deposit → reward funding → claim → redeem smoke flow before enabling
the chain in the application. Load the reviewed manifest into both Web and
Worker with:

```sh
export ARTISANALBREW_EVM_MANIFEST="$PWD/deployments/bsc-testnet.json"
```

The application loader accepts only chain ID 97 for a `bsc-testnet` manifest,
so an incomplete or mismatched deployment remains disabled rather than being
advertised by the selectors.

## Local Solana

Install the versions pinned in `contracts/solana/Anchor.toml`, start a local
validator, and run `anchor test`. The smoke suite can persist its exact public
fixture addresses by setting `SOLANA_FIXTURE_OUTPUT` to a temporary manifest
path. `transfer_st_cafe` is the only supported
receipt movement path because it checkpoints both reward identities before the
Token-2022 transfer. Solana Testnet remains hidden until a separately authorized
deployment passes the same gate and its validated manifest is supplied.

## Web and worker

1. Start PostgreSQL and set `ConnectionStrings__DefaultConnection`.
2. Apply migrations with `dotnet ef database update` from the web startup project.
3. Start `dotnet run --project src/ThisCafeteria.Web`.
4. Start `dotnet run --project src/ThisCafeteria.Worker`.

The public chain registry is code-backed when `Blockchain:Chains` is absent and
registers the nine requested networks. Selectors and `GET /api/chains` expose
only entries whose deployment gate sets `Enabled=true`; unfinished connections
stay hidden. Private server RPC overrides belong in
environment configuration and are never returned by `GET /api/chains`.

The public entries are `ethereum-sepolia`, `hedera-testnet`, `avalanche-fuji`,
`linea-sepolia`, `base-sepolia`, `bsc-testnet`, `monad-testnet`,
`arbitrum-sepolia`, and `solana-testnet`. Only Ethereum Sepolia has legacy
exit, faucet, marketplace, and reward-minting capabilities in the baseline
registry. Liquid staking is disabled on every undeployed public entry. A
validated Solana localnet or Testnet manifest replaces its matching placeholder
and enables the complete Wallet Standard and liquid-staking path.

To load the authorized local EVM manifest without changing production
configuration, set either:

```sh
export ARTISANALBREW_EVM_MANIFEST="$PWD/contracts/evm/deployments/evm-local.json"
```

or `Blockchain:LocalEvmManifest`. This adds `evm-local` with chain ID `31337`
and liquid-vault/faucet capabilities. The server uses the manifest addresses;
the browser receives only public chain metadata.

Ethereum Sepolia keeps its existing CAFE, COFFEE, and legacy pool addresses as
read/claim/exit migration references. New legacy deposits are not enabled by
the capability model. The new vault is a separate deployment and is not an
in-place upgrade of the unverified legacy pool.
