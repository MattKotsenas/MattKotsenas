using MattKotsenas.AppHost;

using Aspire.Hosting.Azure;
using Aspire.Hosting.Azure.AppContainers;
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
var dnsResourceGroup =
    builder.Configuration["Parameters:dnsResourceGroupName"]
    ?? builder.Configuration["Blog:DnsResourceGroup"]
    ?? throw new InvalidOperationException(
        "Blog:DnsResourceGroup is required.");
var containerAppEnvironmentName =
    builder.Configuration["Blog:ContainerAppEnvironmentName"]
    ?? throw new InvalidOperationException(
        "Blog:ContainerAppEnvironmentName is required.");
IResourceBuilder<AzureContainerAppEnvironmentResource>? environment = null;
IResourceBuilder<AzureBicepResource>? legacyWeb = null;
IResourceBuilder<ParameterResource>? legacyWebInboundIpAddress = null;
IResourceBuilder<ParameterResource>? legacyRootVerificationId = null;

if (builder.ExecutionContext.IsPublishMode)
{
    var deploymentPrincipalId = ObjectId.FromString(
        builder.Configuration["DeploymentPrincipalId"]);

    environment = builder
        .AddAzureContainerAppEnvironment("container-apps")
        .WithAzureName(containerAppEnvironmentName)
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

    legacyWeb = builder.AddLegacyWebAppReference();
    legacyWebInboundIpAddress = builder.AddParameter(
        "legacyWebInboundIpAddress",
        "168.62.20.37",
        publishValueAsDefault: true);
    legacyRootVerificationId = builder.AddParameter(
        "legacyRootVerificationId",
        "F883000E15157DBAA27BE77E3C2BFB8F5B8D3E5BED81331607354AA636C349BE",
        publishValueAsDefault: true);
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

if (environment is not null &&
    legacyWeb is not null &&
    legacyWebInboundIpAddress is not null &&
    legacyRootVerificationId is not null)
{
    blog.WithComputeEnvironment(environment);

    var rootZone = builder.AddAzureDnsZone(
        "root-zone",
        "kotsenas.com",
        dnsResourceGroup);
    var mattZone = builder.AddAzureDnsZone(
        "matt-zone",
        "matt.kotsenas.com",
        dnsResourceGroup);

    var rootRoute = rootZone.AddARecord(
        "root-route",
        DnsRelativeName.Apex,
        legacyWebInboundIpAddress);
    var rootWwwRoute = rootZone.AddCnameRecord(
        "root-www-route",
        "www",
        legacyWeb.GetOutput("defaultHostName"));
    var mattRoute = mattZone.AddARecord(
        "matt-route",
        DnsRelativeName.Apex,
        legacyWebInboundIpAddress);
    var mattWwwRoute = mattZone.AddCnameRecord(
        "matt-www-route",
        "www",
        legacyWeb.GetOutput("defaultHostName"));

    var rootDomain = blog
        .AddCustomDomain(rootRoute)
        .WithManagedCertificate();
    blog
        .AddCustomDomain(rootWwwRoute)
        .WithManagedCertificate();
    blog
        .AddCustomDomain(mattRoute)
        .WithManagedCertificate();
    blog
        .AddCustomDomain(mattWwwRoute)
        .WithManagedCertificate();

    // App Service and Container Apps share the subscription verification ID.
    // Only this older root-domain value remains migration-specific.
    rootDomain
        .GetOwnershipRecord()
        .WithValue(legacyRootVerificationId);
}

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
