@description('The location for the resource(s) to be deployed.')
param location string = resourceGroup().location

param container_apps_outputs_azure_container_apps_environment_default_domain string

param container_apps_outputs_azure_container_apps_environment_id string

param blog_containerimage string

param rootCertificateName string

param rootDomain string

param rootWwwCertificateName string

param rootWwwDomain string

param blogCertificateName string

param blogDomain string

param blogWwwCertificateName string

param blogWwwDomain string

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
            name: rootDomain
            bindingType: (rootCertificateName != '') ? 'SniEnabled' : 'Disabled'
            certificateId: (rootCertificateName != '') ? '${container_apps_outputs_azure_container_apps_environment_id}/managedCertificates/${rootCertificateName}' : null
          }
          {
            name: rootWwwDomain
            bindingType: (rootWwwCertificateName != '') ? 'SniEnabled' : 'Disabled'
            certificateId: (rootWwwCertificateName != '') ? '${container_apps_outputs_azure_container_apps_environment_id}/managedCertificates/${rootWwwCertificateName}' : null
          }
          {
            name: blogDomain
            bindingType: (blogCertificateName != '') ? 'SniEnabled' : 'Disabled'
            certificateId: (blogCertificateName != '') ? '${container_apps_outputs_azure_container_apps_environment_id}/managedCertificates/${blogCertificateName}' : null
          }
          {
            name: blogWwwDomain
            bindingType: (blogWwwCertificateName != '') ? 'SniEnabled' : 'Disabled'
            certificateId: (blogWwwCertificateName != '') ? '${container_apps_outputs_azure_container_apps_environment_id}/managedCertificates/${blogWwwCertificateName}' : null
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