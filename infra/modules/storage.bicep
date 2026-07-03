@description('Azure region for the storage account')
param location string

@description('Globally unique name for the storage account (lowercase alphanumeric, 3-24 chars)')
param name string

@description('Principal ID of the user-assigned managed identity that reads/writes blobs')
param dataContributorPrincipalId string

@description('Name of the blob container used for receipt PDFs')
param receiptsContainerName string = 'receipts'

var storageBlobDataContributorRoleDefinitionId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-01-01' = {
  name: name
  location: location
  kind: 'StorageV2'
  sku: {
    name: 'Standard_LRS'
  }
  properties: {
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-01-01' = {
  parent: storageAccount
  name: 'default'
}

resource receiptsContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-01-01' = {
  parent: blobService
  name: receiptsContainerName
  properties: {
    publicAccess: 'None'
  }
}

resource blobDataContributorRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storageAccount.id, dataContributorPrincipalId, storageBlobDataContributorRoleDefinitionId)
  scope: storageAccount
  properties: {
    roleDefinitionId: storageBlobDataContributorRoleDefinitionId
    principalId: dataContributorPrincipalId
    principalType: 'ServicePrincipal'
  }
}

output blobEndpoint string = storageAccount.properties.primaryEndpoints.blob
output name string = storageAccount.name
output receiptsContainerName string = receiptsContainer.name
