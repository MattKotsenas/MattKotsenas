using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using Aspire.Hosting.Azure.AppContainers;
using Aspire.Hosting.Pipelines;
using Azure.ResourceManager;
using Azure.ResourceManager.AppContainers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MattKotsenas.AppHost;

internal static class CustomDomainPipelineExtensions
{
    private const string ValidateDomainsStep =
        "validate-custom-domains";
    private const string RecoverDomainsStep =
        "recover-custom-domains";
    private const string CheckHttpsStep =
        "check-custom-domain-https";

    // Aspire's pipeline API is the intended hook for deployment work.
#pragma warning disable ASPIREPIPELINES001
    internal static void EnsureCustomDomainPipeline(
        this IResourceBuilder<ContainerResource> parent)
    {
        if (parent.Resource.HasAnnotationOfType<
                CustomDomainIntegrationAnnotation>())
        {
            return;
        }

        parent.WithAnnotation(
            new CustomDomainIntegrationAnnotation());
        var services = parent.ApplicationBuilder.Services;
        services.TryAddSingleton<ArmClient>(serviceProvider =>
        {
            var credential = serviceProvider
                .GetRequiredService<ITokenCredentialProvider>()
                .TokenCredential;
            var configuration = serviceProvider
                .GetRequiredService<
                    Microsoft.Extensions.Configuration.IConfiguration>();
            var subscriptionId =
                configuration["Azure:SubscriptionId"]
                ?? throw new InvalidOperationException(
                    "Azure:SubscriptionId is required.");
            return new ArmClient(credential, subscriptionId);
        });
        services.TryAddSingleton<
            IContainerAppControlPlane,
            AzureContainerAppControlPlane>();
        services.TryAddSingleton<
            IDnsValidationRecords,
            AzureDnsValidationRecords>();
        services.TryAddSingleton<
            IHttpsEndpointProbe,
            ResolvedHttpsEndpointProbe>();

        parent.WithPipelineStepFactory(
            ValidateDomainsStep,
            context =>
            {
                var domains = GetDomains(
                    context.Model.Resources,
                    parent.Resource);
                return CreateDeployment(
                        context.Services,
                        parent.Resource)
                    .ValidateAsync(
                        domains,
                        GetManagedCertificates(
                            context.Model.Resources,
                            parent.Resource),
                        context.CancellationToken);
            },
            dependsOn:
            [
                "validate-azure-login",
            ],
            description:
                "Validates the modeled custom-domain set.");

        parent.WithPipelineStepFactory(
            RecoverDomainsStep,
            _ => Task.CompletedTask,
            requiredBy:
            [
                "create-provisioning-context",
            ],
            description:
                "Recovers terminal custom-domain state.");

        parent.WithPipelineStepFactory(
            CheckHttpsStep,
            _ => Task.CompletedTask,
            description:
                "Checks production HTTPS and certificate expiration.");
    }

    internal static void WithManagedCertificatePipeline(
        this IResourceBuilder<CustomDomainCertificateResource>
            certificate)
    {
        var publicationStep =
            PublishValidationStepName(certificate.Resource);
        certificate.WithAnnotation(
            new CustomDomainCertificateReadinessAnnotation(
                publicationStep));
        certificate
            .WithPipelineStepFactory(
                RecoverStepName(certificate.Resource),
                context => CreateDeployment(
                        context.Services,
                        certificate.Resource.Parent.Parent)
                    .RecoverAsync(
                        certificate.Resource,
                        context.CancellationToken),
                dependsOn:
                [
                    ValidateDomainsStep,
                ],
                requiredBy:
                [
                    RecoverDomainsStep,
                ],
                description:
                    $"Recovers terminal certificate state for {certificate.Resource.Parent.Hostname}.")
            .WithPipelineStepFactory(
                publicationStep,
                context => CreateDeployment(
                        context.Services,
                        certificate.Resource.Parent.Parent)
                    .PublishValidationAndWaitForCertificateAsync(
                        certificate.Resource,
                        context.CancellationToken),
                dependsOn:
                [
                    RecoverDomainsStep,
                ],
                requiredBy:
                [
                    "deploy",
                ],
                description:
                    $"Publishes TXT validation for {certificate.Resource.Parent.Hostname}.");
    }

    internal static void WithSelectedCertificatePipeline(
        this IResourceBuilder<CustomDomainResource> domain,
        CustomDomainCertificateResource certificate)
    {
        var dependencies =
            certificate.TryGetLastAnnotation<
                CustomDomainCertificateReadinessAnnotation>(
                out var readiness)
                ? new[]
                {
                    readiness.StepName,
                    "provision-azure-bicep-resources",
                }
                :
                [
                    "provision-azure-bicep-resources",
                ];
        domain
            .WithPipelineStepFactory(
                VerifyStepName(domain.Resource),
                context => CreateDeployment(
                        context.Services,
                        domain.Resource.Parent)
                    .VerifyCurrentDeploymentAsync(
                        domain.Resource,
                        certificate,
                        context.CancellationToken),
                dependsOn: dependencies,
                requiredBy:
                [
                    "deploy",
                ],
                description:
                    $"Verifies the deployment of {domain.Resource.Hostname}.")
            .WithPipelineStepFactory(
                CheckHttpsStepName(domain.Resource),
                context => CustomDomainHttpsHealth.CheckAsync(
                    context,
                    domain.Resource),
                description:
                    $"Checks HTTPS for {domain.Resource.Hostname}.",
                requiredBy:
                [
                    CheckHttpsStep,
                ]);
    }
#pragma warning restore ASPIREPIPELINES001

    private static CustomDomainDeployment CreateDeployment(
        IServiceProvider services,
        ContainerResource parent)
    {
        var configuration = services.GetRequiredService<
            Microsoft.Extensions.Configuration.IConfiguration>();
        var subscriptionId =
            configuration["Azure:SubscriptionId"]
            ?? throw new InvalidOperationException(
                "Azure:SubscriptionId is required.");
        var resourceGroup =
            configuration["Azure:ResourceGroup"]
            ?? throw new InvalidOperationException(
                "Azure:ResourceGroup is required.");
        var environment = parent.GetComputeEnvironment()
            as AzureContainerAppEnvironmentResource
            ?? throw new InvalidOperationException(
                $"Container '{parent.Name}' is not assigned to an Azure Container App environment.");
        var environmentName = environment.Annotations
            .OfType<ContainerAppEnvironmentNameAnnotation>()
            .Single()
            .Name;
        return new(
            services.GetRequiredService<IContainerAppControlPlane>(),
            services.GetRequiredService<IDnsValidationRecords>(),
            services.GetRequiredService<IHttpsEndpointProbe>(),
            services.GetRequiredService<TimeProvider>(),
            ContainerAppResource.CreateResourceIdentifier(
                subscriptionId,
                resourceGroup,
                parent.Name),
            ContainerAppManagedEnvironmentResource
                .CreateResourceIdentifier(
                    subscriptionId,
                    resourceGroup,
                    environmentName));
    }

    private static CustomDomainResource[] GetDomains(
        IEnumerable<IResource> resources,
        ContainerResource parent) =>
        resources
            .OfType<CustomDomainResource>()
            .Where(domain =>
                ReferenceEquals(domain.Parent, parent))
            .OrderBy(domain => domain.Name, StringComparer.Ordinal)
            .ToArray();

    private static CustomDomainCertificateResource[]
        GetManagedCertificates(
            IEnumerable<IResource> resources,
            ContainerResource parent) =>
        resources
            .OfType<CustomDomainCertificateResource>()
            .Where(certificate =>
                ReferenceEquals(
                    certificate.Parent.Parent,
                    parent) &&
                certificate.HasAnnotationOfType<
                    ManagedCertificateAnnotation>())
            .OrderBy(
                certificate => certificate.Name,
                StringComparer.Ordinal)
            .ToArray();

    private static string RecoverStepName(
        CustomDomainCertificateResource certificate) =>
        $"recover-{certificate.Name}";

    private static string PublishValidationStepName(
        CustomDomainCertificateResource certificate) =>
        $"publish-validation-{certificate.Name}";

    private static string VerifyStepName(
        CustomDomainResource domain) =>
        $"verify-{domain.Name}";

    private static string CheckHttpsStepName(
        CustomDomainResource domain) =>
        $"check-https-{domain.Name}";
}
