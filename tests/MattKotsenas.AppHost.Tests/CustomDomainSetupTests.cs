using System.Text.Json;

using MattKotsenas.AppHost;

namespace MattKotsenas.AppHost.Tests;

public sealed class CustomDomainSetupTests
{
    private static IReadOnlyList<CustomDomainSetup.CustomDomainDefinition>
        Domains => CustomDomainSetup.Domains;

    [Fact]
    public void DomainCatalogDefinesApexAndWwwForEveryZone()
    {
        var zones = Domains
            .Select(domain => domain.ZoneName)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(2, zones.Length);
        Assert.Equal(
            zones.Length,
            Domains.Select(domain => domain.ZoneIdentifier)
                .Distinct(StringComparer.Ordinal)
                .Count());
        Assert.Equal(
            zones.SelectMany(zone => new[] { zone, $"www.{zone}" })
                .Order(StringComparer.Ordinal),
            Domains.Select(domain => domain.Hostname)
                .Order(StringComparer.Ordinal));
        Assert.All(
            Domains,
            domain => Assert.Equal(
                domain.Hostname == domain.ZoneName
                    ? "_dnsauth"
                    : "_dnsauth.www",
                domain.ValidationRecordName));
        Assert.All(
            Domains.GroupBy(domain => domain.ZoneName),
            zone => Assert.Single(
                zone.Select(domain =>
                        domain.RetainLegacyApexVerificationId)
                    .Distinct()));
        Assert.Single(
            Domains,
            domain =>
                domain.Hostname == domain.ZoneName &&
                domain.RetainLegacyApexVerificationId);
        Assert.Equal(
            Domains.Count,
            Domains.Select(domain => domain.CertificateName)
                .Distinct(StringComparer.Ordinal)
                .Count());
        Assert.Equal(
            Domains.Count,
            Domains.Select(domain => domain.DomainParameterName)
                .Distinct(StringComparer.Ordinal)
                .Count());
        Assert.Equal(
            Domains.Count,
            Domains.Select(domain => domain.CertificateParameterName)
                .Distinct(StringComparer.Ordinal)
                .Count());
    }

    [Fact]
    public async Task PrepareCreatesAndVerifiesEveryCustomDomainInOrder()
    {
        var scenario = SetupScenario.Empty();

        await new CustomDomainSetup(scenario)
            .PrepareAsync(TestContext.Current.CancellationToken);

        scenario.AssertAllDomainsPrepared();
    }

    [Fact]
    public async Task PrepareLeavesMatchingConfigurationIntact()
    {
        var scenario = SetupScenario.Matching();

        await new CustomDomainSetup(scenario)
            .PrepareAsync(TestContext.Current.CancellationToken);

        scenario.AssertAllDomainsPrepared();
        Assert.Empty(scenario.Mutations);
    }

    [Theory]
    [InlineData("Failed")]
    [InlineData("Canceled")]
    [InlineData("DeleteFailed")]
    public async Task PrepareRecreatesUnboundTerminalCertificateInOrder(
        string terminalState)
    {
        var target = Domains[0];
        var scenario = SetupScenario.WithTerminalCertificate(
            target,
            terminalState);

        await new CustomDomainSetup(scenario)
            .PrepareAsync(TestContext.Current.CancellationToken);

        scenario.AssertAllDomainsPrepared();
        var expectedRecovery = new[]
        {
            $"dns-remove:{target.ZoneName}/{target.ValidationRecordName}",
            $"certificate-delete:{target.CertificateName}",
            $"certificate-create:{target.CertificateName}",
            $"dns-add:{target.ZoneName}/{target.ValidationRecordName}",
            $"certificate-poll:{target.CertificateName}",
            $"bind:{target.Hostname}",
            $"probe:{target.Hostname}",
        };
        Assert.Equal(
            expectedRecovery,
            scenario.Events.Where(expectedRecovery.Contains));
    }

    [Fact]
    public async Task PrepareRejectsUnexpectedCustomDomain()
    {
        var scenario = SetupScenario.WithUnexpectedBinding();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new CustomDomainSetup(scenario)
                .PrepareAsync(TestContext.Current.CancellationToken));

        Assert.Contains(
            "unexpected custom domains",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Empty(scenario.Events);
        Assert.Empty(scenario.Mutations);
    }

    [Theory]
    [InlineData("Disabled", "unexpected")]
    [InlineData("SniEnabled", null)]
    [InlineData("Unknown", null)]
    [InlineData("Unknown", "expected")]
    public async Task PrepareRejectsMalformedCertificateBindingBeforeMutation(
        string bindingType,
        string? certificateId)
    {
        var scenario = SetupScenario.WithMalformedCertificateBinding(
            bindingType,
            certificateId);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new CustomDomainSetup(scenario)
                .PrepareAsync(TestContext.Current.CancellationToken));

        Assert.Contains(
            "unexpected binding",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Empty(scenario.Events);
        Assert.Empty(scenario.Mutations);
    }

    private sealed class SetupScenario : ICommandRunner
    {
        private const string EnvironmentId =
            "/subscriptions/11111111-1111-1111-1111-111111111111/resourceGroups/blog/providers/Microsoft.App/managedEnvironments/container-apps";
        private const string StaticIp = "4.148.87.198";
        private readonly Dictionary<string, CertificateState> certificates =
            new(StringComparer.Ordinal);
        private readonly Dictionary<string, BindingState> bindings =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<(string Zone, string Name), List<string>>
            validationRecords = [];
        private readonly HashSet<string> verifiedHostnames =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> probedHostnames =
            new(StringComparer.OrdinalIgnoreCase);

        private SetupScenario()
        {
        }

        public List<string> Events { get; } = [];

        public IReadOnlyList<string> Mutations =>
            Events.Where(IsMutation).ToArray();

        public void AssertAllDomainsPrepared()
        {
            Assert.All(
                Domains,
                domain =>
                {
                    var certificate = certificates[domain.CertificateName];
                    Assert.Equal(
                        "Succeeded",
                        certificate.Properties.ProvisioningState);
                    Assert.Equal(
                        CertificateId(domain.CertificateName),
                        bindings[domain.Hostname].CertificateId);
                    Assert.Contains(
                        validationRecords[
                            (domain.ZoneName, domain.ValidationRecordName)],
                        value => !string.IsNullOrWhiteSpace(value));
                    Assert.Contains(domain.Hostname, verifiedHostnames);
                    Assert.Contains(domain.Hostname, probedHostnames);
                });
        }

        public static SetupScenario Empty() => new();

        public static SetupScenario Matching()
        {
            var scenario = new SetupScenario();
            foreach (var domain in Domains)
            {
                scenario.certificates.Add(
                    domain.CertificateName,
                    CertificateState.Succeeded(domain));
                scenario.bindings.Add(
                    domain.Hostname,
                    new BindingState(
                        domain.Hostname,
                        "SniEnabled",
                        CertificateId(domain.CertificateName)));
                scenario.validationRecords.Add(
                    (domain.ZoneName, domain.ValidationRecordName),
                    ["retained-token"]);
            }

            return scenario;
        }

        public static SetupScenario WithTerminalCertificate(
            CustomDomainSetup.CustomDomainDefinition target,
            string terminalState)
        {
            var scenario = new SetupScenario();
            foreach (var domain in Domains)
            {
                scenario.certificates.Add(
                    domain.CertificateName,
                    domain == target
                        ? CertificateState.Terminal(
                            domain,
                            terminalState,
                                "old-token")
                        : CertificateState.Succeeded(domain));
                scenario.bindings.Add(
                    domain.Hostname,
                    new BindingState(domain.Hostname, "Disabled", null));
                scenario.validationRecords.Add(
                    (domain.ZoneName, domain.ValidationRecordName),
                    [domain == target ? "old-token" : "retained-token"]);
            }

            return scenario;
        }

        public static SetupScenario WithUnexpectedBinding()
        {
            var scenario = new SetupScenario();
            scenario.bindings.Add(
                "unexpected.kotsenas.com",
                new BindingState(
                    "unexpected.kotsenas.com",
                    "SniEnabled",
                    "unexpected"));
            return scenario;
        }

        public static SetupScenario WithMalformedCertificateBinding(
            string bindingType,
            string? certificateId)
        {
            var scenario = new SetupScenario();
            var domain = Domains[0];
            scenario.bindings.Add(
                domain.Hostname,
                new BindingState(
                    domain.Hostname,
                    bindingType,
                    certificateId switch
                    {
                        "expected" => CertificateId(domain.CertificateName),
                        _ => certificateId,
                    }));
            return scenario;
        }

        public Task<CommandOutput> RunAsync(
            string command,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(command switch
            {
                "az" => RunAzure(arguments),
                "curl" => RunCurl(arguments),
                _ => throw Unexpected(command, arguments),
            });
        }

        private CommandOutput RunAzure(IReadOnlyList<string> arguments) =>
            arguments switch
            {
                ["containerapp", "show", ..] => Json(new
                {
                    environmentId = EnvironmentId,
                    customDomains = bindings.Values,
                }),
                ["containerapp", "env", "show", ..] =>
                    Json(new { staticIp = StaticIp }),
                ["rest", ..] => AnalyzeDomain(arguments),
                ["containerapp", "hostname", "add", ..] =>
                    AddHostname(arguments),
                ["containerapp", "env", "certificate", "list", ..]
                    when arguments.Contains("--certificate") =>
                    PollCertificate(arguments),
                ["containerapp", "env", "certificate", "list", ..] =>
                    Json(certificates.Values),
                ["containerapp", "env", "certificate", "create", ..] =>
                    CreateCertificate(arguments),
                ["containerapp", "env", "certificate", "delete", ..] =>
                    DeleteCertificate(arguments),
                ["network", "dns", "record-set", "txt", "list", ..] =>
                    ReadValidationRecord(arguments),
                ["network", "dns", "record-set", "txt", "create", ..] =>
                    CreateValidationRecord(arguments),
                ["network", "dns", "record-set", "txt", "add-record", ..] =>
                    AddValidationToken(arguments),
                ["network", "dns", "record-set", "txt", "remove-record", ..] =>
                    RemoveValidationToken(arguments),
                ["containerapp", "hostname", "bind", ..] =>
                    BindHostname(arguments),
                _ => throw Unexpected("az", arguments),
            };

        private CommandOutput AnalyzeDomain(IReadOnlyList<string> arguments)
        {
            var uri = new Uri(ValueAfter(arguments, "--uri"));
            Assert.Empty(uri.Query);
            var hostname = ValueFromUrlParameters(
                arguments,
                "customHostname");
            Assert.Equal(
                "2025-07-01",
                ValueFromUrlParameters(arguments, "api-version"));
            Assert.Contains(
                Domains,
                domain => string.Equals(
                    domain.Hostname,
                    hostname,
                    StringComparison.Ordinal));
            verifiedHostnames.Add(hostname);
            Events.Add($"ownership:{hostname}");

            return Json(new
            {
                customDomainVerificationTest = "Passed",
                hasConflictOnManagedEnvironment = false,
            });
        }

        private CommandOutput AddHostname(IReadOnlyList<string> arguments)
        {
            var hostname = ValueAfter(arguments, "--hostname");
            Assert.Contains(hostname, verifiedHostnames);
            Assert.False(bindings.ContainsKey(hostname));

            bindings.Add(
                hostname,
                new BindingState(hostname, "Disabled", null));
            Events.Add($"hostname-add:{hostname}");
            return Success();
        }

        private CommandOutput CreateCertificate(
            IReadOnlyList<string> arguments)
        {
            var certificateName = ValueAfter(arguments, "--certificate-name");
            var hostname = ValueAfter(arguments, "--hostname");
            var domain = Expected(certificateName);
            Assert.Equal(domain.Hostname, hostname);
            Assert.Equal("TXT", ValueAfter(arguments, "--validation-method"));
            Assert.True(bindings.ContainsKey(hostname));
            Assert.False(certificates.ContainsKey(certificateName));

            var certificate = CertificateState.Pending(domain);
            certificates.Add(certificateName, certificate);
            Events.Add($"certificate-create:{certificateName}");
            return Json(certificate);
        }

        private CommandOutput DeleteCertificate(
            IReadOnlyList<string> arguments)
        {
            var certificateName = ValueAfter(arguments, "--certificate");
            var certificate = certificates[certificateName];
            Assert.True(
                certificate.Properties.ProvisioningState is
                    "Failed" or "Canceled" or "DeleteFailed");
            Assert.DoesNotContain(
                certificate.Properties.ValidationToken!,
                validationRecords[
                    (certificate.Domain.ZoneName,
                        certificate.Domain.ValidationRecordName)]);

            certificates.Remove(certificateName);
            Events.Add($"certificate-delete:{certificateName}");
            return Success();
        }

        private CommandOutput PollCertificate(
            IReadOnlyList<string> arguments)
        {
            var certificateName = ValueAfter(arguments, "--certificate");
            var current = certificates[certificateName];
            var certificate = current with
            {
                Properties = current.Properties with
                {
                    ProvisioningState = "Succeeded",
                    SubjectName = $"CN={current.Domain.Hostname}",
                    ValidationToken = null,
                },
            };
            certificates[certificateName] = certificate;
            Events.Add($"certificate-poll:{certificateName}");
            return Json(new[] { certificate });
        }

        private CommandOutput ReadValidationRecord(
            IReadOnlyList<string> arguments)
        {
            var key = ValidationRecordKey(arguments);
            Events.Add($"dns-read:{key.Zone}/{key.Name}");

            return validationRecords.TryGetValue(key, out var values)
                ? Json(new[]
                {
                    new
                    {
                        name = key.Name,
                        txtRecords = values.Select(value =>
                            new { value = new[] { value } }),
                    },
                })
                : Json(Array.Empty<object>());
        }

        private CommandOutput CreateValidationRecord(
            IReadOnlyList<string> arguments)
        {
            var key = (
                Zone: ValueAfter(arguments, "--zone-name"),
                Name: ValueAfter(arguments, "--name"));
            Assert.False(validationRecords.ContainsKey(key));

            validationRecords.Add(key, []);
            Events.Add($"dns-create:{key.Zone}/{key.Name}");
            return Success();
        }

        private CommandOutput AddValidationToken(
            IReadOnlyList<string> arguments)
        {
            var key = ValidationRecordKey(arguments);
            var value = ValueAfter(arguments, "--value");
            var domain = ExpectedByValidationRecord(key);
            Assert.Equal(
                certificates[domain.CertificateName].Properties.ValidationToken,
                value);

            validationRecords[key].Add(value);
            Events.Add($"dns-add:{key.Zone}/{key.Name}");
            return Success();
        }

        private CommandOutput RemoveValidationToken(
            IReadOnlyList<string> arguments)
        {
            var key = ValidationRecordKey(arguments);
            var value = ValueAfter(arguments, "--value");
            Assert.True(validationRecords[key].Remove(value));

            Events.Add($"dns-remove:{key.Zone}/{key.Name}");
            return Success();
        }

        private CommandOutput BindHostname(
            IReadOnlyList<string> arguments)
        {
            var hostname = ValueAfter(arguments, "--hostname");
            var certificateId = ValueAfter(arguments, "--certificate");
            var domain = ExpectedByHostname(hostname);
            var certificate = certificates[domain.CertificateName];
            Assert.Equal(CertificateId(domain.CertificateName), certificateId);
            Assert.Equal(
                "Succeeded",
                certificate.Properties.ProvisioningState);
            Assert.Contains(
                validationRecords[
                    (domain.ZoneName, domain.ValidationRecordName)],
                value => !string.IsNullOrWhiteSpace(value));

            bindings[hostname] =
                new BindingState(hostname, "SniEnabled", certificateId);
            Events.Add($"bind:{hostname}");
            return Success();
        }

        private CommandOutput RunCurl(IReadOnlyList<string> arguments)
        {
            var resolve = ValueAfter(arguments, "--resolve");
            var separator = resolve.IndexOf(':');
            var hostname = resolve[..separator];
            var domain = ExpectedByHostname(hostname);
            Assert.Equal($"{hostname}:443:{StaticIp}", resolve);
            Assert.Equal("30", ValueAfter(arguments, "--connect-timeout"));
            Assert.Equal("30", ValueAfter(arguments, "--max-time"));
            Assert.Equal(
                CertificateId(domain.CertificateName),
                bindings[hostname].CertificateId);

            probedHostnames.Add(hostname);
            Events.Add($"probe:{hostname}");
            return new CommandOutput(
                0,
                "HTTP/1.1 200 OK",
                string.Empty);
        }

        private static (string Zone, string Name) ValidationRecordKey(
            IReadOnlyList<string> arguments)
        {
            var zone = ValueAfter(arguments, "--zone-name");
            var name = arguments.Contains("--record-set-name")
                ? ValueAfter(arguments, "--record-set-name")
                : QueryRecordName(ValueAfter(arguments, "--query"));
            return (zone, name);
        }

        private static string QueryRecordName(string query)
        {
            const string prefix = "[?name=='";
            const string suffix = "']";
            Assert.StartsWith(prefix, query, StringComparison.Ordinal);
            Assert.EndsWith(suffix, query, StringComparison.Ordinal);
            return query[prefix.Length..^suffix.Length];
        }

        private static string ValueFromUrlParameters(
            IReadOnlyList<string> arguments,
            string name)
        {
            var start = arguments
                .Select((argument, index) => (argument, index))
                .Single(item =>
                    string.Equals(
                        item.argument,
                        "--url-parameters",
                        StringComparison.Ordinal))
                .index + 1;
            var prefix = $"{name}=";
            var parameter = arguments
                .Skip(start)
                .TakeWhile(argument =>
                    !argument.StartsWith("--", StringComparison.Ordinal))
                .Single(argument =>
                    argument.StartsWith(prefix, StringComparison.Ordinal));
            return parameter[prefix.Length..];
        }

        private static CustomDomainSetup.CustomDomainDefinition Expected(
            string certificateName) =>
            Domains.Single(domain =>
                string.Equals(
                    domain.CertificateName,
                    certificateName,
                    StringComparison.Ordinal));

        private static CustomDomainSetup.CustomDomainDefinition
            ExpectedByHostname(string hostname) =>
            Domains.Single(domain =>
                string.Equals(
                    domain.Hostname,
                    hostname,
                    StringComparison.Ordinal));

        private static CustomDomainSetup.CustomDomainDefinition
            ExpectedByValidationRecord(
            (string Zone, string Name) key) =>
            Domains.Single(domain =>
                string.Equals(
                    domain.ZoneName,
                    key.Zone,
                    StringComparison.Ordinal) &&
                string.Equals(
                    domain.ValidationRecordName,
                    key.Name,
                    StringComparison.Ordinal));

        private static string CertificateId(string certificateName) =>
            $"{EnvironmentId}/managedCertificates/{certificateName}";

        private static bool IsMutation(string eventName) =>
            eventName.StartsWith("hostname-add:", StringComparison.Ordinal) ||
            eventName.StartsWith("certificate-create:", StringComparison.Ordinal) ||
            eventName.StartsWith("certificate-delete:", StringComparison.Ordinal) ||
            eventName.StartsWith("dns-create:", StringComparison.Ordinal) ||
            eventName.StartsWith("dns-add:", StringComparison.Ordinal) ||
            eventName.StartsWith("dns-remove:", StringComparison.Ordinal) ||
            eventName.StartsWith("bind:", StringComparison.Ordinal);

        private static string ValueAfter(
            IReadOnlyList<string> arguments,
            string option)
        {
            var index = arguments
                .Select((argument, index) => (argument, index))
                .Single(item =>
                    string.Equals(
                        item.argument,
                        option,
                        StringComparison.Ordinal))
                .index;
            return arguments[index + 1];
        }

        private static InvalidOperationException Unexpected(
            string command,
            IReadOnlyList<string> arguments) =>
            new(
                $"Unexpected invocation: {command} {string.Join(' ', arguments)}");

        private static CommandOutput Json(object value) =>
            new(0, JsonSerializer.Serialize(value), string.Empty);

        private static CommandOutput Success() =>
            new(0, string.Empty, string.Empty);

        private sealed record BindingState(
            string Name,
            string BindingType,
            string? CertificateId);

        private sealed record CertificateState(
            string Id,
            string Name,
            CertificateProperties Properties,
            CustomDomainSetup.CustomDomainDefinition Domain)
        {
            public static CertificateState Pending(
                CustomDomainSetup.CustomDomainDefinition domain) =>
                Create(
                    domain,
                    "Pending",
                    domain.Hostname,
                    $"opaque-{domain.CertificateName}-token");

            public static CertificateState Succeeded(
                CustomDomainSetup.CustomDomainDefinition domain) =>
                Create(domain, "Succeeded", $"CN={domain.Hostname}", null);

            public static CertificateState Terminal(
                CustomDomainSetup.CustomDomainDefinition domain,
                string state,
                string token) =>
                Create(domain, state, $"CN={domain.Hostname}", token);

            private static CertificateState Create(
                CustomDomainSetup.CustomDomainDefinition domain,
                string state,
                string subjectName,
                string? validationToken) =>
                new(
                    CertificateId(domain.CertificateName),
                    domain.CertificateName,
                    new CertificateProperties(
                        state,
                        subjectName,
                        "TXT",
                        "TXT",
                        validationToken,
                        null),
                    domain);
        }

        private sealed record CertificateProperties(
            string ProvisioningState,
            string SubjectName,
            string DomainControlValidation,
            string ValidationMethod,
            string? ValidationToken,
            string? Error);
    }
}
