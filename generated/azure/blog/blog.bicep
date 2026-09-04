@description('The location for the resource(s) to be deployed.')
param location string = resourceGroup().location

param container_apps_outputs_azure_container_apps_environment_default_domain string

param container_apps_outputs_azure_container_apps_environment_id string

param blog_containerimage string

param root_route_ownership_outputs_id string

param container_apps_outputs_azure_container_apps_environment_name string

param root_www_route_ownership_outputs_id string

param matt_route_ownership_outputs_id string

param matt_www_route_ownership_outputs_id string

param container_apps_outputs_azure_container_registry_endpoint string

param container_apps_outputs_azure_container_registry_managed_identity_id string

resource blog 'Microsoft.App/containerApps@2025-07-01' = {
  name: 'blog'
  location: location
  properties: {
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: 8080
        transport: 'http'
        customDomains: [
          {
            name: 'kotsenas.com'
            bindingType: 'Auto'
          }
          {
            name: 'www.kotsenas.com'
            bindingType: 'Auto'
          }
          {
            name: 'matt.kotsenas.com'
            bindingType: 'Auto'
          }
          {
            name: 'www.matt.kotsenas.com'
            bindingType: 'Auto'
          }
        ]
      }
      registries: [
        {
          server: container_apps_outputs_azure_container_registry_endpoint
          identity: container_apps_outputs_azure_container_registry_managed_identity_id
        }
      ]
    }
    environmentId: container_apps_outputs_azure_container_apps_environment_id
    template: {
      containers: [
        {
          image: blog_containerimage
          name: 'blog'
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 1
      }
    }
  }
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${container_apps_outputs_azure_container_registry_managed_identity_id}': { }
    }
  }
}

resource container_apps 'Microsoft.App/managedEnvironments@2025-07-01' existing = {
  name: container_apps_outputs_azure_container_apps_environment_name
}

resource root_route_domain_managed 'Microsoft.App/managedEnvironments/managedCertificates@2025-07-01' = {
  name: 'managed-kotsenas-com'
  location: resourceGroup().location
  properties: {
    subjectName: 'kotsenas.com'
    domainControlValidation: 'TXT'
  }
  parent: container_apps
  dependsOn: [
    blog
  ]
}

resource root_www_route_domain_managed 'Microsoft.App/managedEnvironments/managedCertificates@2025-07-01' = {
  name: 'managed-www-kotsenas-com'
  location: resourceGroup().location
  properties: {
    subjectName: 'www.kotsenas.com'
    domainControlValidation: 'TXT'
  }
  parent: container_apps
  dependsOn: [
    blog
  ]
}

resource matt_route_domain_managed 'Microsoft.App/managedEnvironments/managedCertificates@2025-07-01' = {
  name: 'managed-matt-kotsenas-com'
  location: resourceGroup().location
  properties: {
    subjectName: 'matt.kotsenas.com'
    domainControlValidation: 'TXT'
  }
  parent: container_apps
  dependsOn: [
    blog
  ]
}

resource matt_www_route_domain_managed 'Microsoft.App/managedEnvironments/managedCertificates@2025-07-01' = {
  name: 'managed-www-matt-kotsenas-com'
  location: resourceGroup().location
  properties: {
    subjectName: 'www.matt.kotsenas.com'
    domainControlValidation: 'TXT'
  }
  parent: container_apps
  dependsOn: [
    blog
  ]
}