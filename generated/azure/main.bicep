targetScope = 'subscription'

param blogResourceGroupName string = 'Default-Web-WestUS'

param location string

param principalId string

param dnsResourceGroupName string = 'homelab'

param websiteInboundIpAddress string = '168.62.20.37'

resource rg 'Microsoft.Resources/resourceGroups@2023-07-01' = {
  name: blogResourceGroupName
  location: location
}

module blog_existing 'blog-existing/blog-existing.bicep' = {
  name: 'blog-existing'
  scope: rg
  params: {
    location: location
  }
}

module blog_dns 'blog-dns/blog-dns.bicep' = {
  name: 'blog-dns'
  scope: resourceGroup(dnsResourceGroupName)
  params: {
    location: location
    defaultHostName: blog_existing.outputs.defaultHostName
    customDomainVerificationId: blog_existing.outputs.customDomainVerificationId
    websiteInboundIpAddress: websiteInboundIpAddress
  }
}