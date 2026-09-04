targetScope = 'subscription'

param resourceGroupName string

param location string

param principalId string

param legacyWebResourceGroupName string = 'Default-Web-WestUS'

param legacyWebInboundIpAddress string = '168.62.20.37'

param legacyRootVerificationId string = 'F883000E15157DBAA27BE77E3C2BFB8F5B8D3E5BED81331607354AA636C349BE'

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

module legacy_web 'legacy-web/legacy-web.bicep' = {
  name: 'legacy-web'
  scope: resourceGroup(legacyWebResourceGroupName)
  params: {
    location: location
  }
}

module root_route 'root-route/root-route.bicep' = {
  name: 'root-route'
  scope: resourceGroup('269c9734-b273-4b64-ae6c-4e8877fb5f52', 'dns')
  params: {
    location: location
    target: legacyWebInboundIpAddress
  }
}

module root_www_route 'root-www-route/root-www-route.bicep' = {
  name: 'root-www-route'
  scope: resourceGroup('269c9734-b273-4b64-ae6c-4e8877fb5f52', 'dns')
  params: {
    location: location
    target: legacy_web.outputs.defaultHostName
  }
}

module matt_route 'matt-route/matt-route.bicep' = {
  name: 'matt-route'
  scope: resourceGroup('269c9734-b273-4b64-ae6c-4e8877fb5f52', 'dns')
  params: {
    location: location
    target: legacyWebInboundIpAddress
  }
}

module matt_www_route 'matt-www-route/matt-www-route.bicep' = {
  name: 'matt-www-route'
  scope: resourceGroup('269c9734-b273-4b64-ae6c-4e8877fb5f52', 'dns')
  params: {
    location: location
    target: legacy_web.outputs.defaultHostName
  }
}

module root_route_ownership 'root-route-ownership/root-route-ownership.bicep' = {
  name: 'root-route-ownership'
  scope: resourceGroup('269c9734-b273-4b64-ae6c-4e8877fb5f52', 'dns')
  params: {
    location: location
    value0: container_apps.outputs.customDomainVerificationId
    value1: legacyRootVerificationId
  }
}

module root_www_route_ownership 'root-www-route-ownership/root-www-route-ownership.bicep' = {
  name: 'root-www-route-ownership'
  scope: resourceGroup('269c9734-b273-4b64-ae6c-4e8877fb5f52', 'dns')
  params: {
    location: location
    value0: container_apps.outputs.customDomainVerificationId
  }
}

module matt_route_ownership 'matt-route-ownership/matt-route-ownership.bicep' = {
  name: 'matt-route-ownership'
  scope: resourceGroup('269c9734-b273-4b64-ae6c-4e8877fb5f52', 'dns')
  params: {
    location: location
    value0: container_apps.outputs.customDomainVerificationId
  }
}

module matt_www_route_ownership 'matt-www-route-ownership/matt-www-route-ownership.bicep' = {
  name: 'matt-www-route-ownership'
  scope: resourceGroup('269c9734-b273-4b64-ae6c-4e8877fb5f52', 'dns')
  params: {
    location: location
    value0: container_apps.outputs.customDomainVerificationId
  }
}

output container_apps_AZURE_CONTAINER_APPS_ENVIRONMENT_DEFAULT_DOMAIN string = container_apps.outputs.AZURE_CONTAINER_APPS_ENVIRONMENT_DEFAULT_DOMAIN

output container_apps_AZURE_CONTAINER_APPS_ENVIRONMENT_ID string = container_apps.outputs.AZURE_CONTAINER_APPS_ENVIRONMENT_ID

output container_apps_AZURE_CONTAINER_REGISTRY_ENDPOINT string = container_apps.outputs.AZURE_CONTAINER_REGISTRY_ENDPOINT

output container_apps_AZURE_CONTAINER_REGISTRY_MANAGED_IDENTITY_ID string = container_apps.outputs.AZURE_CONTAINER_REGISTRY_MANAGED_IDENTITY_ID

output root_route_ownership_id string = root_route_ownership.outputs.id

output container_apps_AZURE_CONTAINER_APPS_ENVIRONMENT_NAME string = container_apps.outputs.AZURE_CONTAINER_APPS_ENVIRONMENT_NAME

output root_www_route_ownership_id string = root_www_route_ownership.outputs.id

output matt_route_ownership_id string = matt_route_ownership.outputs.id

output matt_www_route_ownership_id string = matt_www_route_ownership.outputs.id