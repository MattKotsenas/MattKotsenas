@description('The location for the resource(s) to be deployed.')
param location string = resourceGroup().location

resource plan 'Microsoft.Web/serverfarms@2025-03-01' existing = {
  name: 'DefaultServerFarm'
}

resource website 'Microsoft.Web/sites@2025-03-01' existing = {
  name: 'mattkotsenas'
}

output websiteId string = website.id

output defaultHostName string = website.properties.defaultHostName

output customDomainVerificationId string = website.properties.customDomainVerificationId

output appServicePlanId string = plan.id