@description('The location for the resource(s) to be deployed.')
param location string = resourceGroup().location

param deployment_bootstrap_outputs_principalid string

resource dnsDeployments 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(resourceGroup().id, deployment_bootstrap_outputs_principalid, '1c9b6475-caf0-4164-b5a1-2142a7116f4b')
  properties: {
    principalId: deployment_bootstrap_outputs_principalid
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '1c9b6475-caf0-4164-b5a1-2142a7116f4b')
    principalType: 'ServicePrincipal'
  }
}

resource rootZone 'Microsoft.Network/dnsZones@2018-05-01' existing = {
  name: 'kotsenas.com'
}

resource rootZoneContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(rootZone.id, deployment_bootstrap_outputs_principalid, subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'befefa01-2a29-4197-83a8-272ff33ce314'))
  properties: {
    principalId: deployment_bootstrap_outputs_principalid
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'befefa01-2a29-4197-83a8-272ff33ce314')
    principalType: 'ServicePrincipal'
  }
  scope: rootZone
}

resource blogZone 'Microsoft.Network/dnsZones@2018-05-01' existing = {
  name: 'matt.kotsenas.com'
}

resource blogZoneContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(blogZone.id, deployment_bootstrap_outputs_principalid, subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'befefa01-2a29-4197-83a8-272ff33ce314'))
  properties: {
    principalId: deployment_bootstrap_outputs_principalid
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'befefa01-2a29-4197-83a8-272ff33ce314')
    principalType: 'ServicePrincipal'
  }
  scope: blogZone
}