using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using Azure.Provisioning;
using Azure.Provisioning.Dns;
using Azure.Provisioning.Primitives;

namespace MattKotsenas.Hosting.Azure.Dns;

#pragma warning disable AZPROVISION001

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

    /// <summary>
    /// Gets the provisioned zone name.
    /// </summary>
    public BicepOutputReference NameOutputReference =>
        new("name", this);

    /// <inheritdoc />
    public override ProvisionableResource AddAsExistingResource(
        AzureResourceInfrastructure infra)
    {
        var zone = DnsZone.FromExisting(
            this.GetBicepIdentifier());
        zone.Name = NameOutputReference.AsProvisioningParameter(
            infra);
        infra.Add(zone);
        return zone;
    }
}

/// <summary>
/// Represents an Azure DNS record.
/// </summary>
public abstract class AzureDnsRecordResource
    : AzureProvisioningResource
{
    internal AzureDnsRecordResource(
        string name,
        string relativeName,
        AzureDnsZoneResource parent,
        TimeSpan timeToLive,
        Action<AzureResourceInfrastructure> configure)
        : base(name, configure)
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

/// <summary>Represents an Azure DNS A record.</summary>
public sealed class AzureDnsARecordResource
    : AzureDnsRecordResource
{
    internal AzureDnsARecordResource(
        string name,
        string relativeName,
        AzureDnsZoneResource parent,
        TimeSpan timeToLive,
        Action<AzureResourceInfrastructure> configure)
        : base(name, relativeName, parent, timeToLive, configure)
    {
    }
}

/// <summary>Represents an Azure DNS CNAME record.</summary>
public sealed class AzureDnsCnameRecordResource
    : AzureDnsRecordResource
{
    internal AzureDnsCnameRecordResource(
        string name,
        string relativeName,
        AzureDnsZoneResource parent,
        TimeSpan timeToLive,
        Action<AzureResourceInfrastructure> configure)
        : base(name, relativeName, parent, timeToLive, configure)
    {
        if (relativeName == "@")
        {
            throw new ArgumentException(
                "A CNAME record cannot be created at the zone apex.",
                nameof(relativeName));
        }
    }
}

/// <summary>Represents an Azure DNS TXT record.</summary>
public sealed class AzureDnsTxtRecordResource
    : AzureDnsRecordResource
{
    internal AzureDnsTxtRecordResource(
        string name,
        string relativeName,
        AzureDnsZoneResource parent,
        TimeSpan timeToLive,
        Action<AzureResourceInfrastructure> configure)
        : base(name, relativeName, parent, timeToLive, configure)
    {
    }
}

internal sealed record AzureDnsTxtValueAnnotation(
    string ParameterName)
    : IResourceAnnotation;

#pragma warning restore AZPROVISION001
