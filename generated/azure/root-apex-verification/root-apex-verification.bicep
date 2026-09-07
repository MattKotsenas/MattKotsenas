@description('The location for the resource(s) to be deployed.')
param location string = resourceGroup().location

param root_zone_outputs_name string

param value0 string

param value1 string

resource root_zone 'Microsoft.Network/dnsZones@2018-05-01' existing = {
  name: root_zone_outputs_name
}

resource root_apex_verification 'Microsoft.Network/dnsZones/TXT@2018-05-01' = {
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

output id string = root_apex_verification.id