targetScope = 'subscription'

param resourceGroupName string

param location string

param principalId string

param legacyWebResourceGroupName string = 'Default-Web-WestUS'

param dnsResourceGroupName string = 'dns'

param rootDnsZoneName string = 'kotsenas.com'

param legacyWebInboundIpAddress string = '168.62.20.37'

param legacyRootVerificationId string = 'F883000E15157DBAA27BE77E3C2BFB8F5B8D3E5BED81331607354AA636C349BE'

param blogDnsZoneName string = 'matt.kotsenas.com'

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

module root_zone 'root-zone/root-zone.bicep' = {
  name: 'root-zone'
  scope: resourceGroup(dnsResourceGroupName)
  params: {
    location: location
    rootDnsZoneName: rootDnsZoneName
    root_apex_address_0: legacyWebInboundIpAddress
    root_www_target_0: legacy_web.outputs.defaultHostName
    root_apex_verification_record_0: legacy_web.outputs.customDomainVerificationId
    root_apex_verification_record_1: legacyRootVerificationId
    root_www_verification_record_0: legacy_web.outputs.customDomainVerificationId
  }
}

module blog_zone 'blog-zone/blog-zone.bicep' = {
  name: 'blog-zone'
  scope: resourceGroup(dnsResourceGroupName)
  params: {
    location: location
    blogDnsZoneName: blogDnsZoneName
    blog_apex_address_0: legacyWebInboundIpAddress
    blog_www_target_0: legacy_web.outputs.defaultHostName
    blog_apex_verification_record_0: legacy_web.outputs.customDomainVerificationId
    blog_www_verification_record_0: legacy_web.outputs.customDomainVerificationId
  }
}

output container_apps_AZURE_CONTAINER_APPS_ENVIRONMENT_DEFAULT_DOMAIN string = container_apps.outputs.AZURE_CONTAINER_APPS_ENVIRONMENT_DEFAULT_DOMAIN

output container_apps_AZURE_CONTAINER_APPS_ENVIRONMENT_ID string = container_apps.outputs.AZURE_CONTAINER_APPS_ENVIRONMENT_ID

output container_apps_AZURE_CONTAINER_REGISTRY_ENDPOINT string = container_apps.outputs.AZURE_CONTAINER_REGISTRY_ENDPOINT

output container_apps_AZURE_CONTAINER_REGISTRY_MANAGED_IDENTITY_ID string = container_apps.outputs.AZURE_CONTAINER_REGISTRY_MANAGED_IDENTITY_ID