@description('Azure region for the Key Vault')
param location string

@description('Globally unique name for the Key Vault')
param name string

@description('Tenant ID for RBAC authorization')
param tenantId string = subscription().tenantId

@description('Principal ID of the user-assigned managed identity that reads secrets')
param keyVaultReaderPrincipalId string

@description('Principal ID of the CI/CD identity that reads the DB connection string to run migrations. Leave empty to skip.')
param cicdReaderPrincipalId string = ''

@secure()
@description('Secrets to store, as an object map of secretName: secretValue')
param secrets object

var keyVaultSecretsUserRoleDefinitionId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6')

resource vault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: name
  location: location
  properties: {
    sku: {
      family: 'A'
      name: 'standard'
    }
    tenantId: tenantId
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 7
  }
}

resource vaultSecrets 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = [for secretName in items(secrets): {
  parent: vault
  name: secretName.key
  properties: {
    value: secretName.value
  }
}]

resource secretsUserRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(vault.id, keyVaultReaderPrincipalId, keyVaultSecretsUserRoleDefinitionId)
  scope: vault
  properties: {
    roleDefinitionId: keyVaultSecretsUserRoleDefinitionId
    principalId: keyVaultReaderPrincipalId
    principalType: 'ServicePrincipal'
  }
}

resource cicdSecretsUserRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(cicdReaderPrincipalId)) {
  name: guid(vault.id, cicdReaderPrincipalId, keyVaultSecretsUserRoleDefinitionId)
  scope: vault
  properties: {
    roleDefinitionId: keyVaultSecretsUserRoleDefinitionId
    principalId: cicdReaderPrincipalId
    principalType: 'ServicePrincipal'
  }
}

output uri string = vault.properties.vaultUri
