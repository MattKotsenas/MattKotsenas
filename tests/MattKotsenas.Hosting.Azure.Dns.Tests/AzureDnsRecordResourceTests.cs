using Aspire.Hosting;

namespace MattKotsenas.Hosting.Azure.Dns.Tests;

public sealed class AzureDnsRecordResourceTests
{
    [Fact]
    public void RecordsPreserveParentNameAndTtl()
    {
        var builder = DistributedApplication.CreateBuilder();
        var zone = builder.AddAzureDnsZone("zone", "example.com");

        var apex = zone.AddARecord(
            "apex",
            "@",
            "192.0.2.1",
            TimeSpan.FromMinutes(5));
        var www = zone.AddCnameRecord(
            "www",
            "www",
            "legacy.example.com");
        var txt = zone
            .AddTxtRecord(
                "verification",
                "asuid",
                "first")
            .WithValue("second");

        Assert.Same(zone.Resource, apex.Resource.Parent);
        Assert.Equal("example.com", apex.Resource.Hostname);
        Assert.Equal(TimeSpan.FromMinutes(5), apex.Resource.TimeToLive);
        Assert.Equal("www.example.com", www.Resource.Hostname);
        Assert.Equal(TimeSpan.FromHours(1), www.Resource.TimeToLive);
        Assert.Equal("asuid.example.com", txt.Resource.Hostname);
        Assert.Contains("TTL: 300", apex.Resource.GetBicepTemplateString());
        Assert.Contains("value1", txt.Resource.GetBicepTemplateString());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(0.5)]
    public void InvalidTtlIsRejectedBeforeMutation(double seconds)
    {
        var builder = DistributedApplication.CreateBuilder();
        var zone = builder.AddAzureDnsZone("zone", "example.com");

        Assert.Throws<ArgumentOutOfRangeException>(
            () => zone.AddARecord(
                "invalid",
                "@",
                "192.0.2.1",
                TimeSpan.FromSeconds(seconds)));
        Assert.DoesNotContain(
            builder.Resources,
            resource => resource.Name == "invalid");
    }

    [Fact]
    public void DuplicateRecordIsRejected()
    {
        var builder = DistributedApplication.CreateBuilder();
        var zone = builder.AddAzureDnsZone("zone", "example.com");
        zone.AddTxtRecord("first", "asuid", "one");

        Assert.Throws<InvalidOperationException>(
            () => zone.AddTxtRecord("second", "asuid", "two"));
    }

    [Fact]
    public void CnameAtApexIsRejected()
    {
        var builder = DistributedApplication.CreateBuilder();
        var zone = builder.AddAzureDnsZone("zone", "example.com");

        Assert.Throws<ArgumentException>(
            () => zone.AddCnameRecord(
                "invalid",
                "@",
                "target.example.com"));
    }
}
