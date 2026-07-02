using 'main.bicep'

// Non-secret defaults. Override at deploy time with --parameters if needed, e.g.:
//   az deployment group create --resource-group thiscafeteria-prod-rg --template-file infra/main.bicep --parameters infra/main.bicepparam
// westus is the confirmed-working region for this Azure for Students subscription:
// eastus2 (in the subscription's general "allowed locations" policy) is separately
// blocked for both Postgres Flexible Server (LocationIsOfferRestricted) and Container
// Apps environments (MaxNumberOfEnvironmentsInSubExceeded) by resource-specific
// capacity restrictions on this account. mexicocentral is allowed generally but
// doesn't support Container Apps at all. westus, canadacentral, and southcentralus are
// the three regions that satisfy both the general policy and both services' own
// region availability; westus was the one tested to actually work end-to-end.
param location = 'westus'
param environmentName = 'prod'
param projectName = 'thiscafeteria'
param postgresAdminLogin = 'thiscafeteria_admin'

// Secrets: never hardcode real values here. Export these as environment variables
// before running `az deployment sub create`, e.g.:
//   export POSTGRES_ADMIN_PASSWORD='...'
//   export AUTH_ADMIN_PASSWORD='...'   (optional, only needed if seeding an admin account)
param postgresAdminPassword = readEnvironmentVariable('POSTGRES_ADMIN_PASSWORD', '')
param authenticationAdminEmail = readEnvironmentVariable('AUTH_ADMIN_EMAIL', '')
param authenticationAdminPassword = readEnvironmentVariable('AUTH_ADMIN_PASSWORD', '')

// Blockchain/Web3 configuration is deliberately NOT templated here — it must never be
// committed to source control. Pass it at deploy time if you want the Container Apps to
// carry it, e.g. by building this array from your local .env before deploying:
// param additionalWebEnvVars = [
//   { name: 'Blockchain__Network__RpcUrl', value: readEnvironmentVariable('BLOCKCHAIN_RPC_URL', '') }
//   { name: 'Blockchain__Network__CoffeeCoinContract', value: readEnvironmentVariable('COFFEE_COIN_CONTRACT', '') }
// ]
param additionalWebEnvVars = []
param additionalWorkerEnvVars = []
