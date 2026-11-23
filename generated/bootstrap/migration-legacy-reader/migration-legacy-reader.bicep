@description('The location for the resource(s) to be deployed.')
param location string = resourceGroup().location

param deployment_bootstrap_outputs_principalid string

resource legacySite 'Microsoft.Web/sites@2025-03-01' existing = {
  name: 'mattkotsenas'
}

resource legacyReader 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(legacySite.id, deployment_bootstrap_outputs_principalid, subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'acdd72a7-3385-48ef-bd42-f606fba81ae7'))
  properties: {
    principalId: deployment_bootstrap_outputs_principalid
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'acdd72a7-3385-48ef-bd42-f606fba81ae7')
    principalType: 'ServicePrincipal'
  }
  scope: legacySite
}