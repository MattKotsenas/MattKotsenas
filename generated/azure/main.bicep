targetScope = 'subscription'

param resourceGroupName string

param location string

param principalId string

resource rg 'Microsoft.Resources/resourceGroups@2023-07-01' = {
  name: resourceGroupName
  location: location
}

module blog_existing 'blog-existing/blog-existing.bicep' = {
  name: 'blog-existing'
  scope: rg
  params: {
    location: location
  }
}