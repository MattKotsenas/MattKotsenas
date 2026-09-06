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
    public void ResourcesPreserveDnsAndAzureOwnership()
    {
        var builder = DistributedApplication.CreateBuilder();
        var resourceGroup = builder.AddParameter("dns-resource-group");
        var address = builder.AddParameter("address");
        var target = builder.AddAzureInfrastructure(
            "target",
            infrastructure =>
                infrastructure.Add(new ProvisioningOutput(
                    "hostname",
                    typeof(string))
                {
                    Value = "legacy.example.com",
                }));
        var hostname = target.GetOutput("hostname");
        var zone = builder.AddAzureDnsZone(
            "zone",
            "Example.COM",
            resourceGroup);
        var apex = zone.AddARecord(
            "apex",
            DnsRelativeName.Apex,
            address);
        var www = zone.AddCnameRecord(
            "www",
            DnsRelativeName.From("www"),
            hostname);
        var verification = zone
            .AddTxtRecord(
                "verification",
                DnsRelativeName.From("asuid"),
                "first")
            .WithValue("second");

        var existing = Assert.Single(
            zone.Resource.Annotations
                .OfType<ExistingAzureResourceAnnotation>());
        Assert.Equal("example.com", zone.Resource.ZoneName);
        Assert.Same(resourceGroup.Resource, existing.ResourceGroup);
        Assert.Null(existing.Subscription);
        Assert.Same(zone.Resource, apex.Resource.Parent);
        Assert.Same(zone.Resource, www.Resource.Parent);
        Assert.Same(zone.Resource, verification.Resource.Parent);
        Assert.Same(
            address.Resource,
            apex.Resource.Parameters["target"]);
        Assert.Same(hostname, www.Resource.Parameters["target"]);
        Assert.Equal("example.com", apex.Resource.Hostname);
        Assert.Equal("www.example.com", www.Resource.Hostname);
        Assert.Equal(
            "asuid.example.com",
            verification.Resource.Hostname);
        var scope = Assert.IsType<AzureBicepResourceScope>(
            apex.Resource.Scope);
        Assert.Same(
            resourceGroup.Resource,
            scope.ResourceGroup);
        Assert.Null(scope.Subscription);
    }

    [Fact]
    public void AddingZoneRegistersAzureProvisioning()
    {
        var builder = DistributedApplication.CreateBuilder();
        var resourceGroup = builder.AddParameter("dns-resource-group");

        var zone = builder.AddAzureDnsZone(
            "zone",
            "example.com",
            resourceGroup);

        Assert.Single(
            builder.Resources,
            resource =>
                resource.GetType().Name ==
                    "AzureEnvironmentResource");
        var manifest = Assert.Single(
            zone.Resource.Annotations
                .OfType<ManifestPublishingCallbackAnnotation>());
        Assert.Same(
            ManifestPublishingCallbackAnnotation.Ignore,
            manifest);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a zone")]
    [InlineData("192.0.2.1")]
    public void ZonesRejectInvalidDnsNames(string zoneName)
    {
        var builder = DistributedApplication.CreateBuilder();
        var resourceGroup = builder.AddParameter("dns-resource-group");

        Assert.Throws<ArgumentException>(
            () => builder.AddAzureDnsZone(
                "zone",
                zoneName,
                resourceGroup));
    }

    [Fact]
    public void ZoneNamesAreNormalized()
    {
        var builder = DistributedApplication.CreateBuilder();
        var resourceGroup = builder.AddParameter("dns-resource-group");

        var zone = builder.AddAzureDnsZone(
            "zone",
            "Example.COM.",
            resourceGroup);

        Assert.Equal("example.com", zone.Resource.ZoneName);
    }

    [Fact]
    public void ResourcesRejectDuplicatePhysicalWriters()
    {
        var builder = DistributedApplication.CreateBuilder();
        var resourceGroup = builder.AddParameter("dns-resource-group");
        var zone = builder.AddAzureDnsZone(
            "zone",
            "example.com",
            resourceGroup);
        zone.AddARecord(
            "apex",
            DnsRelativeName.Apex,
            "192.0.2.1");
        zone.AddARecord(
            "www-address",
            DnsRelativeName.From("www"),
            "192.0.2.1");
        zone
            .AddTxtRecord(
                "verification",
                DnsRelativeName.Apex,
                "value");

        Assert.Throws<InvalidOperationException>(
            () => zone.AddARecord(
                "duplicate-apex",
                DnsRelativeName.Apex,
                "192.0.2.2"));
        Assert.Throws<ArgumentException>(
            () => zone.AddCnameRecord(
                "apex-cname",
                DnsRelativeName.Apex,
                "target.example.com"));
        Assert.DoesNotContain(
            builder.Resources,
            resource => resource.Name == "apex-cname");
        var conflict = Assert.Throws<InvalidOperationException>(
            () => zone.AddCnameRecord(
                "conflicting-cname",
                DnsRelativeName.From("www"),
                "target.example.com"));
        Assert.Contains("www-address", conflict.Message);
        Assert.Throws<InvalidOperationException>(
            () => zone.AddTxtRecord(
                "duplicate-txt",
                DnsRelativeName.Apex,
                "other"));
    }

    [Fact]
    public void ZonesRejectDuplicateNames()
    {
        var builder = DistributedApplication.CreateBuilder();
        var resourceGroup = builder.AddParameter("dns-resource-group");
        builder.AddAzureDnsZone(
            "zone",
            "example.com",
            resourceGroup);

        Assert.Throws<InvalidOperationException>(
            () => builder.AddAzureDnsZone(
                "duplicate-zone",
                "EXAMPLE.COM",
                resourceGroup));
    }

    [Fact]
    public void ExistingCnameBlocksOtherRecordKinds()
    {
        var builder = DistributedApplication.CreateBuilder();
        var resourceGroup = builder.AddParameter("dns-resource-group");
        var zone = builder.AddAzureDnsZone(
            "zone",
            "example.com",
            resourceGroup);
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
    public void InvalidTargetDoesNotMutateResourceGraph()
    {
        var builder = DistributedApplication.CreateBuilder();
        var resourceGroup = builder.AddParameter("dns-resource-group");
        var zone = builder.AddAzureDnsZone(
            "zone",
            "example.com",
            resourceGroup);

        Assert.Throws<ArgumentException>(
            () => zone.AddARecord(
                "invalid",
                DnsRelativeName.Apex,
                new object()));
        Assert.DoesNotContain(
            builder.Resources,
            resource => resource.Name == "invalid");
    }

    [Theory]
    [InlineData("not-an-address")]
    [InlineData("2001:db8::1")]
    public void InvalidAddressLiteralDoesNotMutateResourceGraph(
        string address)
    {
        var builder = DistributedApplication.CreateBuilder();
        var resourceGroup = builder.AddParameter("dns-resource-group");
        var zone = builder.AddAzureDnsZone(
            "zone",
            "example.com",
            resourceGroup);

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
    public void RecordsRejectOversizedHostnames()
    {
        var builder = DistributedApplication.CreateBuilder();
        var resourceGroup = builder.AddParameter("dns-resource-group");
        var zone = builder.AddAzureDnsZone(
            "zone",
            "example.com",
            resourceGroup);
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
        Assert.DoesNotContain(
            builder.Resources,
            resource => resource.Name == "oversized");
    }

    [Fact]
    public void RecordsGenerateBicepAgainstExistingZone()
    {
        var builder = DistributedApplication.CreateBuilder();
        var resourceGroup = builder.AddParameter("dns-resource-group");
        var zone = builder.AddAzureDnsZone(
            "zone",
            "example.com",
            resourceGroup);
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

        Assert.Contains(
            "resource zone 'Microsoft.Network/dnsZones@",
            aTemplate);
        Assert.Contains("existing", aTemplate);
        Assert.Contains(
            "resource apex 'Microsoft.Network/dnsZones/A@",
            aTemplate);
        Assert.Contains("name: '@'", aTemplate);
        Assert.Contains("param target string", aTemplate);
        Assert.Contains("output id string", aTemplate);

        Assert.Contains(
            "resource www 'Microsoft.Network/dnsZones/CNAME@",
            cnameTemplate);
        Assert.Contains("name: 'www'", cnameTemplate);
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

    [Theory]
    [InlineData("", false)]
    [InlineData("@", true)]
    [InlineData(".www", false)]
    [InlineData("www.", false)]
    [InlineData("not valid", false)]
    [InlineData("www*", false)]
    [InlineData("www.*", false)]
    [InlineData("*.www", true)]
    [InlineData("_dnsauth.www", true)]
    [InlineData("WWW", true)]
    [InlineData("*", true)]
    public void RelativeNamesEnforceDnsSyntax(
        string value,
        bool valid)
    {
        if (valid)
        {
            var normalized = value.ToLowerInvariant();
            Assert.Equal(
                normalized,
                DnsRelativeName.From(value).Value);
        }
        else
        {
            Assert.Throws<ArgumentException>(
                () => DnsRelativeName.From(value));
        }
    }

    [Fact]
    public void RelativeNamesRejectNull()
    {
        Assert.Throws<ArgumentNullException>(
            () => DnsRelativeName.From(null!));
    }

    [Fact]
    public void RelativeNamesRejectOversizedValues()
    {
        Assert.Throws<ArgumentException>(
            () => DnsRelativeName.From(new string('a', 64)));
        Assert.Throws<ArgumentException>(
            () => DnsRelativeName.From(
                string.Join(
                    '.',
                    Enumerable.Repeat(
                        new string('a', 63),
                        5))));
    }
}
