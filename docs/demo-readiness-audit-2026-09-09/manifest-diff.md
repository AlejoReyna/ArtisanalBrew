# Manifest / config address diffs (facts)

## A) Web `Blockchain:Network:StakingPoolContract` vs root Sepolia `liquidVault` — MISMATCH
- `src/ThisCafeteria.Web/appsettings.json:19` — `StakingPoolContract`: `0x932d50E20F917B9BbBe2C40F30D43BCef0e93F90`
- `deployments/ethereum-sepolia.json:24` — `liquidVault`: `0x0a7b7d7eb39ca835ef5854875bb97885d23d1df3`
- Same wrong staking address also in `src/ThisCafeteria.Web/appsettings.Development.json` (Network block)
- That `0x932d50…` value equals **`deployments/bsc-testnet.json` `addresses.erc8004Registry`**, not Sepolia vault
- Live `/api/chains` (production) correctly exposes Sepolia `liquidVault`/`stCafe` = `0x0a7b7d…` (manifest path)
- Legacy path: `StakingController` / `CoffeeWeb3Service` bind `BlockchainNetworkOptions.StakingPoolContract` → risk for legacy stake APIs if exercised
- Liquid path: `LiquidStakingController` + `ILiquidStakingGateway` use registry deployment → aligns with live API

## B) Root runtime manifest vs contracts proof manifest — DIFF (documented in README)
- Root: `deployments/ethereum-sepolia.json:24` liquidVault `0x0a7b7d7eb39ca835ef5854875bb97885d23d1df3`
- Proof: `contracts/evm/deployments/ethereum-sepolia.json:24` liquidVault `0x492132c5ec8b70a4d44fa365604d4c365b1d1a9f`
- Additional proof diffs (entryPoint, accountFactory, verifyingPaymaster, erc8004/7683/8183) vs root — see README “must not be treated as interchangeable”
- UI hardcode of **proof** vault: `src/ThisCafeteria.Web/Components/Story/StoryTechShowcase.razor:133` links `0x492132c5…`

## C) Matching Sepolia Network fields (Web appsettings vs root manifest)
- PaymentTokenContract ↔ cafe — MATCH (`0x15DbED…`)
- CoffeeCoinContract ↔ coffee — MATCH
- CafeFaucetContract ↔ faucet — MATCH
- MarketplaceWallet ↔ legacyPool — MATCH

## D) Worker Network contract blanks
- `src/ThisCafeteria.Worker/appsettings.json` Network Payment/Staking/Coffee/Marketplace empty strings; relies on `LocalEvmManifest` load
- Worker Dev: `LocalEvmManifest=deployments/evm-local.json` but **file missing** at `deployments/evm-local.json`

## E) Agentic network vs storefront checkout chain — CROSS-CHAIN mismatch
- AgentGateway hardcodes Base Sepolia: `src/ThisCafeteria.AgentGateway/src/server.ts:62,125,282,285-289,310` — `eip155:84532`
- Live `https://cafe.alexisrs.dev/.well-known/agent-card.json` → `"network":"eip155:84532"`
- Storefront default chain live `/api/chains` → `ethereum-sepolia` / `11155111` (marketplacePayment true)
- atlantic-migration text still mentions Base Sepolia for gateway; storefront demo checkout is Sepolia ETH

## F) Domain / hosting source mismatch
- `deployments/atlantic/Caddyfile:1` — `cafe.alexisreyna.dev`
- `docs/atlantic-migration.md:16,33` — `cafe.alexisreyna.dev`
- `README.md:7` — Azure + `cafe.alexisreyna.dev`
- Live probed host — `cafe.alexisrs.dev` (works)

## G) Solana
- Root `deployments/solana-devnet.json` loaded by Web/Worker; live `/api/chains` exposes program/mints matching root (Helius host without query key in public URL)
