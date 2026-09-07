using MattKotsenas.AppHost;
using MattKotsenas.Hosting.Azure.Dns;

using Aspire.Hosting.Azure;
using Azure.Provisioning;
using Azure.Provisioning.Authorization;
using Azure.Provisioning.ContainerRegistry;
using Azure.Provisioning.Expressions;
using Microsoft.Extensions.Configuration;

var builder = DistributedApplication.CreateBuilder(args);
builder.AddPublicHttpsHealthCheckPipeline(
    BlogDomains.PublicHostnames);

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

    var legacyWeb = builder.AddLegacyWebAppReference();
    var dnsResourceGroup = builder.AddParameter(
        "dnsResourceGroupName",
        "dns",
        publishValueAsDefault: true);
    var rootZoneName = builder.AddParameter(
        "rootDnsZoneName",
        BlogDomains.Root,
        publishValueAsDefault: true);
    var blogZoneName = builder.AddParameter(
        "blogDnsZoneName",
        BlogDomains.Blog,
        publishValueAsDefault: true);
    // App Service does not expose its shared inbound address through ARM.
    var legacyWebInboundIpAddress = builder.AddParameter(
        "legacyWebInboundIpAddress",
        "168.62.20.37",
        publishValueAsDefault: true);
    var legacyRootVerificationId = builder.AddParameter(
        "legacyRootVerificationId",
        "F883000E15157DBAA27BE77E3C2BFB8F5B8D3E5BED81331607354AA636C349BE",
        publishValueAsDefault: true);
    var rootZone = builder
        .AddAzureDnsZone("root-zone", BlogDomains.Root)
        .PublishAsExisting(rootZoneName, dnsResourceGroup);
    rootZone.Resource.Scope =
        new AzureBicepResourceScope(dnsResourceGroup.Resource);
    var blogZone = builder
        .AddAzureDnsZone("blog-zone", BlogDomains.Blog)
        .PublishAsExisting(blogZoneName, dnsResourceGroup);
    blogZone.Resource.Scope =
        new AzureBicepResourceScope(dnsResourceGroup.Resource);
    var defaultHostName = legacyWeb.GetOutput("defaultHostName");
    var verificationId =
        legacyWeb.GetOutput("customDomainVerificationId");

    rootZone.AddARecord(
        "root-apex",
        "@",
        legacyWebInboundIpAddress);
    rootZone.AddCnameRecord(
        "root-www",
        "www",
        defaultHostName);
    rootZone
        .AddTxtRecord(
            "root-apex-verification",
            "asuid",
            verificationId)
        .WithValue(legacyRootVerificationId);
    rootZone.AddTxtRecord(
        "root-www-verification",
        "asuid.www",
        verificationId);
    blogZone.AddARecord(
        "blog-apex",
        "@",
        legacyWebInboundIpAddress);
    blogZone.AddCnameRecord(
        "blog-www",
        "www",
        defaultHostName);
    blogZone.AddTxtRecord(
        "blog-apex-verification",
        "asuid",
        verificationId);
    blogZone.AddTxtRecord(
        "blog-www-verification",
        "asuid.www",
        verificationId);
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
