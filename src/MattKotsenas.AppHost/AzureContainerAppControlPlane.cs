using Azure;
using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.AppContainers;
using Azure.ResourceManager.AppContainers.Models;

namespace MattKotsenas.AppHost;

internal sealed class AzureContainerAppControlPlane(ArmClient armClient)
    : IContainerAppControlPlane
{
    public async Task<ContainerAppSnapshot?> GetAppAsync(
        ResourceIdentifier appId,
        CancellationToken cancellationToken)
    {
        try
        {
            var app = (await armClient
                .GetContainerAppResource(appId)
                .GetAsync(cancellationToken))
                .Value;
            var bindings = app.Data.Configuration?
                .Ingress?
                .CustomDomains
                .Select(binding => new CustomDomainBindingSnapshot(
                    binding.Name,
                    ToBindingKind(binding.BindingType),
                    binding.CertificateId))
                .ToArray()
                ?? [];
            return new(
                app.Data.EnvironmentId,
                bindings);
        }
        catch (RequestFailedException exception)
            when (exception.Status == 404)
        {
            return null;
        }
    }

    public async Task<ContainerAppEnvironmentSnapshot>
        GetEnvironmentAsync(
            ResourceIdentifier environmentId,
            CancellationToken cancellationToken)
    {
        var environment = (await armClient
            .GetContainerAppManagedEnvironmentResource(environmentId)
            .GetAsync(cancellationToken))
            .Value;
        return new(
            environment.Data.StaticIP);
    }

    public async Task<IReadOnlyList<ManagedCertificateSnapshot>>
        GetManagedCertificatesAsync(
            ResourceIdentifier environmentId,
            CancellationToken cancellationToken)
    {
        var environment = armClient
            .GetContainerAppManagedEnvironmentResource(environmentId);
        var certificates =
            new List<ManagedCertificateSnapshot>();
        await foreach (var certificate in environment
            .GetContainerAppManagedCertificates()
            .GetAllAsync(cancellationToken))
        {
            var properties = certificate.Data.Properties;
            certificates.Add(new(
                certificate.Id,
                ToCertificateState(properties.ProvisioningState),
                properties.ValidationToken,
                properties.Error));
        }

        return certificates;
    }

    public Task DeleteManagedCertificateAsync(
        ResourceIdentifier certificateId,
        CancellationToken cancellationToken) =>
        armClient
            .GetContainerAppManagedCertificateResource(certificateId)
            .DeleteAsync(
                WaitUntil.Completed,
                cancellationToken);

    private static CustomDomainBindingKind ToBindingKind(
        ContainerAppCustomDomainBindingType? bindingType) =>
        bindingType switch
        {
            var value when value ==
                ContainerAppCustomDomainBindingType.Disabled =>
                CustomDomainBindingKind.Disabled,
            var value when value ==
                ContainerAppCustomDomainBindingType.Auto =>
                CustomDomainBindingKind.Auto,
            var value when value ==
                ContainerAppCustomDomainBindingType.SniEnabled =>
                CustomDomainBindingKind.SniEnabled,
            _ => CustomDomainBindingKind.Unknown,
        };

    private static ManagedCertificateState ToCertificateState(
        ContainerAppCertificateProvisioningState? state) =>
        state switch
        {
            var value when value ==
                ContainerAppCertificateProvisioningState.Pending =>
                ManagedCertificateState.Pending,
            var value when value ==
                ContainerAppCertificateProvisioningState.Succeeded =>
                ManagedCertificateState.Succeeded,
            var value when value ==
                ContainerAppCertificateProvisioningState.Failed =>
                ManagedCertificateState.Failed,
            var value when value ==
                ContainerAppCertificateProvisioningState.Canceled =>
                ManagedCertificateState.Canceled,
            var value when value ==
                ContainerAppCertificateProvisioningState.Deleting =>
                ManagedCertificateState.Deleting,
            var value when value ==
                ContainerAppCertificateProvisioningState.DeleteFailed =>
                ManagedCertificateState.DeleteFailed,
            _ => ManagedCertificateState.Unknown,
        };
}
