using Aspire.Hosting;
using Aspire.Hosting.Azure;
using Aspire.Hosting.Pipelines;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MattKotsenas.AppHost.Tests;

#pragma warning disable ASPIREPIPELINES001
public sealed class BootstrapPipelineTests
{
    private const string StopStep = "test-stop-before-actions";
    private static readonly string[] BootstrapResources =
        ["deployment-bootstrap", "migration-dns-permissions", "migration-legacy-reader"];

    [Fact]
    public async Task ExistingAppHostContainsNativeBootstrapAlongsideApplication()
    {
        var token = TestContext.Current.CancellationToken;
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.MattKotsenas_AppHost>(["--operation", "publish"], token);
        builder.Services.Configure<PipelineOptions>(options => options.Step = WellKnownPipelineSteps.BeforeStart);
        await using var app = await builder.BuildAsync(token);
        await app.StartAsync(token);
        var model = app.Services.GetRequiredService<DistributedApplicationModel>();
        Assert.Contains(model.Resources, resource => resource.Name == "blog");
        Assert.IsAssignableFrom<AzureBicepResource>(Assert.Single(model.Resources, resource => resource.Name == "deployment-bootstrap"));
    }

    [Fact]
    public async Task RunModeDoesNotRegisterBootstrapOrAzureInfrastructure()
    {
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.MattKotsenas_AppHost>(
            ["Blog:HostPort=0", "Blog:UseProductionContainer=false"], TestContext.Current.CancellationToken);
        await using var app = await builder.BuildAsync(TestContext.Current.CancellationToken);
        var model = app.Services.GetRequiredService<DistributedApplicationModel>();
        Assert.Contains(model.Resources, resource => resource.Name == "blog");
        Assert.DoesNotContain(model.Resources, resource => BootstrapResources.Contains(resource.Name));
        Assert.Empty(model.Resources.OfType<AzureBicepResource>());
        var steps = await CaptureSelectedAsync(app, "bootstrap", expectMissingTarget: true);
        Assert.Empty(steps);
    }

    [Fact]
    public async Task BootstrapSelectsOnlyItsNativeModulesWithoutApplicationOrImages()
    {
        await using var app = await PreparePublishAppAsync();
        var steps = await CaptureSelectedAsync(app, "bootstrap");
        var modules = steps.Where(step => step.Tags.Contains(WellKnownPipelineTags.ProvisionInfrastructure)).ToArray();
        Assert.Equal(BootstrapResources.Order(StringComparer.Ordinal), modules.Select(step => step.Resource!.Name).Order(StringComparer.Ordinal));
        Assert.All(modules, step => Assert.IsAssignableFrom<AzureBicepResource>(step.Resource));
        Assert.DoesNotContain(steps, step => step.Name == WellKnownPipelineSteps.Deploy);
        Assert.DoesNotContain(steps, step => step.Resource?.Name is "blog" or "legacy-web" or "root-zone" or "blog-zone" or "container-apps" or "container-apps-acr");
        Assert.DoesNotContain(steps, step => step.Tags.Contains(WellKnownPipelineTags.BuildCompute) ||
            step.Tags.Contains(WellKnownPipelineTags.PushContainerImage) || step.Tags.Contains(WellKnownPipelineTags.DeployCompute));
        Assert.DoesNotContain(steps, step => step.Name == "check-container-runtime");
        var bootstrap = Assert.Single(steps, step => step.Name == "bootstrap");
        Assert.All(modules, step => Assert.Contains(step.Name, bootstrap.DependsOnSteps));
    }

    [Fact]
    public async Task NormalDeployExcludesBootstrapAndSkipsItsPreflight()
    {
        await using var app = await PreparePublishAppAsync();
        var steps = await CaptureSelectedAsync(app, WellKnownPipelineSteps.Deploy, runPreflight: true);
        Assert.Contains(steps, step => step.Name == "bootstrap-preflight");
        Assert.Contains(steps, step => step.Resource?.Name == "blog" && step.Tags.Contains(WellKnownPipelineTags.BuildCompute));
        Assert.Contains(steps, step => step.Resource?.Name == "blog" && step.Tags.Contains(WellKnownPipelineTags.PushContainerImage));
        Assert.Contains(steps, step => step.Name == "provision-blog-containerapp");
        Assert.Contains(steps, step => step.Resource?.Name == "root-zone" && step.Tags.Contains(WellKnownPipelineTags.ProvisionInfrastructure));
        Assert.Contains(steps, step => step.Resource?.Name == "container-apps" && step.Tags.Contains(WellKnownPipelineTags.ProvisionInfrastructure));
        Assert.DoesNotContain(steps, step => step.Name == "bootstrap" || BootstrapResources.Contains(step.Resource?.Name));
    }

    [Theory]
    [InlineData("bootstrap")]
    [InlineData("provision-deployment-bootstrap")]
    [InlineData("provision-migration-dns-permissions")]
    [InlineData("provision-migration-legacy-reader")]
    [InlineData("test-bootstrap-alias")]
    public async Task EveryBootstrapEntryPointRequiresPreflightBeforeNativeAuthenticationAndContext(string target)
    {
        await using var app = await PreparePublishAppAsync();
        app.Services.GetRequiredService<IDistributedApplicationPipeline>()
            .AddStep("test-bootstrap-alias", _ => Task.CompletedTask, dependsOn: "provision-migration-dns-permissions");
        var steps = await CaptureSelectedAsync(app, target);
        Assert.Contains(steps, step => BootstrapResources.Contains(step.Resource?.Name) && step.Tags.Contains(WellKnownPipelineTags.ProvisionInfrastructure));
        Assert.Contains(steps, step => step.Name == "bootstrap-preflight");
        foreach (var name in new[] { "validate-azure-login", "create-provisioning-context" })
        {
            Assert.Single(steps, step => step.Name == name);
            Assert.True(DependsOn(steps, name, "bootstrap-preflight"), $"{name} must wait for bootstrap-preflight when selecting {target}");
        }
    }

    [Theory]
    [InlineData("bootstrap")]
    [InlineData("provision-deployment-bootstrap")]
    [InlineData("provision-migration-dns-permissions")]
    [InlineData("provision-migration-legacy-reader")]
    [InlineData("test-bootstrap-alias")]
    public async Task FailedPreflightStopsBeforeAnyAzureOrGitHubWriteAction(string target)
    {
        await using var app = await PreparePublishAppAsync();
        var pipeline = app.Services.GetRequiredService<IDistributedApplicationPipeline>();
        pipeline.AddStep("test-bootstrap-alias", _ => Task.CompletedTask, dependsOn: "provision-migration-legacy-reader");
        var reachedExternalBoundary = 0;
        pipeline.AddStep(StopStep, _ =>
        {
            Interlocked.Increment(ref reachedExternalBoundary);
            throw new InvalidOperationException("An external action was reachable after a failed preflight.");
        }, dependsOn: WellKnownPipelineSteps.DeployPrereq);
        pipeline.AddPipelineConfiguration(context =>
        {
            foreach (var step in context.Steps.Where(IsExternalAction))
            {
                step.DependsOn(StopStep);
            }
            return Task.CompletedTask;
        });
        SetTarget(app, target);
        Assert.Equal(0, reachedExternalBoundary);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => ExecuteAsync(app));
        var preflightError = Assert.IsType<OptionsValidationException>(error.InnerException);
        Assert.Contains("Azure:SubscriptionId must be a GUID in D format.", preflightError.Failures);
        Assert.Equal(0, reachedExternalBoundary);
    }

#pragma warning disable ASPIREPIPELINES005
    [Fact]
    public async Task NativePublishAndDiffDetectsStaleBootstrapOutputAndPassesAfterRegeneration()
    {
        var directory = Path.Combine(Path.GetTempPath(), "blog-publish-diff-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await using var app = await PreparePublishAppAsync(directory);
            var pipeline = app.Services.GetRequiredService<IDistributedApplicationPipeline>();
            pipeline.AddStep(StopStep, _ => throw new InvalidOperationException("Publishing must not execute external actions."));
            pipeline.AddPipelineConfiguration(context =>
            {
                foreach (var step in context.Steps.Where(IsExternalAction))
                {
                    step.DependsOn(StopStep);
                }
                return Task.CompletedTask;
            });
            SetTarget(app, WellKnownPipelineSteps.Publish);
            await ExecuteAsync(app);
            var artifact = Path.Combine(directory, "deployment-bootstrap", "deployment-bootstrap.bicep");
            Assert.True(File.Exists(artifact), artifact);
            var original = await File.ReadAllTextAsync(artifact, TestContext.Current.CancellationToken);
            Assert.Contains("Microsoft.ManagedIdentity/userAssignedIdentities", original);
            await File.AppendAllTextAsync(artifact, "\n// stale generated output\n", TestContext.Current.CancellationToken);
            Assert.NotEqual(original, await File.ReadAllTextAsync(artifact, TestContext.Current.CancellationToken));

            SetTarget(app, "publish-and-diff");
            var error = await Assert.ThrowsAsync<DistributedApplicationException>(() => ExecuteAsync(app));
            Assert.Contains("Pipeline output changed:", error.Message, StringComparison.Ordinal);
            Assert.Contains(Path.Combine("deployment-bootstrap", "deployment-bootstrap.bicep"), error.Message, StringComparison.Ordinal);
            Assert.Equal(original, await File.ReadAllTextAsync(artifact, TestContext.Current.CancellationToken));

            await ExecuteAsync(app);
            Assert.Equal(original, await File.ReadAllTextAsync(artifact, TestContext.Current.CancellationToken));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
#pragma warning restore ASPIREPIPELINES005

    private static async Task<DistributedApplication> PreparePublishAppAsync(string? outputPath = null)
    {
        var token = TestContext.Current.CancellationToken;
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.MattKotsenas_AppHost>(["--operation", "publish"], token);
        // Prepare the model; tests invoke the guarded public pipeline explicitly.
        builder.Configuration["Pipeline:ListSteps"] = "true";
        // Store test parameter state in memory.
        builder.Configuration["AppHost:DeploymentStatePathSha256"] = string.Empty;
        builder.Configuration["AppHost:LegacyDeploymentStatePathSha256"] = string.Empty;
        // Invalid on purpose: selecting bootstrap must fail before CLI execution; normal deploy must skip the check.
        builder.Configuration["Azure:SubscriptionId"] = "not-a-guid";
        builder.Configuration["Azure:Location"] = "westus3";
        builder.Configuration["Azure:ResourceGroup"] = "blog";
        builder.Services.Configure<PipelineOptions>(options =>
        {
            options.Step = WellKnownPipelineSteps.BeforeStart;
            options.OutputPath = outputPath;
        });
        if (outputPath is not null)
        {
#pragma warning disable ASPIREPIPELINES005
            builder.Services.Configure<PipelineOutputDiffOptions>(options => options.Mode = PipelineOutputDiffMode.FileSystem);
#pragma warning restore ASPIREPIPELINES005
        }
        var app = await builder.BuildAsync(token);
        try
        {
            Assert.Null(app.Services.GetRequiredService<IDeploymentStateManager>().StateFilePath);
            await app.StartAsync(token);
            return app;
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }
    }

    private static async Task<IReadOnlyList<PipelineStep>> CaptureSelectedAsync(
        DistributedApplication app, string target, bool runPreflight = false, bool expectMissingTarget = false)
    {
        var pipeline = app.Services.GetRequiredService<IDistributedApplicationPipeline>();
        pipeline.AddStep(StopStep, _ => throw new PipelineStoppedException(),
            dependsOn: runPreflight ? "bootstrap-preflight" : null);
        pipeline.AddPipelineConfiguration(context =>
        {
            // Stop execution before native actions while preserving their associations and dependencies.
            foreach (var step in context.Steps.Where(step => step.Name != StopStep && step.Name != "publish-and-diff-baseline" &&
                (!runPreflight || step.Name != "bootstrap-preflight")))
            {
                step.DependsOn(StopStep);
            }
            return Task.CompletedTask;
        });
        SetTarget(app, target);
        var context = CreateContext(app);
        if (expectMissingTarget)
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => pipeline.ExecuteAsync(context));
            Assert.StartsWith($"Step '{target}' not found in pipeline.", error.Message, StringComparison.Ordinal);
        }
        else
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => pipeline.ExecuteAsync(context));
            Assert.IsType<PipelineStoppedException>(error.InnerException);
        }
        return context.SelectedSteps;
    }

    private static bool IsExternalAction(PipelineStep step) =>
        step.Name is "validate-azure-login" or "create-provisioning-context" or "bootstrap" or "check-public-https" or "check-container-runtime" ||
        step.Tags.Any(tag => tag is WellKnownPipelineTags.ProvisionInfrastructure or WellKnownPipelineTags.BuildCompute or
            WellKnownPipelineTags.PushContainerImage or WellKnownPipelineTags.DeployCompute);

    private static bool DependsOn(IReadOnlyList<PipelineStep> steps, string name, string dependency)
    {
        var byName = steps.ToDictionary(step => step.Name, StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        return Visit(name);
        bool Visit(string current) => visited.Add(current) && byName[current].DependsOnSteps.Any(next => next == dependency || Visit(next));
    }

    private static void SetTarget(DistributedApplication app, string target) =>
        app.Services.GetRequiredService<IOptions<PipelineOptions>>().Value.Step = target;

    private static PipelineContext CreateContext(DistributedApplication app) => new(
        app.Services.GetRequiredService<DistributedApplicationModel>(),
        app.Services.GetRequiredService<DistributedApplicationExecutionContext>(), app.Services,
        NullLogger.Instance, TestContext.Current.CancellationToken);

    private static Task ExecuteAsync(DistributedApplication app) =>
        app.Services.GetRequiredService<IDistributedApplicationPipeline>().ExecuteAsync(CreateContext(app));

    private sealed class PipelineStoppedException : Exception;
}
#pragma warning restore ASPIREPIPELINES001
