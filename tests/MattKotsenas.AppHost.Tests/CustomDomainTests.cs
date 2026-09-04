using System.Net;

using Aspire.Hosting;
using Aspire.Hosting.Azure;
using Azure.Core;
using Azure.ResourceManager.Dns;
using Azure.ResourceManager.Dns.Models;
using MattKotsenas.AppHost;
using Microsoft.Extensions.Time.Testing;

namespace MattKotsenas.AppHost.Tests;

public sealed class CustomDomainTests
{
    private const string SubscriptionId =
        "11111111-1111-1111-1111-111111111111";
    private const string ResourceGroup = "app";
    private static readonly ResourceIdentifier EnvironmentId = new(
        $"/subscriptions/{SubscriptionId}/resourceGroups/{ResourceGroup}/providers/Microsoft.App/managedEnvironments/environment");
    private static readonly ResourceIdentifier AppId = new(
        $"/subscriptions/{SubscriptionId}/resourceGroups/{ResourceGroup}/providers/Microsoft.App/containerApps/app");

    [Fact]
    public async Task AppHostModelsProductionCustomDomains()
    {
        var cancellationToken =
            TestContext.Current.CancellationToken;
        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.MattKotsenas_AppHost>(
                [
                    "--publisher",
                    "manifest",
                ],
                cancellationToken);

        var zones = appHost.Resources
            .OfType<AzureDnsZoneResource>()
            .ToArray();
        var routes = appHost.Resources
            .OfType<IAzureDnsRoutingRecordResource>()
            .ToArray();
        var domains = appHost.Resources
            .OfType<CustomDomainResource>()
            .ToArray();

        Assert.Equal(2, zones.Length);
        Assert.All(
            zones,
            zone => Assert.Single(
                zone.Annotations.OfType<
                    ExistingAzureResourceAnnotation>()));
        Assert.Equal(4, routes.Length);
        var ownershipRecords = appHost.Resources
            .OfType<AzureDnsTxtRecordResource>()
            .ToArray();
        Assert.Equal(4, ownershipRecords.Length);
        Assert.Equal(
            2,
            ownershipRecords.Single(record =>
                record.Hostname == "asuid.kotsenas.com")
                .Annotations
                .OfType<AzureDnsTxtValueAnnotation>()
                .Count());
        Assert.All(
            ownershipRecords.Where(record =>
                record.Hostname != "asuid.kotsenas.com"),
            record => Assert.Single(
                record.Annotations
                    .OfType<AzureDnsTxtValueAnnotation>()));
        Assert.Equal(
            4,
            appHost.Resources
                .OfType<AzureDnsDynamicTxtRecordResource>()
                .Count());
        Assert.Equal(
            4,
            appHost.Resources
                .OfType<CustomDomainCertificateResource>()
                .Count());
        Assert.Equal(
            [
                "kotsenas.com",
                "matt.kotsenas.com",
                "www.kotsenas.com",
                "www.matt.kotsenas.com",
            ],
            domains
                .Select(domain => domain.Hostname)
                .Order(StringComparer.Ordinal));
        Assert.All(
            domains,
            domain => Assert.Contains(
                domain.RoutingRecord,
                routes));
    }

    [Fact]
    public void ResourcesPreserveZoneAndRecordOwnership()
    {
        var graph = CreateGraph();

        Assert.Equal(4, graph.Domains.Count);
        Assert.Equal(
            graph.Domains.Count,
            graph.Domains.Select(domain => domain.Name)
                .Distinct(StringComparer.Ordinal)
                .Count());
        Assert.All(
            graph.Domains,
            domain =>
            {
                Assert.Same(graph.Parent, domain.Parent);
                Assert.Same(
                    domain.RoutingRecord.Parent,
                    domain.OwnershipRecord.Parent);
                Assert.Equal(
                    domain.RoutingRecord.Hostname,
                    domain.Hostname);
            });
        Assert.All(
            graph.Certificates,
            certificate =>
            {
                Assert.Contains(certificate.Parent, graph.Domains);
                Assert.Same(
                    certificate.Parent.RoutingRecord.Parent,
                    certificate.GetManagedCertificate()
                        .ValidationRecord.Parent);
            });
    }

    [Fact]
    public void RelativeNamesRepresentApexAndSubdomains()
    {
        Assert.Equal(
            "example.com",
            DnsRelativeName.Apex.ToHostname("example.com"));
        Assert.Equal(
            "www.example.com",
            DnsRelativeName.From("www")
                .ToHostname("example.com"));
        Assert.Equal(
            "_dnsauth.www",
            DnsRelativeName.From("_dnsauth.www").Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData(".www")]
    [InlineData("www.")]
    [InlineData("not valid")]
    public void RelativeNamesRejectInvalidValues(string value)
    {
        Assert.Throws<ArgumentException>(
            () => DnsRelativeName.From(value));
    }

    [Fact]
    public void ZoneRejectsDuplicatePhysicalRecordWriters()
    {
        var builder = DistributedApplication.CreateBuilder();
        builder.Configuration["Azure:SubscriptionId"] =
            SubscriptionId;
        var zone = builder.AddAzureDnsZone(
            "zone",
            "example.com",
            "dns");

        zone.AddDynamicTxtRecord(
            "validation",
            DnsRelativeName.From("_dnsauth"));

        Assert.Throws<InvalidOperationException>(
            () =>
            {
                zone.AddTxtRecord(
                    "duplicate",
                    DnsRelativeName.From("_dnsauth"));
            });

        Assert.Throws<InvalidOperationException>(
            () =>
            {
                builder.AddAzureDnsZone(
                    "zone-alias",
                    "example.com",
                    "dns");
            });
    }

    [Fact]
    public async Task ManagedCertificatesPublishDistinctTokensConcurrently()
    {
        var graph = CreateGraph();
        var events = new List<string>();
        var containerApps = new FakeContainerAppControlPlane(
            graph,
            events);
        var dns = new FakeDnsValidationRecords(
            events,
            expectedConcurrentEnsures: graph.Certificates.Count);
        var deployment = CreateDeployment(
            containerApps,
            dns,
            new FakeHttpsEndpointProbe());
        var cancellationToken =
            TestContext.Current.CancellationToken;

        await deployment.ValidateAsync(
            graph.Domains,
            graph.Certificates,
            cancellationToken);
        await Task.WhenAll(graph.Certificates.Select(certificate =>
            deployment.RecoverAsync(
                certificate,
                cancellationToken)));
        await Task.WhenAll(graph.Certificates.Select(certificate =>
            deployment.PublishValidationAndWaitForCertificateAsync(
                certificate,
                cancellationToken)));

        Assert.Equal(
            graph.Certificates.Count,
            dns.MaximumConcurrentEnsures);
        Assert.Equal(
            graph.Certificates
                .Select(certificate =>
                    $"{certificate.GetManagedCertificate().ValidationRecord.Parent.ZoneName}|{certificate.GetManagedCertificate().ValidationRecord.RelativeName}|token-{certificate.GetManagedCertificateName()}")
                .Order(StringComparer.Ordinal),
            dns.Writes.Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("Failed")]
    [InlineData("Canceled")]
    [InlineData("DeleteFailed")]
    public async Task RecoveryRemovesTokenBeforeTerminalCertificate(
        string terminalState)
    {
        var graph = CreateGraph();
        var events = new List<string>();
        var containerApps = new FakeContainerAppControlPlane(
            graph,
            events,
            terminalState: Enum.Parse<ManagedCertificateState>(
                terminalState));
        var dns = new FakeDnsValidationRecords(events);
        var deployment = CreateDeployment(
            containerApps,
            dns,
            new FakeHttpsEndpointProbe());
        var cancellationToken =
            TestContext.Current.CancellationToken;

        await deployment.ValidateAsync(
            graph.Domains,
            graph.Certificates,
            cancellationToken);
        await deployment.RecoverAsync(
            graph.Certificates[0],
            cancellationToken);

        Assert.Equal(
            ["remove-token", "delete-certificate"],
            events);
    }

    [Fact]
    public async Task ValidationRejectsUnmodeledDomainBeforeMutation()
    {
        var graph = CreateGraph();
        var events = new List<string>();
        var containerApps = new FakeContainerAppControlPlane(
            graph,
            events,
            unexpectedDomain: "other.example.com");
        var deployment = CreateDeployment(
            containerApps,
            new FakeDnsValidationRecords(events),
            new FakeHttpsEndpointProbe());

        var exception = await Assert.ThrowsAsync<
            InvalidOperationException>(
            () => deployment.ValidateAsync(
                graph.Domains,
                graph.Certificates,
                TestContext.Current.CancellationToken));

        Assert.Contains("other.example.com", exception.Message);
        Assert.Empty(events);
    }

    [Fact]
    public async Task ValidationRejectsUnmodeledBindingBeforeMutation()
    {
        var graph = CreateGraph();
        var events = new List<string>();
        var containerApps = new FakeContainerAppControlPlane(
            graph,
            events,
            malformedBinding: true);
        var deployment = CreateDeployment(
            containerApps,
            new FakeDnsValidationRecords(events),
            new FakeHttpsEndpointProbe());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => deployment.ValidateAsync(
                graph.Domains,
                graph.Certificates,
                TestContext.Current.CancellationToken));

        Assert.Empty(events);
    }

    [Fact]
    public async Task ValidationRejectsBoundTerminalCertificateBeforeMutation()
    {
        var graph = CreateGraph();
        var events = new List<string>();
        var containerApps = new FakeContainerAppControlPlane(
            graph,
            events,
            terminalState: ManagedCertificateState.Failed,
            boundTerminalCertificate: true);
        var deployment = CreateDeployment(
            containerApps,
            new FakeDnsValidationRecords(events),
            new FakeHttpsEndpointProbe());

        var exception = await Assert.ThrowsAsync<
            InvalidOperationException>(
            () => deployment.ValidateAsync(
                graph.Domains,
                graph.Certificates,
                TestContext.Current.CancellationToken));

        Assert.Contains(
            graph.Certificates[1].GetManagedCertificateName(),
            exception.Message);
        Assert.Empty(events);
    }

    [Fact]
    public async Task ValidationRejectsDifferentSelectedEnvironment()
    {
        var graph = CreateGraph();
        var events = new List<string>();
        var deployment = CreateDeployment(
            new FakeContainerAppControlPlane(graph, events),
            new FakeDnsValidationRecords(events),
            new FakeHttpsEndpointProbe(),
            selectedEnvironmentId: new ResourceIdentifier(
                $"/subscriptions/{SubscriptionId}/resourceGroups/{ResourceGroup}/providers/Microsoft.App/managedEnvironments/other"));

        var exception = await Assert.ThrowsAsync<
            InvalidOperationException>(
            () => deployment.ValidateAsync(
                graph.Domains,
                graph.Certificates,
                TestContext.Current.CancellationToken));

        Assert.Contains("model selects", exception.Message);
        Assert.Empty(events);
    }

    [Fact]
    public async Task VerificationRetriesTransientProbeFailures()
    {
        var graph = CreateGraph();
        var probe = new FakeHttpsEndpointProbe(
            failFirstAttempt: true);
        var deployment = CreateDeployment(
            new FakeContainerAppControlPlane(
                graph,
                []),
            new FakeDnsValidationRecords([]),
            probe);

        await RunDeploymentAsync(
            deployment,
            graph,
            TestContext.Current.CancellationToken);

        Assert.Equal(graph.Domains.Count * 2, probe.Attempts);
    }

    [Fact]
    public async Task DeploymentRetriesUntilContainerAppExists()
    {
        var graph = CreateGraph();
        var containerApps = new FakeContainerAppControlPlane(
            graph,
            [],
            missingAppReads: 2);
        var deployment = CreateDeployment(
            containerApps,
            new FakeDnsValidationRecords([]),
            new FakeHttpsEndpointProbe());

        await RunDeploymentAsync(
            deployment,
            graph,
            TestContext.Current.CancellationToken);

        Assert.True(containerApps.AppReads >= 3);
    }

    [Fact]
    public async Task CertificateTimeoutUsesInjectedTimeProvider()
    {
        var graph = CreateGraph();
        var timeProvider = new FakeTimeProvider();
        var deployment = CreateDeployment(
            new FakeContainerAppControlPlane(
                graph,
                [],
                certificatesNeverAppear: true),
            new FakeDnsValidationRecords([]),
            new FakeHttpsEndpointProbe(),
            timeProvider,
            pollInterval: TimeSpan.FromSeconds(10),
            provisioningTimeout: TimeSpan.FromMinutes(1));

        var task = deployment
            .PublishValidationAndWaitForCertificateAsync(
                graph.Certificates[0],
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

    [Fact]
    public async Task UnselectedManagedCertificateCanValidate()
    {
        var graph = CreateGraph(selectManagedCertificates: false);
        var current = AddCurrentCertificate(
            graph,
            graph.Domains[0]);
        graph.Builder
            .CreateResourceBuilder(graph.Domains[0])
            .BindCertificate(
                graph.Builder.CreateResourceBuilder(current));
        var containerApps = new FakeContainerAppControlPlane(
            graph,
            [],
            selectedCertificate: current);
        var dns = new FakeDnsValidationRecords(
            [],
            expectedConcurrentEnsures: 1);
        var deployment = CreateDeployment(
            containerApps,
            dns,
            new FakeHttpsEndpointProbe());

        await deployment.ValidateAsync(
            graph.Domains,
            graph.Certificates,
            TestContext.Current.CancellationToken);
        await deployment
            .PublishValidationAndWaitForCertificateAsync(
                graph.Certificates[0],
                TestContext.Current.CancellationToken);

        Assert.Single(dns.Writes);
        Assert.Equal(
            CustomDomainBindingKind.SniEnabled,
            containerApps.LastBinding!.BindingKind);
    }

    [Fact]
    public void DomainAllowsCandidatesButRejectsSecondSelection()
    {
        var graph = CreateGraph(selectManagedCertificates: false);
        var domain = graph.Builder.CreateResourceBuilder(
            graph.Domains[0]);
        var current = graph.Builder.CreateResourceBuilder(
            AddCurrentCertificate(
                graph,
                graph.Domains[0]));
        var alternate = graph.Builder.CreateResourceBuilder(
            AddCurrentCertificate(
                graph,
                graph.Domains[0],
                name: "alternate"));

        domain.BindCertificate(current);

        Assert.Same(
            current.Resource,
            domain.Resource.Annotations
                .OfType<
                    CustomDomainCertificateSelectionAnnotation>()
                .Single()
                .Certificate);
        Assert.Throws<InvalidOperationException>(
            () =>
            {
                domain.BindCertificate(alternate);
            });
        Assert.Equal(
            3,
            graph.Builder.Resources
                .OfType<CustomDomainCertificateResource>()
                .Count(certificate =>
                    ReferenceEquals(
                        certificate.Parent,
                        graph.Domains[0])));
    }

    [Fact]
    public void CustomDomainUsesExplicitlySelectedEnvironment()
    {
        var builder = DistributedApplication.CreateBuilder();
        builder.Configuration["Azure:SubscriptionId"] =
            SubscriptionId;
        var unusedEnvironment = builder
            .AddAzureContainerAppEnvironment("unused");
        var selectedEnvironment = builder
            .AddAzureContainerAppEnvironment("selected");
        var app = builder
            .AddContainer("app", "image")
            .WithComputeEnvironment(selectedEnvironment);
        var zone = builder.AddAzureDnsZone(
            "zone",
            "example.com",
            "dns");
        var route = zone.AddARecord(
            "route",
            DnsRelativeName.Apex,
            "192.0.2.1");

        var domain = app.AddCustomDomain(route);
        domain.AddManagedCertificate("managed");

        Assert.False(
            unusedEnvironment.Resource.HasAnnotationOfType<
                CustomDomainEnvironmentAnnotation>());
        Assert.True(
            selectedEnvironment.Resource.HasAnnotationOfType<
                CustomDomainEnvironmentAnnotation>());
        Assert.Same(
            selectedEnvironment.Resource,
            app.Resource.GetComputeEnvironment());
    }

    [Fact]
    public void DomainRejectsSecondManagedCandidate()
    {
        var builder = DistributedApplication.CreateBuilder();
        builder.Configuration["Azure:SubscriptionId"] =
            SubscriptionId;
        var environment = builder
            .AddAzureContainerAppEnvironment("environment");
        var app = builder
            .AddContainer("app", "image")
            .WithComputeEnvironment(environment);
        var zone = builder.AddAzureDnsZone(
            "zone",
            "example.com",
            "dns");
        var route = zone.AddARecord(
            "route",
            DnsRelativeName.Apex,
            "192.0.2.1");
        var domain = app.AddCustomDomain(route);

        domain.AddManagedCertificate("first");

        Assert.Throws<InvalidOperationException>(
            () =>
            {
                domain.AddManagedCertificate("second");
            });
    }

    [Fact]
    public void TxtReplacementPreservesChunksAndRemovesExactRecord()
    {
        var source = new DnsTxtRecordData
        {
            TtlInSeconds = 600,
        };
        var retained = new DnsTxtRecordInfo();
        retained.Values.Add("part-one");
        retained.Values.Add("part-two");
        source.DnsTxtRecords.Add(retained);
        var removed = new DnsTxtRecordInfo();
        removed.Values.Add("old-token");
        source.DnsTxtRecords.Add(removed);

        var replacement = AzureDnsValidationRecords.Copy(
            source,
            TimeSpan.Zero,
            excludedValue: "old-token");

        Assert.Equal(600, replacement.TtlInSeconds);
        var record = Assert.Single(replacement.DnsTxtRecords);
        Assert.Equal(["part-one", "part-two"], record.Values);
    }

    [Fact]
    public async Task TxtReconciliationRetriesEtagConflict()
    {
        var data = new DnsTxtRecordData
        {
            TtlInSeconds = 300,
        };
        var retained = new DnsTxtRecordInfo();
        retained.Values.Add("retained");
        data.DnsTxtRecords.Add(retained);
        DnsTxtRecordData? current = data;
        var updateAttempts = 0;
        var updateConflicts = 1;
        var records = new AzureDnsValidationRecords(
            get: (_, _) =>
                Task.FromResult<DnsTxtRecordData?>(current),
            create: (_, replacement, _) =>
            {
                current = replacement;
                return Task.CompletedTask;
            },
            update: (_, replacement, _, _) =>
            {
                updateAttempts++;
                if (updateConflicts-- > 0)
                {
                    throw new Azure.RequestFailedException(
                        412,
                        "ETag conflict");
                }

                current = replacement;
                return Task.CompletedTask;
            },
            delete: (_, _, _) =>
            {
                current = null;
                return Task.CompletedTask;
            });
        var key = new DnsTxtRecordKey(
            SubscriptionId,
            "dns",
            "example.com",
            "_dnsauth");

        await records.EnsureValueAsync(
            key,
            "new-token",
            TimeSpan.FromMinutes(5),
            TestContext.Current.CancellationToken);

        Assert.Equal(2, updateAttempts);
        Assert.Equal(
            ["new-token", "retained"],
            current!.DnsTxtRecords
                .SelectMany(record => record.Values)
                .Order(StringComparer.Ordinal));
    }

    private static async Task RunDeploymentAsync(
        CustomDomainDeployment deployment,
        TestGraph graph,
        CancellationToken cancellationToken)
    {
        await deployment.ValidateAsync(
            graph.Domains,
            graph.Certificates,
            cancellationToken);
        await Task.WhenAll(graph.Certificates.Select(certificate =>
            deployment.RecoverAsync(
                certificate,
                cancellationToken)));
        await Task.WhenAll(graph.Certificates.Select(certificate =>
            deployment.PublishValidationAndWaitForCertificateAsync(
                certificate,
                cancellationToken)));
        await Task.WhenAll(graph.Domains.Zip(
            graph.Certificates,
            (domain, certificate) =>
                deployment.VerifyCurrentDeploymentAsync(
                    domain,
                    certificate,
                    cancellationToken)));
    }

    private static CustomDomainDeployment CreateDeployment(
        IContainerAppControlPlane containerApps,
        IDnsValidationRecords dns,
        IHttpsEndpointProbe probe,
        TimeProvider? timeProvider = null,
        ResourceIdentifier? selectedEnvironmentId = null,
        TimeSpan? pollInterval = null,
        TimeSpan? provisioningTimeout = null) =>
        new(
            containerApps,
            dns,
            probe,
            timeProvider ?? new FakeTimeProvider(),
            AppId,
            selectedEnvironmentId ?? EnvironmentId,
            pollInterval ?? TimeSpan.Zero,
            provisioningTimeout ?? TimeSpan.FromMinutes(1));

    private static TestGraph CreateGraph(
        bool selectManagedCertificates = true)
    {
        var builder = DistributedApplication.CreateBuilder();
        builder.Configuration["Azure:SubscriptionId"] =
            SubscriptionId;
        var environment = builder
            .AddAzureContainerAppEnvironment("environment")
            .WithAzureName("environment");
        var parent = builder
            .AddContainer("app", "image")
            .WithComputeEnvironment(environment);
        var rootZone = builder.AddAzureDnsZone(
            "root-zone",
            "example.com",
            "dns");
        var homeZone = builder.AddAzureDnsZone(
            "home-zone",
            "home.example.com",
            "dns");
        var rootRoute = rootZone.AddARecord(
            "root-route",
            DnsRelativeName.Apex,
            "192.0.2.1");
        var rootWwwRoute = rootZone.AddCnameRecord(
            "root-www-route",
            "www",
            "legacy.example.com");
        var homeRoute = homeZone.AddARecord(
            "home-route",
            DnsRelativeName.Apex,
            "192.0.2.1");
        var homeWwwRoute = homeZone.AddCnameRecord(
            "home-www-route",
            "www",
            "legacy.example.com");
        IResourceBuilder<CustomDomainResource>[] domains =
        [
            parent.AddCustomDomain(rootRoute),
            parent.AddCustomDomain(rootWwwRoute),
            parent.AddCustomDomain(homeRoute),
            parent.AddCustomDomain(homeWwwRoute),
        ];
        var certificates = domains
            .Select(domain =>
                domain.AddManagedCertificate("managed"))
            .ToArray();
        if (selectManagedCertificates)
        {
            foreach (var (domain, certificate) in domains.Zip(
                certificates))
            {
                domain.BindCertificate(certificate);
            }
        }

        return new(
            builder,
            parent.Resource,
            domains.Select(domain => domain.Resource).ToArray(),
            certificates
                .Select(certificate => certificate.Resource)
                .ToArray());
    }

    private static CustomDomainCertificateResource
        AddCurrentCertificate(
            TestGraph graph,
            CustomDomainResource domain,
            string name = "current")
    {
        var certificateId = new ResourceIdentifier(
            $"{EnvironmentId}/certificates/current");
        return graph.Builder
            .CreateResourceBuilder(domain)
            .AddCertificate(
                name,
            new CustomDomainCertificateProviderAnnotation(
                (_, _) => { },
                (binding, _) =>
                    binding.BindingKind ==
                        CustomDomainBindingKind.SniEnabled &&
                    binding.CertificateId?.Equals(certificateId)
                        is true,
                (binding, _) =>
                    binding.BindingKind ==
                        CustomDomainBindingKind.SniEnabled &&
                    binding.CertificateId?.Equals(certificateId)
                        is true))
            .Resource;
    }

    private sealed record TestGraph(
        IDistributedApplicationBuilder Builder,
        ContainerResource Parent,
        IReadOnlyList<CustomDomainResource> Domains,
        IReadOnlyList<CustomDomainCertificateResource> Certificates);

    private sealed class FakeContainerAppControlPlane(
        TestGraph graph,
        List<string> events,
        int missingAppReads = 0,
        ManagedCertificateState? terminalState = null,
        bool boundTerminalCertificate = false,
        string? unexpectedDomain = null,
        bool malformedBinding = false,
        bool certificatesNeverAppear = false,
        CustomDomainCertificateResource? selectedCertificate = null)
        : IContainerAppControlPlane
    {
        private int remainingMissingAppReads = missingAppReads;
        private bool certificateDeleted;

        public int AppReads { get; private set; }

        public CustomDomainBindingSnapshot? LastBinding { get; private set; }

        public Task<ContainerAppSnapshot?> GetAppAsync(
            ResourceIdentifier appId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AppReads++;
            if (remainingMissingAppReads-- > 0)
            {
                return Task.FromResult<ContainerAppSnapshot?>(null);
            }

            CustomDomainBindingSnapshot[] bindings;
            if (unexpectedDomain is not null)
            {
                bindings =
                [
                    new(
                        unexpectedDomain,
                        CustomDomainBindingKind.Auto,
                        null),
                ];
            }
            else if (malformedBinding)
            {
                bindings =
                [
                    new(
                        graph.Domains[0].Hostname,
                        CustomDomainBindingKind.Unknown,
                        null),
                ];
            }
            else if (boundTerminalCertificate)
            {
                bindings =
                [
                    ManagedBinding(graph.Certificates[1]),
                ];
            }
            else if (selectedCertificate is not null)
            {
                bindings =
                [
                    new(
                        selectedCertificate.Parent.Hostname,
                        CustomDomainBindingKind.SniEnabled,
                        new ResourceIdentifier(
                            $"{EnvironmentId}/certificates/current")),
                ];
            }
            else
            {
                bindings = AppReads == 1 ||
                    terminalState is not null &&
                    !certificateDeleted
                        ? []
                        : graph.Certificates
                            .Select(ManagedBinding)
                            .ToArray();
            }

            LastBinding = bindings.Length == 0
                ? null
                : bindings[0];
            return Task.FromResult<ContainerAppSnapshot?>(
                new(EnvironmentId, bindings));
        }

        public Task<ContainerAppEnvironmentSnapshot>
            GetEnvironmentAsync(
                ResourceIdentifier environmentId,
                CancellationToken cancellationToken) =>
            Task.FromResult(new ContainerAppEnvironmentSnapshot(
                IPAddress.Parse("192.0.2.1")));

        public Task<IReadOnlyList<ManagedCertificateSnapshot>>
            GetManagedCertificatesAsync(
                ResourceIdentifier environmentId,
                CancellationToken cancellationToken)
        {
            if (certificatesNeverAppear)
            {
                return Task.FromResult<
                    IReadOnlyList<ManagedCertificateSnapshot>>([]);
            }

            var certificates = graph.Certificates
                .Select((certificate, index) => new
                    ManagedCertificateSnapshot(
                        ManagedCertificateId(certificate),
                        !certificateDeleted &&
                        terminalState is not null &&
                        (index == 0 ||
                            boundTerminalCertificate &&
                            index == 1)
                            ? terminalState.Value
                            : ManagedCertificateState.Succeeded,
                        !certificateDeleted &&
                        terminalState is not null &&
                        index == 0
                            ? "old-token"
                            : $"token-{certificate.GetManagedCertificateName()}",
                        null))
                .ToArray();
            return Task.FromResult<
                IReadOnlyList<ManagedCertificateSnapshot>>(
                certificates);
        }

        public Task DeleteManagedCertificateAsync(
            ResourceIdentifier certificateId,
            CancellationToken cancellationToken)
        {
            certificateDeleted = true;
            events.Add("delete-certificate");
            return Task.CompletedTask;
        }

        private static CustomDomainBindingSnapshot ManagedBinding(
            CustomDomainCertificateResource certificate) =>
            new(
                certificate.Parent.Hostname,
                CustomDomainBindingKind.Auto,
                ManagedCertificateId(certificate));

        private static ResourceIdentifier ManagedCertificateId(
            CustomDomainCertificateResource certificate) =>
            new(
                $"{EnvironmentId}/managedCertificates/{certificate.GetManagedCertificateName()}");
    }

    private sealed class FakeDnsValidationRecords(
        List<string> events,
        int expectedConcurrentEnsures = 0)
        : IDnsValidationRecords
    {
        private readonly Dictionary<DnsTxtRecordKey, HashSet<string>>
            values = [];
        private readonly Lock sync = new();
        private readonly TaskCompletionSource allEnsuresEntered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int concurrentEnsures;
        private int enteredEnsures;

        public int MaximumConcurrentEnsures { get; private set; }

        public List<string> Writes { get; } = [];

        public Task<bool> HasAnyValueAsync(
            DnsTxtRecordKey key,
            CancellationToken cancellationToken)
        {
            lock (sync)
            {
                return Task.FromResult(
                values.TryGetValue(key, out var current) &&
                current.Count > 0);
            }
        }

        public async Task EnsureValueAsync(
            DnsTxtRecordKey key,
            string value,
            TimeSpan defaultTtl,
            CancellationToken cancellationToken)
        {
            lock (sync)
            {
                Writes.Add(
                    $"{key.Zone}|{key.RelativeName}|{value}");
            }

            var concurrent = Interlocked.Increment(
                ref concurrentEnsures);
            lock (sync)
            {
                MaximumConcurrentEnsures = Math.Max(
                    MaximumConcurrentEnsures,
                    concurrent);
            }
            if (expectedConcurrentEnsures > 0 &&
                Interlocked.Increment(ref enteredEnsures) ==
                    expectedConcurrentEnsures)
            {
                allEnsuresEntered.SetResult();
            }

            if (expectedConcurrentEnsures > 0)
            {
                await allEnsuresEntered.Task.WaitAsync(
                    cancellationToken);
            }

            lock (sync)
            {
                if (!values.TryGetValue(key, out var current))
                {
                    current = [];
                    values.Add(key, current);
                }

                current.Add(value);
            }
            Interlocked.Decrement(ref concurrentEnsures);
        }

        public Task RemoveValueAsync(
            DnsTxtRecordKey key,
            string value,
            bool keepEmptyRecordSet,
            CancellationToken cancellationToken)
        {
            lock (sync)
            {
                events.Add("remove-token");
                if (values.TryGetValue(key, out var current))
                {
                    current.Remove(value);
                }
            }

            return Task.CompletedTask;
        }
    }

    private sealed class FakeHttpsEndpointProbe(
        bool failFirstAttempt = false)
        : IHttpsEndpointProbe
    {
        private readonly HashSet<string> failed = [];

        public int Attempts { get; private set; }

        public Task<bool> IsHealthyAsync(
            string hostname,
            IPAddress address,
            CancellationToken cancellationToken)
        {
            Attempts++;
            return Task.FromResult(
                !failFirstAttempt || !failed.Add(hostname));
        }
    }

}
