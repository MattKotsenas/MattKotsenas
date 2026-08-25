using Aspire.Hosting.Azure;
using Azure.Provisioning;
using Azure.Provisioning.Authorization;
using Azure.Provisioning.ContainerRegistry;
using Azure.Provisioning.Expressions;
using Microsoft.Extensions.Configuration;

var builder = DistributedApplication.CreateBuilder(args);
var repositoryRoot = Path.GetFullPath(
    Path.Combine(builder.AppHostDirectory, "..", ".."));
var isRunMode = builder.ExecutionContext.IsRunMode;

if (builder.ExecutionContext.IsPublishMode)
{
    var environment = builder
        .AddAzureContainerAppEnvironment("container-apps")
        .WithDashboard(false);
    var registry = environment.Resource.ContainerRegistry
        ?? throw new InvalidOperationException("The Container Apps environment requires a registry.");

    builder
        .CreateResourceBuilder(registry)
        .ConfigureInfrastructure(infrastructure =>
        {
            var registryService = infrastructure
                .GetProvisionableResources()
                .OfType<ContainerRegistryService>()
                .Single();
            var principalId = new ProvisioningParameter(
                AzureBicepResource.KnownParameters.UserPrincipalId,
                typeof(Guid));
            infrastructure.Add(principalId);

            var pushAssignment = registryService.CreateRoleAssignment(
                ContainerRegistryBuiltInRole.AcrPush,
                RoleManagementPrincipalType.ServicePrincipal,
                principalId);
            pushAssignment.Name = BicepFunction.CreateGuid(
                registryService.Id,
                principalId,
                pushAssignment.RoleDefinitionId);
            infrastructure.Add(pushAssignment);
        });
}

var configuredPort = isRunMode
    ? builder.Configuration.GetValue<int>("Blog:HostPort")
    : 0;

var blog = builder
    .AddDockerfile(
        "blog",
        repositoryRoot,
        "build/Dockerfile",
        stage: isRunMode ? "dev" : "final")
    .WithHttpEndpoint(
        port: configuredPort is 0 ? null : configuredPort,
        targetPort: isRunMode ? 1313 : 8080,
        name: "http")
    .WithHttpHealthCheck("/", endpointName: "http")
    .WithExternalHttpEndpoints()
    .PublishAsAzureContainerApp((_, containerApp) =>
    {
        containerApp.Template.Scale.MinReplicas = 1;
        containerApp.Template.Scale.MaxReplicas = 1;

        var container = containerApp.Template.Containers[0].Value!;
        container.Resources.Cpu = 0.25;
        container.Resources.Memory = "0.5Gi";
    });

if (isRunMode)
{
    blog
        .WithBindMount(repositoryRoot, "/src")
        .WithArgs("server", "--bind", "0.0.0.0");
}

builder.Build().Run();
