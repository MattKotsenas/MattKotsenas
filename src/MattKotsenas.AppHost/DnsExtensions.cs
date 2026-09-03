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
        IResourceBuilder<AzureBicepResource> legacyWeb,
        IReadOnlyList<BlogCustomDomainResource> domains,
        string dnsResourceGroupName)
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
            dnsResourceGroupName,
            publishValueAsDefault: true);
        var dns = builder
            .AddAzureInfrastructure(
                "blog-dns",
                infrastructure => AddDnsResources(
                    infrastructure,
                    domains))
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

    private static void AddDnsResources(
        Infrastructure infrastructure,
        IReadOnlyList<BlogCustomDomainResource> domains)
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

        foreach (var zoneDomains in domains.GroupBy(domain => domain.Zone))
        {
            var zone = DnsZone.FromExisting(
                $"{zoneDomains.Key.BicepIdentifier}Zone");
            zone.Name = zoneDomains.Key.Name;
            infrastructure.Add(zone);

            foreach (var domain in zoneDomains)
            {
                AddRoutingRecord(
                    infrastructure,
                    zone,
                    domain,
                    defaultHostName,
                    websiteInboundIpAddress);
            }

            foreach (var domain in zoneDomains)
            {
                AddOwnershipRecord(
                    infrastructure,
                    zone,
                    domain,
                    customDomainVerificationId,
                    legacyRootVerificationId);
            }
        }
    }

    private static void AddRoutingRecord(
        Infrastructure infrastructure,
        DnsZone zone,
        BlogCustomDomainResource domain,
        BicepValue<string> defaultHostName,
        BicepValue<IPAddress> websiteInboundIpAddress)
    {
        if (domain.IsApex)
        {
            infrastructure.Add(new DnsARecord(domain.DnsBicepIdentifier)
            {
                Parent = zone,
                Name = domain.DnsRecordName,
                TtlInSeconds = TtlInSeconds,
                ARecords =
                {
                    new DnsARecordInfo
                    {
                        Ipv4Address = websiteInboundIpAddress,
                    },
                },
            });
        }
        else
        {
            infrastructure.Add(new DnsCnameRecord(domain.DnsBicepIdentifier)
            {
                Parent = zone,
                Name = domain.DnsRecordName,
                TtlInSeconds = TtlInSeconds,
                Cname = defaultHostName,
            });
        }

    }

    private static void AddOwnershipRecord(
        Infrastructure infrastructure,
        DnsZone zone,
        BlogCustomDomainResource domain,
        BicepValue<string> customDomainVerificationId,
        BicepValue<string> legacyRootVerificationId)
    {
        var additionalVerificationId =
            domain.IsApex && domain.Zone.IsRoot
                ? legacyRootVerificationId
                : null;
        AddVerificationRecord(
            infrastructure,
            zone,
            $"{domain.DnsBicepIdentifier}Verification",
            domain.OwnershipRecordName,
            customDomainVerificationId,
            additionalVerificationId);
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

#pragma warning restore AZPROVISION001
