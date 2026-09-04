@description('The location for the resource(s) to be deployed.')
param location string = resourceGroup().location

param target string

resource root_zone 'Microsoft.Network/dnsZones@2018-05-01' existing = {
  name: 'kotsenas.com'
}

resource root_route 'Microsoft.Network/dnsZones/A@2018-05-01' = {
  name: '@'
  parent: root_zone
  properties: {
    ARecords: [
      {
        ipv4Address: target
      }
    ]
    TTL: 3600
  }
}

output id string = root_route.id