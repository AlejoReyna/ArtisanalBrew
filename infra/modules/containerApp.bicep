@description('Azure region for the Container App')
param location string

@description('Name of the Container App')
param name string

@description('Resource ID of the Container Apps managed environment')
param containerAppsEnvironmentId string

@description('Resource ID of the user-assigned managed identity used for ACR pull and Key Vault access')
param userAssignedIdentityId string

@description('Login server of the Azure Container Registry, e.g. myregistry.azurecr.io')
param acrLoginServer string

@description('Full container image reference to deploy, e.g. myregistry.azurecr.io/thiscafeteria-web:latest')
param image string

@description('Whether this Container App exposes external HTTP ingress (true for Web, false for Worker)')
param enableIngress bool

@description('Target port the container listens on; ignored when enableIngress is false')
param targetPort int = 8080

@description('vCPU allocation, e.g. 0.5')
param cpu string = '0.5'

@description('Memory allocation, e.g. 1Gi')
param memory string = '1Gi'

@description('Minimum replica count; 0 enables scale-to-zero')
param minReplicas int = 0

@description('Maximum replica count')
param maxReplicas int = 2

@description('Plain (non-secret) environment variables')
param plainEnvVars array = []

@description('Key Vault URI, e.g. https://myvault.vault.azure.net/')
param keyVaultUri string = ''

@description('Environment variables sourced from Key Vault secrets: [{ envVarName: string, keyVaultSecretName: string }]')
param secretEnvVars array = []

@description('Principal ID of the CI/CD identity that deploys new revisions to this Container App. Leave empty to skip.')
param deployerPrincipalId string = ''

var containerAppsContributorRoleDefinitionId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '358470bc-b998-42bd-ab17-a7e34c199c0f')

var secrets = [for item in secretEnvVars: {
  name: item.keyVaultSecretName
  keyVaultUrl: '${keyVaultUri}secrets/${item.keyVaultSecretName}'
  identity: userAssignedIdentityId
}]

var secretEnv = [for item in secretEnvVars: {
  name: item.envVarName
  secretRef: item.keyVaultSecretName
}]

resource containerApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: name
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${userAssignedIdentityId}': {}
    }
  }
  properties: {
    environmentId: containerAppsEnvironmentId
    configuration: {
      activeRevisionsMode: 'Single'
      registries: [
        {
          server: acrLoginServer
          identity: userAssignedIdentityId
        }
      ]
      secrets: secrets
      ingress: enableIngress ? {
        external: true
        targetPort: targetPort
        transport: 'auto'
        allowInsecure: false
      } : null
    }
    template: {
      containers: [
        {
          name: name
          image: image
          resources: {
            cpu: json(cpu)
            memory: memory
          }
          env: concat(plainEnvVars, secretEnv)
        }
      ]
      scale: {
        minReplicas: minReplicas
        maxReplicas: maxReplicas
      }
    }
  }
}

resource deployerRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(deployerPrincipalId)) {
  name: guid(containerApp.id, deployerPrincipalId, containerAppsContributorRoleDefinitionId)
  scope: containerApp
  properties: {
    roleDefinitionId: containerAppsContributorRoleDefinitionId
    principalId: deployerPrincipalId
    principalType: 'ServicePrincipal'
  }
}

output name string = containerApp.name
output fqdn string = enableIngress ? containerApp.properties.configuration.ingress.fqdn : ''
