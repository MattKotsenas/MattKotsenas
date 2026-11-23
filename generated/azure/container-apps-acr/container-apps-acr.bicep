@description('The location for the resource(s) to be deployed.')
param location string = resourceGroup().location

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

output name string = container_apps_acr.name

output loginServer string = container_apps_acr.properties.loginServer

output id string = container_apps_acr.id