using Aspire.Hosting.Azure;
using Aspire.Hosting.Azure.AppContainers;
using Azure.Provisioning;
using Azure.Provisioning.AppContainers;
using Azure.Provisioning.Expressions;

namespace MattKotsenas.AppHost;

internal static class CustomDomainProvisioningExtensions
{
    internal static void ConfigureManagedCertificate(
        this ContainerApp containerApp,
        AzureResourceInfrastructure infrastructure,
        AzureContainerAppEnvironmentResource environment,
        CustomDomainResource domain,
        CustomDomainCertificateResource certificate)
    {
        var managedEnvironment =
            (ContainerAppManagedEnvironment)environment
                .AddAsExistingResource(infrastructure);
        var managedCertificate = new ContainerAppManagedCertificate(
            Infrastructure.NormalizeBicepIdentifier(
                certificate.Name))
        {
            Parent = managedEnvironment,
            Name = certificate
                .GetManagedCertificate()
                .CertificateName,
            Location = BicepFunction.GetResourceGroup().Location,
            Properties = new ManagedCertificateProperties
            {
                SubjectName = domain.Hostname,
                DomainControlValidation =
                    ManagedCertificateDomainControlValidation.TXT,
            },
        };
        managedCertificate.DependsOn.Add(containerApp);
        infrastructure.Add(managedCertificate);
    }
}
