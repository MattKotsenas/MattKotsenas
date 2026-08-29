@description('The location for the resource(s) to be deployed.')
param location string = resourceGroup().location

resource website 'Microsoft.Web/sites@2025-03-01' existing = {
  name: 'mattkotsenas'
}

output defaultHostName string = website.properties.defaultHostName

output customDomainVerificationId string = website.properties.customDomainVerificationId