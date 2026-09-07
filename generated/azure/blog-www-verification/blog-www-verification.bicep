@description('The location for the resource(s) to be deployed.')
param location string = resourceGroup().location

param blog_zone_outputs_name string

param value0 string

resource blog_zone 'Microsoft.Network/dnsZones@2018-05-01' existing = {
  name: blog_zone_outputs_name
}

resource blog_www_verification 'Microsoft.Network/dnsZones/TXT@2018-05-01' = {
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
  parent: blog_zone
}

output id string = blog_www_verification.id