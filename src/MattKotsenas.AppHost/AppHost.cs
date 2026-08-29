using MattKotsenas.AppHost;

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
var useDevelopmentContainer =
    isRunMode &&
    !builder.Configuration.GetValue<bool>("Blog:UseProductionContainer");

if (builder.ExecutionContext.IsPublishMode)
{
    var deploymentPrincipalId = ObjectId.FromString(
        builder.Configuration["DeploymentPrincipalId"]);

    var environment = builder
        .AddAzureContainerAppEnvironment("container-apps")
        .WithDashboard(false);
    environment
        .GetAzureContainerRegistry()
        .ConfigureInfrastructure(infrastructure =>
        {
            var registryService = infrastructure
                .GetProvisionableResources()
                .OfType<ContainerRegistryService>()
                .Single();
            var principalId = new ProvisioningParameter(
                AzureBicepResource.KnownParameters.UserPrincipalId,
                typeof(string))
            {
                Value = new BicepValue<string>(
                    deploymentPrincipalId.ToString()),
            };
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

    var legacyWebResourceGroup = builder.AddParameter(
        "legacyWebResourceGroupName",
        "Default-Web-WestUS",
        publishValueAsDefault: true);
    var legacyWeb = builder.AddAzureInfrastructure(
        "legacy-web",
        LegacyWebAppInfrastructure.Configure);
    legacyWeb.Resource.Scope = new(legacyWebResourceGroup.Resource);

    // App Service does not expose its shared inbound address through ARM.
    var legacyWebInboundIpAddress = builder.AddParameter(
        "legacyWebInboundIpAddress",
        "168.62.20.37",
        publishValueAsDefault: true);
    var legacyRootVerificationId = builder.AddParameter(
        "legacyRootVerificationId",
        "F883000E15157DBAA27BE77E3C2BFB8F5B8D3E5BED81331607354AA636C349BE",
        publishValueAsDefault: true);
    var dnsResourceGroup = builder.AddParameter(
        "dnsResourceGroupName",
        "dns",
        publishValueAsDefault: true);
    var dns = builder
        .AddAzureInfrastructure("blog-dns", DnsInfrastructure.Configure)
        .WithParameter(
            "defaultHostName",
            legacyWeb.GetOutput("defaultHostName"))
        .WithParameter(
            "customDomainVerificationId",
            legacyWeb.GetOutput("customDomainVerificationId"))
        .WithParameter(
            "websiteInboundIpAddress",
            legacyWebInboundIpAddress)
        .WithParameter(
            "legacyRootVerificationId",
            legacyRootVerificationId);
    dns.Resource.Scope = new(dnsResourceGroup.Resource);
}

var configuredPort = isRunMode
    ? builder.Configuration.GetValue<int>("Blog:HostPort")
    : 0;

var blog = builder
    .AddDockerfile(
        "blog",
        repositoryRoot,
        "build/Dockerfile",
        stage: useDevelopmentContainer ? "dev" : "final")
    .WithHttpEndpoint(
        port: configuredPort is 0 ? null : configuredPort,
        targetPort: useDevelopmentContainer ? 1313 : 8080,
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

if (useDevelopmentContainer)
{
    blog
        .WithBindMount(repositoryRoot, "/src")
        .WithArgs("server", "--bind", "0.0.0.0");
}

if (isRunMode)
{
    blog.WithCommand(
        name: "configure-container-app-deployment",
        displayName: "Configure Container App deployment",
        executeCommand: DeploymentSetup.ExecuteAsync,
        commandOptions: new CommandOptions
        {
            Description = "Configures GitHub-to-Azure OIDC deployment.",
            ConfirmationMessage = "Create or update the Entra application, federated credential, subscription roles, immutable GitHub OIDC subject, and repository variables?",
            IconName = "CloudArrowUp",
            Arguments =
            [
                new InteractionInput
                {
                    Name = "applicationId",
                    Label = "Existing application ID (optional)",
                    InputType = InputType.Text,
                    Required = false,
                    MaxLength = 36,
                },
            ],
        });
}

builder.Build().Run();
