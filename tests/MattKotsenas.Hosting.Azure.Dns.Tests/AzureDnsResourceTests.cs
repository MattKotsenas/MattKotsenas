using Aspire.Hosting;
using Aspire.Hosting.Azure;

namespace MattKotsenas.Hosting.Azure.Dns.Tests;

public sealed class AzureDnsZoneResourceTests
{
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
    }

    [Fact]
    public void ZoneCanPublishAsExisting()
    {
        var builder = DistributedApplication.CreateBuilder(
            ["--publisher", "manifest"]);
        var zone = builder.AddAzureDnsZone(
            "zone",
            "example.com");

        zone.PublishAsExisting("example.com", "dns");
        var template = zone.Resource.GetBicepTemplateString();

        var existing = Assert.Single(
            zone.Resource.Annotations
                .OfType<ExistingAzureResourceAnnotation>());
        Assert.Equal("example.com", existing.Name);
        Assert.Equal("dns", existing.ResourceGroup);
        Assert.Contains("existing =", template);
        Assert.Equal(
            "dns",
            Assert.IsType<AzureBicepResourceScope>(
                zone.Resource.Scope).ResourceGroup);
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
    public void AddingZoneRegistersAzureProvisioning()
    {
        var builder = DistributedApplication.CreateBuilder();

        builder.AddAzureDnsZone(
            "zone",
            "example.com");

        Assert.Single(
            builder.Resources,
            resource =>
                resource.GetType().Name ==
                    "AzureEnvironmentResource");
    }
}
