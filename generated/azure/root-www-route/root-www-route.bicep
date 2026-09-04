@description('The location for the resource(s) to be deployed.')
param location string = resourceGroup().location

param target string

resource root_zone 'Microsoft.Network/dnsZones@2018-05-01' existing = {
  name: 'kotsenas.com'
}

resource root_www_route 'Microsoft.Network/dnsZones/CNAME@2018-05-01' = {
  name: 'www'
  parent: root_zone
  properties: {
    CNAMERecord: {
      cname: target
    }
    TTL: 3600
  }
}

output id string = root_www_route.id