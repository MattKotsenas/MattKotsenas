using Aspire.Hosting.ApplicationModel;
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

    internal List<AzureDnsRecordSetResource> RecordSets { get; } = [];
}

/// <summary>
/// Represents an Azure DNS record set.
/// </summary>
public abstract class AzureDnsRecordSetResource
    : Resource,
      IResourceWithParent<AzureDnsZoneResource>
{
    internal AzureDnsRecordSetResource(
        string name,
        string relativeName,
        AzureDnsZoneResource parent,
        TimeSpan timeToLive)
        : base(name)
    {
        RelativeName = relativeName;
        Parent = parent;
        TimeToLive = timeToLive;
    }

    /// <summary>Gets the parent DNS zone.</summary>
    public AzureDnsZoneResource Parent { get; }
    /// <summary>Gets the record name relative to the zone.</summary>
    public string RelativeName { get; }
    /// <summary>Gets the fully qualified hostname.</summary>
    public string Hostname =>
        RelativeName == "@"
            ? Parent.ZoneName
            : $"{RelativeName}.{Parent.ZoneName}";
    /// <summary>Gets the record time to live.</summary>
    public TimeSpan TimeToLive { get; }
}

/// <summary>Represents an Azure DNS A record set.</summary>
public sealed class AzureDnsARecordSetResource
    : AzureDnsRecordSetResource
{
    internal AzureDnsARecordSetResource(
        string name,
        string relativeName,
        AzureDnsZoneResource parent,
        TimeSpan timeToLive)
        : base(name, relativeName, parent, timeToLive)
    {
    }

    internal List<IExpressionValue> Addresses { get; } = [];
}

/// <summary>Represents an Azure DNS CNAME record set.</summary>
public sealed class AzureDnsCnameRecordSetResource
    : AzureDnsRecordSetResource
{
    internal AzureDnsCnameRecordSetResource(
        string name,
        string relativeName,
        AzureDnsZoneResource parent,
        TimeSpan timeToLive)
        : base(name, relativeName, parent, timeToLive)
    {
        if (relativeName == "@")
        {
            throw new ArgumentException(
                "A CNAME record cannot be created at the zone apex.",
                nameof(relativeName));
        }
    }

    internal IExpressionValue? Target { get; set; }
}

/// <summary>Represents an Azure DNS TXT record set.</summary>
public sealed class AzureDnsTxtRecordSetResource
    : AzureDnsRecordSetResource
{
    internal AzureDnsTxtRecordSetResource(
        string name,
        string relativeName,
        AzureDnsZoneResource parent,
        TimeSpan timeToLive)
        : base(name, relativeName, parent, timeToLive)
    {
    }

    internal List<IExpressionValue> Records { get; } = [];
}
