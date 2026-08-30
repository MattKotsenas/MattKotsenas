targetScope = 'subscription'

param resourceGroupName string

param location string

param principalId string

param legacyWebResourceGroupName string = 'Default-Web-WestUS'

param dnsResourceGroupName string = 'dns'

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

module blog_dns 'blog-dns/blog-dns.bicep' = {
  name: 'blog-dns'
  scope: resourceGroup(dnsResourceGroupName)
  params: {
    location: location
    defaultHostName: legacy_web.outputs.defaultHostName
    customDomainVerificationId: legacy_web.outputs.customDomainVerificationId
    websiteInboundIpAddress: legacyWebInboundIpAddress
    legacyRootVerificationId: legacyRootVerificationId
  }
}

output container_apps_AZURE_CONTAINER_APPS_ENVIRONMENT_DEFAULT_DOMAIN string = container_apps.outputs.AZURE_CONTAINER_APPS_ENVIRONMENT_DEFAULT_DOMAIN

output container_apps_AZURE_CONTAINER_APPS_ENVIRONMENT_ID string = container_apps.outputs.AZURE_CONTAINER_APPS_ENVIRONMENT_ID

output container_apps_AZURE_CONTAINER_REGISTRY_ENDPOINT string = container_apps.outputs.AZURE_CONTAINER_REGISTRY_ENDPOINT

output container_apps_AZURE_CONTAINER_REGISTRY_MANAGED_IDENTITY_ID string = container_apps.outputs.AZURE_CONTAINER_REGISTRY_MANAGED_IDENTITY_ID