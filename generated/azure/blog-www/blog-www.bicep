@description('The location for the resource(s) to be deployed.')
param location string = resourceGroup().location

param target string

param blog_zone_outputs_name string

resource blog_zone 'Microsoft.Network/dnsZones@2018-05-01' existing = {
  name: blog_zone_outputs_name
}

resource blog_www 'Microsoft.Network/dnsZones/CNAME@2018-05-01' = {
  name: 'www'
  parent: blog_zone
  properties: {
    CNAMERecord: {
      cname: target
    }
    TTL: 3600
  }
}

output id string = blog_www.id