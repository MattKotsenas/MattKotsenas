@description('The location for the resource(s) to be deployed.')
param location string = resourceGroup().location

param defaultHostName string

param customDomainVerificationId string

param websiteInboundIpAddress string

param legacyRootVerificationId string

resource rootZone 'Microsoft.Network/dnsZones@2018-05-01' existing = {
  name: 'kotsenas.com'
}

resource blogZone 'Microsoft.Network/dnsZones@2018-05-01' existing = {
  name: 'matt.kotsenas.com'
}

resource rootApex 'Microsoft.Network/dnsZones/A@2018-05-01' = {
  name: '@'
  parent: rootZone
  properties: {
    ARecords: [
      {
        ipv4Address: websiteInboundIpAddress
      }
    ]
    TTL: 3600
  }
}

resource rootWww 'Microsoft.Network/dnsZones/CNAME@2018-05-01' = {
  name: 'www'
  parent: rootZone
  properties: {
    CNAMERecord: {
      cname: defaultHostName
    }
    TTL: 3600
  }
}

resource rootLegacyApexVerification 'Microsoft.Network/dnsZones/CNAME@2018-05-01' = {
  name: 'awverify'
  parent: rootZone
  properties: {
    CNAMERecord: {
      cname: 'awverify.${defaultHostName}'
    }
    TTL: 3600
  }
}

resource rootLegacyWwwVerification 'Microsoft.Network/dnsZones/CNAME@2018-05-01' = {
  name: 'awverify.www'
  parent: rootZone
  properties: {
    CNAMERecord: {
      cname: 'awverify.${defaultHostName}'
    }
    TTL: 3600
  }
}

resource rootApexVerification 'Microsoft.Network/dnsZones/TXT@2018-05-01' = {
  name: 'asuid'
  properties: {
    TXTRecords: [
      {
        value: [
          customDomainVerificationId
        ]
      }
      {
        value: [
          legacyRootVerificationId
        ]
      }
    ]
    TTL: 3600
  }
  parent: rootZone
}

resource rootWwwVerification 'Microsoft.Network/dnsZones/TXT@2018-05-01' = {
  name: 'asuid.www'
  properties: {
    TXTRecords: [
      {
        value: [
          customDomainVerificationId
        ]
      }
    ]
    TTL: 3600
  }
  parent: rootZone
}

resource blogApex 'Microsoft.Network/dnsZones/A@2018-05-01' = {
  name: '@'
  parent: blogZone
  properties: {
    ARecords: [
      {
        ipv4Address: websiteInboundIpAddress
      }
    ]
    TTL: 3600
  }
}

resource blogWww 'Microsoft.Network/dnsZones/CNAME@2018-05-01' = {
  name: 'www'
  parent: blogZone
  properties: {
    CNAMERecord: {
      cname: defaultHostName
    }
    TTL: 3600
  }
}

resource blogLegacyApexVerification 'Microsoft.Network/dnsZones/CNAME@2018-05-01' = {
  name: 'awverify'
  parent: blogZone
  properties: {
    CNAMERecord: {
      cname: 'awverify.${defaultHostName}'
    }
    TTL: 3600
  }
}

resource blogLegacyWwwVerification 'Microsoft.Network/dnsZones/CNAME@2018-05-01' = {
  name: 'awverify.www'
  parent: blogZone
  properties: {
    CNAMERecord: {
      cname: 'awverify.${defaultHostName}'
    }
    TTL: 3600
  }
}

resource blogApexVerification 'Microsoft.Network/dnsZones/TXT@2018-05-01' = {
  name: 'asuid'
  properties: {
    TXTRecords: [
      {
        value: [
          customDomainVerificationId
        ]
      }
    ]
    TTL: 3600
  }
  parent: blogZone
}

resource blogWwwVerification 'Microsoft.Network/dnsZones/TXT@2018-05-01' = {
  name: 'asuid.www'
  properties: {
    TXTRecords: [
      {
        value: [
          customDomainVerificationId
        ]
      }
    ]
    TTL: 3600
  }
  parent: blogZone
}