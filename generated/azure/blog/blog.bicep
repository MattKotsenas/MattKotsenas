@description('The location for the resource(s) to be deployed.')
param location string = resourceGroup().location

param container_apps_outputs_azure_container_apps_environment_default_domain string

param container_apps_outputs_azure_container_apps_environment_id string

param blog_containerimage string

param rootCustomDomainCertificateName string

param rootCustomDomain string

param rootWwwCustomDomainCertificateName string

param rootWwwCustomDomain string

param blogCustomDomainCertificateName string

param blogCustomDomain string

param blogWwwCustomDomainCertificateName string

param blogWwwCustomDomain string

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
            name: rootCustomDomain
            bindingType: (rootCustomDomainCertificateName != '') ? 'SniEnabled' : 'Disabled'
            certificateId: (rootCustomDomainCertificateName != '') ? '${container_apps_outputs_azure_container_apps_environment_id}/managedCertificates/${rootCustomDomainCertificateName}' : null
          }
          {
            name: rootWwwCustomDomain
            bindingType: (rootWwwCustomDomainCertificateName != '') ? 'SniEnabled' : 'Disabled'
            certificateId: (rootWwwCustomDomainCertificateName != '') ? '${container_apps_outputs_azure_container_apps_environment_id}/managedCertificates/${rootWwwCustomDomainCertificateName}' : null
          }
          {
            name: blogCustomDomain
            bindingType: (blogCustomDomainCertificateName != '') ? 'SniEnabled' : 'Disabled'
            certificateId: (blogCustomDomainCertificateName != '') ? '${container_apps_outputs_azure_container_apps_environment_id}/managedCertificates/${blogCustomDomainCertificateName}' : null
          }
          {
            name: blogWwwCustomDomain
            bindingType: (blogWwwCustomDomainCertificateName != '') ? 'SniEnabled' : 'Disabled'
            certificateId: (blogWwwCustomDomainCertificateName != '') ? '${container_apps_outputs_azure_container_apps_environment_id}/managedCertificates/${blogWwwCustomDomainCertificateName}' : null
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