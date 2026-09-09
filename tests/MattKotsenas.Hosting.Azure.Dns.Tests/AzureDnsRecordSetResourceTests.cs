using System.Net;
using System.Text.RegularExpressions;

using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;

namespace MattKotsenas.Hosting.Azure.Dns.Tests;

public sealed class AzureDnsRecordSetResourceTests
{
    [Fact]
    public void RecordSetsPreserveParentNameAndTtl()
    {
        var builder = DistributedApplication.CreateBuilder();
        var zone = builder.AddAzureDnsZone("zone", "example.com");

        var apex = zone
            .AddARecordSet(
                "apex",
                "@",
                TimeSpan.FromMinutes(5))
            .WithAddress(IPAddress.Parse("192.0.2.1"));
        var www = zone
            .AddCnameRecordSet(
                "www",
                "www")
            .WithTarget("legacy.example.com");
        var txt = zone
            .AddTxtRecordSet(
                "verification",
                "asuid")
            .WithRecord("first")
            .WithRecord("second");

        Assert.Same(zone.Resource, apex.Resource.Parent);
        Assert.Equal("example.com", apex.Resource.Hostname);
        Assert.Equal(TimeSpan.FromMinutes(5), apex.Resource.TimeToLive);
        Assert.Equal("www.example.com", www.Resource.Hostname);
        Assert.Equal(TimeSpan.FromHours(1), www.Resource.TimeToLive);
        Assert.Equal("asuid.example.com", txt.Resource.Hostname);
        Assert.IsNotAssignableFrom<AzureBicepResource>(apex.Resource);
        Assert.Contains(apex.Resource, builder.Resources);
        Assert.Contains(www.Resource, builder.Resources);
        Assert.Contains(txt.Resource, builder.Resources);

        var template = zone.Resource.GetBicepTemplateString();
        Assert.Contains("TTL: 300", template);
        Assert.Equal(
            2,
            Regex.Count(
                template,
                @"\bvalue:\s*\["));
    }

    [Fact]
    public void EmptyRecordSetsAreProvisioned()
    {
        var builder = DistributedApplication.CreateBuilder();
        var zone = builder.AddAzureDnsZone("zone", "example.com");

        zone.AddARecordSet("empty-a", "empty-a");
        zone.AddCnameRecordSet("empty-cname", "empty-cname");
        zone.AddTxtRecordSet("empty-txt", "empty-txt");

        var template = zone.Resource.GetBicepTemplateString();

        Assert.Contains("resource empty_a 'Microsoft.Network/dnsZones/A@", template);
        Assert.Contains("resource empty_cname 'Microsoft.Network/dnsZones/CNAME@", template);
        Assert.Contains("resource empty_txt 'Microsoft.Network/dnsZones/TXT@", template);
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
            () => zone.AddARecordSet(
                "invalid",
                "@",
                TimeSpan.FromSeconds(seconds)));
        Assert.DoesNotContain(
            builder.Resources,
            resource => resource.Name == "invalid");
    }

    [Fact]
    public void DuplicateRecordSetIsRejected()
    {
        var builder = DistributedApplication.CreateBuilder();
        var zone = builder.AddAzureDnsZone("zone", "example.com");
        zone.AddTxtRecordSet("first", "asuid");

        Assert.Throws<InvalidOperationException>(
            () => zone.AddTxtRecordSet("second", "asuid"));
    }

    [Fact]
    public void CnameRecordSetCannotCoexistWithOtherTypes()
    {
        var firstBuilder = DistributedApplication.CreateBuilder();
        var firstZone = firstBuilder.AddAzureDnsZone(
            "first-zone",
            "first.example.com");
        firstZone.AddARecordSet("a", "www");

        Assert.Throws<InvalidOperationException>(
            () => firstZone.AddCnameRecordSet("cname", "www"));

        var secondBuilder = DistributedApplication.CreateBuilder();
        var secondZone = secondBuilder.AddAzureDnsZone(
            "second-zone",
            "second.example.com");
        secondZone.AddCnameRecordSet("cname", "www");

        Assert.Throws<InvalidOperationException>(
            () => secondZone.AddTxtRecordSet("txt", "www"));
    }

    [Fact]
    public void CnameAtApexIsRejected()
    {
        var builder = DistributedApplication.CreateBuilder();
        var zone = builder.AddAzureDnsZone("zone", "example.com");

        Assert.Throws<ArgumentException>(
            () => zone.AddCnameRecordSet(
                "invalid",
                "@"));
    }

    [Fact]
    public void CnameRecordSetRejectsSecondTarget()
    {
        var builder = DistributedApplication.CreateBuilder();
        var recordSet = builder
            .AddAzureDnsZone("zone", "example.com")
            .AddCnameRecordSet("www", "www")
            .WithTarget("first.example.com");

        Assert.Throws<InvalidOperationException>(
            () => recordSet.WithTarget("second.example.com"));
    }

    [Fact]
    public void RepeatedRenderingProducesTheSameTemplate()
    {
        var builder = DistributedApplication.CreateBuilder();
        var address = builder.AddParameter("address");
        var zone = builder.AddAzureDnsZone("zone", "example.com");
        zone
            .AddARecordSet("apex", "@")
            .WithAddress(address);

        var firstTemplate =
            zone.Resource.GetBicepTemplateString();
        var secondTemplate =
            zone.Resource.GetBicepTemplateString();

        Assert.Equal(firstTemplate, secondTemplate);
    }

    [Fact]
    public void ZoneOwnsRecordSetValueParameters()
    {
        var builder = DistributedApplication.CreateBuilder();
        var parameter = builder.AddParameter("address");
        var source = builder.AddBicepTemplateString(
            "source",
            "output target string = 'target.example.com'");
        var output = source.GetOutput("target");
        var expression =
            ReferenceExpression.Create($"{parameter.Resource}-{output}");
        var zone = builder.AddAzureDnsZone("zone", "example.com");

        zone.AddARecordSet("a", "a").WithAddress(parameter);
        zone.AddCnameRecordSet("cname", "cname").WithTarget(output);
        zone.AddTxtRecordSet("txt", "txt").WithRecord(expression);

        _ = zone.Resource.GetBicepTemplateString();

        Assert.Same(
            parameter.Resource,
            zone.Resource.Parameters["a_address_0"]);
        Assert.Same(
            output,
            zone.Resource.Parameters["cname_target_0"]);
        Assert.Same(
            expression,
            zone.Resource.Parameters["txt_record_0"]);
    }

    [Fact]
    public void SecretRecordSetValueProducesSecureBicepParameter()
    {
        var builder = DistributedApplication.CreateBuilder();
        var secret = builder.AddParameter(
            "verification",
            secret: true);
        var zone = builder.AddAzureDnsZone("zone", "example.com");
        zone
            .AddTxtRecordSet("txt", "txt")
            .WithRecord(secret);

        var template = zone.Resource.GetBicepTemplateString();

        Assert.Contains("@secure()", template);
        Assert.Contains("param txt_record_0 string", template);
    }

    [Fact]
    public async Task TxtLiteralPreservesBraces()
    {
        const string value =
            "v=spf1 exists:%{i}.example.com -all";
        var builder = DistributedApplication.CreateBuilder();
        var zone = builder.AddAzureDnsZone("zone", "example.com");
        zone
            .AddTxtRecordSet("txt", "txt")
            .WithRecord(value);

        _ = zone.Resource.GetBicepTemplateString();
        var expression = Assert.IsType<ReferenceExpression>(
            zone.Resource.Parameters["txt_record_0"]);

        Assert.Equal(
            value,
            await expression.GetValueAsync(
                TestContext.Current.CancellationToken));
    }
}
