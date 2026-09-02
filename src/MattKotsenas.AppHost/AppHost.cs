using MattKotsenas.AppHost;

using Aspire.Hosting.Azure;
using Azure.Provisioning;
using Azure.Provisioning.Authorization;
using Azure.Provisioning.ContainerRegistry;
using Azure.Provisioning.Expressions;
using Microsoft.Extensions.Configuration;

if (args is ["prepare-custom-domains"])
{
    await new CustomDomainSetup(new CliWrapCommandRunner())
        .PrepareAsync(CancellationToken.None);
    return;
}

var builder = DistributedApplication.CreateBuilder(args);
var repositoryRoot = Path.GetFullPath(
    Path.Combine(builder.AppHostDirectory, "..", ".."));
var isRunMode = builder.ExecutionContext.IsRunMode;
var useDevelopmentContainer =
    isRunMode &&
    !builder.Configuration.GetValue<bool>("Blog:UseProductionContainer");
var bindCustomDomainCertificates = builder.Configuration.GetValue(
    "CustomDomains:BindCertificates",
    defaultValue: true);
var customDomainParameters =
    new List<(
        IResourceBuilder<ParameterResource> DomainParameter,
        IResourceBuilder<ParameterResource> CertificateParameter)>();

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
    builder.AddBlogDns(legacyWeb);

    foreach (var domain in CustomDomainSetup.Domains)
    {
        customDomainParameters.Add((
            builder.AddParameter(
                domain.DomainParameterName,
                domain.Hostname,
                publishValueAsDefault: true),
            builder.AddParameter(
                domain.CertificateParameterName,
                bindCustomDomainCertificates
                    ? domain.CertificateName
                    : string.Empty,
                publishValueAsDefault: true)));
    }
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
        foreach (var (domainParameter, certificateParameter) in
            customDomainParameters)
        {
            containerApp.ConfigureCustomDomain(
                domainParameter,
                certificateParameter);
        }

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
    blog.WithCommand(
        name: "prepare-custom-domains",
        displayName: "Prepare custom domains",
        executeCommand: CustomDomainSetup.ExecuteAsync,
        commandOptions: new CommandOptions
        {
            Description = "Creates and binds the managed certificates used by the production custom domains.",
            ConfirmationMessage = "Reconcile the production custom-domain certificates and DNS validation records? Failed unbound certificates and their validation tokens may be replaced.",
            IconName = "Certificate",
        });
}

builder.Build().Run();
