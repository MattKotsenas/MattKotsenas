using Aspire.Hosting.Pipelines;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace MattKotsenas.AppHost;

internal static class HealthCheckPipelineExtensions
{
    internal const string PublicHttpsTag = "public-https";
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(30);

    internal static IDistributedApplicationBuilder AddPublicHttpsHealthCheckPipeline(
        this IDistributedApplicationBuilder builder,
        IEnumerable<string> hostnames)
    {
        var healthChecks = builder.Services.AddHealthChecks();
        foreach (var hostname in hostnames)
        {
            var name = hostname.Replace('.', '-');
            healthChecks
                .AddTlsCertificateHealthCheck(
                    options => options.Hostname = hostname,
                    name: $"tls-{name}",
                    tags: [PublicHttpsTag],
                    timeout: CheckTimeout)
                .AddUrlGroup(
                    options => options.AddUri(
                        new Uri($"https://{hostname}/"),
                        uri => uri
                            .UseHttpMethod(HttpMethod.Head)
                            .ExpectHttpCode(200)),
                    name: $"http-{name}",
                    tags: [PublicHttpsTag],
                    timeout: CheckTimeout);
        }

#pragma warning disable ASPIREPIPELINES001
        builder.Pipeline.AddStep(
            "check-public-https",
            async context =>
            {
                var service = context.Services
                    .GetRequiredService<HealthCheckService>();
                var report = await service.CheckHealthAsync(
                    registration => registration.Tags.Contains(
                        PublicHttpsTag),
                    context.CancellationToken);
                foreach (var (name, entry) in report.Entries
                    .OrderBy(entry => entry.Key, StringComparer.Ordinal))
                {
                    context.Summary.Add(
                        name,
                        entry.Status is HealthStatus.Healthy
                            ? $"Healthy ({entry.Duration.TotalMilliseconds:F0} ms)"
                            : Describe(entry));
                }

                RequireHealthy(report);
            });
#pragma warning restore ASPIREPIPELINES001

        return builder;
    }

    internal static void RequireHealthy(HealthReport report)
    {
        if (report.Entries.Count is 0)
        {
            throw new InvalidOperationException(
                "No public HTTPS health checks were registered.");
        }

        if (report.Status is HealthStatus.Healthy)
        {
            return;
        }

        var failures = report.Entries
            .Where(entry =>
                entry.Value.Status is not HealthStatus.Healthy)
            .Select(entry =>
                $"{entry.Key}: {Describe(entry.Value)}");
        throw new InvalidOperationException(
            $"Public HTTPS health checks failed: {string.Join("; ", failures)}");
    }

    private static string Describe(HealthReportEntry entry) =>
        entry.Exception?.Message ??
        entry.Description ??
        entry.Status.ToString();
}
