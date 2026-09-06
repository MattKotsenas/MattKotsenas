using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;

namespace MattKotsenas.Hosting.Azure.Dns;

/// <summary>
/// Represents an existing Azure DNS zone.
/// </summary>
public sealed class AzureDnsZoneResource : Resource
{
    internal AzureDnsZoneResource(string name, string zoneName)
        : base(name)
    {
        ZoneName = zoneName;
    }

    /// <summary>
    /// Gets the fully qualified DNS zone name.
    /// </summary>
    public string ZoneName { get; }
}

/// <summary>
/// Describes an Azure DNS record resource.
/// </summary>
public interface IAzureDnsRecordResource
    : IResourceWithParent<AzureDnsZoneResource>
{
    /// <summary>
    /// Gets the record name relative to its zone.
    /// </summary>
    DnsRelativeName RelativeName { get; }

    /// <summary>
    /// Gets the fully qualified hostname represented by the record.
    /// </summary>
    string Hostname { get; }

}

/// <summary>
/// Marks a DNS record that can route traffic to an application.
/// </summary>
public interface IAzureDnsRoutingRecordResource
    : IAzureDnsRecordResource;

/// <summary>
/// Represents an Azure DNS A record.
/// </summary>
public sealed class AzureDnsARecordResource
    : AzureProvisioningResource,
      IAzureDnsRoutingRecordResource
{
    internal AzureDnsARecordResource(
        string name,
        DnsRelativeName relativeName,
        AzureDnsZoneResource parent,
        Action<AzureResourceInfrastructure> configure)
        : base(name, configure)
    {
        RelativeName = relativeName;
        Parent = parent;
    }

    /// <inheritdoc />
    public AzureDnsZoneResource Parent { get; }

    /// <inheritdoc />
    public DnsRelativeName RelativeName { get; }

    /// <inheritdoc />
    public string Hostname => RelativeName.ToHostname(Parent.ZoneName);
}

/// <summary>
/// Represents an Azure DNS CNAME record.
/// </summary>
public sealed class AzureDnsCnameRecordResource
    : AzureProvisioningResource,
      IAzureDnsRoutingRecordResource
{
    internal AzureDnsCnameRecordResource(
        string name,
        DnsRelativeName relativeName,
        AzureDnsZoneResource parent,
        Action<AzureResourceInfrastructure> configure)
        : base(name, configure)
    {
        RelativeName = relativeName;
        Parent = parent;
    }

    /// <inheritdoc />
    public AzureDnsZoneResource Parent { get; }

    /// <inheritdoc />
    public DnsRelativeName RelativeName { get; }

    /// <inheritdoc />
    public string Hostname => RelativeName.ToHostname(Parent.ZoneName);
}

/// <summary>
/// Represents an Azure DNS TXT record.
/// </summary>
public sealed class AzureDnsTxtRecordResource
    : AzureProvisioningResource,
      IAzureDnsRecordResource
{
    internal AzureDnsTxtRecordResource(
        string name,
        DnsRelativeName relativeName,
        AzureDnsZoneResource parent,
        Action<AzureResourceInfrastructure> configure)
        : base(name, configure)
    {
        RelativeName = relativeName;
        Parent = parent;
    }

    /// <inheritdoc />
    public AzureDnsZoneResource Parent { get; }

    /// <inheritdoc />
    public DnsRelativeName RelativeName { get; }

    /// <inheritdoc />
    public string Hostname => RelativeName.ToHostname(Parent.ZoneName);
}

internal sealed record AzureDnsTxtValueAnnotation(
    string ParameterName)
    : IResourceAnnotation;
