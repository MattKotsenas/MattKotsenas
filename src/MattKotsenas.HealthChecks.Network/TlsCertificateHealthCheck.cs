using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace MattKotsenas.HealthChecks.Network;

/// <summary>
/// Checks TLS connectivity, certificate trust, and remaining certificate
/// lifetime for a remote endpoint.
/// </summary>
public sealed class TlsCertificateHealthCheck : IHealthCheck
{
    private readonly string _hostname;
    private readonly TimeSpan _minimumRemainingLifetime;
    private readonly int _port;
    private readonly X509RevocationMode _revocationMode;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a TLS certificate health check.
    /// </summary>
    /// <param name="options">The target and certificate policy.</param>
    /// <param name="timeProvider">The source of current time.</param>
    public TlsCertificateHealthCheck(
        TlsCertificateHealthCheckOptions options,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        options.Validate();
        _hostname = options.Hostname;
        _port = options.Port;
        _minimumRemainingLifetime =
            options.MinimumRemainingLifetime;
        _revocationMode = options.RevocationMode;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(
                _hostname,
                _port,
                cancellationToken);
            await using var ssl = new SslStream(
                client.GetStream(),
                leaveInnerStreamOpen: false);
            await ssl.AuthenticateAsClientAsync(
                new SslClientAuthenticationOptions
                {
                    TargetHost = _hostname,
                    CertificateRevocationCheckMode =
                        _revocationMode,
                },
                cancellationToken);
            var remoteCertificate = ssl.RemoteCertificate
                ?? throw new AuthenticationException(
                    $"'{_hostname}' returned no TLS certificate.");
            using var certificate =
                X509CertificateLoader.LoadCertificate(
                    remoteCertificate.GetRawCertData());
            return EvaluateCertificate(
                certificate,
                context.Registration.FailureStatus);
        }
        catch (Exception exception)
            when (exception is
                AuthenticationException or
                IOException or
                SocketException)
        {
            return new HealthCheckResult(
                context.Registration.FailureStatus,
                exception: exception);
        }
    }

    internal HealthCheckResult EvaluateCertificate(
        X509Certificate2 certificate,
        HealthStatus failureStatus)
    {
        var expiresOn = new DateTimeOffset(
            certificate.NotAfter.ToUniversalTime());
        var fingerprint = Convert.ToHexString(
            SHA256.HashData(certificate.RawData));
        var data = new Dictionary<string, object>
        {
            ["expiresOn"] = expiresOn,
            ["sha256"] = fingerprint,
        };
        var description =
            $"The certificate for '{_hostname}' expires on {expiresOn:O}.";
        return expiresOn <
            _timeProvider.GetUtcNow() +
            _minimumRemainingLifetime
                ? new HealthCheckResult(
                    failureStatus,
                    description,
                    data: data)
                : HealthCheckResult.Healthy(
                    description,
                    data);
    }
}
