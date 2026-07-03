@description('Azure region for the Container Apps environment')
param location string

@description('Name of the Container Apps managed environment')
param name string

@description('Log Analytics workspace customer ID')
param logAnalyticsCustomerId string

@secure()
@description('Log Analytics workspace shared key')
param logAnalyticsSharedKey string

resource environment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: name
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalyticsCustomerId
        sharedKey: logAnalyticsSharedKey
      }
    }
    // Consumption-only environment (no dedicated workload profiles, no custom VNet) to
    // stay on the cheapest Container Apps billing model for an Azure for Students subscription.
    workloadProfiles: [
      {
        name: 'Consumption'
        workloadProfileType: 'Consumption'
      }
    ]
  }
}

output id string = environment.id
output defaultDomain string = environment.properties.defaultDomain
