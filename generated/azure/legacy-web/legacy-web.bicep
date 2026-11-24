@description('The location for the resource(s) to be deployed.')
param location string = resourceGroup().location

param legacyWebResourceGroupName string

output defaultHostName string = reference(resourceId(subscription().subscriptionId, legacyWebResourceGroupName, 'Microsoft.Web/sites', 'mattkotsenas'), '2025-03-01').defaultHostName

output customDomainVerificationId string = reference(resourceId(subscription().subscriptionId, legacyWebResourceGroupName, 'Microsoft.Web/sites', 'mattkotsenas'), '2025-03-01').customDomainVerificationId