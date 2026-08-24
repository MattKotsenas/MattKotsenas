@description('The location for the resource(s) to be deployed.')
param location string = resourceGroup().location

resource plan 'Microsoft.Web/serverfarms@2025-03-01' = {
  name: 'DefaultServerFarm'
  location: 'westus'
  kind: 'app'
  sku: {
    name: 'B1'
    tier: 'Basic'
    size: 'B1'
    family: 'B'
    capacity: 1
  }
}

resource website 'Microsoft.Web/sites@2025-03-01' = {
  name: 'mattkotsenas'
  location: 'westus'
  properties: {
    serverFarmId: plan.id
    clientCertMode: 'Required'
    clientAffinityEnabled: true
    clientCertEnabled: false
    enabled: true
    httpsOnly: true
    reserved: false
    publicNetworkAccess: 'Enabled'
    siteConfig: {
      netFrameworkVersion: 'v4.0'
      localMySqlEnabled: false
    }
  }
  kind: 'app'
}

resource rootCertificate 'Microsoft.Web/certificates@2025-03-01' existing = {
  name: 'kotsenas.com-mattkotsenas'
}

resource rootBinding 'Microsoft.Web/sites/hostNameBindings@2025-03-01' = {
  name: 'kotsenas.com'
  properties: {
    hostNameType: 'Verified'
    sslState: 'SniEnabled'
    thumbprint: rootCertificate.properties.thumbprint
  }
  parent: website
}

resource rootWwwCertificate 'Microsoft.Web/certificates@2025-03-01' existing = {
  name: 'www.kotsenas.com-mattkotsenas'
}

resource rootWwwBinding 'Microsoft.Web/sites/hostNameBindings@2025-03-01' = {
  name: 'www.kotsenas.com'
  properties: {
    hostNameType: 'Verified'
    sslState: 'SniEnabled'
    thumbprint: rootWwwCertificate.properties.thumbprint
  }
  parent: website
}

resource blogCertificate 'Microsoft.Web/certificates@2025-03-01' existing = {
  name: 'matt.kotsenas.com-mattkotsenas'
}

resource blogBinding 'Microsoft.Web/sites/hostNameBindings@2025-03-01' = {
  name: 'matt.kotsenas.com'
  properties: {
    hostNameType: 'Verified'
    sslState: 'SniEnabled'
    thumbprint: blogCertificate.properties.thumbprint
  }
  parent: website
}

resource blogWwwCertificate 'Microsoft.Web/certificates@2025-03-01' existing = {
  name: 'www.matt.kotsenas.com-mattkotsenas'
}

resource blogWwwBinding 'Microsoft.Web/sites/hostNameBindings@2025-03-01' = {
  name: 'www.matt.kotsenas.com'
  properties: {
    hostNameType: 'Verified'
    sslState: 'SniEnabled'
    thumbprint: blogWwwCertificate.properties.thumbprint
  }
  parent: website
}

output websiteId string = website.id

output defaultHostName string = website.properties.defaultHostName

output customDomainVerificationId string = website.properties.customDomainVerificationId

output appServicePlanId string = plan.id