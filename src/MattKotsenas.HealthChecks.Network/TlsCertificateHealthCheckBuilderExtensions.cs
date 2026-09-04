using MattKotsenas.HealthChecks.Network;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registers TLS certificate health checks.
/// </summary>
public static class TlsCertificateHealthCheckBuilderExtensions
{
    /// <summary>
    /// Adds a TLS certificate health check.
    /// </summary>
    /// <param name="builder">The health-check builder.</param>
    /// <param name="configure">Configures the target and certificate policy.</param>
    /// <param name="name">The health-check registration name.</param>
    /// <param name="failureStatus">The status reported when the check fails.</param>
    /// <param name="tags">Tags used to select the check.</param>
    /// <param name="timeout">The maximum check duration.</param>
    /// <returns>The supplied health-check builder.</returns>
    public static IHealthChecksBuilder AddTlsCertificateHealthCheck(
        this IHealthChecksBuilder builder,
        Action<TlsCertificateHealthCheckOptions> configure,
        string? name = null,
        HealthStatus? failureStatus = null,
        IEnumerable<string>? tags = null,
        TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new TlsCertificateHealthCheckOptions();
        configure(options);
        options.Validate();
        builder.Services.TryAddSingleton(TimeProvider.System);
        return builder.Add(new HealthCheckRegistration(
            name ?? "tls-certificate",
            services => new TlsCertificateHealthCheck(
                options,
                services.GetRequiredService<TimeProvider>()),
            failureStatus,
            tags,
            timeout));
    }
}
