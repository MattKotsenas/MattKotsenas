using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace MattKotsenas.AppHost;

internal sealed class ResolvedHttpsEndpointProbe(TimeProvider timeProvider)
    : IHttpsEndpointProbe,
      IDisposable
{
    private static readonly TimeSpan Timeout =
        TimeSpan.FromSeconds(30);
    private readonly ConcurrentDictionary<string, HttpClient> clients =
        new(StringComparer.OrdinalIgnoreCase);

    public async Task<bool> IsHealthyAsync(
        string hostname,
        IPAddress address,
        CancellationToken cancellationToken)
    {
        var key = $"{hostname}|{address}";
        var client = clients.GetOrAdd(
            key,
            _ => CreateClient(address));
        using var timeout = new CancellationTokenSource(
            Timeout,
            timeProvider);
        using var linked = CancellationTokenSource
            .CreateLinkedTokenSource(
                cancellationToken,
                timeout.Token);

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Head,
                new Uri($"https://{hostname}/"));
            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                linked.Token);
            return response.StatusCode is HttpStatusCode.OK;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    public void Dispose()
    {
        foreach (var client in clients.Values)
        {
            client.Dispose();
        }
    }

    private static HttpClient CreateClient(IPAddress address)
    {
        var handler = new SocketsHttpHandler
        {
            UseProxy = false,
            AllowAutoRedirect = false,
            ConnectTimeout = Timeout,
            ConnectCallback = async (context, cancellationToken) =>
            {
                var socket = new Socket(
                    address.AddressFamily,
                    SocketType.Stream,
                    ProtocolType.Tcp)
                {
                    NoDelay = true,
                };
                try
                {
                    await socket.ConnectAsync(
                        new IPEndPoint(
                            address,
                            context.DnsEndPoint.Port),
                        cancellationToken);
                    return new NetworkStream(
                        socket,
                        ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            },
        };

        return new HttpClient(handler)
        {
            Timeout = System.Threading.Timeout.InfiniteTimeSpan,
        };
    }
}
