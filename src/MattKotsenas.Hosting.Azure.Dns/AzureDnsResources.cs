using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using Azure.Provisioning;
using Azure.Provisioning.Dns;
using Azure.Provisioning.Primitives;

namespace MattKotsenas.Hosting.Azure.Dns;

// Azure.Provisioning.Dns is prerelease and marks its entire API as experimental.
#pragma warning disable AZPROVISION001

/// <summary>
/// Represents an Azure DNS zone.
/// </summary>
public sealed class AzureDnsZoneResource
    : AzureProvisioningResource
{
    internal const string NameOutputName = "name";

    internal AzureDnsZoneResource(
        string name,
        string zoneName,
        Action<AzureResourceInfrastructure> configure)
        : base(name, configure)
    {
        ZoneName = zoneName;
    }

    /// <summary>
    /// Gets the fully qualified DNS zone name.
    /// </summary>
    public string ZoneName { get; }

    internal BicepOutputReference NameOutputReference =>
        new(NameOutputName, this);

    /// <inheritdoc />
    public override ProvisionableResource AddAsExistingResource(
        AzureResourceInfrastructure infra)
    {
        ArgumentNullException.ThrowIfNull(infra);
        AzureDnsResourceBuilderExtensions
            .ValidateExistingZoneIdentity(this);
        var identifier = this.GetBicepIdentifier();
        var existing = infra
            .GetProvisionableResources()
            .OfType<DnsZone>()
            .SingleOrDefault(zone =>
                zone.BicepIdentifier == identifier);
        if (existing is not null)
        {
            return existing;
        }

        var zone = DnsZone.FromExisting(identifier);
        zone.Name = IsExcludedFromManifest()
            ? new BicepValue<string>(ZoneName)
            : NameOutputReference.AsProvisioningParameter(
                infra);
        infra.Add(zone);
        return zone;
    }

    private bool IsExcludedFromManifest() =>
        Annotations
            .OfType<ManifestPublishingCallbackAnnotation>()
            .Any(annotation =>
                ReferenceEquals(
                    annotation,
                    ManifestPublishingCallbackAnnotation.Ignore));
}

/// <summary>
/// Identifies a modeled DNS record by zone, name, and record type.
/// </summary>
/// <remarks>
/// Implement this interface for custom record resources that should
/// participate in
/// <see cref="AzureDnsResourceBuilderExtensions.ThrowIfRecordConflicts"/>.
/// </remarks>
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

    /// <summary>
    /// Gets the DNS record type.
    /// </summary>
    DnsRecordType RecordType { get; }
}

/// <summary>
/// Represents an Azure DNS A record.
/// </summary>
public sealed class AzureDnsARecordResource
    : AzureProvisioningResource,
      IAzureDnsRecordResource
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
    public string Hostname =>
        RelativeName.ToHostname(Parent.ZoneName);

    /// <inheritdoc />
    public DnsRecordType RecordType => DnsRecordType.A;
}

/// <summary>
/// Represents an Azure DNS CNAME record.
/// </summary>
public sealed class AzureDnsCnameRecordResource
    : AzureProvisioningResource,
      IAzureDnsRecordResource
{
    internal AzureDnsCnameRecordResource(
        string name,
        DnsRelativeName relativeName,
        AzureDnsZoneResource parent,
        Action<AzureResourceInfrastructure> configure)
        : base(name, configure)
    {
        if (relativeName.IsApex)
        {
            throw new ArgumentException(
                "A CNAME record cannot be created at the zone apex.",
                nameof(relativeName));
        }

        RelativeName = relativeName;
        Parent = parent;
    }

    /// <inheritdoc />
    public AzureDnsZoneResource Parent { get; }

    /// <inheritdoc />
    public DnsRelativeName RelativeName { get; }

    /// <inheritdoc />
    public string Hostname =>
        RelativeName.ToHostname(Parent.ZoneName);

    /// <inheritdoc />
    public DnsRecordType RecordType => DnsRecordType.Cname;
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
    public string Hostname =>
        RelativeName.ToHostname(Parent.ZoneName);

    /// <inheritdoc />
    public DnsRecordType RecordType => DnsRecordType.Txt;
}

internal sealed record AzureDnsTxtValueAnnotation(
    string ParameterName)
    : IResourceAnnotation;

#pragma warning restore AZPROVISION001
