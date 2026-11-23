using Aspire.Hosting;
using Aspire.Hosting.Azure;
using Aspire.Hosting.Pipelines;
using Azure.Provisioning.AppService;

namespace MattKotsenas.AppHost.Tests;

public sealed class LegacyWebAppTests
{
    [Theory]
    [InlineData(null, "Default-Web-WestUS")]
    [InlineData("legacy-web-override", "legacy-web-override")]
    public async Task PublishReadsLegacyWebsiteWithoutDeployingToItsResourceGroup(
        string? configuredResourceGroup,
        string expectedResourceGroup)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.MattKotsenas_AppHost>(
                [
                    "--operation", "publish",
                ],
                cancellationToken);
        appHost.Services.Configure<PipelineOptions>(
            options => options.Step = WellKnownPipelineSteps.BeforeStart);
        await using var app = await appHost.BuildAsync(cancellationToken);
        var model = app.Services.GetRequiredService<DistributedApplicationModel>();
        await app.StartAsync(cancellationToken);
        var resourceGroup = Assert.Single(
            model.Resources.OfType<ParameterResource>(),
            parameter => parameter.Name == "legacyWebResourceGroupName");
        Assert.Equal("Default-Web-WestUS", await resourceGroup.GetValueAsync(cancellationToken));
        if (configuredResourceGroup is not null)
        {
            await resourceGroup.SetValueAsync(configuredResourceGroup, cancellationToken);
        }

        Assert.Equal(expectedResourceGroup, await resourceGroup.GetValueAsync(cancellationToken));

        var legacyWeb = Assert.IsAssignableFrom<AzureBicepResource>(
            Assert.Single(model.Resources, resource => resource.Name == "legacy-web"));
        var template = legacyWeb.GetBicepTemplateString();
        var apiVersion = WebSite.FromExisting("website").ResourceVersion;
        var reference = $"reference(resourceId(subscription().subscriptionId, legacyWebResourceGroupName, 'Microsoft.Web/sites', 'mattkotsenas'), '{apiVersion}')";

        Assert.Contains($"output defaultHostName string = {reference}.defaultHostName", template);
        Assert.Contains($"output customDomainVerificationId string = {reference}.customDomainVerificationId", template);
        Assert.Contains("param legacyWebResourceGroupName string", template);
        Assert.Same(resourceGroup, legacyWeb.Parameters["legacyWebResourceGroupName"]);
        Assert.Null(legacyWeb.Scope);
        Assert.DoesNotMatch(@"(?m)^\s*(resource|module)\s", template);
        Assert.DoesNotContain("targetScope = 'subscription'", template);
    }
}
