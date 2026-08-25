using Microsoft.Extensions.Configuration;

var builder = DistributedApplication.CreateBuilder(args);

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
