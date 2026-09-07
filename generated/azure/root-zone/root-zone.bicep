@description('The location for the resource(s) to be deployed.')
param location string = resourceGroup().location

param rootDnsZoneName string

resource root_zone 'Microsoft.Network/dnsZones@2018-05-01' existing = {
  name: rootDnsZoneName
}

output id string = root_zone.id

output name string = rootDnsZoneName