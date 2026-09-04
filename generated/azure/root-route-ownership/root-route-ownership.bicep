@description('The location for the resource(s) to be deployed.')
param location string = resourceGroup().location

param value0 string

param value1 string

resource root_zone 'Microsoft.Network/dnsZones@2018-05-01' existing = {
  name: 'kotsenas.com'
}

resource root_route_ownership 'Microsoft.Network/dnsZones/TXT@2018-05-01' = {
  name: 'asuid'
  properties: {
    TXTRecords: [
      {
        value: [
          value0
        ]
      }
      {
        value: [
          value1
        ]
      }
    ]
    TTL: 3600
  }
  parent: root_zone
}

output id string = root_route_ownership.id