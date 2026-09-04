using System.Net;

using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using Azure.Provisioning;
using Azure.Provisioning.Dns;

namespace MattKotsenas.AppHost;

// Azure.Provisioning.Dns is prerelease and marks its entire API as experimental.
#pragma warning disable AZPROVISION001

internal static class DnsExtensions
{
    private const int TtlInSeconds = 3600;

    public static IResourceBuilder<AzureBicepResource> AddBlogDns(
        this IDistributedApplicationBuilder builder,
        IResourceBuilder<AzureBicepResource> legacyWeb)
    {
        // App Service does not expose its shared inbound address through ARM.
        var legacyWebInboundIpAddress = builder.AddParameter(
            "legacyWebInboundIpAddress",
            "168.62.20.37",
            publishValueAsDefault: true);
        var legacyRootVerificationId = builder.AddParameter(
            "legacyRootVerificationId",
            "F883000E15157DBAA27BE77E3C2BFB8F5B8D3E5BED81331607354AA636C349BE",
            publishValueAsDefault: true);
        var dnsResourceGroup = builder.AddParameter(
            "dnsResourceGroupName",
            "dns",
            publishValueAsDefault: true);
        var dns = builder
            .AddAzureInfrastructure("blog-dns", AddDnsResources)
            .WithParameter(
                "defaultHostName",
                legacyWeb.GetOutput("defaultHostName"))
            .WithParameter(
                "customDomainVerificationId",
                legacyWeb.GetOutput("customDomainVerificationId"))
            .WithParameter(
                "websiteInboundIpAddress",
                legacyWebInboundIpAddress)
            .WithParameter(
                "legacyRootVerificationId",
                legacyRootVerificationId);
        dns.Resource.Scope = new(dnsResourceGroup.Resource);

        return dns;
    }

    private static void AddDnsResources(Infrastructure infrastructure)
    {
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
        rootZone.Name = BlogDomains.Root;
        infrastructure.Add(rootZone);

        var blogZone = DnsZone.FromExisting("blogZone");
        blogZone.Name = BlogDomains.Blog;
        infrastructure.Add(blogZone);

        AddWebsiteRecords(
            infrastructure,
            rootZone,
            "root",
            defaultHostName,
            customDomainVerificationId,
            websiteInboundIpAddress,
            legacyRootVerificationId);
        AddWebsiteRecords(
            infrastructure,
            blogZone,
            "blog",
            defaultHostName,
            customDomainVerificationId,
            websiteInboundIpAddress,
            additionalApexVerificationId: null);
    }

    private static void AddWebsiteRecords(
        Infrastructure infrastructure,
        DnsZone zone,
        string identifierPrefix,
        BicepValue<string> defaultHostName,
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
