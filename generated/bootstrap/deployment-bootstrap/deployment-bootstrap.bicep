@description('The location for the resource(s) to be deployed.')
param location string = resourceGroup().location

resource deploymentIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: 'github-MattKotsenas-blog'
  location: location
}

resource githubMain 'Microsoft.ManagedIdentity/userAssignedIdentities/federatedIdentityCredentials@2024-11-30' = {
  name: 'github-main-immutable'
  properties: {
    audiences: [
      'api://AzureADTokenExchange'
    ]
    issuer: 'https://token.actions.githubusercontent.com'
    subject: 'repo:MattKotsenas@51421/MattKotsenas@8395662:ref:refs/heads/main'
  }
  parent: deploymentIdentity
}

resource Contributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(resourceGroup().id, deploymentIdentity.id, 'b24988ac-6180-42a0-ab88-20f7382dd24c')
  properties: {
    principalId: deploymentIdentity.properties.principalId
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'b24988ac-6180-42a0-ab88-20f7382dd24c')
    principalType: 'ServicePrincipal'
  }
}

resource RoleBasedAccessControlAdministrator 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(resourceGroup().id, deploymentIdentity.id, 'f58310d9-a9f6-439a-9e8d-f62e7b41a168')
  properties: {
    principalId: deploymentIdentity.properties.principalId
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'f58310d9-a9f6-439a-9e8d-f62e7b41a168')
    principalType: 'ServicePrincipal'
  }
}

output clientId string = deploymentIdentity.properties.clientId

output principalId string = deploymentIdentity.properties.principalId

output tenantId string = deploymentIdentity.properties.tenantId

output subscriptionId string = subscription().subscriptionId