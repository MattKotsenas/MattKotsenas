using Aspire.Hosting.ApplicationModel;
using CliWrap.Exceptions;
using MattKotsenas.AppHost;
using Microsoft.Extensions.Configuration;

namespace MattKotsenas.AppHost.Tests;

[CollectionDefinition("Bootstrap CLI", DisableParallelization = true)]
public sealed class BootstrapCliTestIsolation;

[Collection("Bootstrap CLI")]
public sealed class BootstrapCliTests
{
    private const string Subscription = "11111111-1111-1111-1111-111111111111";
    private const string Tenant = "22222222-2222-2222-2222-222222222222";

    [Theory]
    [InlineData("missing", "Default-Web-WestUS")]
    [InlineData("dns", "missing")]
    public async Task MissingExternalGroupStopsBootstrapBeforeHostingOrIdentityProvisioning(string dns, string legacy)
    {
        using var cli = new FakeCli("missing");
        Assert.Empty(cli.Trace);
        var error = await Assert.ThrowsAsync<CommandExecutionException>(() => BootstrapPreflight.VerifyAsync(
            Configuration(), [new ParameterResource("dns", _ => dns), new ParameterResource("legacy", _ => legacy)],
            TestContext.Current.CancellationToken));
        Assert.Equal(44, error.ExitCode);
        Assert.Contains("group show --name missing --subscription " + Subscription, cli.Trace);
        Assert.DoesNotContain("group exists", cli.Trace);
        Assert.DoesNotContain("identity", cli.Trace);
    }

    [Theory]
    [InlineData("dns", "Default-Web-WestUS")]
    [InlineData("dns-override", "legacy-override")]
    public async Task BootstrapChecksResolvedExternalGroupsAndAllowsNewHostingGroup(string dns, string legacy)
    {
        using var cli = new FakeCli("missing");
        Assert.Empty(cli.Trace);
        var tenant = await BootstrapPreflight.VerifyAsync(
            Configuration(), [new ParameterResource("dns", _ => dns), new ParameterResource("legacy", _ => legacy)],
            TestContext.Current.CancellationToken);
        Assert.Equal(Tenant, tenant);
        Assert.Contains($"group show --name {dns} --subscription {Subscription}", cli.Trace);
        Assert.Contains($"group show --name {legacy} --subscription {Subscription}", cli.Trace);
        Assert.Contains("group exists --name blog", cli.Trace);
        Assert.DoesNotContain("identity", cli.Trace);
    }

    private static IConfiguration Configuration() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Azure:SubscriptionId"] = Subscription,
        ["Azure:Location"] = "westus3",
        ["Azure:ResourceGroup"] = "blog",
    }).Build();

    private sealed class FakeCli : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "blog-cli-" + Guid.NewGuid().ToString("N"));
        private readonly string? originalPath = Environment.GetEnvironmentVariable("PATH");
        private readonly string tracePath;
        internal string Trace => File.ReadAllText(tracePath);

        internal FakeCli(string missingGroup)
        {
            Directory.CreateDirectory(directory);
            tracePath = Path.Combine(directory, "trace");
            File.WriteAllText(tracePath, string.Empty);
            var trace = "'" + tracePath.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";
            Write("gh", "printf '%s\\n' '{\"id\":8395662,\"name\":\"MattKotsenas\",\"full_name\":\"MattKotsenas/MattKotsenas\",\"owner\":{\"id\":51421,\"login\":\"MattKotsenas\"}}'");
            Write("az", $$"""
                printf '%s\n' "$*" >> {{trace}}
                case "$1 $2" in
                  'account show') printf '%s\n' '{"id":"{{Subscription}}","tenantId":"{{Tenant}}"}' ;;
                  'group show') if [ "$4" = '{{missingGroup}}' ]; then exit 44; fi ;;
                  'group exists') printf '%s\n' false ;;
                  *) exit 95 ;;
                esac
                """);
            Environment.SetEnvironmentVariable("PATH", directory + Path.PathSeparator + originalPath);
        }

        private void Write(string name, string body)
        {
            var path = Path.Combine(directory, name);
            File.WriteAllText(path, "#!/bin/sh\n" + body + "\n");
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
            Directory.Delete(directory, recursive: true);
        }
    }
}
