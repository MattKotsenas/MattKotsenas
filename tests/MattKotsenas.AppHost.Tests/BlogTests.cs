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
                [
                    "Blog:HostPort=0",
                    "Blog:UseProductionContainer=false",
                ],
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

    [Fact]
    public async Task ProductionContainerPreservesWebsiteBehavior()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.MattKotsenas_AppHost>(
                [
                    "Blog:HostPort=0",
                    "Blog:UseProductionContainer=true",
                ],
                cancellationToken);

        await using var app = await appHost
            .BuildAsync(cancellationToken)
            .WaitAsync(DefaultTimeout, cancellationToken);

        await app
            .StartAsync(cancellationToken)
            .WaitAsync(DefaultTimeout, cancellationToken);

        var blogResource = await app.ResourceNotifications
            .WaitForResourceHealthyAsync("blog", cancellationToken)
            .WaitAsync(DefaultTimeout, cancellationToken);

        using var client = new HttpClient(
            new HttpClientHandler { AllowAutoRedirect = false })
        {
            BaseAddress = app.GetEndpoint("blog", "http"),
        };
        using var about = await client.GetAsync(
            "/about/",
            cancellationToken);
        using var legacyAbout = await client.GetAsync(
            "/about.html",
            cancellationToken);
        using var webConfig = await client.GetAsync(
            "/web.config",
            cancellationToken);
        using var manifest = await client.GetAsync(
            "/site.webmanifest",
            cancellationToken);
        using var filteringImage = await client.GetAsync(
            "/posts/using-wpa-to-analyze-performance-marks/filter-to-marks.png",
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, about.StatusCode);
        Assert.Equal(HttpStatusCode.MovedPermanently, legacyAbout.StatusCode);
        Assert.Equal("/about", legacyAbout.Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.NotFound, webConfig.StatusCode);
        Assert.Equal(HttpStatusCode.OK, manifest.StatusCode);
        Assert.Equal(
            "application/manifest+json",
            manifest.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.OK, filteringImage.StatusCode);
        Assert.Equal(
            "image/png",
            filteringImage.Content.Headers.ContentType?.MediaType);

        var containerId = Assert.IsType<string>(
            Assert.Single(
                blogResource.Snapshot.Properties,
                property => string.Equals(
                    property.Name,
                    "container.id",
                    StringComparison.Ordinal))
            .Value);
        var runner = new CliWrapCommandRunner();
        var user = await runner.RunAsync(
            "docker",
            ["inspect", "--format", "{{.Config.User}}", containerId],
            cancellationToken);

        Assert.Equal(0, user.ExitCode);
        Assert.Equal("101", user.StandardOutput.Trim());
    }
}
