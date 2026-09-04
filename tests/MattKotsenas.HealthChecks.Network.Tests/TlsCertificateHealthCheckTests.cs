using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace MattKotsenas.HealthChecks.Network.Tests;

public sealed class TlsCertificateHealthCheckTests
{
    [Fact]
    public void RegistrationRejectsInvalidHostname()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentException>(
            () => services
                .AddHealthChecks()
                .AddTlsCertificateHealthCheck(
                    options => options.Hostname = "not a host"));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(65_536, 0)]
    [InlineData(443, -1)]
    public void RegistrationRejectsInvalidOptions(
        int port,
        int minimumLifetimeMilliseconds)
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => services
                .AddHealthChecks()
                .AddTlsCertificateHealthCheck(
                    options =>
                    {
                        options.Hostname = "example.com";
                        options.Port = port;
                        options.MinimumRemainingLifetime =
                            TimeSpan.FromMilliseconds(
                                minimumLifetimeMilliseconds);
                    }));
    }

    [Fact]
    public async Task CheckTimesOutDuringStalledHandshake()
    {
        using var listener = new TcpListener(
            IPAddress.Loopback,
            port: 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHealthChecks().AddTlsCertificateHealthCheck(
            options =>
            {
                options.Hostname = "localhost";
                options.Port = port;
            },
            name: "stalled",
            timeout: TimeSpan.FromMilliseconds(200));
        await using var provider = services.BuildServiceProvider();

        var check = provider
            .GetRequiredService<HealthCheckService>()
            .CheckHealthAsync(TestContext.Current.CancellationToken);
        using var accepted = await listener.AcceptTcpClientAsync(
            TestContext.Current.CancellationToken);
        var report = await check.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);

        Assert.Equal(HealthStatus.Unhealthy, report.Status);
        var entry = report.Entries["stalled"];
        Assert.IsAssignableFrom<OperationCanceledException>(
            entry.Exception);
        Assert.True(entry.Duration < TimeSpan.FromSeconds(2));
    }

    [Theory]
    [InlineData(29, HealthStatus.Degraded)]
    [InlineData(30, HealthStatus.Healthy)]
    [InlineData(31, HealthStatus.Healthy)]
    public void CertificateLifetimeDeterminesStatus(
        int remainingDays,
        HealthStatus expectedStatus)
    {
        var now = new DateTimeOffset(
            2026, 9, 4, 12, 0, 0, TimeSpan.Zero);
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=example.com",
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(
            now.AddDays(-1),
            now.AddDays(remainingDays));
        var check = new TlsCertificateHealthCheck(
            new TlsCertificateHealthCheckOptions
            {
                Hostname = "example.com",
                MinimumRemainingLifetime = TimeSpan.FromDays(30),
            },
            new FixedTimeProvider(now));

        var result = check.EvaluateCertificate(
            certificate,
            HealthStatus.Degraded);

        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(
            certificate.NotAfter.ToUniversalTime(),
            Assert.IsType<DateTimeOffset>(
                result.Data["expiresOn"]).UtcDateTime);
        Assert.Equal(
            Convert.ToHexString(
                SHA256.HashData(certificate.RawData)),
            result.Data["sha256"]);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow)
        : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
