using Aspire.Hosting;
using Aspire.Hosting.Azure;
using Aspire.Hosting.Pipelines;

namespace MattKotsenas.AppHost.Tests;

public sealed class CustomDomainTests
{
    [Theory]
    [InlineData("rootCustomDomain", "kotsenas.com")]
    [InlineData("rootWwwCustomDomain", "www.kotsenas.com")]
    [InlineData("blogCustomDomain", "matt.kotsenas.com")]
    [InlineData("blogWwwCustomDomain", "www.matt.kotsenas.com")]
    public async Task CertificateBindingRemainsDisabledUntilCertificateIsAssigned(
        string domainParameterName,
        string hostname)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.MattKotsenas_AppHost>(
                [
                    "--operation", "publish",
                    "DeploymentPrincipalId=55555555-5555-5555-5555-555555555555",
                ],
                cancellationToken);
        appHost.Services.Configure<PipelineOptions>(
            options => options.Step = WellKnownPipelineSteps.BeforeStart);
        await using var app = await appHost.BuildAsync(cancellationToken);
        var model = app.Services.GetRequiredService<DistributedApplicationModel>();
        await app.StartAsync(cancellationToken);
        var domain = Assert.Single(
            model.Resources.OfType<ParameterResource>(),
            parameter => parameter.Name == domainParameterName);
        var certificate = Assert.Single(
            model.Resources.OfType<ParameterResource>(),
            parameter => parameter.Name == $"{domainParameterName}CertificateName");

        Assert.Equal(
            hostname,
            await domain.GetValueAsync(cancellationToken));
        Assert.Equal(
            string.Empty,
            await certificate.GetValueAsync(cancellationToken));

        await app.Services.GetRequiredService<ParameterProcessor>()
            .InitializeParametersAsync([domain, certificate]);
        Assert.Equal(
            string.Empty,
            await certificate.GetValueAsync(cancellationToken));

        var blog = Assert.Single(
            model.Resources,
            resource => resource.Name == "blog");
        var target = Assert.Single(
            blog.Annotations.OfType<DeploymentTargetAnnotation>());
        var template = Assert
            .IsAssignableFrom<AzureBicepResource>(target.DeploymentTarget)
            .GetBicepTemplateString();

        Assert.Contains($"name: {domain.Name}", template);
        Assert.Contains(
            $"bindingType: ({certificate.Name} != '') ? 'SniEnabled' : 'Disabled'",
            template);
        Assert.Contains(
            $"certificateId: ({certificate.Name} != '') ? '${{container_apps_outputs_azure_container_apps_environment_id}}/managedCertificates/${{{certificate.Name}}}' : null",
            template);

        var certificateName = $"{hostname}-certificate";
        await certificate.SetValueAsync(certificateName, cancellationToken);
        Assert.Equal(
            certificateName,
            await certificate.GetValueAsync(cancellationToken));

        await certificate.SetValueAsync(string.Empty, cancellationToken);
        Assert.Equal(
            string.Empty,
            await certificate.GetValueAsync(cancellationToken));
    }
}
