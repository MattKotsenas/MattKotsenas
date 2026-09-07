using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using Azure.Core;
using Azure.Provisioning.Dns;

namespace MattKotsenas.Hosting.Azure.Dns;

// Azure.Provisioning.Dns is prerelease and marks its entire API as experimental.
#pragma warning disable AZPROVISION001

/// <summary>
/// Adds Azure DNS resources to an Aspire application model.
/// </summary>
public static class AzureDnsResourceBuilderExtensions
{
    /// <summary>
    /// Adds an Azure DNS zone.
    /// </summary>
    /// <param name="builder">The distributed application builder.</param>
    /// <param name="name">The Aspire resource name.</param>
    /// <param name="zoneName">The fully qualified DNS zone name.</param>
    /// <returns>The Azure DNS zone resource builder.</returns>
    public static IResourceBuilder<AzureDnsZoneResource> AddAzureDnsZone(
        this IDistributedApplicationBuilder builder,
        string name,
        string zoneName)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(zoneName);
        var normalizedZoneName = zoneName
            .TrimEnd('.')
            .ToLowerInvariant();
        if (Uri.CheckHostName(normalizedZoneName) is
            not UriHostNameType.Dns)
        {
            throw new ArgumentException(
                $"'{zoneName}' is not a DNS zone name.",
                nameof(zoneName));
        }

        if (builder.Resources
            .OfType<AzureDnsZoneResource>()
            .Any(zone => string.Equals(
                zone.ZoneName,
                normalizedZoneName,
                StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"Azure DNS zone '{normalizedZoneName}' is already registered.");
        }

        builder.AddAzureProvisioning();
        return builder.AddResource(
            new AzureDnsZoneResource(
                name,
                normalizedZoneName,
                ConfigureZone));
    }

    private static void ConfigureZone(
        AzureResourceInfrastructure infrastructure)
    {
        var resource = (AzureDnsZoneResource)
            infrastructure.AspireResource;
        _ = AzureProvisioningResource
            .CreateExistingOrNewProvisionableResource(
                infrastructure,
                static (identifier, name) =>
                {
                    var existing = DnsZone.FromExisting(
                        identifier);
                    existing.Name = name;
                    return existing;
                },
                _ => new DnsZone(
                    resource.GetBicepIdentifier(),
                    DnsZone.ResourceVersions.V2018_05_01)
                {
                    Name = resource.ZoneName,
                    Location = new AzureLocation("global"),
                    ZoneType = DnsZoneType.Public,
                });
    }
}

#pragma warning restore AZPROVISION001
