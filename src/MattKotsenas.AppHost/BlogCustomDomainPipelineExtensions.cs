using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Pipelines;
using Microsoft.Extensions.DependencyInjection;

namespace MattKotsenas.AppHost;

internal static class BlogCustomDomainPipelineExtensions
{
    private const string RecoverDomainsStep =
        "recover-blog-custom-domains";
    private const string PublishValidationStep =
        "publish-blog-domain-validation";
    private const string VerifyDomainsStep =
        "verify-blog-custom-domains";
    private const string CheckHttpsStep =
        "check-blog-https";

    // Aspire's pipeline API is the intended hook for custom deployment work.
#pragma warning disable ASPIREPIPELINES001
    internal static IResourceBuilder<T>
        WithBlogCustomDomainSteps<T>(
            this IResourceBuilder<T> blog,
            string azureSubscriptionId,
            string azureResourceGroup,
            string dnsResourceGroup)
        where T : IResource
    {
        var builder = blog.ApplicationBuilder;
        builder.Services.AddSingleton<ICommandRunner, CliWrapCommandRunner>();
        BlogCustomDomainDeployment CreateDeployment(
            IServiceProvider services) =>
            new(
                services.GetRequiredService<ICommandRunner>(),
                azureSubscriptionId,
                azureResourceGroup,
                dnsResourceGroup,
                blog.Resource.Name);

        return blog
            .WithPipelineStepFactory(
                RecoverDomainsStep,
                context => CreateDeployment(context.Services)
                    .RecoverAsync(context.CancellationToken),
                dependsOn:
                [
                    "validate-azure-login",
                ],
                requiredBy:
                [
                    "create-provisioning-context",
                ],
                description:
                    "Removes unbound terminal managed certificates.")
            .WithPipelineStepFactory(
                PublishValidationStep,
                context => CreateDeployment(context.Services)
                    .PublishValidationAndWaitForCertificatesAsync(
                        context.CancellationToken),
                dependsOn:
                [
                    RecoverDomainsStep,
                ],
                description:
                    "Publishes TXT validation and waits for certificates.")
            .WithPipelineStepFactory(
                VerifyDomainsStep,
                context => CreateDeployment(context.Services)
                    .VerifyCurrentDeploymentAsync(
                        context.CancellationToken),
                dependsOn:
                [
                    PublishValidationStep,
                    "provision-azure-bicep-resources",
                ],
                requiredBy:
                [
                    "deploy",
                ],
                description:
                    "Verifies current custom-domain bindings and HTTPS.")
            .WithPipelineStepFactory(
                CheckHttpsStep,
                BlogHttpsHealth.CheckAsync,
                description:
                    "Checks production HTTPS and certificate expiration.");
    }
#pragma warning restore ASPIREPIPELINES001
}
