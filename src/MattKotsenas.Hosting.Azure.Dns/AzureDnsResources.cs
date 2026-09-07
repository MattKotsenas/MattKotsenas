using Aspire.Hosting.Azure;

namespace MattKotsenas.Hosting.Azure.Dns;

/// <summary>
/// Represents an Azure DNS zone.
/// </summary>
public sealed class AzureDnsZoneResource
    : AzureProvisioningResource
{
    internal AzureDnsZoneResource(
        string name,
        string zoneName,
        Action<AzureResourceInfrastructure> configure)
        : base(name, configure)
    {
        ZoneName = zoneName;
    }

    /// <summary>
    /// Gets the zone name used when the resource is created.
    /// </summary>
    public string ZoneName { get; }
}
