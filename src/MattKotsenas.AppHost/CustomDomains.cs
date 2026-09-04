using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using Aspire.Hosting.Azure.AppContainers;
using Azure.Core;
using Azure.Provisioning;
using Azure.Provisioning.AppContainers;

namespace MattKotsenas.AppHost;

internal sealed class CustomDomainResource(
    string name,
    ContainerResource parent,
    IAzureDnsRoutingRecordResource routingRecord,
    AzureDnsTxtRecordResource ownershipRecord)
    : Resource(name),
      IResourceWithParent<ContainerResource>
{
    public ContainerResource Parent { get; } = parent;

    internal IAzureDnsRoutingRecordResource RoutingRecord { get; } =
        routingRecord;

    internal AzureDnsTxtRecordResource OwnershipRecord { get; } =
        ownershipRecord;

    internal string Hostname => RoutingRecord.Hostname;
}

internal sealed class CustomDomainCertificateResource(
    string name,
    CustomDomainResource parent)
    : Resource(name),
      IResourceWithParent<CustomDomainResource>
{
    public CustomDomainResource Parent { get; } = parent;
}

internal sealed record CustomDomainIntegrationAnnotation
    : IResourceAnnotation;

internal sealed record CustomDomainEnvironmentAnnotation
    : IResourceAnnotation;

internal sealed record ContainerAppEnvironmentNameAnnotation(
    string Name)
    : IResourceAnnotation;

internal sealed record CustomDomainCertificateProviderAnnotation(
    Action<
        AzureResourceInfrastructure,
        ContainerAppCustomDomain> ConfigureBinding,
    Func<
        CustomDomainBindingSnapshot,
        ResourceIdentifier,
        bool> IsAllowedBinding,
    Func<
        CustomDomainBindingSnapshot,
        ResourceIdentifier,
        bool> IsReadyBinding)
    : IResourceAnnotation;

internal sealed record CustomDomainCertificateSelectionAnnotation(
    CustomDomainCertificateResource Certificate)
    : IResourceAnnotation;

internal sealed record ManagedCertificateAnnotation(
    string CertificateName,
    AzureDnsDynamicTxtRecordResource ValidationRecord)
    : IResourceAnnotation;

internal sealed record CustomDomainCertificateReadinessAnnotation(
    string StepName)
    : IResourceAnnotation;

internal static class CustomDomainResourceExtensions
{
    private const string VerificationIdOutput =
        "customDomainVerificationId";

    internal static IResourceBuilder<
        AzureContainerAppEnvironmentResource>
        WithAzureName(
            this IResourceBuilder<
                AzureContainerAppEnvironmentResource> environment,
            string name)
    {
        var annotation =
            new ContainerAppEnvironmentNameAnnotation(name);
        environment.WithAnnotation(
            annotation);
        return environment.ConfigureInfrastructure(
            infrastructure =>
            {
                var managedEnvironment = infrastructure
                    .GetProvisionableResources()
                    .OfType<ContainerAppManagedEnvironment>()
                    .Single();
                managedEnvironment.Name = annotation.Name;
            });
    }

    internal static IResourceBuilder<CustomDomainResource>
        AddCustomDomain<TRecord>(
            this IResourceBuilder<ContainerResource> parent,
            IResourceBuilder<TRecord> routingRecord)
        where TRecord : AzureBicepResource, IAzureDnsRoutingRecordResource
    {
        var environment = parent.Resource.GetComputeEnvironment()
            as AzureContainerAppEnvironmentResource
            ?? throw new InvalidOperationException(
                $"Container '{parent.Resource.Name}' must select an Azure Container App environment before adding custom domains.");
        var environmentBuilder = parent.ApplicationBuilder
            .CreateResourceBuilder(environment)
            .WithCustomDomainOutput();

        var ownershipRecord = parent.ApplicationBuilder.AddTxtRecord(
                routingRecord.Resource.Parent,
                $"{routingRecord.Resource.Name}-ownership",
                GetOwnershipRecordName(
                    routingRecord.Resource.RelativeName))
            .WithValue(
                environmentBuilder.GetOutput(
                    VerificationIdOutput));
        var resource = new CustomDomainResource(
            $"{routingRecord.Resource.Name}-domain",
            parent.Resource,
            routingRecord.Resource,
            ownershipRecord.Resource);
        var domain = parent.ApplicationBuilder
            .AddResource(resource)
            .ExcludeFromManifest();

        parent.WithAnnotation(
            new AzureContainerAppCustomizationAnnotation(
                (infrastructure, containerApp) =>
                {
                    _ = ownershipRecord
                        .GetOutput("id")
                        .AsProvisioningParameter(infrastructure);
                    var binding = new ContainerAppCustomDomain
                    {
                        Name = resource.Hostname,
                        BindingType =
                            ContainerAppCustomDomainBindingType.Disabled,
                    };
                    if (resource.TryGetLastAnnotation<
                            CustomDomainCertificateSelectionAnnotation>(
                            out var selection))
                    {
                        var provider = selection.Certificate.Annotations
                            .OfType<
                                CustomDomainCertificateProviderAnnotation>()
                            .Single();
                        provider.ConfigureBinding(
                            infrastructure,
                            binding);
                    }

                    containerApp.Configuration.Ingress.CustomDomains.Add(
                        binding);
                }),
            ResourceAnnotationMutationBehavior.Append);

        parent.EnsureCustomDomainPipeline();

        return domain;
    }

    internal static IResourceBuilder<CustomDomainCertificateResource>
        AddManagedCertificate(
            this IResourceBuilder<CustomDomainResource> domain,
            string name)
    {
        if (domain.ApplicationBuilder.Resources
            .OfType<CustomDomainCertificateResource>()
            .Where(certificate =>
                ReferenceEquals(
                    certificate.Parent,
                    domain.Resource))
            .Any(certificate =>
                certificate.HasAnnotationOfType<
                    ManagedCertificateAnnotation>()))
        {
            throw new InvalidOperationException(
                $"Custom domain '{domain.Resource.Hostname}' already has an Azure-managed certificate candidate.");
        }

        var validationRecord = domain.ApplicationBuilder
            .AddDynamicTxtRecord(
                domain.Resource.RoutingRecord.Parent,
                $"{domain.Resource.Name}-validation",
                GetValidationRecordName(
                    domain.Resource.RoutingRecord.RelativeName));
        var managedCertificate = new ManagedCertificateAnnotation(
            $"managed-{domain.Resource.Hostname.Replace('.', '-')}",
            validationRecord.Resource);
        var certificate = AddCertificate(
                domain,
                name,
                new CustomDomainCertificateProviderAnnotation(
                    (_, binding) =>
                        binding.BindingType =
                            ContainerAppCustomDomainBindingType.Auto,
                    (binding, environmentId) =>
                        IsManagedBinding(
                            binding,
                            environmentId,
                            managedCertificate.CertificateName,
                            requireCertificate: false),
                    (binding, environmentId) =>
                        IsManagedBinding(
                            binding,
                            environmentId,
                            managedCertificate.CertificateName,
                            requireCertificate: true)))
            .WithAnnotation(managedCertificate);

        var environment = domain.Resource.Parent
            .GetComputeEnvironment()
            as AzureContainerAppEnvironmentResource
            ?? throw new InvalidOperationException(
                $"Container '{domain.Resource.Parent.Name}' is not assigned to an Azure Container App environment.");
        domain.ApplicationBuilder
            .CreateResourceBuilder(domain.Resource.Parent)
            .WithAnnotation(
            new AzureContainerAppCustomizationAnnotation(
                (infrastructure, containerApp) =>
                    containerApp.ConfigureManagedCertificate(
                        infrastructure,
                        environment,
                        domain.Resource,
                        certificate.Resource)),
            ResourceAnnotationMutationBehavior.Append);
        certificate.WithManagedCertificatePipeline();

        return certificate;
    }

    internal static IResourceBuilder<CustomDomainCertificateResource>
        AddCertificate(
            this IResourceBuilder<CustomDomainResource> domain,
            string name,
            CustomDomainCertificateProviderAnnotation provider)
    {
        var certificate = domain.ApplicationBuilder
            .AddResource(
                new CustomDomainCertificateResource(
                    $"{domain.Resource.Name}-{name}",
                    domain.Resource))
            .WithAnnotation(provider)
            .ExcludeFromManifest();
        return certificate;
    }

    internal static IResourceBuilder<CustomDomainResource>
        BindCertificate(
            this IResourceBuilder<CustomDomainResource> domain,
            IResourceBuilder<CustomDomainCertificateResource>
                certificate)
    {
        if (!ReferenceEquals(
                certificate.Resource.Parent,
                domain.Resource))
        {
            throw new InvalidOperationException(
                $"Certificate '{certificate.Resource.Name}' belongs to another custom domain.");
        }

        if (domain.Resource.HasAnnotationOfType<
                CustomDomainCertificateSelectionAnnotation>())
        {
            throw new InvalidOperationException(
                $"Custom domain '{domain.Resource.Hostname}' already has a selected certificate.");
        }

        domain.WithAnnotation(
            new CustomDomainCertificateSelectionAnnotation(
                certificate.Resource));
        domain.WithSelectedCertificatePipeline(
            certificate.Resource);
        return domain;
    }

    internal static IResourceBuilder<CustomDomainResource>
        WithManagedCertificate(
            this IResourceBuilder<CustomDomainResource> domain)
    {
        var certificate = domain.AddManagedCertificate("managed");
        return domain.BindCertificate(certificate);
    }

    internal static IResourceBuilder<AzureDnsTxtRecordResource>
        GetOwnershipRecord(
            this IResourceBuilder<CustomDomainResource> domain) =>
        domain.ApplicationBuilder.CreateResourceBuilder(
            domain.Resource.OwnershipRecord);

    private static IResourceBuilder<
        AzureContainerAppEnvironmentResource>
        WithCustomDomainOutput(
            this IResourceBuilder<
                AzureContainerAppEnvironmentResource> environment)
    {
        if (environment.Resource.HasAnnotationOfType<
                CustomDomainEnvironmentAnnotation>())
        {
            return environment;
        }

        environment.WithAnnotation(
            new CustomDomainEnvironmentAnnotation());
        return environment.ConfigureInfrastructure(
            infrastructure =>
            {
                var managedEnvironment = infrastructure
                    .GetProvisionableResources()
                    .OfType<ContainerAppManagedEnvironment>()
                    .Single();
                infrastructure.Add(new ProvisioningOutput(
                    VerificationIdOutput,
                    typeof(string))
                {
                    Value = managedEnvironment
                        .CustomDomainConfiguration
                        .CustomDomainVerificationId,
                });
            });
    }

    private static DnsRelativeName GetOwnershipRecordName(
        DnsRelativeName relativeName) =>
        relativeName.IsApex
            ? DnsRelativeName.From("asuid")
            : DnsRelativeName.From(
                $"asuid.{relativeName.Value}");

    private static DnsRelativeName GetValidationRecordName(
        DnsRelativeName relativeName) =>
        relativeName.IsApex
            ? DnsRelativeName.From("_dnsauth")
            : DnsRelativeName.From(
                $"_dnsauth.{relativeName.Value}");

    private static bool IsManagedBinding(
        CustomDomainBindingSnapshot binding,
        ResourceIdentifier environmentId,
        string certificateName,
        bool requireCertificate)
    {
        var expectedCertificateId = new ResourceIdentifier(
            $"{environmentId}/managedCertificates/{certificateName}");
        if (requireCertificate &&
            binding.CertificateId is null)
        {
            return false;
        }

        return binding.BindingKind switch
        {
            CustomDomainBindingKind.Auto =>
                binding.CertificateId is null ||
                binding.CertificateId.Equals(expectedCertificateId),
            CustomDomainBindingKind.SniEnabled =>
                binding.CertificateId is not null &&
                binding.CertificateId.Equals(expectedCertificateId),
            _ => false,
        };
    }
}

internal static class CustomDomainModelExtensions
{
    internal static ManagedCertificateAnnotation
        GetManagedCertificate(
            this CustomDomainCertificateResource certificate) =>
        certificate.Annotations
            .OfType<ManagedCertificateAnnotation>()
            .Single();

    internal static string GetManagedCertificateName(
        this CustomDomainCertificateResource certificate) =>
        certificate.GetManagedCertificate().CertificateName;
}
