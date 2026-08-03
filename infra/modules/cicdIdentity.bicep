@description('Azure region for the identity')
param location string

@description('Name of the CI/CD user-assigned managed identity')
param name string

@description('GitHub repository in owner/repo form, e.g. AlejoReyna/ArtisanalBrew')
param githubRepo string

@description('Branch this identity is allowed to authenticate from')
param githubBranch string = 'main'

@description('Protected GitHub environment allowed to authenticate for production deployments')
param githubEnvironment string = 'production'

resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: name
  location: location
}

resource federatedCredential 'Microsoft.ManagedIdentity/userAssignedIdentities/federatedIdentityCredentials@2023-01-31' = {
  parent: identity
  name: 'github-actions-${githubBranch}'
  properties: {
    issuer: 'https://token.actions.githubusercontent.com'
    subject: 'repo:${githubRepo}:ref:refs/heads/${githubBranch}'
    audiences: [
      'api://AzureADTokenExchange'
    ]
  }
}

// Jobs that reference a GitHub environment receive an environment-scoped OIDC
// subject instead of the branch-scoped subject above. Keep both credentials:
// the branch subject supports existing non-environment automation, while this
// narrower subject enables the protected production approval boundary.
resource environmentFederatedCredential 'Microsoft.ManagedIdentity/userAssignedIdentities/federatedIdentityCredentials@2023-01-31' = {
  parent: identity
  name: 'github-actions-environment-${githubEnvironment}'
  properties: {
    issuer: 'https://token.actions.githubusercontent.com'
    subject: 'repo:${githubRepo}:environment:${githubEnvironment}'
    audiences: [
      'api://AzureADTokenExchange'
    ]
  }
}

output id string = identity.id
output principalId string = identity.properties.principalId
output clientId string = identity.properties.clientId
