@description('The location for the resource(s) to be deployed.')
param location string = resourceGroup().location

param value0 string

resource matt_zone 'Microsoft.Network/dnsZones@2018-05-01' existing = {
  name: 'matt.kotsenas.com'
}

resource matt_www_route_ownership 'Microsoft.Network/dnsZones/TXT@2018-05-01' = {
  name: 'asuid.www'
  properties: {
    TXTRecords: [
      {
        value: [
          value0
        ]
      }
    ]
    TTL: 3600
  }
  parent: matt_zone
}

output id string = matt_www_route_ownership.id