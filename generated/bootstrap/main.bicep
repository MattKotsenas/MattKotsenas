targetScope = 'subscription'

param resourceGroupName string

param location string

param principalId string

param dnsResourceGroupName string = 'dns'

param legacyWebResourceGroupName string = 'Default-Web-WestUS'

resource rg 'Microsoft.Resources/resourceGroups@2023-07-01' = {
  name: resourceGroupName
  location: location
}

module deployment_bootstrap 'deployment-bootstrap/deployment-bootstrap.bicep' = {
  name: 'deployment-bootstrap'
  scope: rg
  params: {
    location: location
  }
}

module migration_dns_permissions 'migration-dns-permissions/migration-dns-permissions.bicep' = {
  name: 'migration-dns-permissions'
  scope: resourceGroup(dnsResourceGroupName)
  params: {
    location: location
    deployment_bootstrap_outputs_principalid: deployment_bootstrap.outputs.principalId
  }
}

module migration_legacy_reader 'migration-legacy-reader/migration-legacy-reader.bicep' = {
  name: 'migration-legacy-reader'
  scope: resourceGroup(legacyWebResourceGroupName)
  params: {
    location: location
    deployment_bootstrap_outputs_principalid: deployment_bootstrap.outputs.principalId
  }
}