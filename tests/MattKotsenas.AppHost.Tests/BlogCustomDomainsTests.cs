using System.Text.Json;

using MattKotsenas.AppHost;

namespace MattKotsenas.AppHost.Tests;

public sealed class BlogCustomDomainsTests
{
    private const string EnvironmentId =
        "/subscriptions/11111111-1111-1111-1111-111111111111/resourceGroups/blog/providers/Microsoft.App/managedEnvironments/container-apps";
    [Fact]
    public void CatalogDefinesApexAndWwwForEachZone()
    {
        var zones = BlogCustomDomains.All
            .Select(domain => domain.Zone.Name)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(BlogCustomDomains.Zones.Count, zones.Length);
        Assert.Equal(
            zones.SelectMany(zone => new[] { zone, $"www.{zone}" })
                .Order(StringComparer.Ordinal),
            BlogCustomDomains.All
                .Select(domain => domain.Hostname)
                .Order(StringComparer.Ordinal));
        Assert.All(
            BlogCustomDomains.All,
            domain => Assert.Equal(
                domain.Hostname == domain.Zone.Name
                    ? "_dnsauth"
                    : "_dnsauth.www",
                domain.ValidationRecordName));
    }

    [Fact]
    public async Task PublishValidationPublishesDistinctTokensConcurrently()
    {
        var runner = new RecordingCommandRunner();
        var operations = new BlogCustomDomainDeployment(
            runner,
            azureSubscriptionId:
                "11111111-1111-1111-1111-111111111111",
            azureResourceGroup: "blog",
            dnsResourceGroup: "dns",
            appName: "blog",
            pollInterval: TimeSpan.Zero,
            provisioningTimeout: TimeSpan.FromMinutes(1));

        await operations.RecoverAsync(
            TestContext.Current.CancellationToken);
        await operations.PublishValidationAndWaitForCertificatesAsync(
            TestContext.Current.CancellationToken);

        var certificateList = runner.Invocations.FindLastIndex(
            invocation => invocation is
                "az containerapp env certificate list");
        var tokenWrites = runner.Invocations
            .Select((invocation, index) => (invocation, index))
            .Where(item => item.invocation is
                "az network dns record-set txt add-record")
            .ToArray();

        Assert.Equal(BlogCustomDomains.All.Count, tokenWrites.Length);
        Assert.All(
            tokenWrites,
            item => Assert.InRange(
                item.index,
                certificateList + 1,
                runner.Invocations.Count - 1));
        Assert.Equal(
            BlogCustomDomains.All.Count,
            runner.MaxConcurrentTokenWrites);
        Assert.Equal(
            BlogCustomDomains.All
                .Select(domain =>
                    $"{domain.Zone.Name}|{domain.ValidationRecordName}|token-{domain.CertificateName}")
                .Order(StringComparer.Ordinal),
            runner.TokenRecords.Order(StringComparer.Ordinal));
        Assert.DoesNotContain(
            runner.Invocations,
            invocation => invocation is
                "az network dns record-set txt create");
    }

    [Fact]
    public async Task DeploymentStepsLeaveCertificateCreationAndBindingToBicep()
    {
        var runner = new RecordingCommandRunner();

        await RunDeploymentStepsAsync(runner);

        Assert.DoesNotContain(
            runner.Invocations,
            invocation =>
                invocation.Contains(
                    "certificate create",
                    StringComparison.Ordinal) ||
                invocation.Contains(
                    "hostname add",
                    StringComparison.Ordinal) ||
                invocation.Contains(
                    "hostname bind",
                    StringComparison.Ordinal));
    }

    [Fact]
    public async Task DeploymentStepsRetryTransientProbeFailures()
    {
        var runner = new RecordingCommandRunner();

        await RunDeploymentStepsAsync(runner);

        Assert.Equal(
            BlogCustomDomains.All.Count * 2,
            runner.Invocations.Count(invocation =>
                invocation is "curl"));
    }

    [Fact]
    public async Task DeploymentStepsRetryWhenResourceGroupIsNotCreatedYet()
    {
        var runner = new RecordingCommandRunner(
            missingAppReads: 2,
            tokenCertificateList: 1);

        await RunDeploymentStepsAsync(runner);

        Assert.True(
            runner.Invocations.Count(invocation =>
                invocation is "az containerapp show") >= 3);
    }

    [Fact]
    public async Task DeploymentStepsRepairExistingEmptyValidationRecords()
    {
        var runner = new RecordingCommandRunner(
            emptyValidationRecords: true);

        await RunDeploymentStepsAsync(runner);

        Assert.Equal(
            BlogCustomDomains.All.Count,
            runner.TokenRecords.Count);
    }

    [Theory]
    [InlineData("Failed")]
    [InlineData("Canceled")]
    [InlineData("DeleteFailed")]
    public async Task RecoverRemovesTokenBeforeDeletingTerminalCertificate(
        string terminalState)
    {
        var runner = new RecordingCommandRunner(
            terminalCertificateState: terminalState);

        await CreateOperations(runner)
            .RecoverAsync(TestContext.Current.CancellationToken);

        var removeToken = runner.Invocations.FindIndex(
            invocation => invocation is
                "az network dns record-set txt remove-record");
        var deleteCertificate = runner.Invocations.FindIndex(
            invocation => invocation is
                "az containerapp env certificate delete");
        Assert.InRange(removeToken, 0, deleteCertificate - 1);
    }

    [Fact]
    public async Task RecoverRejectsUnmodeledCustomDomainBeforeMutation()
    {
        var runner = new RecordingCommandRunner(
            unexpectedCustomDomain: "other.kotsenas.com");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateOperations(runner)
                .RecoverAsync(TestContext.Current.CancellationToken));

        Assert.Contains("other.kotsenas.com", exception.Message);
        Assert.DoesNotContain(
            runner.Invocations,
            invocation => invocation is
                "az containerapp env certificate list" or
                "az network dns record-set txt remove-record" or
                "az containerapp env certificate delete");
    }

    [Theory]
    [InlineData("Disabled", "unexpected")]
    [InlineData("SniEnabled", null)]
    [InlineData("Unknown", null)]
    [InlineData("Unknown", "expected")]
    [InlineData("Auto", "unexpected")]
    [InlineData("SniEnabled", "unexpected")]
    public async Task RecoverRejectsUnmodeledBindingBeforeMutation(
        string bindingType,
        string? certificateId)
    {
        var runner = new RecordingCommandRunner(
            existingBindingType: bindingType,
            existingBindingCertificateId: certificateId);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateOperations(runner)
                .RecoverAsync(TestContext.Current.CancellationToken));

        Assert.DoesNotContain(
            runner.Invocations,
            invocation => invocation is
                "az containerapp env certificate list" or
                "az network dns record-set txt remove-record" or
                "az containerapp env certificate delete");
    }

    private static BlogCustomDomainDeployment CreateOperations(
        ICommandRunner runner) =>
        new(
            runner,
            azureSubscriptionId:
                "11111111-1111-1111-1111-111111111111",
            azureResourceGroup: "blog",
            dnsResourceGroup: "dns",
            appName: "blog",
            pollInterval: TimeSpan.Zero,
            provisioningTimeout: TimeSpan.FromMinutes(1));

    private static async Task RunDeploymentStepsAsync(
        ICommandRunner runner)
    {
        var operations = CreateOperations(runner);
        var cancellationToken =
            TestContext.Current.CancellationToken;
        await operations.RecoverAsync(cancellationToken);
        await operations.PublishValidationAndWaitForCertificatesAsync(
            cancellationToken);
        await operations.VerifyCurrentDeploymentAsync(
            cancellationToken);
    }

    private sealed class RecordingCommandRunner(
        int missingAppReads = 0,
        bool emptyValidationRecords = false,
        string? terminalCertificateState = null,
        int tokenCertificateList = 2,
        string? unexpectedCustomDomain = null,
        string? existingBindingType = null,
        string? existingBindingCertificateId = null)
        : ICommandRunner
    {
        private static readonly object[] ValidationRecords =
        [
            new
            {
                txtRecords = new[]
                {
                    new { value = new[] { "retained-token" } },
                },
            },
        ];
        private static readonly object[] OldTokenRecords =
        [
            new
            {
                txtRecords = new[]
                {
                    new { value = new[] { "old-token" } },
                },
            },
        ];
        private static readonly object[] EmptyValidationRecords =
        [
            new { txtRecords = (object?)null },
        ];
        private int appShows;
        private int certificateLists;
        private int concurrentTokenWrites;
        private int enteredTokenWrites;
        private int maxConcurrentTokenWrites;
        private int tokenWrites;
        private readonly Lock sync = new();
        private readonly TaskCompletionSource allTokenWritesEntered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly HashSet<string> failedProbes =
            new(StringComparer.OrdinalIgnoreCase);
        private int remainingMissingAppReads =
            missingAppReads;
        private bool oldTokenPresent =
            terminalCertificateState is not null;

        public List<string> Invocations { get; } = [];

        public int MaxConcurrentTokenWrites =>
            Volatile.Read(ref maxConcurrentTokenWrites);

        public List<string> TokenRecords { get; } = [];

        public async Task<CommandOutput> RunAsync(
            string command,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (sync)
            {
                Invocations.Add(
                    command == "curl"
                        ? command
                        : $"{command} {string.Join(
                            ' ',
                            arguments.TakeWhile(argument =>
                                !argument.StartsWith(
                                    "--",
                                    StringComparison.Ordinal)))}");
            }

            if (command == "az" &&
                arguments is
                    ["network", "dns", "record-set", "txt", "add-record", ..])
            {
                return await AddValidationTokenAsync(
                    arguments,
                    cancellationToken);
            }

            return (command, arguments) switch
            {
                ("az", ["containerapp", "show", ..]) =>
                    ShowContainerApp(),
                ("az", ["containerapp", "env", "certificate", "list", ..]) =>
                    ListCertificates(),
                ("az", ["network", "dns", "record-set", "txt", "list", ..]) =>
                    ListValidationRecords(),
                ("az", ["network", "dns", "record-set", "txt", "create", ..]) =>
                    Success(),
                ("az", ["network", "dns", "record-set", "txt", "remove-record", ..]) =>
                    RemoveValidationToken(),
                ("az", ["containerapp", "env", "certificate", "delete", ..]) =>
                    Success(),
                ("az", ["containerapp", "env", "show", ..]) =>
                    Json(new { staticIp = "4.148.87.198" }),
                ("curl", _) =>
                    Probe(arguments),
                _ => throw new InvalidOperationException(
                    $"Unexpected invocation: {command} {string.Join(' ', arguments)}"),
            };
        }

        private CommandOutput ShowContainerApp()
        {
            if (remainingMissingAppReads > 0)
            {
                remainingMissingAppReads--;
                return new CommandOutput(
                    3,
                    string.Empty,
                    "(ResourceGroupNotFound) Resource group 'blog' could not be found.");
            }

            appShows++;
            object[] customDomains =
                appShows == 1 && unexpectedCustomDomain is not null
                ? [new
                {
                    name = unexpectedCustomDomain,
                    bindingType = "Auto",
                    certificateId = (string?)null,
                }]
                : appShows == 1 && existingBindingType is not null
                ? [new
                {
                    name = BlogCustomDomains.All[0].Hostname,
                    bindingType = existingBindingType,
                    certificateId =
                        existingBindingCertificateId == "expected"
                            ? $"{EnvironmentId}/managedCertificates/{BlogCustomDomains.All[0].CertificateName}"
                            : existingBindingCertificateId,
                }]
                : appShows == 1
                ? []
                : BlogCustomDomains.All
                    .Select(domain => new
                    {
                        name = domain.Hostname,
                        bindingType = "Auto",
                        certificateId =
                            $"{EnvironmentId}/managedCertificates/{domain.CertificateName}",
                    })
                    .Cast<object>()
                    .ToArray();
            return Json(new
            {
                environmentId = EnvironmentId,
                customDomains,
            });
        }

        private CommandOutput ListCertificates()
        {
            certificateLists++;
            return Json(BlogCustomDomains.All.Select((domain, index) => new
            {
                id =
                    $"{EnvironmentId}/managedCertificates/{domain.CertificateName}",
                name = domain.CertificateName,
                properties = new
                {
                    provisioningState =
                        certificateLists == 1 &&
                        index == 0 &&
                        terminalCertificateState is not null
                            ? terminalCertificateState
                            : "Succeeded",
                    validationToken =
                        certificateLists == 1 &&
                        index == 0 &&
                        terminalCertificateState is not null
                            ? "old-token"
                            : certificateLists ==
                                tokenCertificateList
                            ? $"token-{domain.CertificateName}"
                            : null,
                    error = (string?)null,
                },
            }));
        }

        private CommandOutput ListValidationRecords() =>
            tokenWrites == BlogCustomDomains.All.Count
                ? Json(ValidationRecords)
                : oldTokenPresent
                    ? Json(OldTokenRecords)
                : emptyValidationRecords
                    ? Json(EmptyValidationRecords)
                    : Json(Array.Empty<object>());

        private CommandOutput RemoveValidationToken()
        {
            oldTokenPresent = false;
            return Success();
        }

        private async Task<CommandOutput> AddValidationTokenAsync(
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken)
        {
            lock (sync)
            {
                TokenRecords.Add(
                    $"{ValueAfter(arguments, "--zone-name")}|{ValueAfter(arguments, "--record-set-name")}|{ValueAfter(arguments, "--value")}");
            }

            var concurrent = Interlocked.Increment(
                ref concurrentTokenWrites);
            SetMaximum(ref maxConcurrentTokenWrites, concurrent);
            if (Interlocked.Increment(ref enteredTokenWrites) ==
                BlogCustomDomains.All.Count)
            {
                allTokenWritesEntered.SetResult();
            }

            await allTokenWritesEntered.Task.WaitAsync(cancellationToken);
            Interlocked.Decrement(ref concurrentTokenWrites);
            Interlocked.Increment(ref tokenWrites);
            return Success();
        }

        private CommandOutput Probe(IReadOnlyList<string> arguments)
        {
            var resolve = ValueAfter(arguments, "--resolve");
            var hostname = resolve[..resolve.IndexOf(':')];
            bool fail;
            lock (sync)
            {
                fail = failedProbes.Add(hostname);
            }

            if (fail)
            {
                return new CommandOutput(
                    28,
                    string.Empty,
                    "Not ready");
            }

            return new CommandOutput(
                0,
                "HTTP/1.1 200 OK",
                string.Empty);
        }

        private static void SetMaximum(ref int target, int value)
        {
            var current = Volatile.Read(ref target);
            while (value > current)
            {
                var observed = Interlocked.CompareExchange(
                    ref target,
                    value,
                    current);
                if (observed == current)
                {
                    return;
                }

                current = observed;
            }
        }

        private static string ValueAfter(
            IReadOnlyList<string> arguments,
            string option)
        {
            var index = arguments
                .Select((argument, index) => (argument, index))
                .Single(item =>
                    item.argument == option)
                .index;
            return arguments[index + 1];
        }

        private static CommandOutput Json(object value) =>
            new(0, JsonSerializer.Serialize(value), string.Empty);

        private static CommandOutput Success() =>
            new(0, string.Empty, string.Empty);
    }
}
