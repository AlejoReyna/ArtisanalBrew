@description('Azure region for the workspace')
param location string

@description('Name of the Log Analytics workspace')
param name string

resource workspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: name
  location: location
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
  }
}

output id string = workspace.id
output customerId string = workspace.properties.customerId

#disable-next-line outputs-should-not-contain-secrets
output sharedKey string = workspace.listKeys().primarySharedKey
