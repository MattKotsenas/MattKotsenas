@description('The location for the resource(s) to be deployed.')
param location string = resourceGroup().location

param target string

resource matt_zone 'Microsoft.Network/dnsZones@2018-05-01' existing = {
  name: 'matt.kotsenas.com'
}

resource matt_route 'Microsoft.Network/dnsZones/A@2018-05-01' = {
  name: '@'
  parent: matt_zone
  properties: {
    ARecords: [
      {
        ipv4Address: target
      }
    ]
    TTL: 3600
  }
}

output id string = matt_route.id