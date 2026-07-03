@description('Azure region for the Service Bus namespace')
param location string

@description('Globally unique name for the Service Bus namespace')
param name string

@description('Principal ID of the user-assigned managed identity that sends/receives messages')
param dataAccessPrincipalId string

@description('Queue used for the wallet login/status feature (replaces the AWS SQS wallet-status queue)')
param walletStatusQueueName string = 'wallet-status'

@description('Queue used for order-processing messages consumed by the Worker')
param orderProcessingQueueName string = 'order-processing'

var serviceBusDataSenderRoleDefinitionId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '69a216fc-b8fb-44d8-bc22-1f3c2cd27a39')
var serviceBusDataReceiverRoleDefinitionId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4f6d3b9b-027b-4f4c-9142-0e5a2a2247e0')

resource namespace 'Microsoft.ServiceBus/namespaces@2021-11-01' = {
  name: name
  location: location
  sku: {
    name: 'Basic'
    tier: 'Basic'
  }
}

resource walletStatusQueue 'Microsoft.ServiceBus/namespaces/queues@2021-11-01' = {
  parent: namespace
  name: walletStatusQueueName
}

resource orderProcessingQueue 'Microsoft.ServiceBus/namespaces/queues@2021-11-01' = {
  parent: namespace
  name: orderProcessingQueueName
}

resource dataSenderRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(namespace.id, dataAccessPrincipalId, serviceBusDataSenderRoleDefinitionId)
  scope: namespace
  properties: {
    roleDefinitionId: serviceBusDataSenderRoleDefinitionId
    principalId: dataAccessPrincipalId
    principalType: 'ServicePrincipal'
  }
}

resource dataReceiverRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(namespace.id, dataAccessPrincipalId, serviceBusDataReceiverRoleDefinitionId)
  scope: namespace
  properties: {
    roleDefinitionId: serviceBusDataReceiverRoleDefinitionId
    principalId: dataAccessPrincipalId
    principalType: 'ServicePrincipal'
  }
}

output fullyQualifiedNamespace string = '${namespace.name}.servicebus.windows.net'
output walletStatusQueueName string = walletStatusQueue.name
output orderProcessingQueueName string = orderProcessingQueue.name
