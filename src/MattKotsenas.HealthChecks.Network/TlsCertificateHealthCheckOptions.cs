using System.Security.Cryptography.X509Certificates;

namespace MattKotsenas.HealthChecks.Network;

/// <summary>
/// Configures a TLS certificate health check.
/// </summary>
public sealed class TlsCertificateHealthCheckOptions
{
    /// <summary>
    /// Gets or sets the DNS hostname used for the connection and TLS
    /// server-name indication.
    /// </summary>
    public string Hostname { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the TCP port.
    /// </summary>
    public int Port { get; set; } = 443;

    /// <summary>
    /// Gets or sets the minimum remaining certificate lifetime required for a
    /// healthy result.
    /// </summary>
    public TimeSpan MinimumRemainingLifetime { get; set; } =
        TimeSpan.FromDays(30);

    /// <summary>
    /// Gets or sets the certificate revocation checking mode.
    /// </summary>
    public X509RevocationMode RevocationMode { get; set; } =
        X509RevocationMode.Online;

    internal void Validate()
    {
        if (Uri.CheckHostName(Hostname) is not UriHostNameType.Dns)
        {
            throw new ArgumentException(
                $"'{Hostname}' is not a DNS hostname.",
                nameof(Hostname));
        }

        if (Port is < 1 or > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(Port),
                Port,
                "The port must be between 1 and 65535.");
        }

        if (MinimumRemainingLifetime < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MinimumRemainingLifetime),
                MinimumRemainingLifetime,
                "The minimum remaining lifetime cannot be negative.");
        }
    }
}
