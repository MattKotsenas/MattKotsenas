@description('The location for the resource(s) to be deployed.')
param location string = resourceGroup().location

param rootDnsZoneName string

param root_apex_address_0 string

param root_www_target_0 string

param root_apex_verification_record_0 string

param root_apex_verification_record_1 string

param root_www_verification_record_0 string

resource root_zone 'Microsoft.Network/dnsZones@2018-05-01' existing = {
  name: rootDnsZoneName
}

resource root_apex 'Microsoft.Network/dnsZones/A@2018-05-01' = {
  name: '@'
  parent: root_zone
  properties: {
    ARecords: [
      {
        ipv4Address: root_apex_address_0
      }
    ]
    TTL: 3600
  }
}

resource root_www 'Microsoft.Network/dnsZones/CNAME@2018-05-01' = {
  name: 'www'
  parent: root_zone
  properties: {
    CNAMERecord: {
      cname: root_www_target_0
    }
    TTL: 3600
  }
}

resource root_apex_verification 'Microsoft.Network/dnsZones/TXT@2018-05-01' = {
  name: 'asuid'
  properties: {
    TXTRecords: [
      {
        value: [
          root_apex_verification_record_0
        ]
      }
      {
        value: [
          root_apex_verification_record_1
        ]
      }
    ]
    TTL: 3600
  }
  parent: root_zone
}

resource root_www_verification 'Microsoft.Network/dnsZones/TXT@2018-05-01' = {
  name: 'asuid.www'
  properties: {
    TXTRecords: [
      {
        value: [
          root_www_verification_record_0
        ]
      }
    ]
    TTL: 3600
  }
  parent: root_zone
}