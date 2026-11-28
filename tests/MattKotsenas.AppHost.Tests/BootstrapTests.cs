using System.Text.Json;
using System.Text.Json.Nodes;
using Aspire.Hosting;
using Aspire.Hosting.Azure;
using Aspire.Hosting.Pipelines;
using MattKotsenas.AppHost;
using Microsoft.Extensions.Options;

namespace MattKotsenas.AppHost.Tests;

public sealed class BootstrapTests
{
    [Theory]
    [InlineData("id", "8395663")]
    [InlineData("name", "\"Renamed\"")]
    [InlineData("full_name", "\"Other/MattKotsenas\"")]
    [InlineData("owner.id", "51422")]
    [InlineData("owner.login", "\"Transferred\"")]
    public void RepositoryRenameOrTransferRequiresExplicitTrustUpdate(string field, string value)
    {
        var repository = JsonNode.Parse("""
            {"id":8395662,"name":"MattKotsenas","full_name":"MattKotsenas/MattKotsenas","owner":{"id":51421,"login":"MattKotsenas"}}
            """)!;
        GitHubBootstrap.VerifyRepository(JsonSerializer.SerializeToElement(repository));
        var parts = field.Split('.');
        if (parts.Length is 1)
        {
            repository[field] = JsonNode.Parse(value);
        }
        else
        {
            repository[parts[0]]![parts[1]] = JsonNode.Parse(value);
        }
        Assert.Throws<InvalidOperationException>(() => GitHubBootstrap.VerifyRepository(JsonSerializer.SerializeToElement(repository)));
    }

    [Theory]
    [InlineData("name", "\"unexpected\"")]
    [InlineData("issuer", "\"https://token.actions.githubusercontent.com/\"")]
    [InlineData("subject", "\"repo:MattKotsenas/MattKotsenas:ref:refs/heads/main\"")]
    [InlineData("subject", "\"repo:MattKotsenas@51421/MattKotsenas@8395662:ref:refs/heads/preview\"")]
    [InlineData("audiences", "[\"other\"]")]
    [InlineData("audiences", "[\"api://AzureADTokenExchange\",\"extra\"]")]
    public void ExistingIdentityMustHaveOnlyExpectedTrust(string field, string value)
    {
        var credential = Credential();
        var credentials = new JsonArray(credential);
        BootstrapPreflight.VerifyCredentials(JsonSerializer.SerializeToElement(credentials));
        credential[field] = JsonNode.Parse(value);
        Assert.Throws<InvalidOperationException>(() => BootstrapPreflight.VerifyCredentials(JsonSerializer.SerializeToElement(credentials)));
    }

    [Fact]
    public void EmptyTrustCanConvergeAfterPartialDeploymentButExtraTrustIsRejected()
    {
        var credentials = new JsonArray();
        Assert.Empty(credentials);
        BootstrapPreflight.VerifyCredentials(JsonSerializer.SerializeToElement(credentials));
        credentials.Add(Credential());
        BootstrapPreflight.VerifyCredentials(JsonSerializer.SerializeToElement(credentials));
        credentials.Add(Credential());
        Assert.Equal(2, credentials.Count);
        Assert.Throws<InvalidOperationException>(() => BootstrapPreflight.VerifyCredentials(JsonSerializer.SerializeToElement(credentials)));
    }

    [Theory]
    [InlineData("use_default", "false")]
    [InlineData("use_immutable_subject", "false")]
    [InlineData("sub_claim_prefix", "\"repo:MattKotsenas/MattKotsenas\"")]
    public void GitHubReadbackMustMatchImmutableSubject(string field, string value)
    {
        var configuration = JsonNode.Parse("""
            {"use_default":true,"use_immutable_subject":true,"sub_claim_prefix":"repo:MattKotsenas@51421/MattKotsenas@8395662"}
            """)!;
        GitHubBootstrap.VerifyOidcConfiguration(JsonSerializer.SerializeToElement(configuration));
        configuration[field] = JsonNode.Parse(value);
        Assert.Throws<InvalidOperationException>(() => GitHubBootstrap.VerifyOidcConfiguration(JsonSerializer.SerializeToElement(configuration)));
    }

    [Theory]
    [InlineData(null, "westus3", "blog", "Azure:SubscriptionId")]
    [InlineData("", "westus3", "blog", "Azure:SubscriptionId")]
    [InlineData("not-a-guid", "westus3", "blog", "Azure:SubscriptionId")]
    [InlineData("11111111111111111111111111111111", "westus3", "blog", "Azure:SubscriptionId")]
    [InlineData("11111111-1111-1111-1111-111111111111", null, "blog", "Azure:Location")]
    [InlineData("11111111-1111-1111-1111-111111111111", " ", "blog", "Azure:Location")]
    [InlineData("11111111-1111-1111-1111-111111111111", "westus3", null, "Azure:ResourceGroup")]
    [InlineData("11111111-1111-1111-1111-111111111111", "westus3", "", "Azure:ResourceGroup")]
    [InlineData("11111111-1111-1111-1111-111111111111", "westus3", "other", "Azure:ResourceGroup")]
    public async Task InvalidTargetFailsBeforeCliExecution(string? subscription, string? location, string? group, string expectedKey)
    {
        var token = TestContext.Current.CancellationToken;
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.MattKotsenas_AppHost>(["--operation", "publish"], token);
        builder.Configuration["Azure:SubscriptionId"] = subscription;
        builder.Configuration["Azure:Location"] = location;
        builder.Configuration["Azure:ResourceGroup"] = group;
        await using var app = await builder.BuildAsync(token);
        var options = app.Services.GetRequiredService<IOptions<BootstrapAzureOptions>>();
        var error = await Assert.ThrowsAsync<OptionsValidationException>(() => BootstrapPreflight.VerifyAsync(options, [], token));
        Assert.StartsWith(expectedKey, Assert.Single(error.Failures), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("11111111-1111-1111-1111-111111111111", "westus3", "blog", null)]
    [InlineData("AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA", "eastus2", "BLOG", "22222222-2222-2222-2222-222222222222")]
    public async Task BootstrapOptionsBindExistingAzureKeys(string subscription, string location, string group, string? tenant)
    {
        var token = TestContext.Current.CancellationToken;
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.MattKotsenas_AppHost>(["--operation", "publish"], token);
        builder.Configuration["Azure:SubscriptionId"] = subscription;
        builder.Configuration["Azure:Location"] = location;
        builder.Configuration["Azure:ResourceGroup"] = group;
        builder.Configuration["Azure:TenantId"] = tenant;
        await using var app = await builder.BuildAsync(token);
        var target = app.Services.GetRequiredService<IOptions<BootstrapAzureOptions>>().Value;
        Assert.Equal(subscription, target.SubscriptionId);
        Assert.Equal(location, target.Location);
        Assert.Equal(group, target.ResourceGroup);
        Assert.Equal(tenant, target.TenantId);
    }

    [Fact]
    public async Task BootstrapOptionsReportAllConfigurationFailures()
    {
        var token = TestContext.Current.CancellationToken;
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.MattKotsenas_AppHost>(["--operation", "publish"], token);
        builder.Configuration["Azure:SubscriptionId"] = "invalid";
        builder.Configuration["Azure:Location"] = string.Empty;
        builder.Configuration["Azure:ResourceGroup"] = "other";
        await using var app = await builder.BuildAsync(token);
        var options = app.Services.GetRequiredService<IOptions<BootstrapAzureOptions>>();
        var error = Assert.Throws<OptionsValidationException>(() => options.Value);
        Assert.Equal(
            ["Azure:SubscriptionId", "Azure:Location", "Azure:ResourceGroup"],
            error.Failures.Select(failure => failure.Split(' ')[0]));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-guid")]
    [InlineData("11111111-1111-1111-1111-111111111111")]
    public async Task BadNativeOutputsDoNotChangeGitHub(string? clientId)
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => GitHubBootstrap.ConfigureAsync(clientId,
            "22222222-2222-2222-2222-222222222222", "33333333-3333-3333-3333-333333333333",
            "44444444-4444-4444-4444-444444444444", "22222222-2222-2222-2222-222222222222", TestContext.Current.CancellationToken));
        Assert.StartsWith("Native bootstrap outputs", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NativeBootstrapDeclaresMainOnlyIdentityAndGroupBoundaryWithExplicitMigrationExceptions()
    {
        var token = TestContext.Current.CancellationToken;
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.MattKotsenas_AppHost>(["--operation", "publish"], token);
        builder.Services.Configure<PipelineOptions>(options => options.Step = WellKnownPipelineSteps.BeforeStart);
        await using var app = await builder.BuildAsync(token);
        await app.StartAsync(token);
        var model = app.Services.GetRequiredService<DistributedApplicationModel>();
        var bootstrap = Assert.IsAssignableFrom<AzureBicepResource>(Assert.Single(model.Resources, resource => resource.Name == "deployment-bootstrap"));
        var template = bootstrap.GetBicepTemplateString();
        Assert.Null(bootstrap.Scope);
        Assert.Contains("issuer: 'https://token.actions.githubusercontent.com'", template);
        Assert.Contains($"subject: '{BootstrapPreflight.Subject}'", template);
        Assert.Contains("'b24988ac-6180-42a0-ab88-20f7382dd24c'", template);
        Assert.Contains("'f58310d9-a9f6-439a-9e8d-f62e7b41a168'", template);
        Assert.DoesNotContain("scope:", template);
        Assert.DoesNotContain("Microsoft.Graph", template);
        foreach (var name in new[] { "clientId", "principalId", "tenantId", "subscriptionId" })
        {
            Assert.Contains($"output {name} string", template);
        }
        var dns = Assert.IsAssignableFrom<AzureBicepResource>(Assert.Single(model.Resources, resource => resource.Name == "migration-dns-permissions"));
        Assert.NotNull(dns.Scope);
        Assert.Contains("scope: rootZone", dns.GetBicepTemplateString());
        Assert.Contains("scope: blogZone", dns.GetBicepTemplateString());
        var legacy = Assert.IsAssignableFrom<AzureBicepResource>(Assert.Single(model.Resources, resource => resource.Name == "migration-legacy-reader"));
        Assert.NotNull(legacy.Scope);
        Assert.Contains("scope: legacySite", legacy.GetBicepTemplateString());
    }

    [Theory]
    [InlineData("dns", "Default-Web-WestUS")]
    [InlineData("custom-dns-group", "custom-legacy-group")]
    public async Task BootstrapAndApplicationUseTheSameResolvedExternalGroups(string dnsGroup, string legacyGroup)
    {
        var token = TestContext.Current.CancellationToken;
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.MattKotsenas_AppHost>(
            [
                "--operation", "publish",
                "Pipeline:ListSteps=true",
                "AppHost:DeploymentStatePathSha256=",
                "AppHost:LegacyDeploymentStatePathSha256=",
            ], token);
        builder.Services.Configure<PipelineOptions>(options => options.Step = WellKnownPipelineSteps.BeforeStart);
        await using var app = await builder.BuildAsync(token);
        await app.StartAsync(token);
        var model = app.Services.GetRequiredService<DistributedApplicationModel>();
        var dnsParameter = Assert.Single(model.Resources.OfType<ParameterResource>(), parameter => parameter.Name == "dnsResourceGroupName");
        var legacyParameter = Assert.Single(model.Resources.OfType<ParameterResource>(), parameter => parameter.Name == "legacyWebResourceGroupName");
        Assert.Equal("dns", await dnsParameter.GetValueAsync(token));
        Assert.Equal("Default-Web-WestUS", await legacyParameter.GetValueAsync(token));
        await dnsParameter.SetValueAsync(dnsGroup, token);
        await legacyParameter.SetValueAsync(legacyGroup, token);
        Assert.Equal(dnsGroup, await dnsParameter.GetValueAsync(token));
        Assert.Equal(legacyGroup, await legacyParameter.GetValueAsync(token));
        foreach (var name in new[] { "root-zone", "blog-zone", "migration-dns-permissions" })
        {
            var module = Assert.IsAssignableFrom<AzureBicepResource>(Assert.Single(model.Resources, resource => resource.Name == name));
            Assert.NotNull(module.Scope);
            Assert.Same(dnsParameter, module.Scope.ResourceGroup);
        }
        var legacyPermissions = Assert.IsAssignableFrom<AzureBicepResource>(Assert.Single(model.Resources, resource => resource.Name == "migration-legacy-reader"));
        Assert.NotNull(legacyPermissions.Scope);
        Assert.Same(legacyParameter, legacyPermissions.Scope.ResourceGroup);
        var legacyReference = Assert.IsAssignableFrom<AzureBicepResource>(Assert.Single(model.Resources, resource => resource.Name == "legacy-web"));
        Assert.Contains("param legacyWebResourceGroupName string", legacyReference.GetBicepTemplateString());
        Assert.Same(legacyParameter, legacyReference.Parameters["legacyWebResourceGroupName"]);
    }

    [Fact]
    public async Task ApplicationRegistryUsesNativeRuntimePullAssignments()
    {
        var token = TestContext.Current.CancellationToken;
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.MattKotsenas_AppHost>(["--operation", "publish"], token);
        builder.Services.Configure<PipelineOptions>(options => options.Step = WellKnownPipelineSteps.BeforeStart);
        await using var app = await builder.BuildAsync(token);
        await app.StartAsync(token);
        var model = app.Services.GetRequiredService<DistributedApplicationModel>();
        var registry = Assert.IsAssignableFrom<AzureBicepResource>(Assert.Single(model.Resources, resource => resource.Name == "container-apps-acr"));
        var template = registry.GetBicepTemplateString();
        Assert.Contains("Microsoft.ContainerRegistry/registries", template);
        Assert.DoesNotContain("userPrincipalId", template);
        Assert.DoesNotContain("8311e382-0749-4cb8-b61a-304f252e45ec", template);
        Assert.DoesNotContain(model.Resources, resource => resource.Name == "DeploymentPrincipalId");
        var hosting = Assert.IsAssignableFrom<AzureBicepResource>(Assert.Single(model.Resources, resource => resource.Name == "container-apps"));
        Assert.Contains("'7f951dda-4ed3-4680-a7ca-43fe172d538d'", hosting.GetBicepTemplateString());
        Assert.Contains("principalId: container_apps_mi.properties.principalId", hosting.GetBicepTemplateString());
    }

    private static JsonObject Credential() => new()
    {
        ["name"] = BootstrapPreflight.CredentialName,
        ["issuer"] = BootstrapPreflight.Issuer,
        ["subject"] = BootstrapPreflight.Subject,
        ["audiences"] = new JsonArray(BootstrapPreflight.Audience),
    };
}
