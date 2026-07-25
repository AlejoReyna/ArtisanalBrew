targetScope = 'resourceGroup'

// Deploy into an existing resource group, created beforehand with:
//   az group create --name <rg-name> --location <location>
// (Subscription-scope deployments that create the resource group inline hit an
// Azure-side "best available regions" restriction on this Azure for Students
// subscription for several resource types, even in regions that work fine for
// direct resource creation into an already-existing resource group. Deploying at
// resourceGroup scope into a pre-created RG avoids that restriction entirely.)

@description('Azure region for all resources')
param location string = 'westus'

@description('Environment name, used in resource naming (e.g. prod, dev)')
param environmentName string = 'prod'

@description('Base name for the project, used in resource naming')
param projectName string = 'thiscafeteria'

@description('Administrator login for the PostgreSQL Flexible Server')
param postgresAdminLogin string = 'thiscafeteria_admin'

@secure()
@description('Administrator password for the PostgreSQL Flexible Server. Pass at deploy time, never commit a real value.')
param postgresAdminPassword string

@description('Admin email for ASP.NET Identity seeding. Leave empty to skip creating an admin account (matches the app\'s existing graceful-skip behavior).')
param authenticationAdminEmail string = ''

@secure()
@description('Admin password for ASP.NET Identity seeding. Leave empty to skip creating an admin account.')
param authenticationAdminPassword string = ''

@description('Web container image reference. Defaults to a public placeholder so infra can be provisioned before CI/CD has pushed a real image to ACR; the deploy pipeline overwrites this via az containerapp update.')
param webImage string = 'mcr.microsoft.com/dotnet/samples:aspnetapp'

@description('Worker container image reference. Same placeholder convention as webImage.')
param workerImage string = 'mcr.microsoft.com/dotnet/samples:aspnetapp'

@description('Set true only when a tracer-capable Sepolia RPC URL and a dedicated funded bundler signer are supplied.')
param enableSepoliaBundler bool = false

@description('Rundler image reference, built separately from the Web and Worker images.')
param sepoliaBundlerImage string = 'mcr.microsoft.com/oss/ubuntu/ubuntu:22.04'

@secure()
@description('Sepolia node RPC URL with debug_traceCall JavaScript-tracer support. Never commit this provider credential.')
param sepoliaBundlerNodeRpcUrl string = ''

@secure()
@description('Dedicated Sepolia beneficiary/signer private key for Rundler. Never reuse the deployed-contract owner key.')
param sepoliaBundlerSignerPrivateKey string = ''

@description('Extra non-secret environment variables for the Web app, e.g. Blockchain__Network__* settings that must not be templated into version-controlled Bicep. Pass at deploy time.')
param additionalWebEnvVars array = []

@description('Extra non-secret environment variables for the Worker, mirroring additionalWebEnvVars.')
param additionalWorkerEnvVars array = []

@description('GitHub repository in owner/repo form, for the CI/CD OIDC federated credential')
param githubRepo string = 'AlejoReyna/ArtisanalBrew'

@description('Branch the CI/CD identity is allowed to deploy from')
param githubBranch string = 'main'

var resourceToken = uniqueString(subscription().subscriptionId, resourceGroup().id, environmentName)
var namePrefix = '${projectName}-${environmentName}'

module identity 'modules/managedIdentity.bicep' = {
  name: 'identity'
  params: {
    location: location
    name: '${namePrefix}-identity'
  }
}

module cicdIdentity 'modules/cicdIdentity.bicep' = {
  name: 'cicdIdentity'
  params: {
    location: location
    name: '${namePrefix}-cicd-identity'
    githubRepo: githubRepo
    githubBranch: githubBranch
  }
}

module acr 'modules/containerRegistry.bicep' = {
  name: 'acr'
  params: {
    location: location
    name: toLower('${projectName}${environmentName}acr${resourceToken}')
    pullIdentityPrincipalId: identity.outputs.principalId
    pushIdentityPrincipalId: cicdIdentity.outputs.principalId
  }
}

module logAnalytics 'modules/logAnalytics.bicep' = {
  name: 'logAnalytics'
  params: {
    location: location
    name: '${namePrefix}-logs'
  }
}

module containerAppsEnvironment 'modules/containerAppsEnvironment.bicep' = {
  name: 'containerAppsEnvironment'
  params: {
    location: location
    name: '${namePrefix}-env'
    logAnalyticsCustomerId: logAnalytics.outputs.customerId
    logAnalyticsSharedKey: logAnalytics.outputs.sharedKey
  }
}

module postgres 'modules/postgres.bicep' = {
  name: 'postgres'
  params: {
    location: location
    name: toLower('${projectName}-${environmentName}-pg-${resourceToken}')
    administratorLogin: postgresAdminLogin
    administratorPassword: postgresAdminPassword
    firewallManagerPrincipalId: cicdIdentity.outputs.principalId
  }
}

module storage 'modules/storage.bicep' = {
  name: 'storage'
  params: {
    location: location
    name: toLower('tcst${resourceToken}')
    dataContributorPrincipalId: identity.outputs.principalId
  }
}

module serviceBus 'modules/serviceBus.bicep' = {
  name: 'serviceBus'
  params: {
    location: location
    name: toLower('${projectName}-${environmentName}-sb-${resourceToken}')
    dataAccessPrincipalId: identity.outputs.principalId
  }
}

module communicationEmail 'modules/communicationEmail.bicep' = {
  name: 'communicationEmail'
  params: {
    emailServiceName: toLower('${projectName}-${environmentName}-email-${resourceToken}')
    communicationServiceName: toLower('${projectName}-${environmentName}-acs-${resourceToken}')
  }
}

var dbConnectionString = 'Host=${postgres.outputs.fqdn};Port=5432;Database=${postgres.outputs.databaseName};Username=${postgresAdminLogin};Password=${postgresAdminPassword};Ssl Mode=Require;Trust Server Certificate=true'

var baseSecrets = {
  'db-connection-string': dbConnectionString
  'azure-communication-connection-string': communicationEmail.outputs.connectionString
}

var adminSecrets = empty(authenticationAdminPassword) ? {} : {
  'authentication-admin-password': authenticationAdminPassword
}

var sepoliaBundlerSecrets = enableSepoliaBundler ? {
  'sepolia-bundler-node-rpc-url': sepoliaBundlerNodeRpcUrl
  'sepolia-bundler-signer-private-key': sepoliaBundlerSignerPrivateKey
} : {}

module keyVault 'modules/keyVault.bicep' = {
  name: 'keyVault'
  params: {
    location: location
    // Key Vault names are capped at 24 characters, unlike the other resources here.
    name: toLower('tc-kv-${resourceToken}')
    keyVaultReaderPrincipalId: identity.outputs.principalId
    cicdReaderPrincipalId: cicdIdentity.outputs.principalId
    secrets: union(baseSecrets, adminSecrets, sepoliaBundlerSecrets)
  }
}

var sharedPlainEnvVars = [
  { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
  { name: 'Azure__ManagedIdentity__ClientId', value: identity.outputs.clientId }
  { name: 'Azure__ServiceBus__FullyQualifiedNamespace', value: serviceBus.outputs.fullyQualifiedNamespace }
  { name: 'Azure__ServiceBus__WalletStatusQueueName', value: serviceBus.outputs.walletStatusQueueName }
  { name: 'Azure__ServiceBus__OrderProcessingQueueName', value: serviceBus.outputs.orderProcessingQueueName }
  { name: 'Azure__Storage__BlobEndpoint', value: storage.outputs.blobEndpoint }
  { name: 'Azure__Storage__ReceiptsContainerName', value: storage.outputs.receiptsContainerName }
  { name: 'Azure__Communication__SenderAddress', value: communicationEmail.outputs.senderAddress }
  { name: 'Authentication__AdminEmail', value: authenticationAdminEmail }
]

var sharedSecretEnvVars = concat(
  [
    { envVarName: 'ConnectionStrings__DefaultConnection', keyVaultSecretName: 'db-connection-string' }
    { envVarName: 'Azure__Communication__ConnectionString', keyVaultSecretName: 'azure-communication-connection-string' }
  ],
  empty(authenticationAdminPassword) ? [] : [
    { envVarName: 'Authentication__AdminPassword', keyVaultSecretName: 'authentication-admin-password' }
  ]
)

module webApp 'modules/containerApp.bicep' = {
  name: 'webApp'
  params: {
    location: location
    name: '${namePrefix}-web'
    containerAppsEnvironmentId: containerAppsEnvironment.outputs.id
    userAssignedIdentityId: identity.outputs.id
    acrLoginServer: acr.outputs.loginServer
    image: webImage
    enableIngress: true
    targetPort: 8080
    cpu: '0.5'
    memory: '1Gi'
    minReplicas: 1
    maxReplicas: 2
    keyVaultUri: keyVault.outputs.uri
    plainEnvVars: concat(sharedPlainEnvVars, additionalWebEnvVars)
    secretEnvVars: sharedSecretEnvVars
    deployerPrincipalId: cicdIdentity.outputs.principalId
  }
}

module workerApp 'modules/containerApp.bicep' = {
  name: 'workerApp'
  params: {
    location: location
    name: '${namePrefix}-worker'
    containerAppsEnvironmentId: containerAppsEnvironment.outputs.id
    userAssignedIdentityId: identity.outputs.id
    acrLoginServer: acr.outputs.loginServer
    image: workerImage
    enableIngress: false
    cpu: '0.25'
    memory: '0.5Gi'
    minReplicas: 1
    maxReplicas: 1
    keyVaultUri: keyVault.outputs.uri
    plainEnvVars: concat(sharedPlainEnvVars, additionalWorkerEnvVars)
    secretEnvVars: sharedSecretEnvVars
    deployerPrincipalId: cicdIdentity.outputs.principalId
  }
}

// Private by design: do not expose an unauthenticated bundler RPC to the internet.
module sepoliaBundler 'modules/containerApp.bicep' = if (enableSepoliaBundler) {
  name: 'sepoliaBundler'
  params: {
    location: location
    name: '${namePrefix}-sepolia-bundler'
    containerAppsEnvironmentId: containerAppsEnvironment.outputs.id
    userAssignedIdentityId: identity.outputs.id
    acrLoginServer: acr.outputs.loginServer
    image: sepoliaBundlerImage
    enableIngress: true
    externalIngress: false
    targetPort: 4338
    cpu: '0.5'
    memory: '1Gi'
    minReplicas: 1
    maxReplicas: 1
    keyVaultUri: keyVault.outputs.uri
    plainEnvVars: []
    secretEnvVars: [
      { envVarName: 'RUNDLER_NODE_HTTP', keyVaultSecretName: 'sepolia-bundler-node-rpc-url' }
      { envVarName: 'RUNDLER_SIGNER_PRIVATE_KEY', keyVaultSecretName: 'sepolia-bundler-signer-private-key' }
    ]
    deployerPrincipalId: cicdIdentity.outputs.principalId
  }
}

output resourceGroupName string = resourceGroup().name
output containerRegistryLoginServer string = acr.outputs.loginServer
output webAppFqdn string = webApp.outputs.fqdn
output managedIdentityClientId string = identity.outputs.clientId
output cicdIdentityClientId string = cicdIdentity.outputs.clientId
output postgresServerName string = postgres.outputs.name
output keyVaultName string = toLower('tc-kv-${resourceToken}')
output sepoliaBundlerInternalFqdn string = enableSepoliaBundler ? sepoliaBundler.outputs.fqdn : ''
