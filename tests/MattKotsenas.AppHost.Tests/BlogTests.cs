namespace MattKotsenas.AppHost.Tests;

public sealed class BlogTests
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(5);

    [Fact]
    public async Task BlogRootReturnsOk()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.MattKotsenas_AppHost>(
                ["Blog:HostPort=0"],
                cancellationToken);

        await using var app = await appHost
            .BuildAsync(cancellationToken)
            .WaitAsync(DefaultTimeout, cancellationToken);

        await app
            .StartAsync(cancellationToken)
            .WaitAsync(DefaultTimeout, cancellationToken);

        await app.ResourceNotifications
            .WaitForResourceHealthyAsync("blog", cancellationToken)
            .WaitAsync(DefaultTimeout, cancellationToken);

        using var client = app.CreateHttpClient("blog");
        using var response = await client.GetAsync("/", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
