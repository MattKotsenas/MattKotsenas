@description('The location for the resource(s) to be deployed.')
param location string = resourceGroup().location

param target string

param root_zone_outputs_name string

resource root_zone 'Microsoft.Network/dnsZones@2018-05-01' existing = {
  name: root_zone_outputs_name
}

resource root_www 'Microsoft.Network/dnsZones/CNAME@2018-05-01' = {
  name: 'www'
  parent: root_zone
  properties: {
    CNAMERecord: {
      cname: target
    }
    TTL: 3600
  }
}

output id string = root_www.id