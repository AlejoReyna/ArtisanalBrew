# ArtisanalBrew

**ArtisanalBrew** is an ASP.NET reconstruction of [thisCafeteriaDoesntExist](https://github.com/AlejoReyna/thisCafeteriaDoesntExist), the original first version and idea of the coffee storefront. It is prepared as a clean .NET 10 solution with Blazor, ASP.NET Identity, PostgreSQL, Entity Framework Core, Docker, and a fully containerized Azure deployment (Container Apps, Postgres Flexible Server, Blob Storage, Service Bus, Key Vault, Communication Services Email).

## Live Demo

**[cafe.alexisreyna.dev](https://cafe.alexisreyna.dev)** — click through to see it running live on Azure Container Apps.

## Tech Stack

- .NET 10 LTS and ASP.NET Core
- Blazor Web App with interactive server rendering
- Clean Architecture: Web, Application, Domain, Infrastructure, Worker
- Entity Framework Core with PostgreSQL
- ASP.NET Core Identity
- Serilog
- Nethereum, Solana Wallet Standard, Anchor 0.31.1, Solana CLI 2.2.1, and Token-2022
- Hardhat 3, OpenZeppelin Contracts 5.4, TypeScript, and Rust
- xUnit, FluentAssertions, Moq, WebApplicationFactory, Chai, and Mocha
- Deployed on Azure Container Apps, backed by Azure Blob Storage, Service Bus, Key Vault, and Communication Services Email

## Architecture

- `src/ThisCafeteria.Domain`: entities and enums.
- `src/ThisCafeteria.Application`: DTOs, validation, service interfaces, application services, repository contracts.
- `src/ThisCafeteria.Infrastructure`: EF Core DbContext, entity configurations, repositories, seed data, Identity user, Azure-backed storage/messaging/email services.
- `src/ThisCafeteria.Web`: Blazor pages, API controllers, Identity, Swagger, health checks.
- `src/ThisCafeteria.Worker`: background service that consumes order-processing messages from Azure Service Bus.
- `src/ThisCafeteria.AgentGateway`: pinned TypeScript x402/MCP boundary for paid agent resources.
- `contracts/evm`: local EVM tokens, faucet, liquid-staking vault, and ERC-8183 escrow.
- `contracts/solana`: Anchor liquid-staking program and browser/program smoke tests.
- `tests`: unit and integration test projects.

## Multichain Liquid Staking

ArtisanalBrew is moving from a single-chain, lock-and-reward staking pool to a capability-gated multichain liquid-staking system:

1. The user selects a network from the same persisted selector in the login pill or staking sidebar.
2. The connected wallet proves ownership using the chain family's native signature flow.
3. The user deposits CAFE and receives stCAFE, a liquid receipt representing redeemable CAFE.
4. COFFEE rewards accrue to the current stCAFE holder and can be claimed separately.
5. Redeeming stCAFE burns the receipt and returns the corresponding CAFE.
6. Web and Worker independently verify and reconcile every recorded operation using chain-qualified identities and cursors.

This is liquid staking of the application's CAFE asset. It is not validator staking of ETH, SOL, AVAX, HBAR, BNB, or another network's native currency.

### Network roadmap and visibility

The registry contains all nine requested test networks, but the public API and both selectors only expose entries whose deployment and capability gates are satisfied.

| Network | Registry state | User-visible behavior |
|---|---|---|
| Ethereum Sepolia | Enabled legacy deployment | Wallet login, CAFE faucet, marketplace payment, legacy claim/exit; new legacy deposits remain disabled |
| Solana Localnet | Enabled by a validated runtime manifest | Wallet Standard login, liquid deposit/redeem/claim, reward funding, RPC dashboard reads, and reconciliation |
| Solana Testnet | Planned; disabled without a verified public manifest | Hidden |
| Hedera Testnet | Planned; contracts not deployed | Hidden |
| Avalanche Fuji | Planned; contracts not deployed | Hidden |
| Linea Sepolia | Planned; contracts not deployed | Hidden |
| Base Sepolia | Planned; contracts not deployed | Hidden |
| BNB Smart Chain Testnet | Planned; contracts not deployed | Hidden |
| Monad Testnet | Planned; contracts not deployed | Hidden |
| Arbitrum Sepolia | Planned; contracts not deployed | Hidden |

Solana Testnet becomes visible only after the program and token fixtures are deployed, the public smoke scenario passes, and a validated `solana-testnet` manifest is supplied to both Web and Worker. The same manifest rule prevents an unfinished or mismatched connection from being advertised accidentally.

The full orchestration design is documented in [`docs/multichain-liquid-staking-plan.md`](docs/multichain-liquid-staking-plan.md). Operational commands and release controls live in [`docs/multichain-liquid-staking-operations.md`](docs/multichain-liquid-staking-operations.md) and [`docs/solana-local-manifest.md`](docs/solana-local-manifest.md).

### Deployed contracts — Ethereum Sepolia (chain id 11155111)

The server resolves these addresses from [`contracts/evm/deployments/ethereum-sepolia.json`](contracts/evm/deployments/ethereum-sepolia.json); the browser never chooses them. Compiled with solc `0.8.24` (optimizer runs 200, viaIR).

Liquid staking and tokens:

| Contract | Address |
|---|---|
| CAFE token | [`0x15DbED39271D2788a9Be63ffB34C8E2DdED8754A`](https://sepolia.etherscan.io/address/0x15DbED39271D2788a9Be63ffB34C8E2DdED8754A) |
| COFFEE reward token | [`0x4056E7F5FD1584C3db6223c9483761Dcb30Bf21C`](https://sepolia.etherscan.io/address/0x4056E7F5FD1584C3db6223c9483761Dcb30Bf21C) |
| Liquid vault (stCAFE, ERC-4626) | [`0x492132c5ec8b70a4d44fa365604d4c365b1d1a9f`](https://sepolia.etherscan.io/address/0x492132c5ec8b70a4d44fa365604d4c365b1d1a9f) |
| CAFE faucet | [`0xBD1517529BB0BA20c43b4E39323C70058FADe86D`](https://sepolia.etherscan.io/address/0xBD1517529BB0BA20c43b4E39323C70058FADe86D) |

Agentic-commerce and account abstraction:

| Contract | Address |
|---|---|
| ERC-4337 EntryPoint (v0.7) | [`0xdd9a61064ef9e2d9612da1f1307e168b85fe43a6`](https://sepolia.etherscan.io/address/0xdd9a61064ef9e2d9612da1f1307e168b85fe43a6) |
| Account factory | [`0x03e558b6af3e871f1884b670bd10d785b414e3fb`](https://sepolia.etherscan.io/address/0x03e558b6af3e871f1884b670bd10d785b414e3fb) |
| Verifying paymaster | [`0x35409fae884605c1ab9a1dcd561d3cb39da6619f`](https://sepolia.etherscan.io/address/0x35409fae884605c1ab9a1dcd561d3cb39da6619f) |
| ERC-8004 identity registry | [`0x44315b44555ca20d98eccd95720827a5b4bbdab6`](https://sepolia.etherscan.io/address/0x44315b44555ca20d98eccd95720827a5b4bbdab6) |
| ERC-7683 resolver | [`0xfdc86171e50f848fe539e74efafc9f34d471ff9f`](https://sepolia.etherscan.io/address/0xfdc86171e50f848fe539e74efafc9f34d471ff9f) |
| ERC-8183 escrow | [`0x78dd528ceb6f3de28365727270be865ff6840dea`](https://sepolia.etherscan.io/address/0x78dd528ceb6f3de28365727270be865ff6840dea) |

A sponsored ERC-4337 UserOperation has been mined successfully through this EntryPoint and paymaster (self-hosted Rundler v0.11.0, safe mode): UserOperation `0x87d8f80711508c7be740ee136e7909c4449276486321f21dbd221f4efb96c5c0`, mined in [transaction `0xb945492fc894b7a2d9defa7245120fe9b7bf2a9fb83b09de3cf49a4c79dbf5bb`](https://sepolia.etherscan.io/tx/0xb945492fc894b7a2d9defa7245120fe9b7bf2a9fb83b09de3cf49a4c79dbf5bb).

### What is implemented

- A validated, immutable chain registry with family, network, deployment, and capability metadata.
- One persisted chain selection shared by the desktop/mobile login pill and staking sidebar.
- Family-qualified wallet identities and a chain-safe ledger key: `(ChainKey, TransactionId, OperationIndex)`.
- PostgreSQL-backed Solana authentication challenges with hashed payloads, expiry, atomic consumption, origin/chain/address binding, Ed25519 verification, and identity reassignment protection.
- An EVM liquid vault with transferable stCAFE, ERC-4626 previews, COFFEE reward checkpoints, pause controls, reentrancy protection, exact accounting, and server-side transaction verification.
- An Anchor Solana program with PDA-controlled custody, Token-2022 stCAFE mint/burn, frozen receipt accounts, vault-mediated receipt transfers, reward funding/checkpointing/claims, pause controls, and emitted reconciliation events.
- Browser-side Solana Wallet Standard login and sign-and-send flows for deposit, redeem, and claim.
- Real Solana RPC dashboard reads for CAFE, stCAFE, COFFEE, custody, share supply, exchange rate, and pending rewards using raw integer arithmetic.
- Independent EVM and Solana reconciliation supervisors with persistent cursors, restart/replay idempotency, bounded pagination, and a Solana repair/backfill command.
- Deterministic local EVM and Solana contract workspaces, ABI/IDL output, deployment manifests, and automated tests.
- A local-first ERC-8183 escrow and pinned x402 gateway slice. ERC-4337, ERC-8004, and ERC-7683 remain future agent-commerce work and are not presented as complete.
- Agentic-commerce scaffolding for the audited first three phases: local escrow and protocol fixtures, an integrated x402/MCP gateway, projection storage, and an initial Procurement Lab state view. The phase gates remain open (Phases 3–5) until the indexer, job lifecycle, ERC-4337, and ERC-7683 smoke tests are completely implemented.

### Security and enablement model

- The browser never chooses trusted contract addresses; the server resolves them from the registry and deployment manifest.
- Solana verification checks finalized transactions, the trusted program, instruction and event discriminators, signer, PDAs, mints, custody/token accounts, owners, decimals, and canonical SPL programs.
- Solana events are accepted only while the trusted program is active in the invocation stack, preventing matching data emitted through an unrelated CPI path.
- Raw Token-2022 receipt transfers are blocked. `transfer_st_cafe` checkpoints sender and recipient rewards before moving and refreezing both accounts.
- Public manifests contain addresses and checksums only—never a private key or seed phrase.
- Public broadcasts require explicit release acknowledgement; unattended tests and builds remain local-only.

### Remaining rollout work

- Fund the authorized Solana Testnet deployer, deploy the program and token fixtures, execute the public deposit → fund → transfer → claim → redeem smoke scenario, and generate the verified Testnet manifest.
- Deploy and verify a new EVM liquid vault per selected EVM testnet; do not reuse or upgrade the unverified legacy Sepolia pool.
- Add public-RPC health/observability and rollback evidence before enabling each network.
- Complete the agent-commerce stack described in [`docs/agentic-commerce-stack-plan.md`](docs/agentic-commerce-stack-plan.md): ERC-4337, ERC-8004, and ERC-7683 are still outstanding.
- The audited phase status and remaining gates are tracked in [`docs/agentic-commerce-stack-plan.md`](docs/agentic-commerce-stack-plan.md); the current Procurement Lab is a projection viewer, not a completed procurement workflow.



## Run Locally

Local PostgreSQL uses Apple Container and is published on host port **55432**. The script creates only the scoped `artisanalbrew-postgres-test` container.

```bash
cd /path/to/ArtisanalBrew
cp .env.example .env
# Edit .env and replace every CHANGE_ME / YOUR_DB_* value.
scripts/apple-container-postgres.sh start
export ConnectionStrings__DefaultConnection='Host=127.0.0.1;Port=55432;Database=thiscafeteria_test;Username=test_only;Password=test_only_password'
dotnet restore
dotnet ef database update --project src/ThisCafeteria.Infrastructure --startup-project src/ThisCafeteria.Web
dotnet run --project src/ThisCafeteria.Web
```

Swagger is available in Development at `/swagger`, and health checks are exposed at `/health`.

Start the worker in a second terminal with the same connection string:

```bash
dotnet run --project src/ThisCafeteria.Worker
```

Stop the local database when finished:

```bash
scripts/apple-container-postgres.sh stop
```

The app requires `ConnectionStrings__DefaultConnection` from environment variables (`.env` for local development). No production password is embedded in source code.

### Local blockchain manifests

Build, test, and create the local EVM fixture:

```bash
cd contracts/evm
npm ci
npm run build
npm test
XDG_CONFIG_HOME=/private/tmp/artisanalbrew-hardhat-config npm run deploy:ephemeral
cd ../..
export ARTISANALBREW_EVM_MANIFEST="$PWD/contracts/evm/deployments/evm-local.json"
```

For Solana, install the versions pinned in `contracts/solana/Anchor.toml`, run `anchor test`, generate a public-address-only manifest as described in [`docs/solana-local-manifest.md`](docs/solana-local-manifest.md), and export it before starting both application processes:

```bash
export ARTISANALBREW_SOLANA_MANIFEST=/absolute/path/to/solana-local-deployment-manifest.json
```

Web and Worker must load the same manifest. A valid local manifest adds `solana-localnet`; a valid Testnet manifest replaces and enables the otherwise hidden `solana-testnet` placeholder.

## Migrations

```bash
dotnet tool install --global dotnet-ef
dotnet ef migrations add InitialCreate --project src/ThisCafeteria.Infrastructure --startup-project src/ThisCafeteria.Web
dotnet ef database update --project src/ThisCafeteria.Infrastructure --startup-project src/ThisCafeteria.Web
```

Admin user seeding reads:

- `Authentication:AdminEmail`
- `Authentication:AdminPassword`

## Tests

```bash
dotnet restore
dotnet build ThisCafeteria.sln --configuration Release --no-restore
dotnet test tests/ThisCafeteria.UnitTests --configuration Release --no-build
# Expected: 154 passing

TEST_POSTGRES_CONNECTION='Host=127.0.0.1;Port=55432;Database=thiscafeteria_test;Username=test_only;Password=test_only_password' \
  dotnet test tests/ThisCafeteria.IntegrationTests --configuration Release --no-build

npm --prefix contracts/evm test
# Expected: 24 passing

npm --prefix src/ThisCafeteria.AgentGateway test
# Expected: 11 passing

npm --prefix src/ThisCafeteria.AgentGateway run build
cargo test --manifest-path contracts/solana/Cargo.toml --locked
npm --prefix contracts/solana run test:browser
```

### Phase 3 Acceptance Harness

The acceptance harness (`./run-acceptance.sh`) drives a complete job lifecycle against a local
Hardhat node and a local PostgreSQL database. It uses **Hardhat-controlled local wallets** — it
is not a browser wallet test, MetaMask test, or UI E2E test. No browser extension is required.

```bash
ACCEPTANCE_ISOLATED=1 ./run-acceptance.sh
# Evidence is written to: acceptance-evidence-<timestamp>.log
# Final marker: ACCEPTANCE_RESULT=PASS  exit=0
```

Phase 3 is **not** declared complete until the script exits 0 and the full output is preserved.

## Worker

```bash
dotnet run --project src/ThisCafeteria.Worker
```

The worker connects to Azure Service Bus and processes messages from the `order-processing` queue via a real `ServiceBusProcessor`.

## Azure Infrastructure

Everything below is provisioned as Bicep IaC in `infra/` and deployed to Azure Container Apps (Consumption plan). Full audit, decisions, and phase-by-phase history live in [`docs/azure-migration-plan.md`](docs/azure-migration-plan.md).

```mermaid
flowchart TB
    Users(["Users<br/>cafe.alexisreyna.dev"])
    Sepolia(["Ethereum Sepolia<br/>MetaMask checkout — unchanged"])

    subgraph GH["GitHub"]
        Actions["GitHub Actions<br/>build-test + deploy (OIDC, no secrets)"]
    end

    subgraph RG["Azure Resource Group: thiscafeteria-prod-rg (westus)"]
        ACR["Container Registry"]

        subgraph CAE["Container Apps Environment (Consumption)"]
            Web["Web Container App<br/>scale-to-zero"]
            Worker["Worker Container App<br/>Service Bus consumer"]
        end

        PG[("Postgres Flexible Server<br/>Burstable B1ms")]
        Blob[("Storage Account<br/>Blob: receipts")]
        SB{{"Service Bus<br/>wallet-status, order-processing"}}
        KV[["Key Vault"]]
        ACS["Communication Services<br/>Email"]

        MI["Managed Identity (app)<br/>AcrPull, Key Vault Secrets User,<br/>Storage Blob Data Contributor,<br/>Service Bus Sender/Receiver"]
        CicdMI["CI/CD Identity<br/>GitHub OIDC federated<br/>AcrPush, Container Apps Contributor,<br/>Postgres Contributor, Key Vault Secrets User"]
    end

    Actions -- "az acr build" --> ACR
    Actions -- "az containerapp update" --> Web
    Actions -- "az containerapp update" --> Worker
    Actions -. "OIDC token exchange" .-> CicdMI

    Users -- "HTTPS + managed cert" --> Web
    Web --> PG
    Web --> Blob
    Web --> SB
    Web --> KV
    Web --> ACS
    Web -. "wallet connect + checkout" .-> Sepolia

    Worker --> SB
    Worker --> PG

    MI -. identity .- Web
    MI -. identity .- Worker
```

**Status:** Phases 1–6 (containerize, IaC, real Azure service implementations, CI/CD via OIDC, data migration, DNS cutover) are complete and live. The old EC2/RDS/SQS/S3/SES footprint is fully out of the traffic path and its artifacts have been retired from `main` — see [`docs/aws-legacy-infra.md`](docs/aws-legacy-infra.md) for what it was and the [`aws_legacy`](https://github.com/AlejoReyna/ArtisanalBrew/tree/aws_legacy) branch for the preserved pre-migration snapshot.
