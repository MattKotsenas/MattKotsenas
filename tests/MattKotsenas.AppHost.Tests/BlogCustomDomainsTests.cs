using System.Text.Json;

using MattKotsenas.AppHost;
using Microsoft.Extensions.Time.Testing;

namespace MattKotsenas.AppHost.Tests;

public sealed class BlogCustomDomainsTests
{
    private const string EnvironmentId =
        "/subscriptions/11111111-1111-1111-1111-111111111111/resourceGroups/blog/providers/Microsoft.App/managedEnvironments/container-apps";
    private static readonly IReadOnlyList<BlogCustomDomainResource>
        Domains = BlogCustomDomainResource.CreateDefaults("blog");

    [Fact]
    public void DefaultsCreateIndependentApexAndWwwResources()
    {
        var zones = Domains
            .Select(domain => domain.Zone.Name)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(4, Domains.Count);
        Assert.Equal(Domains.Count, Domains.Select(domain => domain.Name)
            .Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
            zones.SelectMany(zone => new[] { zone, $"www.{zone}" })
                .Order(StringComparer.Ordinal),
            Domains.Select(domain => domain.Hostname)
                .Order(StringComparer.Ordinal));
        Assert.All(
            Domains,
            domain => Assert.Equal(
                domain.Hostname == domain.Zone.Name
                    ? "_dnsauth"
                    : "_dnsauth.www",
                domain.ValidationRecordName));
        Assert.All(
            Domains,
            domain =>
            {
                Assert.Equal(
                    domain.IsApex ? "@" : "www",
                    domain.DnsRecordName);
                Assert.Equal(
                    domain.IsApex ? "asuid" : "asuid.www",
                    domain.OwnershipRecordName);
            });
    }

    [Fact]
    public async Task DomainPublicationWritesDistinctTokensConcurrently()
    {
        var runner = new RecordingCommandRunner();
        var deployment = CreateDeployment(runner);
        var cancellationToken =
            TestContext.Current.CancellationToken;

        await deployment.ValidateAsync(Domains, cancellationToken);
        await Task.WhenAll(Domains.Select(domain =>
            deployment.RecoverAsync(domain, cancellationToken)));
        await Task.WhenAll(Domains.Select(domain =>
            deployment.PublishValidationAndWaitForCertificateAsync(
                domain,
                cancellationToken)));

        var firstCertificateList = runner.Invocations.FindIndex(
            invocation => invocation is
                "az containerapp env certificate list");
        var tokenWrites = runner.Invocations
            .Select((invocation, index) => (invocation, index))
            .Where(item => item.invocation is
                "az network dns record-set txt add-record")
            .ToArray();

        Assert.Equal(Domains.Count, tokenWrites.Length);
        Assert.All(
            tokenWrites,
            item => Assert.True(item.index > firstCertificateList));
        Assert.Equal(
            Domains.Count,
            runner.MaxConcurrentTokenWrites);
        Assert.Equal(
            Domains
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
    public async Task DeploymentLeavesCertificateCreationAndBindingToBicep()
    {
        var runner = new RecordingCommandRunner();

        await RunDeploymentAsync(runner);

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
    public async Task DomainVerificationRetriesTransientProbeFailures()
    {
        var runner = new RecordingCommandRunner();

        await RunDeploymentAsync(runner);

        Assert.Equal(
            Domains.Count * 2,
            runner.Invocations.Count(invocation =>
                invocation is "curl"));
    }

    [Fact]
    public async Task DeploymentRetriesUntilContainerAppExists()
    {
        var runner = new RecordingCommandRunner(
            missingAppReads: 2);

        await RunDeploymentAsync(runner);

        Assert.True(
            runner.Invocations.Count(invocation =>
                invocation is "az containerapp show") >= 3);
    }

    [Fact]
    public async Task DomainPublicationRepairsEmptyValidationRecord()
    {
        var runner = new RecordingCommandRunner(
            emptyValidationRecords: true);

        await RunDeploymentAsync(runner);

        Assert.Equal(Domains.Count, runner.TokenRecords.Count);
    }

    [Theory]
    [InlineData("Failed")]
    [InlineData("Canceled")]
    [InlineData("DeleteFailed")]
    public async Task DomainRecoveryRemovesTokenBeforeTerminalCertificate(
        string terminalState)
    {
        var runner = new RecordingCommandRunner(
            terminalCertificateState: terminalState);
        var deployment = CreateDeployment(runner);
        var cancellationToken =
            TestContext.Current.CancellationToken;

        await deployment.ValidateAsync(Domains, cancellationToken);
        await deployment.RecoverAsync(
            Domains[0],
            cancellationToken);

        var removeToken = runner.Invocations.FindIndex(
            invocation => invocation is
                "az network dns record-set txt remove-record");
        var deleteCertificate = runner.Invocations.FindIndex(
            invocation => invocation is
                "az containerapp env certificate delete");
        Assert.InRange(removeToken, 0, deleteCertificate - 1);
    }

    [Fact]
    public async Task ValidationRejectsUnmodeledDomainBeforeMutation()
    {
        var runner = new RecordingCommandRunner(
            unexpectedCustomDomain: "other.kotsenas.com");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateDeployment(runner)
                .ValidateAsync(
                    Domains,
                    TestContext.Current.CancellationToken));

        Assert.Contains("other.kotsenas.com", exception.Message);
        AssertNoMutation(runner);
    }

    [Theory]
    [InlineData("Disabled", "unexpected")]
    [InlineData("SniEnabled", null)]
    [InlineData("Unknown", null)]
    [InlineData("Unknown", "expected")]
    [InlineData("Auto", "unexpected")]
    [InlineData("SniEnabled", "unexpected")]
    public async Task ValidationRejectsUnmodeledBindingBeforeMutation(
        string bindingType,
        string? certificateId)
    {
        var runner = new RecordingCommandRunner(
            existingBindingType: bindingType,
            existingBindingCertificateId: certificateId);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateDeployment(runner)
                .ValidateAsync(
                    Domains,
                    TestContext.Current.CancellationToken));

        AssertNoMutation(runner);
    }

    [Fact]
    public async Task ValidationRejectsUnmodeledTerminalCertificate()
    {
        var runner = new RecordingCommandRunner(
            unexpectedTerminalCertificate: true);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateDeployment(runner)
                .ValidateAsync(
                    Domains,
                    TestContext.Current.CancellationToken));

        Assert.Contains("managed-other-domain", exception.Message);
        AssertNoMutation(runner);
    }

    [Fact]
    public async Task ValidationRejectsBoundTerminalCertificateBeforeMutation()
    {
        var runner = new RecordingCommandRunner(
            terminalCertificateState: "Failed",
            boundTerminalCertificate: true);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateDeployment(runner)
                .ValidateAsync(
                    Domains,
                    TestContext.Current.CancellationToken));

        Assert.Contains(Domains[1].CertificateName, exception.Message);
        AssertNoMutation(runner);
    }

    [Fact]
    public async Task CertificateTimeoutUsesInjectedTimeProvider()
    {
        var timeProvider = new FakeTimeProvider();
        var deployment = CreateDeployment(
            new RecordingCommandRunner(
                certificatesNeverAppear: true),
            timeProvider,
            pollInterval: TimeSpan.FromSeconds(10),
            provisioningTimeout: TimeSpan.FromMinutes(1));

        var task = deployment
            .PublishValidationAndWaitForCertificateAsync(
                Domains[0],
                TestContext.Current.CancellationToken);
        await Task.Yield();
        while (!task.IsCompleted)
        {
            timeProvider.Advance(TimeSpan.FromSeconds(10));
            await Task.Yield();
        }

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => task);
    }

    private static void AssertNoMutation(
        RecordingCommandRunner runner) =>
        Assert.DoesNotContain(
            runner.Invocations,
            invocation => invocation is
                "az network dns record-set txt remove-record" or
                "az containerapp env certificate delete");

    private static BlogCustomDomainDeployment CreateDeployment(
        ICommandRunner runner,
        TimeProvider? timeProvider = null,
        TimeSpan? pollInterval = null,
        TimeSpan? provisioningTimeout = null) =>
        new(
            runner,
            timeProvider ?? new FakeTimeProvider(),
            azureSubscriptionId:
                "11111111-1111-1111-1111-111111111111",
            azureResourceGroup: "blog",
            dnsResourceGroup: "dns",
            appName: "blog",
            pollInterval: pollInterval ?? TimeSpan.Zero,
            provisioningTimeout:
                provisioningTimeout ?? TimeSpan.FromMinutes(1));

    private static async Task RunDeploymentAsync(
        ICommandRunner runner)
    {
        var deployment = CreateDeployment(runner);
        var cancellationToken =
            TestContext.Current.CancellationToken;
        await deployment.ValidateAsync(Domains, cancellationToken);
        await Task.WhenAll(Domains.Select(domain =>
            deployment.RecoverAsync(domain, cancellationToken)));
        await Task.WhenAll(Domains.Select(domain =>
            deployment.PublishValidationAndWaitForCertificateAsync(
                domain,
                cancellationToken)));
        await Task.WhenAll(Domains.Select(domain =>
            deployment.VerifyCurrentDeploymentAsync(
                domain,
                cancellationToken)));
    }

    private sealed class RecordingCommandRunner(
        int missingAppReads = 0,
        bool emptyValidationRecords = false,
        string? terminalCertificateState = null,
        string? unexpectedCustomDomain = null,
        string? existingBindingType = null,
        string? existingBindingCertificateId = null,
        bool unexpectedTerminalCertificate = false,
        bool boundTerminalCertificate = false,
        bool certificatesNeverAppear = false)
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
        private int concurrentTokenWrites;
        private int enteredTokenWrites;
        private int maxConcurrentTokenWrites;
        private bool certificateDeleted;
        private readonly Lock sync = new();
        private readonly TaskCompletionSource allTokenWritesEntered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly HashSet<string> failedProbes =
            new(StringComparer.OrdinalIgnoreCase);
        private int remainingMissingAppReads = missingAppReads;
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
                    ListValidationRecords(arguments),
                ("az", ["network", "dns", "record-set", "txt", "remove-record", ..]) =>
                    RemoveValidationToken(),
                ("az", ["containerapp", "env", "certificate", "delete", ..]) =>
                    DeleteCertificate(),
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
                    name = Domains[0].Hostname,
                    bindingType = existingBindingType,
                    certificateId =
                        existingBindingCertificateId == "expected"
                            ? CertificateId(Domains[0])
                            : existingBindingCertificateId,
                }]
                : appShows == 1 && boundTerminalCertificate
                ? [new
                {
                    name = Domains[1].Hostname,
                    bindingType = "SniEnabled",
                    certificateId = CertificateId(Domains[1]),
                }]
                : appShows == 1 ||
                    terminalCertificateState is not null &&
                    !certificateDeleted &&
                    !boundTerminalCertificate
                ? []
                : Domains
                    .Select(domain => new
                    {
                        name = domain.Hostname,
                        bindingType = "Auto",
                        certificateId = CertificateId(domain),
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
            if (certificatesNeverAppear)
            {
                return Json(Array.Empty<object>());
            }

            var certificates = Domains.Select((domain, index) => new
            {
                id = CertificateId(domain),
                name = domain.CertificateName,
                properties = new
                {
                    provisioningState =
                        !certificateDeleted &&
                        (index == 0 ||
                            index == 1 &&
                            boundTerminalCertificate) &&
                        terminalCertificateState is not null
                            ? terminalCertificateState
                            : "Succeeded",
                    validationToken =
                        !certificateDeleted &&
                        index == 0 &&
                        terminalCertificateState is not null
                            ? "old-token"
                            : $"token-{domain.CertificateName}",
                    error = (string?)null,
                },
            }).Cast<object>().ToList();
            if (unexpectedTerminalCertificate)
            {
                certificates.Add(new
                {
                    id =
                        $"{EnvironmentId}/managedCertificates/managed-other-domain",
                    name = "managed-other-domain",
                    properties = new
                    {
                        provisioningState = "Failed",
                        validationToken = "other-token",
                        error = (string?)null,
                    },
                });
            }

            return Json(certificates);
        }

        private CommandOutput ListValidationRecords(
            IReadOnlyList<string> arguments)
        {
            var recordPrefix =
                $"{ValueAfter(arguments, "--zone-name")}|{ValueAfter(arguments, "--query").Split('\'')[1]}|";
            if (TokenRecords.Any(record =>
                record.StartsWith(
                    recordPrefix,
                    StringComparison.Ordinal)))
            {
                return Json(ValidationRecords);
            }

            return oldTokenPresent
                ? Json(OldTokenRecords)
                : emptyValidationRecords
                ? Json(EmptyValidationRecords)
                : Json(Array.Empty<object>());
        }

        private CommandOutput RemoveValidationToken()
        {
            oldTokenPresent = false;
            return Success();
        }

        private CommandOutput DeleteCertificate()
        {
            certificateDeleted = true;
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
                Domains.Count)
            {
                allTokenWritesEntered.SetResult();
            }

            await allTokenWritesEntered.Task.WaitAsync(cancellationToken);
            Interlocked.Decrement(ref concurrentTokenWrites);
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

            return fail
                ? new CommandOutput(
                    28,
                    string.Empty,
                    "Not ready")
                : new CommandOutput(
                    0,
                    "HTTP/1.1 200 OK",
                    string.Empty);
        }

        private static string CertificateId(
            BlogCustomDomainResource domain) =>
            $"{EnvironmentId}/managedCertificates/{domain.CertificateName}";

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
