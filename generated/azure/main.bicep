targetScope = 'subscription'

param resourceGroupName string

param location string

param principalId string

resource rg 'Microsoft.Resources/resourceGroups@2023-07-01' = {
  name: resourceGroupName
  location: location
}

module container_apps_acr 'container-apps-acr/container-apps-acr.bicep' = {
  name: 'container-apps-acr'
  scope: rg
  params: {
    location: location
    userPrincipalId: principalId
  }
}

module container_apps 'container-apps/container-apps.bicep' = {
  name: 'container-apps'
  scope: rg
  params: {
    location: location
    container_apps_acr_outputs_name: container_apps_acr.outputs.name
    userPrincipalId: principalId
  }
}

output container_apps_AZURE_CONTAINER_APPS_ENVIRONMENT_DEFAULT_DOMAIN string = container_apps.outputs.AZURE_CONTAINER_APPS_ENVIRONMENT_DEFAULT_DOMAIN

output container_apps_AZURE_CONTAINER_APPS_ENVIRONMENT_ID string = container_apps.outputs.AZURE_CONTAINER_APPS_ENVIRONMENT_ID

output container_apps_AZURE_CONTAINER_REGISTRY_ENDPOINT string = container_apps.outputs.AZURE_CONTAINER_REGISTRY_ENDPOINT

output container_apps_AZURE_CONTAINER_REGISTRY_MANAGED_IDENTITY_ID string = container_apps.outputs.AZURE_CONTAINER_REGISTRY_MANAGED_IDENTITY_ID