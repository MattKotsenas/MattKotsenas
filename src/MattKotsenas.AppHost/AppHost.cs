using MattKotsenas.AppHost;
using MattKotsenas.Hosting.Azure.Dns;

using Aspire.Hosting.Azure;
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
    .WithExternalHttpEndpoints();

if (builder.ExecutionContext.IsPublishMode)
{
    builder.AddAzureContainerAppEnvironment("container-apps")
        .WithDashboard(false);

    var legacyResourceGroup = builder.AddParameter(
        "legacyWebResourceGroupName",
        "Default-Web-WestUS",
        publishValueAsDefault: true);
    var legacyWeb = builder.AddLegacyWebAppReference(legacyResourceGroup);
    var dnsResourceGroup = builder.AddParameter(
        "dnsResourceGroupName",
        "dns",
        publishValueAsDefault: true);
    builder.AddDeploymentBootstrap(dnsResourceGroup, legacyResourceGroup);
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

    rootZone
        .AddARecordSet(
            "root-apex",
            "@")
        .WithAddress(legacyWebInboundIpAddress);
    rootZone
        .AddCnameRecordSet(
            "root-www",
            "www")
        .WithTarget(defaultHostName);
    rootZone
        .AddTxtRecordSet(
            "root-apex-verification",
            "asuid")
        .WithRecord(verificationId)
        .WithRecord(legacyRootVerificationId);
    rootZone
        .AddTxtRecordSet(
            "root-www-verification",
            "asuid.www")
        .WithRecord(verificationId);
    blogZone
        .AddARecordSet(
            "blog-apex",
            "@")
        .WithAddress(legacyWebInboundIpAddress);
    blogZone
        .AddCnameRecordSet(
            "blog-www",
            "www")
        .WithTarget(defaultHostName);
    blogZone
        .AddTxtRecordSet(
            "blog-apex-verification",
            "asuid")
        .WithRecord(verificationId);
    blogZone
        .AddTxtRecordSet(
            "blog-www-verification",
            "asuid.www")
        .WithRecord(verificationId);

    var rootDomain = builder.AddParameter(
        "rootCustomDomain",
        BlogDomains.Root,
        publishValueAsDefault: true);
    var rootCertificateName = builder.AddParameter(
        "rootCustomDomainCertificateName",
        string.Empty,
        publishValueAsDefault: true).AsOptional();
    var rootWwwDomain = builder.AddParameter(
        "rootWwwCustomDomain",
        $"www.{BlogDomains.Root}",
        publishValueAsDefault: true);
    var rootWwwCertificateName = builder.AddParameter(
        "rootWwwCustomDomainCertificateName",
        string.Empty,
        publishValueAsDefault: true).AsOptional();
    var blogDomain = builder.AddParameter(
        "blogCustomDomain",
        BlogDomains.Blog,
        publishValueAsDefault: true);
    var blogCertificateName = builder.AddParameter(
        "blogCustomDomainCertificateName",
        string.Empty,
        publishValueAsDefault: true).AsOptional();
    var blogWwwDomain = builder.AddParameter(
        "blogWwwCustomDomain",
        $"www.{BlogDomains.Blog}",
        publishValueAsDefault: true);
    var blogWwwCertificateName = builder.AddParameter(
        "blogWwwCustomDomainCertificateName",
        string.Empty,
        publishValueAsDefault: true).AsOptional();

    blog.PublishAsAzureContainerApp((_, containerApp) =>
    {
        containerApp.Template.Scale.MinReplicas = 1;
        containerApp.Template.Scale.MaxReplicas = 1;

        var container = containerApp.Template.Containers[0].Value!;
        container.Resources.Cpu = 0.25;
        container.Resources.Memory = "0.5Gi";

        containerApp.ConfigureCustomDomain(
            rootDomain,
            rootCertificateName);
        containerApp.ConfigureCustomDomain(
            rootWwwDomain,
            rootWwwCertificateName);
        containerApp.ConfigureCustomDomain(
            blogDomain,
            blogCertificateName);
        containerApp.ConfigureCustomDomain(
            blogWwwDomain,
            blogWwwCertificateName);
    });
}

if (useDevelopmentContainer)
{
    blog
        .WithBindMount(repositoryRoot, "/src")
        .WithArgs("server", "--bind", "0.0.0.0");
}

#pragma warning disable ASPIREPIPELINES005
builder.WithOutputDiffing();
#pragma warning restore ASPIREPIPELINES005

builder.Build().Run();
