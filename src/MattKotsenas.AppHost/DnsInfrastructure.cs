using System.Net;

using Azure.Provisioning;
using Azure.Provisioning.Dns;

namespace MattKotsenas.AppHost;

// Azure.Provisioning.Dns is prerelease and marks its entire API as experimental.
#pragma warning disable AZPROVISION001

public static class DnsInfrastructure
{
    private const int TtlInSeconds = 3600;

    public static void Configure(Infrastructure infrastructure)
    {
        ArgumentNullException.ThrowIfNull(infrastructure);

        var defaultHostName = new ProvisioningParameter(
            "defaultHostName",
            typeof(string));
        infrastructure.Add(defaultHostName);

        var customDomainVerificationId = new ProvisioningParameter(
            "customDomainVerificationId",
            typeof(string));
        infrastructure.Add(customDomainVerificationId);

        var websiteInboundIpAddress = new ProvisioningParameter(
            "websiteInboundIpAddress",
            typeof(IPAddress));
        infrastructure.Add(websiteInboundIpAddress);

        var rootZone = DnsZone.FromExisting("rootZone");
        rootZone.Name = "kotsenas.com";
        infrastructure.Add(rootZone);

        var blogZone = DnsZone.FromExisting("blogZone");
        blogZone.Name = "matt.kotsenas.com";
        infrastructure.Add(blogZone);

        AddWebsiteRecords(
            infrastructure,
            rootZone,
            "root",
            defaultHostName,
            customDomainVerificationId,
            websiteInboundIpAddress);
        AddWebsiteRecords(
            infrastructure,
            blogZone,
            "blog",
            defaultHostName,
            customDomainVerificationId,
            websiteInboundIpAddress);
    }

    private static void AddWebsiteRecords(
        Infrastructure infrastructure,
        DnsZone zone,
        string identifierPrefix,
        BicepValue<string> defaultHostName,
        BicepValue<string> customDomainVerificationId,
        BicepValue<IPAddress> websiteInboundIpAddress)
    {
        var apex = new DnsARecord($"{identifierPrefix}Apex")
        {
            Parent = zone,
            Name = "@",
            TtlInSeconds = TtlInSeconds,
            ARecords =
            {
                new DnsARecordInfo
                {
                    Ipv4Address = websiteInboundIpAddress,
                },
            },
        };
        infrastructure.Add(apex);

        var www = new DnsCnameRecord($"{identifierPrefix}Www")
        {
            Parent = zone,
            Name = "www",
            TtlInSeconds = TtlInSeconds,
            Cname = defaultHostName,
        };
        infrastructure.Add(www);

        AddVerificationRecord(
            infrastructure,
            zone,
            $"{identifierPrefix}ApexVerification",
            "asuid",
            customDomainVerificationId);
        AddVerificationRecord(
            infrastructure,
            zone,
            $"{identifierPrefix}WwwVerification",
            "asuid.www",
            customDomainVerificationId);
    }

    private static void AddVerificationRecord(
        Infrastructure infrastructure,
        DnsZone zone,
        string bicepIdentifier,
        string name,
        BicepValue<string> customDomainVerificationId)
    {
        var verification = new DnsTxtRecord(bicepIdentifier)
        {
            Parent = zone,
            Name = name,
            TtlInSeconds = TtlInSeconds,
            TxtRecords =
            {
                new DnsTxtRecordInfo
                {
                    Values = { customDomainVerificationId },
                },
            },
        };
        infrastructure.Add(verification);
    }
}
