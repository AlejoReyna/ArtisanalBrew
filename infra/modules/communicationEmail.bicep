@description('Name for the Email Communication Service resource')
param emailServiceName string

@description('Name for the Communication Services resource')
param communicationServiceName string

// Communication Services resources are only available in a handful of "data locations"
// (a logical grouping, not the same as an Azure region) — this is independent of where
// the rest of the stack runs.
@description('Data location for the Communication/Email resources')
param dataLocation string = 'United States'

resource emailService 'Microsoft.Communication/emailServices@2023-04-01' = {
  name: emailServiceName
  location: 'global'
  properties: {
    dataLocation: dataLocation
  }
}

resource managedDomain 'Microsoft.Communication/emailServices/domains@2023-04-01' = {
  parent: emailService
  name: 'AzureManagedDomain'
  location: 'global'
  properties: {
    domainManagement: 'AzureManaged'
  }
}

resource communicationService 'Microsoft.Communication/communicationServices@2023-04-01' = {
  name: communicationServiceName
  location: 'global'
  properties: {
    dataLocation: dataLocation
    linkedDomains: [
      managedDomain.id
    ]
  }
}

#disable-next-line outputs-should-not-contain-secrets
output connectionString string = communicationService.listKeys().primaryConnectionString
output senderAddress string = 'DoNotReply@${managedDomain.properties.mailFromSenderDomain}'
