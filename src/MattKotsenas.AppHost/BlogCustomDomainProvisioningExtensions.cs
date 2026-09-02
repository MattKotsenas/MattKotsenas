using Aspire.Hosting.Azure;
using Aspire.Hosting.Azure.AppContainers;
using Azure.Provisioning.AppContainers;
using Azure.Provisioning.Expressions;

namespace MattKotsenas.AppHost;

internal static class BlogCustomDomainProvisioningExtensions
{
    internal static void ConfigureBlogCustomDomains(
        this ContainerApp containerApp,
        AzureResourceInfrastructure infrastructure,
        AzureContainerAppEnvironmentResource environment)
    {
        var managedEnvironment =
            (ContainerAppManagedEnvironment)environment
                .AddAsExistingResource(infrastructure);

        foreach (var domain in BlogCustomDomains.All)
        {
            containerApp.Configuration.Ingress.CustomDomains.Add(
                new ContainerAppCustomDomain
                {
                    Name = domain.Hostname,
                    BindingType =
                        ContainerAppCustomDomainBindingType.Auto,
                });

            var certificate = new ContainerAppManagedCertificate(
                domain.BicepIdentifier)
            {
                Parent = managedEnvironment,
                Name = domain.CertificateName,
                Location = BicepFunction.GetResourceGroup().Location,
                Properties = new ManagedCertificateProperties
                {
                    SubjectName = domain.Hostname,
                    DomainControlValidation =
                        ManagedCertificateDomainControlValidation.TXT,
                },
            };
            certificate.DependsOn.Add(containerApp);
            infrastructure.Add(certificate);
        }
    }
}
