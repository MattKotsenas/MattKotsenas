@description('The location for the resource(s) to be deployed.')
param location string = resourceGroup().location

param userPrincipalId string

resource container_apps_acr 'Microsoft.ContainerRegistry/registries@2025-04-01' = {
  name: take('containerappsacr${uniqueString(resourceGroup().id)}', 50)
  location: location
  sku: {
    name: 'Basic'
  }
  tags: {
    'aspire-resource-name': 'container-apps-acr'
  }
}

resource container_apps_acr_AcrPush 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(container_apps_acr.id, userPrincipalId, subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '8311e382-0749-4cb8-b61a-304f252e45ec'))
  properties: {
    principalId: userPrincipalId
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '8311e382-0749-4cb8-b61a-304f252e45ec')
    principalType: 'ServicePrincipal'
  }
  scope: container_apps_acr
}

output name string = container_apps_acr.name

output loginServer string = container_apps_acr.properties.loginServer

output id string = container_apps_acr.id