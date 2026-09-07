@description('The location for the resource(s) to be deployed.')
param location string = resourceGroup().location

param blogDnsZoneName string

resource blog_zone 'Microsoft.Network/dnsZones@2018-05-01' existing = {
  name: blogDnsZoneName
}

output id string = blog_zone.id

output name string = blogDnsZoneName