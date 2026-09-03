using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Pipelines;
using Microsoft.Extensions.DependencyInjection;

namespace MattKotsenas.AppHost;

internal static class BlogCustomDomainPipelineExtensions
{
    private const string ValidateDomainsStep =
        "validate-blog-custom-domains";
    private const string CheckHttpsStep =
        "check-blog-https";

    // Aspire's pipeline API is the intended hook for custom deployment work.
#pragma warning disable ASPIREPIPELINES001
    internal static void WithBlogCustomDomainPipeline<T>(
        this IResourceBuilder<T> blog,
        IReadOnlyList<
            IResourceBuilder<BlogCustomDomainResource>> domains,
        string azureSubscriptionId,
        string azureResourceGroup,
        string dnsResourceGroup)
        where T : IResource
    {
        var builder = blog.ApplicationBuilder;
        var domainResources = domains
            .Select(domain => domain.Resource)
            .ToArray();
        var recoverySteps = domainResources
            .Select(domain => domain.RecoverStepName)
            .ToArray();
        builder.Services.AddSingleton<
            ICommandRunner,
            CliWrapCommandRunner>();

        BlogCustomDomainDeployment CreateDeployment(
            IServiceProvider services) =>
            new(
                services.GetRequiredService<ICommandRunner>(),
                services.GetRequiredService<TimeProvider>(),
                azureSubscriptionId,
                azureResourceGroup,
                dnsResourceGroup,
                blog.Resource.Name);

        blog.WithPipelineStepFactory(
            ValidateDomainsStep,
            context => CreateDeployment(context.Services)
                .ValidateAsync(
                    domainResources,
                    context.CancellationToken),
            dependsOn:
            [
                "validate-azure-login",
            ],
            description:
                "Validates the modeled custom-domain set.");

        foreach (var domain in domains)
        {
            domain
                .WithPipelineStepFactory(
                    domain.Resource.RecoverStepName,
                    context => CreateDeployment(context.Services)
                        .RecoverAsync(
                            domain.Resource,
                            context.CancellationToken),
                    dependsOn:
                    [
                        ValidateDomainsStep,
                    ],
                    requiredBy:
                    [
                        "create-provisioning-context",
                    ],
                    description:
                        $"Recovers terminal certificate state for {domain.Resource.Hostname}.")
                .WithPipelineStepFactory(
                    domain.Resource.PublishValidationStepName,
                    context => CreateDeployment(context.Services)
                        .PublishValidationAndWaitForCertificateAsync(
                            domain.Resource,
                            context.CancellationToken),
                    dependsOn:
                        recoverySteps,
                    description:
                        $"Publishes TXT validation for {domain.Resource.Hostname}.")
                .WithPipelineStepFactory(
                    domain.Resource.VerifyStepName,
                    context => CreateDeployment(context.Services)
                        .VerifyCurrentDeploymentAsync(
                            domain.Resource,
                            context.CancellationToken),
                    dependsOn:
                    [
                        domain.Resource.PublishValidationStepName,
                        "provision-azure-bicep-resources",
                    ],
                    requiredBy:
                    [
                        "deploy",
                    ],
                    description:
                        $"Verifies the deployment of {domain.Resource.Hostname}.")
                .WithPipelineStepFactory(
                    domain.Resource.CheckHttpsStepName,
                    context => BlogHttpsHealth.CheckAsync(
                        context,
                        domain.Resource),
                    description:
                        $"Checks HTTPS for {domain.Resource.Hostname}.");
        }

        blog.WithPipelineStepFactory(
            CheckHttpsStep,
            _ => Task.CompletedTask,
            dependsOn: domainResources
                .Select(domain => domain.CheckHttpsStepName)
                .ToArray(),
            description:
                "Checks production HTTPS and certificate expiration.");
    }
#pragma warning restore ASPIREPIPELINES001
}
