@description('Azure region for the PostgreSQL Flexible Server')
param location string

@description('Globally unique name for the PostgreSQL Flexible Server')
param name string

@description('Administrator login for the PostgreSQL Flexible Server')
param administratorLogin string

@secure()
@description('Administrator password for the PostgreSQL Flexible Server')
param administratorPassword string

@description('Name of the application database to create')
param databaseName string = 'this_cafeteria'

@description('Principal ID of the CI/CD identity that manages temporary firewall rules for running migrations. Leave empty to skip.')
param firewallManagerPrincipalId string = ''

var contributorRoleDefinitionId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'b24988ac-6180-42a0-ab88-20f7382dd24c')

resource server 'Microsoft.DBforPostgreSQL/flexibleServers@2024-08-01' = {
  name: name
  location: location
  sku: {
    // Cheapest tier available for Flexible Server: Burstable B1ms (1 vCore, 2GiB RAM).
    name: 'Standard_B1ms'
    tier: 'Burstable'
  }
  properties: {
    version: '16'
    administratorLogin: administratorLogin
    administratorLoginPassword: administratorPassword
    storage: {
      storageSizeGB: 32
    }
    backup: {
      backupRetentionDays: 7
      geoRedundantBackup: 'Disabled'
    }
    highAvailability: {
      mode: 'Disabled'
    }
  }
}

resource database 'Microsoft.DBforPostgreSQL/flexibleServers/databases@2024-08-01' = {
  parent: server
  name: databaseName
  properties: {
    charset: 'UTF8'
    collation: 'en_US.utf8'
  }
}

// No custom VNet/private endpoint for this demo-scale deployment (Container Apps runs on a
// platform-managed Consumption-only environment, not a VNet-integrated one) — so Postgres is
// reached over the public endpoint with only Azure-service traffic allowed. SSL is enforced by
// the server by default. This is a deliberate cost/complexity trade-off, not an oversight; a
// production deployment would put both behind a VNet with a private endpoint instead.
resource allowAzureServices 'Microsoft.DBforPostgreSQL/flexibleServers/firewallRules@2024-08-01' = {
  parent: server
  name: 'AllowAzureServices'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

// Scoped to just this one server (not the resource group) — CI/CD only needs to manage this
// server's firewall rules to allow the runner's IP through for the duration of a migration run.
resource firewallManagerRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(firewallManagerPrincipalId)) {
  name: guid(server.id, firewallManagerPrincipalId, contributorRoleDefinitionId)
  scope: server
  properties: {
    roleDefinitionId: contributorRoleDefinitionId
    principalId: firewallManagerPrincipalId
    principalType: 'ServicePrincipal'
  }
}

output fqdn string = server.properties.fullyQualifiedDomainName
output databaseName string = database.name
output name string = server.name
