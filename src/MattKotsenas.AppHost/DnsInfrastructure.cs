using System.Net;

using Azure.Provisioning;
using Azure.Provisioning.Dns;
using Azure.Provisioning.Expressions;

namespace MattKotsenas.AppHost;

// Azure.Provisioning.Dns is prerelease and marks its entire API as experimental.
#pragma warning disable AZPROVISION001

internal static class DnsInfrastructure
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

        var legacyRootVerificationId = new ProvisioningParameter(
            "legacyRootVerificationId",
            typeof(string));
        infrastructure.Add(legacyRootVerificationId);

        var rootZone = DnsZone.FromExisting("rootZone");
        rootZone.Name = "kotsenas.com";
        infrastructure.Add(rootZone);

        var blogZone = DnsZone.FromExisting("blogZone");
        blogZone.Name = "matt.kotsenas.com";
        infrastructure.Add(blogZone);

        var legacyVerificationHostName = BicepFunction.Interpolate(
            $"awverify.{defaultHostName.Value}");

        AddWebsiteRecords(
            infrastructure,
            rootZone,
            "root",
            defaultHostName,
            legacyVerificationHostName,
            customDomainVerificationId,
            websiteInboundIpAddress,
            legacyRootVerificationId);
        AddWebsiteRecords(
            infrastructure,
            blogZone,
            "blog",
            defaultHostName,
            legacyVerificationHostName,
            customDomainVerificationId,
            websiteInboundIpAddress,
            additionalApexVerificationId: null);
    }

    private static void AddWebsiteRecords(
        Infrastructure infrastructure,
        DnsZone zone,
        string identifierPrefix,
        BicepValue<string> defaultHostName,
        BicepValue<string> legacyVerificationHostName,
        BicepValue<string> customDomainVerificationId,
        BicepValue<IPAddress> websiteInboundIpAddress,
        BicepValue<string>? additionalApexVerificationId)
    {
        infrastructure.Add(new DnsARecord($"{identifierPrefix}Apex")
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
        });

        infrastructure.Add(new DnsCnameRecord($"{identifierPrefix}Www")
        {
            Parent = zone,
            Name = "www",
            TtlInSeconds = TtlInSeconds,
            Cname = defaultHostName,
        });

        infrastructure.Add(new DnsCnameRecord(
            $"{identifierPrefix}LegacyApexVerification")
        {
            Parent = zone,
            Name = "awverify",
            TtlInSeconds = TtlInSeconds,
            Cname = legacyVerificationHostName,
        });

        infrastructure.Add(new DnsCnameRecord(
            $"{identifierPrefix}LegacyWwwVerification")
        {
            Parent = zone,
            Name = "awverify.www",
            TtlInSeconds = TtlInSeconds,
            Cname = legacyVerificationHostName,
        });

        AddVerificationRecord(
            infrastructure,
            zone,
            $"{identifierPrefix}ApexVerification",
            "asuid",
            customDomainVerificationId,
            additionalApexVerificationId);
        AddVerificationRecord(
            infrastructure,
            zone,
            $"{identifierPrefix}WwwVerification",
            "asuid.www",
            customDomainVerificationId,
            additionalVerificationId: null);
    }

    private static void AddVerificationRecord(
        Infrastructure infrastructure,
        DnsZone zone,
        string bicepIdentifier,
        string name,
        BicepValue<string> customDomainVerificationId,
        BicepValue<string>? additionalVerificationId)
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
        if (additionalVerificationId is not null)
        {
            verification.TxtRecords.Add(new DnsTxtRecordInfo
            {
                Values = { additionalVerificationId },
            });
        }

        infrastructure.Add(verification);
    }
}
