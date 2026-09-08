using System.Net;
using System.Net.Sockets;

using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using Azure.Core;
using Azure.Provisioning;
using Azure.Provisioning.Dns;

namespace MattKotsenas.Hosting.Azure.Dns;

// Azure.Provisioning.Dns is prerelease and marks its entire API as experimental.
#pragma warning disable AZPROVISION001

/// <summary>
/// Adds Azure DNS resources to an Aspire application model.
/// </summary>
public static class AzureDnsResourceBuilderExtensions
{
    private static readonly TimeSpan DefaultTimeToLive =
        TimeSpan.FromHours(1);

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
        var zone = AzureProvisioningResource
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

        foreach (var recordSet in resource.RecordSets)
        {
            switch (recordSet)
            {
                case AzureDnsARecordSetResource a:
                    AddARecordSet(infrastructure, zone, a);
                    break;
                case AzureDnsCnameRecordSetResource cname:
                    AddCnameRecordSet(
                        infrastructure,
                        zone,
                        cname);
                    break;
                case AzureDnsTxtRecordSetResource txt:
                    AddTxtRecordSet(
                        infrastructure,
                        zone,
                        txt);
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Unsupported Azure DNS record set type '{recordSet.GetType().Name}'.");
            }
        }
    }

    /// <summary>Adds an Azure DNS A record set.</summary>
    public static IResourceBuilder<AzureDnsARecordSetResource> AddARecordSet(
        this IResourceBuilder<AzureDnsZoneResource> zone,
        [ResourceName] string name,
        string relativeName,
        TimeSpan? timeToLive = null)
    {
        ArgumentNullException.ThrowIfNull(zone);
        relativeName = NormalizeRelativeName(relativeName);
        var ttl = timeToLive ?? DefaultTimeToLive;
        ValidateTtl(ttl);
        ThrowIfConflict<AzureDnsARecordSetResource>(
            zone,
            relativeName);
        return AddRecordSet(
            zone,
            new AzureDnsARecordSetResource(
                name,
                relativeName,
                zone.Resource,
                ttl));
    }

    /// <summary>Adds an IPv4 address to an Azure DNS A record set.</summary>
    public static IResourceBuilder<AzureDnsARecordSetResource> WithAddress(
        this IResourceBuilder<AzureDnsARecordSetResource> recordSet,
        IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (address.AddressFamily is not AddressFamily.InterNetwork)
        {
            throw new ArgumentException(
                "An A record requires an IPv4 address.",
                nameof(address));
        }

        return AddAddressValue(
            recordSet,
            ReferenceExpression.Create($"{address.ToString()}"));
    }

    /// <summary>Adds a parameterized IPv4 address to an Azure DNS A record set.</summary>
    public static IResourceBuilder<AzureDnsARecordSetResource> WithAddress(
        this IResourceBuilder<AzureDnsARecordSetResource> recordSet,
        IResourceBuilder<ParameterResource> address)
    {
        ArgumentNullException.ThrowIfNull(address);
        return AddAddressValue(
            recordSet,
            address.Resource);
    }

    /// <summary>Adds a dynamic Aspire value as an IPv4 address.</summary>
    public static IResourceBuilder<AzureDnsARecordSetResource> WithAddress(
        this IResourceBuilder<AzureDnsARecordSetResource> recordSet,
        IExpressionValue address)
    {
        ArgumentNullException.ThrowIfNull(address);
        return AddAddressValue(recordSet, address);
    }

    /// <summary>Adds an Azure DNS CNAME record set.</summary>
    public static IResourceBuilder<AzureDnsCnameRecordSetResource>
        AddCnameRecordSet(
        this IResourceBuilder<AzureDnsZoneResource> zone,
        [ResourceName] string name,
        string relativeName,
        TimeSpan? timeToLive = null)
    {
        ArgumentNullException.ThrowIfNull(zone);
        relativeName = NormalizeRelativeName(relativeName);
        var ttl = timeToLive ?? DefaultTimeToLive;
        ValidateTtl(ttl);
        ThrowIfConflict<AzureDnsCnameRecordSetResource>(
            zone,
            relativeName);
        return AddRecordSet(
            zone,
            new AzureDnsCnameRecordSetResource(
                name,
                relativeName,
                zone.Resource,
                ttl));
    }

    /// <summary>Sets the target of an Azure DNS CNAME record set.</summary>
    public static IResourceBuilder<AzureDnsCnameRecordSetResource> WithTarget(
        this IResourceBuilder<AzureDnsCnameRecordSetResource> recordSet,
        string target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        return SetTargetValue(
            recordSet,
            ReferenceExpression.Create($"{target}"));
    }

    /// <summary>Sets a parameterized target on an Azure DNS CNAME record set.</summary>
    public static IResourceBuilder<AzureDnsCnameRecordSetResource> WithTarget(
        this IResourceBuilder<AzureDnsCnameRecordSetResource> recordSet,
        IResourceBuilder<ParameterResource> target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return SetTargetValue(
            recordSet,
            target.Resource);
    }

    /// <summary>Sets a dynamic Aspire value as a CNAME target.</summary>
    public static IResourceBuilder<AzureDnsCnameRecordSetResource> WithTarget(
        this IResourceBuilder<AzureDnsCnameRecordSetResource> recordSet,
        IExpressionValue target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return SetTargetValue(recordSet, target);
    }

    /// <summary>Adds an Azure DNS TXT record set.</summary>
    public static IResourceBuilder<AzureDnsTxtRecordSetResource>
        AddTxtRecordSet(
        this IResourceBuilder<AzureDnsZoneResource> zone,
        [ResourceName] string name,
        string relativeName,
        TimeSpan? timeToLive = null)
    {
        ArgumentNullException.ThrowIfNull(zone);
        relativeName = NormalizeRelativeName(relativeName);
        var ttl = timeToLive ?? DefaultTimeToLive;
        ValidateTtl(ttl);
        ThrowIfConflict<AzureDnsTxtRecordSetResource>(
            zone,
            relativeName);
        return AddRecordSet(
            zone,
            new AzureDnsTxtRecordSetResource(
                name,
                relativeName,
                zone.Resource,
                ttl));
    }

    /// <summary>Adds a TXT record to an Azure DNS TXT record set.</summary>
    public static IResourceBuilder<AzureDnsTxtRecordSetResource> WithRecord(
        this IResourceBuilder<AzureDnsTxtRecordSetResource> recordSet,
        string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return AddTxtRecordValue(
            recordSet,
            ReferenceExpression.Create($"{value}"));
    }

    /// <summary>Adds a parameterized TXT record to an Azure DNS TXT record set.</summary>
    public static IResourceBuilder<AzureDnsTxtRecordSetResource> WithRecord(
        this IResourceBuilder<AzureDnsTxtRecordSetResource> recordSet,
        IResourceBuilder<ParameterResource> value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return AddTxtRecordValue(
            recordSet,
            value.Resource);
    }

    /// <summary>Adds a dynamic Aspire value as a TXT record.</summary>
    public static IResourceBuilder<AzureDnsTxtRecordSetResource> WithRecord(
        this IResourceBuilder<AzureDnsTxtRecordSetResource> recordSet,
        IExpressionValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return AddTxtRecordValue(recordSet, value);
    }

    private static string NormalizeRelativeName(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return value.ToLowerInvariant();
    }

    private static IResourceBuilder<T> AddRecordSet<T>(
        IResourceBuilder<AzureDnsZoneResource> zone,
        T recordSet)
        where T : AzureDnsRecordSetResource
    {
        var builder = zone.ApplicationBuilder.AddResource(recordSet);
        zone.Resource.RecordSets.Add(recordSet);
        return builder;
    }

    private static void ValidateTtl(TimeSpan timeToLive)
    {
        if (timeToLive <= TimeSpan.Zero ||
            timeToLive.Ticks % TimeSpan.TicksPerSecond != 0 ||
            timeToLive.TotalSeconds > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(timeToLive));
        }
    }

    private static void ThrowIfConflict<T>(
        IResourceBuilder<AzureDnsZoneResource> zone,
        string relativeName)
        where T : AzureDnsRecordSetResource
    {
        var recordSets = zone.Resource.RecordSets
            .Where(record => record.RelativeName == relativeName)
            .ToList();
        if (recordSets
            .OfType<T>()
            .Any())
        {
            throw new InvalidOperationException(
                $"DNS record set '{relativeName}' is already registered.");
        }
        if ((typeof(T) == typeof(AzureDnsCnameRecordSetResource) &&
             recordSets.Count > 0) ||
            recordSets.OfType<AzureDnsCnameRecordSetResource>().Any())
        {
            throw new InvalidOperationException(
                $"A CNAME record set cannot coexist with another record set at '{relativeName}'.");
        }
    }

    private static void AddARecordSet(
        AzureResourceInfrastructure infrastructure,
        DnsZone zone,
        AzureDnsARecordSetResource resource)
    {
        var recordSet = new DnsARecord(
            Infrastructure.NormalizeBicepIdentifier(resource.Name))
        {
            Parent = zone,
            Name = resource.RelativeName,
            TtlInSeconds = (int)resource.TimeToLive.TotalSeconds,
        };
        for (var index = 0; index < resource.Addresses.Count; index++)
        {
            recordSet.ARecords.Add(new DnsARecordInfo
            {
                Ipv4Address = resource.Addresses[index]
                    .AsProvisioningParameter(
                        infrastructure,
                        Infrastructure.NormalizeBicepIdentifier(
                            $"{resource.Name}_address_{index}"),
                        GetIsSecure(resource.Addresses[index])),
            });
        }
        infrastructure.Add(recordSet);
    }

    private static void AddCnameRecordSet(
        AzureResourceInfrastructure infrastructure,
        DnsZone zone,
        AzureDnsCnameRecordSetResource resource)
    {
        var recordSet = new DnsCnameRecord(
            Infrastructure.NormalizeBicepIdentifier(resource.Name))
        {
            Parent = zone,
            Name = resource.RelativeName,
            TtlInSeconds = (int)resource.TimeToLive.TotalSeconds,
        };
        if (resource.Target is { } target)
        {
            recordSet.Cname = target.AsProvisioningParameter(
                infrastructure,
                Infrastructure.NormalizeBicepIdentifier(
                    $"{resource.Name}_target_0"),
                GetIsSecure(target));
        }
        infrastructure.Add(recordSet);
    }

    private static void AddTxtRecordSet(
        AzureResourceInfrastructure infrastructure,
        DnsZone zone,
        AzureDnsTxtRecordSetResource resource)
    {
        var recordSet = new DnsTxtRecord(
            Infrastructure.NormalizeBicepIdentifier(resource.Name))
        {
            Parent = zone,
            Name = resource.RelativeName,
            TtlInSeconds = (int)resource.TimeToLive.TotalSeconds,
        };
        for (var index = 0; index < resource.Records.Count; index++)
        {
            recordSet.TxtRecords.Add(new DnsTxtRecordInfo
            {
                Values =
                {
                    resource.Records[index]
                        .AsProvisioningParameter(
                            infrastructure,
                            Infrastructure.NormalizeBicepIdentifier(
                                $"{resource.Name}_record_{index}"),
                            GetIsSecure(resource.Records[index])),
                },
            });
        }
        infrastructure.Add(recordSet);
    }

    private static IResourceBuilder<AzureDnsARecordSetResource>
        AddAddressValue(
            IResourceBuilder<AzureDnsARecordSetResource> recordSet,
            IExpressionValue address)
    {
        ArgumentNullException.ThrowIfNull(recordSet);
        AddReferenceRelationship(GetZoneBuilder(recordSet), address);
        recordSet.Resource.Addresses.Add(address);
        return recordSet;
    }

    private static IResourceBuilder<AzureDnsCnameRecordSetResource>
        SetTargetValue(
            IResourceBuilder<AzureDnsCnameRecordSetResource> recordSet,
            IExpressionValue target)
    {
        ArgumentNullException.ThrowIfNull(recordSet);
        if (recordSet.Resource.Target is not null)
        {
            throw new InvalidOperationException(
                $"CNAME record set '{recordSet.Resource.RelativeName}' already has a target.");
        }
        AddReferenceRelationship(GetZoneBuilder(recordSet), target);
        recordSet.Resource.Target = target;
        return recordSet;
    }

    private static IResourceBuilder<AzureDnsTxtRecordSetResource>
        AddTxtRecordValue(
            IResourceBuilder<AzureDnsTxtRecordSetResource> recordSet,
            IExpressionValue value)
    {
        ArgumentNullException.ThrowIfNull(recordSet);
        AddReferenceRelationship(GetZoneBuilder(recordSet), value);
        recordSet.Resource.Records.Add(value);
        return recordSet;
    }

    private static IResourceBuilder<AzureDnsZoneResource> GetZoneBuilder<T>(
        IResourceBuilder<T> recordSet)
        where T : AzureDnsRecordSetResource =>
        recordSet.ApplicationBuilder.CreateResourceBuilder(
            recordSet.Resource.Parent);

    private static void AddReferenceRelationship(
        IResourceBuilder<AzureDnsZoneResource> zone,
        IExpressionValue value)
    {
        AddReferenceRelationship(zone, (object)value);

        static void AddReferenceRelationship(
            IResourceBuilder<AzureDnsZoneResource> zone,
            object value)
        {
            if (value is IResource resource)
            {
                zone.WithReferenceRelationship(resource);
            }
            if (value is IValueWithReferences references)
            {
                foreach (var reference in references.References)
                {
                    AddReferenceRelationship(zone, reference);
                }
            }
        }
    }

    private static bool? GetIsSecure(IExpressionValue value) =>
        ContainsSecret(value) ? true : null;

    private static bool ContainsSecret(object value) =>
        value switch
        {
            ParameterResource { Secret: true } => true,
            IValueWithReferences references
                when references.References
                    .Any(ContainsSecret) => true,
            _ => false,
        };
}

#pragma warning restore AZPROVISION001
