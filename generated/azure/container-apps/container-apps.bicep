@description('The location for the resource(s) to be deployed.')
param location string = resourceGroup().location

param userPrincipalId string = ''

param tags object = { }

param container_apps_acr_outputs_name string

resource container_apps_mi 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: take('container_apps_mi-${uniqueString(resourceGroup().id)}', 128)
  location: location
  tags: tags
}

resource container_apps_acr 'Microsoft.ContainerRegistry/registries@2025-04-01' existing = {
  name: container_apps_acr_outputs_name
}

resource container_apps_acr_container_apps_mi_AcrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(container_apps_acr.id, container_apps_mi.id, subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d'))
  properties: {
    principalId: container_apps_mi.properties.principalId
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d')
    principalType: 'ServicePrincipal'
  }
  scope: container_apps_acr
}

resource container_apps_law 'Microsoft.OperationalInsights/workspaces@2025-02-01' = {
  name: take('containerappslaw-${uniqueString(resourceGroup().id)}', 63)
  location: location
  properties: {
    sku: {
      name: 'PerGB2018'
    }
  }
  tags: tags
}

resource container_apps 'Microsoft.App/managedEnvironments@2025-07-01' = {
  name: 'containerapps46vtxkge5it'
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: container_apps_law.properties.customerId
        sharedKey: container_apps_law.listKeys().primarySharedKey
      }
    }
    workloadProfiles: [
      {
        name: 'consumption'
        workloadProfileType: 'Consumption'
      }
    ]
  }
  tags: tags
}

output AZURE_LOG_ANALYTICS_WORKSPACE_NAME string = container_apps_law.name

output AZURE_LOG_ANALYTICS_WORKSPACE_ID string = container_apps_law.id

output AZURE_CONTAINER_REGISTRY_NAME string = container_apps_acr.name

output AZURE_CONTAINER_REGISTRY_ENDPOINT string = container_apps_acr.properties.loginServer

output AZURE_CONTAINER_REGISTRY_MANAGED_IDENTITY_ID string = container_apps_mi.id

output AZURE_CONTAINER_APPS_ENVIRONMENT_NAME string = container_apps.name

output AZURE_CONTAINER_APPS_ENVIRONMENT_ID string = container_apps.id

output AZURE_CONTAINER_APPS_ENVIRONMENT_DEFAULT_DOMAIN string = container_apps.properties.defaultDomain

output customDomainVerificationId string = container_apps.properties.customDomainConfiguration.customDomainVerificationId