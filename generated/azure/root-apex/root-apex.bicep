@description('The location for the resource(s) to be deployed.')
param location string = resourceGroup().location

param target string

param root_zone_outputs_name string

resource root_zone 'Microsoft.Network/dnsZones@2018-05-01' existing = {
  name: root_zone_outputs_name
}

resource root_apex 'Microsoft.Network/dnsZones/A@2018-05-01' = {
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

output id string = root_apex.id