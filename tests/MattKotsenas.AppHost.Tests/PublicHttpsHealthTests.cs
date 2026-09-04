using Aspire.Hosting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace MattKotsenas.AppHost.Tests;

public sealed class PublicHttpsHealthTests
{
    [Fact]
    public async Task AppHostRegistersPublicHttpsChecks()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.MattKotsenas_AppHost>(
                [
                    "Blog:HostPort=0",
                    "Blog:UseProductionContainer=false",
                ],
                cancellationToken);
        await using var app = await appHost.BuildAsync(
            cancellationToken);
        var options = app.Services
            .GetRequiredService<
                IOptions<HealthCheckServiceOptions>>()
            .Value;

        Assert.Equal(
            [
                "http-kotsenas-com",
                "http-matt-kotsenas-com",
                "http-www-kotsenas-com",
                "http-www-matt-kotsenas-com",
                "tls-kotsenas-com",
                "tls-matt-kotsenas-com",
                "tls-www-kotsenas-com",
                "tls-www-matt-kotsenas-com",
            ],
            options.Registrations
                .Where(registration =>
                    registration.Tags.Contains(
                        HealthCheckPipelineExtensions.PublicHttpsTag))
                .Select(registration => registration.Name)
                .Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task UnhealthyReportIsRejected()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHealthChecks().AddCheck(
            "broken",
            () => HealthCheckResult.Unhealthy("unavailable"),
            tags:
            [
                HealthCheckPipelineExtensions.PublicHttpsTag,
            ]);
        await using var provider = services.BuildServiceProvider();
        var report = await provider
            .GetRequiredService<HealthCheckService>()
            .CheckHealthAsync(
                registration => registration.Tags.Contains(
                    HealthCheckPipelineExtensions.PublicHttpsTag),
                TestContext.Current.CancellationToken);

        var exception = Assert.Throws<InvalidOperationException>(
            () => HealthCheckPipelineExtensions.RequireHealthy(
                report));

        Assert.Contains("broken: unavailable", exception.Message);
    }

    [Fact]
    public void EmptyReportIsRejected()
    {
        var report = new HealthReport(
            new Dictionary<string, HealthReportEntry>(),
            TimeSpan.Zero);

        var exception = Assert.Throws<InvalidOperationException>(
            () => HealthCheckPipelineExtensions.RequireHealthy(
                report));

        Assert.Contains(
            "No public HTTPS health checks",
            exception.Message);
    }
}
