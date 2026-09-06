using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using Azure.Provisioning;

namespace MattKotsenas.Hosting.Azure.Dns.Tests;

public sealed class AzureDnsResourceTests
{
    [Fact]
    public void ResourcesCanOnlyBeCreatedThroughBuilders()
    {
        Assert.Empty(
            typeof(AzureDnsZoneResource).GetConstructors());
        Assert.Empty(
            typeof(AzureDnsARecordResource).GetConstructors());
        Assert.Empty(
            typeof(AzureDnsCnameRecordResource).GetConstructors());
        Assert.Empty(
            typeof(AzureDnsTxtRecordResource).GetConstructors());
    }

    [Fact]
    public void ZoneIsCreatedByDefault()
    {
        var builder = DistributedApplication.CreateBuilder();

        var zone = builder.AddAzureDnsZone(
            "zone",
            "Example.COM.");
        var template = zone.Resource.GetBicepTemplateString();

        Assert.Equal("example.com", zone.Resource.ZoneName);
        Assert.DoesNotContain(
            zone.Resource.Annotations,
            annotation =>
                annotation is ExistingAzureResourceAnnotation);
        Assert.Contains(
            "resource zone 'Microsoft.Network/dnsZones@",
            template);
        Assert.DoesNotContain("existing =", template);
        Assert.Contains("name: 'example.com'", template);
        Assert.Contains("location: 'global'", template);
        Assert.Contains("output name string", template);
        Assert.Single(
            builder.Resources,
            resource =>
                resource.GetType().Name ==
                    "AzureEnvironmentResource");
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a zone")]
    [InlineData("192.0.2.1")]
    public void InvalidZoneNamesAreRejected(string zoneName)
    {
        var builder = DistributedApplication.CreateBuilder();

        Assert.Throws<ArgumentException>(
            () => builder.AddAzureDnsZone(
                "zone",
                zoneName));
    }

    [Fact]
    public void DuplicateZoneNamesAreRejected()
    {
        var builder = DistributedApplication.CreateBuilder();
        builder.AddAzureDnsZone(
            "zone",
            "example.com");

        Assert.Throws<InvalidOperationException>(
            () => builder.AddAzureDnsZone(
                "duplicate",
                "EXAMPLE.COM"));
    }

    [Fact]
    public void ZoneCanPublishAsExisting()
    {
        var builder = DistributedApplication.CreateBuilder(
            ["--publisher", "manifest"]);
        var zone = builder.AddAzureDnsZone(
            "zone",
            "example.com");
        var before = zone.AddARecord(
            "apex",
            DnsRelativeName.Apex,
            "192.0.2.1");

        zone.PublishAsExisting("example.com", "dns");
        var after = zone.AddCnameRecord(
            "www",
            DnsRelativeName.From("www"),
            "legacy.example.com");

        var existing = Assert.Single(
            zone.Resource.Annotations
                .OfType<ExistingAzureResourceAnnotation>());
        Assert.Equal("example.com", existing.Name);
        Assert.Equal("dns", existing.ResourceGroup);
        var zoneTemplate =
            zone.Resource.GetBicepTemplateString();
        _ = before.Resource.GetBicepTemplateString();
        _ = after.Resource.GetBicepTemplateString();
        Assert.Equal(
            "dns",
            Assert.IsType<AzureBicepResourceScope>(
                zone.Resource.Scope).ResourceGroup);
        Assert.Equal(
            "dns",
            Assert.IsType<AzureBicepResourceScope>(
                before.Resource.Scope).ResourceGroup);
        Assert.Equal(
            "dns",
            Assert.IsType<AzureBicepResourceScope>(
                after.Resource.Scope).ResourceGroup);
        Assert.Contains(
            "existing =",
            zoneTemplate);
    }

    [Fact]
    public void PublishAsExistingDoesNotChangeRunMode()
    {
        var builder = DistributedApplication.CreateBuilder();
        var zone = builder.AddAzureDnsZone(
            "zone",
            "example.com");

        zone.PublishAsExisting("example.com", "dns");

        Assert.Null(zone.Resource.Scope);
        Assert.DoesNotContain(
            zone.Resource.Annotations,
            annotation =>
                annotation is ExistingAzureResourceAnnotation);
    }

    [Fact]
    public void ExistingZoneNameMustMatchModel()
    {
        var builder = DistributedApplication.CreateBuilder(
            ["--publisher", "manifest"]);
        var zone = builder.AddAzureDnsZone(
            "zone",
            "example.com");

        zone.PublishAsExisting(
            "other.example.com",
            "dns");

        Assert.Throws<InvalidOperationException>(
            () => zone.Resource.GetBicepTemplateString());
    }

    [Fact]
    public void ParameterizedExistingZoneNameIsRejected()
    {
        var builder = DistributedApplication.CreateBuilder(
            ["--publisher", "manifest"]);
        var zoneName = builder.AddParameter("zone-name");
        var resourceGroup = builder.AddParameter(
            "dns-resource-group");
        var zone = builder.AddAzureDnsZone(
            "zone",
            "example.com");

        zone.PublishAsExisting(zoneName, resourceGroup);

        Assert.Throws<InvalidOperationException>(
            () => zone.Resource.GetBicepTemplateString());
    }

    [Fact]
    public void ExcludedExistingZoneUsesLiteralName()
    {
        var builder = DistributedApplication.CreateBuilder(
            ["--publisher", "manifest"]);
        var zone = builder.AddAzureDnsZone(
            "zone",
            "example.com");
        var record = zone.AddARecord(
            "apex",
            DnsRelativeName.Apex,
            "192.0.2.1");
        zone.PublishAsExisting("example.com", "dns");
        zone.ExcludeFromManifest();

        var template =
            record.Resource.GetBicepTemplateString();

        Assert.Contains("name: 'example.com'", template);
        Assert.DoesNotContain(
            "zone_outputs_name",
            template);
    }

    [Fact]
    public void RecordsPreserveDnsOwnership()
    {
        var builder = DistributedApplication.CreateBuilder();
        var zone = builder.AddAzureDnsZone(
            "zone",
            "example.com");
        var apex = zone.AddARecord(
            "apex",
            DnsRelativeName.Apex,
            "192.0.2.1");
        var www = zone.AddCnameRecord(
            "www",
            DnsRelativeName.From("www"),
            "legacy.example.com");
        var verification = zone.AddTxtRecord(
            "verification",
            DnsRelativeName.From("asuid"),
            "value");

        Assert.Same(zone.Resource, apex.Resource.Parent);
        Assert.Same(zone.Resource, www.Resource.Parent);
        Assert.Same(zone.Resource, verification.Resource.Parent);
        Assert.Equal("example.com", apex.Resource.Hostname);
        Assert.Equal("www.example.com", www.Resource.Hostname);
        Assert.Equal(
            "asuid.example.com",
            verification.Resource.Hostname);
        Assert.Equal(DnsRecordType.A, apex.Resource.RecordType);
        Assert.Equal(
            DnsRecordType.Cname,
            www.Resource.RecordType);
        Assert.Equal(
            DnsRecordType.Txt,
            verification.Resource.RecordType);
    }

    [Fact]
    public void RecordsRejectConflictingWriters()
    {
        var builder = DistributedApplication.CreateBuilder();
        var zone = builder.AddAzureDnsZone(
            "zone",
            "example.com");
        zone.AddARecord(
            "www-address",
            DnsRelativeName.From("www"),
            "192.0.2.1");
        zone.AddTxtRecord(
            "www-text",
            DnsRelativeName.From("www"),
            "value");

        Assert.Throws<InvalidOperationException>(
            () => zone.AddARecord(
                "duplicate-address",
                DnsRelativeName.From("www"),
                "192.0.2.2"));
        Assert.Throws<InvalidOperationException>(
            () => zone.AddCnameRecord(
                "conflicting-cname",
                DnsRelativeName.From("www"),
                "target.example.com"));
    }

    [Fact]
    public void ExistingCnameBlocksOtherRecordTypes()
    {
        var builder = DistributedApplication.CreateBuilder();
        var zone = builder.AddAzureDnsZone(
            "zone",
            "example.com");
        zone.AddCnameRecord(
            "www-cname",
            DnsRelativeName.From("www"),
            "target.example.com");

        Assert.Throws<InvalidOperationException>(
            () => zone.AddARecord(
                "www-address",
                DnsRelativeName.From("www"),
                "192.0.2.1"));
        Assert.Throws<InvalidOperationException>(
            () => zone.AddTxtRecord(
                "www-text",
                DnsRelativeName.From("www"),
                "value"));
    }

    [Fact]
    public void CustomRecordTypeParticipatesInConflicts()
    {
        var builder = DistributedApplication.CreateBuilder();
        var zone = builder.AddAzureDnsZone(
            "zone",
            "example.com");
        var relativeName = DnsRelativeName.From("mail");
        builder.AddResource(new TestRecordResource(
            "mail-mx",
            zone.Resource,
            relativeName,
            DnsRecordType.From("MX")));

        Assert.Throws<InvalidOperationException>(
            () => zone.ThrowIfRecordConflicts(
                relativeName,
                DnsRecordType.From("MX")));
        Assert.Throws<InvalidOperationException>(
            () => zone.ThrowIfRecordConflicts(
                relativeName,
                DnsRecordType.Cname));
    }

    [Fact]
    public void CustomAzureRecordCanApplyParentScope()
    {
        var builder = DistributedApplication.CreateBuilder(
            ["--publisher", "manifest"]);
        var zone = builder.AddAzureDnsZone(
            "zone",
            "example.com");
        zone.PublishAsExisting("example.com", "dns");
        var record = builder.AddResource(
            new TestAzureRecordResource(
                "mail-mx",
                zone.Resource,
                DnsRelativeName.From("mail"),
                DnsRecordType.From("MX")));

        _ = record.Resource.GetBicepTemplateString();

        Assert.Equal(
            "dns",
            Assert.IsType<AzureBicepResourceScope>(
                record.Resource.Scope).ResourceGroup);
    }

    [Fact]
    public void CnameAtApexIsRejected()
    {
        var builder = DistributedApplication.CreateBuilder();
        var zone = builder.AddAzureDnsZone(
            "zone",
            "example.com");

        Assert.Throws<ArgumentException>(
            () => zone.AddCnameRecord(
                "apex-cname",
                DnsRelativeName.Apex,
                "target.example.com"));
    }

    [Theory]
    [InlineData("not-an-address")]
    [InlineData("2001:db8::1")]
    public void InvalidAddressLiteralIsRejected(
        string address)
    {
        var builder = DistributedApplication.CreateBuilder();
        var zone = builder.AddAzureDnsZone(
            "zone",
            "example.com");

        Assert.Throws<ArgumentException>(
            () => zone.AddARecord(
                "invalid",
                DnsRelativeName.Apex,
                address));
        Assert.DoesNotContain(
            builder.Resources,
            resource => resource.Name == "invalid");
    }

    [Fact]
    public void OversizedHostnameIsRejected()
    {
        var builder = DistributedApplication.CreateBuilder();
        var zone = builder.AddAzureDnsZone(
            "zone",
            "example.com");
        var relativeName = DnsRelativeName.From(
            string.Join(
                '.',
                Enumerable.Repeat(
                    new string('a', 60),
                    4)));

        Assert.Throws<ArgumentException>(
            () => zone.AddARecord(
                "oversized",
                relativeName,
                "192.0.2.1"));
    }

    [Fact]
    public void RecordsGenerateExpectedBicep()
    {
        var builder = DistributedApplication.CreateBuilder();
        var zone = builder.AddAzureDnsZone(
            "zone",
            "example.com");
        var a = zone.AddARecord(
            "apex",
            DnsRelativeName.Apex,
            "192.0.2.1");
        var cname = zone.AddCnameRecord(
            "www",
            DnsRelativeName.From("www"),
            "legacy.example.com");
        var txt = zone
            .AddTxtRecord(
                "verification",
                DnsRelativeName.From("asuid"),
                "first")
            .WithValue("second");

        var aTemplate = a.Resource.GetBicepTemplateString();
        var cnameTemplate =
            cname.Resource.GetBicepTemplateString();
        var txtTemplate = txt.Resource.GetBicepTemplateString();

        Assert.Contains("existing =", aTemplate);
        Assert.Contains(
            "resource apex 'Microsoft.Network/dnsZones/A@",
            aTemplate);
        Assert.Contains("TTL: 3600", aTemplate);
        Assert.Contains(
            "resource www 'Microsoft.Network/dnsZones/CNAME@",
            cnameTemplate);
        Assert.Contains("cname: target", cnameTemplate);
        Assert.Contains(
            "resource verification 'Microsoft.Network/dnsZones/TXT@",
            txtTemplate);
        Assert.Contains(
            """
            TXTRecords: [
                  {
                    value: [
                      value0
                    ]
                  }
                  {
                    value: [
                      value1
                    ]
                  }
                ]
            """,
            txtTemplate);
    }

    private sealed class TestRecordResource(
        string name,
        AzureDnsZoneResource parent,
        DnsRelativeName relativeName,
        DnsRecordType recordType)
        : Resource(name),
          IAzureDnsRecordResource
    {
        public AzureDnsZoneResource Parent { get; } = parent;

        public DnsRelativeName RelativeName { get; } =
            relativeName;

        public string Hostname =>
            RelativeName.ToHostname(Parent.ZoneName);

        public DnsRecordType RecordType { get; } = recordType;
    }

    private sealed class TestAzureRecordResource
        : AzureProvisioningResource,
          IAzureDnsRecordResource
    {
        public TestAzureRecordResource(
            string name,
            AzureDnsZoneResource parent,
            DnsRelativeName relativeName,
            DnsRecordType recordType)
            : base(
                name,
                infrastructure =>
                {
                    ((IAzureDnsRecordResource)
                        infrastructure.AspireResource)
                        .ApplyAzureDnsZoneScope(infrastructure);
                    infrastructure.Add(new ProvisioningOutput(
                        "id",
                        typeof(string))
                    {
                        Value = "test",
                    });
                })
        {
            Parent = parent;
            RelativeName = relativeName;
            RecordType = recordType;
        }

        public AzureDnsZoneResource Parent { get; }

        public DnsRelativeName RelativeName { get; }

        public string Hostname =>
            RelativeName.ToHostname(Parent.ZoneName);

        public DnsRecordType RecordType { get; }
    }
}
