using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

using Aspire.Hosting.Pipelines;
using Microsoft.Extensions.DependencyInjection;

namespace MattKotsenas.AppHost;

internal static class BlogHttpsHealth
{
    private static readonly TimeSpan MinimumCertificateLifetime =
        TimeSpan.FromDays(30);

#pragma warning disable ASPIREPIPELINES001
    internal static async Task CheckAsync(
        PipelineStepContext context,
        BlogCustomDomainResource domain)
    {
        var timeProvider =
            context.Services.GetRequiredService<TimeProvider>();
        using var httpClient = new HttpClient
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
        using var timeout = new CancellationTokenSource(
            TimeSpan.FromSeconds(30),
            timeProvider);
        using var linked = CancellationTokenSource
            .CreateLinkedTokenSource(
                context.CancellationToken,
                timeout.Token);

        try
        {
            await CheckDomainAsync(
                httpClient,
                timeProvider,
                domain,
                context,
                linked.Token);
        }
        catch (OperationCanceledException)
            when (timeout.IsCancellationRequested &&
                !context.CancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException(
                $"The HTTPS check for '{domain.Hostname}' timed out.");
        }
    }
#pragma warning restore ASPIREPIPELINES001

#pragma warning disable ASPIREPIPELINES001
    private static async Task CheckDomainAsync(
        HttpClient httpClient,
        TimeProvider timeProvider,
        BlogCustomDomainResource domain,
        PipelineStepContext context,
        CancellationToken cancellationToken)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(
            domain.Hostname,
            443,
            cancellationToken);
        await using var ssl = new SslStream(
            client.GetStream(),
            leaveInnerStreamOpen: false);
        await ssl.AuthenticateAsClientAsync(
            new SslClientAuthenticationOptions
            {
                TargetHost = domain.Hostname,
                CertificateRevocationCheckMode =
                    X509RevocationMode.Online,
            },
            cancellationToken);
        var remoteCertificate = ssl.RemoteCertificate
            ?? throw new InvalidOperationException(
                $"'{domain.Hostname}' returned no TLS certificate.");
        using var certificate = X509CertificateLoader.LoadCertificate(
            remoteCertificate.GetRawCertData());
        var expiresOn = new DateTimeOffset(
            certificate.NotAfter.ToUniversalTime());
        if (expiresOn <=
            timeProvider.GetUtcNow() + MinimumCertificateLifetime)
        {
            throw new InvalidOperationException(
                $"The certificate for '{domain.Hostname}' expires on {expiresOn:O}.");
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Head,
            $"https://{domain.Hostname}/");
        using var response = await httpClient.SendAsync(
            request,
            cancellationToken);
        if (response.StatusCode is not HttpStatusCode.OK)
        {
            throw new InvalidOperationException(
                $"'{domain.Hostname}' returned HTTP {(int)response.StatusCode}.");
        }

        var fingerprint = Convert.ToHexString(
            SHA256.HashData(certificate.RawData));
        context.Summary.Add(
            domain.Hostname,
            $"{expiresOn:O} ({fingerprint})");
    }
#pragma warning restore ASPIREPIPELINES001
}
