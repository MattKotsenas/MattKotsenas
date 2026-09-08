@description('The location for the resource(s) to be deployed.')
param location string = resourceGroup().location

param blogDnsZoneName string

param blog_apex_address_0 string

param blog_www_target_0 string

param blog_apex_verification_record_0 string

param blog_www_verification_record_0 string

resource blog_zone 'Microsoft.Network/dnsZones@2018-05-01' existing = {
  name: blogDnsZoneName
}

resource blog_apex 'Microsoft.Network/dnsZones/A@2018-05-01' = {
  name: '@'
  parent: blog_zone
  properties: {
    ARecords: [
      {
        ipv4Address: blog_apex_address_0
      }
    ]
    TTL: 3600
  }
}

resource blog_www 'Microsoft.Network/dnsZones/CNAME@2018-05-01' = {
  name: 'www'
  parent: blog_zone
  properties: {
    CNAMERecord: {
      cname: blog_www_target_0
    }
    TTL: 3600
  }
}

resource blog_apex_verification 'Microsoft.Network/dnsZones/TXT@2018-05-01' = {
  name: 'asuid'
  properties: {
    TXTRecords: [
      {
        value: [
          blog_apex_verification_record_0
        ]
      }
    ]
    TTL: 3600
  }
  parent: blog_zone
}

resource blog_www_verification 'Microsoft.Network/dnsZones/TXT@2018-05-01' = {
  name: 'asuid.www'
  properties: {
    TXTRecords: [
      {
        value: [
          blog_www_verification_record_0
        ]
      }
    ]
    TTL: 3600
  }
  parent: blog_zone
}