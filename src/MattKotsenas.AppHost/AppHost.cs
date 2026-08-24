using MattKotsenas.AppHost;
using Aspire.Hosting.Azure;
using Microsoft.Extensions.Configuration;

// Azure environment scoping is experimental.
#pragma warning disable ASPIREAZURE001

var builder = DistributedApplication.CreateBuilder(args);

if (builder.ExecutionContext.IsPublishMode)
{
    var blogResourceGroup = builder.AddParameter(
        "blogResourceGroupName",
        "Default-Web-WestUS",
        publishValueAsDefault: true);
    builder.AddAzureEnvironment().WithResourceGroup(blogResourceGroup);

    var blog = builder.AddAzureInfrastructure(
        "blog",
        BlogInfrastructure.Configure);

    // App Service does not expose its shared inbound address through ARM.
    var websiteInboundIpAddress = builder.AddParameter(
        "websiteInboundIpAddress",
        "168.62.20.37",
        publishValueAsDefault: true);
    var dnsResourceGroup = builder.AddParameter(
        "dnsResourceGroupName",
        "homelab",
        publishValueAsDefault: true);
    var dns = builder
        .AddAzureInfrastructure("blog-dns", DnsInfrastructure.Configure)
        .WithParameter(
            "defaultHostName",
            blog.GetOutput("defaultHostName"))
        .WithParameter(
            "customDomainVerificationId",
            blog.GetOutput("customDomainVerificationId"))
        .WithParameter(
            "websiteInboundIpAddress",
            websiteInboundIpAddress);
    dns.Resource.Scope = new(dnsResourceGroup.Resource);
}

if (builder.ExecutionContext.IsRunMode)
{
    var repositoryRoot = Path.GetFullPath(
        Path.Combine(builder.AppHostDirectory, "..", ".."));
    var configuredPort = builder.Configuration.GetValue<int>("Blog:HostPort");

    builder
        .AddDockerfile("blog", repositoryRoot, "build/Dockerfile")
        .WithBindMount(repositoryRoot, "/src")
        .WithArgs("server", "--bind", "0.0.0.0")
        .WithHttpEndpoint(
            port: configuredPort is 0 ? null : configuredPort,
            targetPort: 1313,
            name: "http")
        .WithHttpHealthCheck("/", endpointName: "http");
}

builder.Build().Run();
