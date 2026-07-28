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

The registry ships nine chain definitions, disabled by default. A committed deployment manifest under [`deployments/`](deployments/) promotes a chain to enabled by replacing its definition (`BlockchainManifestLoader`); the public `/api/chains` endpoint and both selectors then expose it, because `ChainsController` filters on `Enabled`. Three chains are live today: their manifests are present and wired through `appsettings.json` for both Web and Worker (`Blockchain:LocalEvmManifest`, `Blockchain:SolanaDeploymentManifest`).

| Network | Runtime state | Enabled capabilities | Not yet enabled |
|---|---|---|---|
| Ethereum Sepolia (11155111) | Enabled — `deployments/ethereum-sepolia.json` | Wallet login, CAFE faucet, liquid deposit/redeem/claim, reward minting, agentic commerce (ERC-8004 / ERC-7683 / ERC-8183) | Session-key payments, marketplace payment, legacy exit |
| BNB Smart Chain Testnet (97) | Enabled — `deployments/bsc-testnet.json` | Wallet login, CAFE faucet, liquid deposit/redeem/claim, reward minting, agentic commerce | Session-key payments, marketplace payment |
| Solana Devnet | Enabled — `deployments/solana-devnet.json` | Wallet Standard login, liquid deposit/redeem/claim, reward funding, RPC dashboard reads, reconciliation | CAFE faucet, agentic commerce |
| Hedera Testnet, Avalanche Fuji, Linea Sepolia, Base Sepolia, Monad Testnet, Arbitrum Sepolia | Disabled — no manifest | — | Hidden until contracts are deployed and a validated manifest is supplied |

A local Solana run adds `solana-localnet`, and a validated public `solana-testnet` manifest adds `solana-testnet`, by the same replace-on-manifest rule; neither is wired into the committed appsettings. The manifest rule prevents an unfinished or mismatched connection from being advertised accidentally.

One capability nuance is deliberate today: for EVM chains the manifest loader grants a fixed set (wallet login, liquid staking, faucet, reward minting) and reads `agenticCommerce`, `agenticSessionPayments`, `marketplacePayment`, and `legacyExit` from the manifest's `capabilities` object — the manifest is the single source of truth. A flag only takes effect when the deployment it needs is present: `marketplacePayment` requires an escrow (or the legacy pool) and `legacyExit` requires a legacy pool, or `ChainRegistry.Validate` rejects the manifest. The committed liquid-chain manifests do not yet declare `marketplacePayment`/`legacyExit`, so those stay off until their contracts are deployed and addressed — see the capability roadmap in [`docs/multichain-liquid-staking-plan.md`](docs/multichain-liquid-staking-plan.md).

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

## Homepage: a crew that learned its job

The four pixel robots on the landing page are not running an animation. They run a small neural network that was trained to collect coffee coins, and the hero is the environment it was trained in.

The scene used to be CSS keyframes: every robot rode a shared 90-second clock, and each coin sat at exactly one robot's roam destination so the two beats would coincide. Now each robot observes the field every frame — direction and distance to **its own claimed coin**, its own velocity, the four walls — and a 258-parameter network decides where to accelerate. Coins are claimed one robot each, so the crew never clumps on a single prize and no robot goes starved.

The physics are tuned for weightlessness rather than efficiency: low thrust, low drag, so momentum carries a robot well past the point it stops accelerating. A full crossing of the scene takes about eight seconds. It is something you watch drift, not something that darts.

Coffee bags are the difficulty layer. Unlike coins they are not always there — each one appears suddenly at a random spot, stays catchable for only 7–12 seconds, then vanishes whether or not anyone reached it. Catching one grants a **25% speed boost for six seconds**, which makes it a real decision: a bag is worth chasing only if the detour costs less than the boost earns back, and it may expire before the robot arrives. The trained crew catches about half of everything that appears and spends a fifth of its life caffeinated; the untrained one catches 2%.

| | |
|---|---|
| Policy | 13 → 16 → 2 MLP, `tanh`, **258 parameters**, 7 KB shipped |
| Training | OpenAI-style evolution strategies — mirrored sampling, rank-normalised returns |
| Result | **1.8 → 27.4** coins per 30-second episode on held-out layouts |
| Fairness | 6.0 / 7.2 / 7.4 / 6.2 coins per robot — a 0.81 min/max ratio |
| Cost | **4.5 minutes**, one CPU core, zero dependencies |

The one rule that makes it trustworthy: **the simulation lives in a single file that both sides import.** [`pixelCrewSim.js`](src/ThisCafeteria.Web/wwwroot/js/pixelCrewSim.js) holds the physics and the reward; the Node trainer imports it and so does the browser runtime. There is no second implementation to drift. Running the shipped weights through the shipped sim in the browser reproduces the trainer's held-out numbers to the decimal.

Three checkpoints ship — generation 0, 20, and 300 — behind the hero's **CREW BRAIN** switcher, which swaps the steering network without disturbing the field so the difference is actually legible. Generation 0 drifts into walls; generation 300 turns early and lines up the next coin while still moving.

Two things stated plainly, because the repo prefers it that way: five lines of `normalize(coin - pos)` steering would look nearly identical on screen, so the learned policy is here as a craft artifact rather than a necessity; and the whole thing degrades to the original keyframe composition when JavaScript is unavailable or `prefers-reduced-motion` is set.

Method, hyperparameters, learning curve, and reproduction steps: [`docs/pixel-crew-training.md`](docs/pixel-crew-training.md).

```bash
node tools/train_pixel_crew.mjs
```

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

### Running the full stack with Apple Container instead of Docker

`docker-compose.yml` describes the full stack (Postgres, pgadmin, Web, Worker) for Docker, but on a
machine without Docker/Colima/OrbStack installed you can run the same Postgres service with Apple's
native `container` CLI and run Web/Worker directly with `dotnet run` against it:

```bash
# Start the container runtime (one-time per session/reboot)
container system start

# Bring up Postgres matching docker-compose's credentials/port (5433 on the host)
container run -d --name this-cafeteria-postgres \
  -e POSTGRES_DB=this_cafeteria \
  -e POSTGRES_USER=postgres \
  -e POSTGRES_PASSWORD=postgres \
  -p 5433:5432 \
  postgres:16-alpine

# Point the app at it and run
export ConnectionStrings__DefaultConnection="Host=localhost;Port=5433;Database=this_cafeteria;Username=postgres;Password=postgres"
dotnet run --project src/ThisCafeteria.Web --urls http://localhost:5286
```

On a later run, the container already exists, so start it instead of re-creating it:

```bash
container start this-cafeteria-postgres
```

Stop it with `container stop this-cafeteria-postgres` when finished; `container list -a` shows all
containers (state, IP, image) and `container system status` shows whether the Apple Container
apiserver is running.

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

## What “the node” means

This project has two different things that may be called a node:

1. **The production application runtime** is not an AWS EC2 node. It is split into two containerized Azure Container Apps:
   - `ThisCafeteria.Web` serves the Blazor application and HTTP/API endpoints.
   - `ThisCafeteria.Worker` runs background order-processing and reconciliation jobs.

   The container images are built from this repository, pushed to Azure Container Registry, and deployed by GitHub Actions. The containers are stateless and may be restarted or replaced by Azure; application data is not stored on the container filesystem.

2. **A local blockchain node** is a temporary Hardhat JSON-RPC process used for development, contract deployment, and acceptance tests. It runs on the developer machine or CI runner, usually on ports `8545`, `8546`, or `8547`. Its chain state lives only in the process/workspace and is intentionally disposable. Local deployment addresses are recorded in `contracts/evm/deployments/evm-local.json`.

### Where data is stored

The application stores durable state in managed services rather than inside a node or container:

| Data | Location | How it is used |
|---|---|---|
| Users, orders, ledger, projections, and application records | Azure Database for PostgreSQL Flexible Server | Accessed by Web and Worker through Entity Framework Core/Npgsql. Migrations create and update the schema. |
| Receipt files and ASP.NET data-protection keys | Azure Blob Storage (`receipts` and `dataprotection-keys`) | Accessed through the app’s managed identity; blob public access is disabled. |
| Wallet-status and order-processing events | Azure Service Bus queues | Web publishes messages; Worker consumes them. |
| Passwords, connection strings, and provider credentials | Azure Key Vault | Injected into the Container Apps at runtime; they are not committed to this repository. |
| Source code, infrastructure definitions, and deployment metadata | This Git repository | Bicep lives in `infra/`; public contract addresses live in `deployments/` and `contracts/*/deployments/`. |

The old AWS design used an EC2 `t3.micro` application server with RDS, SQS, and S3. That footprint is retired and is not the current production path. The AWS implementation is preserved only on the `aws_legacy` branch; the `t3.micro` reservation screen should therefore not be treated as the runtime architecture documented below.

### How it is operated

- **Local app:** start PostgreSQL with `scripts/apple-container-postgres.sh`, run the Web project, and optionally run the Worker in a second terminal.
- **Local blockchain:** start Hardhat through the commands in [Local blockchain manifests](#local-blockchain-manifests); deploy scripts write a manifest containing public addresses.
- **Production:** merge to the deployment branch; GitHub Actions authenticates to Azure with OIDC, builds and pushes the Web/Worker images, applies the Container Apps revisions, and runs database migration/deployment steps as configured in `.github/workflows/ci.yml`.
- **Secrets:** supply them through `.env` locally or Azure Key Vault in production. Do not put private keys, passwords, or provider API keys in source control.

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
