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
/// <remarks>
/// Record values may be strings, parameter-resource builders, Bicep output
/// references, or Aspire reference expressions.
/// </remarks>
public static class AzureDnsResourceBuilderExtensions
{
    private static readonly TimeSpan DefaultTimeToLive =
        TimeSpan.FromHours(1);
    private static readonly int DefaultTtlSeconds =
        (int)DefaultTimeToLive.TotalSeconds;
    private const string TargetParameterName = "target";

    /// <summary>
    /// Adds an Azure DNS zone that is created when published.
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

    /// <summary>
    /// Adds an Azure DNS A record.
    /// </summary>
    /// <param name="zone">The parent DNS zone.</param>
    /// <param name="name">The Aspire resource name.</param>
    /// <param name="relativeName">The record name relative to the zone.</param>
    /// <param name="address">The IPv4 address or Aspire value reference.</param>
    /// <returns>The Azure DNS A record resource builder.</returns>
    public static IResourceBuilder<AzureDnsARecordResource> AddARecord(
        this IResourceBuilder<AzureDnsZoneResource> zone,
        string name,
        DnsRelativeName relativeName,
        object address)
    {
        ArgumentNullException.ThrowIfNull(zone);
        ArgumentNullException.ThrowIfNull(address);
        ValidateAddressLiteral(address);
        var valueKind = ValidateParameterValue(
            address,
            nameof(address));
        zone.ThrowIfRecordConflicts(
            relativeName,
            DnsRecordType.A);
        ValidateHostnameLength(
            zone.Resource,
            relativeName);
        var record = zone.ApplicationBuilder.AddResource(
            new AzureDnsARecordResource(
                name,
                relativeName,
                zone.Resource,
                ConfigureARecord));
        return record.WithParameterValue(
            TargetParameterName,
            address,
            valueKind);
    }

    /// <summary>
    /// Adds an Azure DNS CNAME record.
    /// </summary>
    /// <param name="zone">The parent DNS zone.</param>
    /// <param name="name">The Aspire resource name.</param>
    /// <param name="relativeName">The record name relative to the zone.</param>
    /// <param name="target">The canonical hostname or Aspire value reference.</param>
    /// <returns>The Azure DNS CNAME record resource builder.</returns>
    public static IResourceBuilder<AzureDnsCnameRecordResource>
        AddCnameRecord(
            this IResourceBuilder<AzureDnsZoneResource> zone,
            string name,
            DnsRelativeName relativeName,
            object target)
    {
        ArgumentNullException.ThrowIfNull(zone);
        ArgumentNullException.ThrowIfNull(target);
        var valueKind = ValidateParameterValue(
            target,
            nameof(target));
        zone.ThrowIfRecordConflicts(
            relativeName,
            DnsRecordType.Cname);
        ValidateHostnameLength(
            zone.Resource,
            relativeName);
        var record = zone.ApplicationBuilder.AddResource(
            new AzureDnsCnameRecordResource(
                name,
                relativeName,
                zone.Resource,
                ConfigureCnameRecord));
        return record.WithParameterValue(
            TargetParameterName,
            target,
            valueKind);
    }

    /// <summary>
    /// Adds an Azure DNS TXT record with its first value.
    /// </summary>
    /// <param name="zone">The parent DNS zone.</param>
    /// <param name="name">The Aspire resource name.</param>
    /// <param name="relativeName">The record name relative to the zone.</param>
    /// <param name="value">The TXT value or Aspire value reference.</param>
    /// <returns>The Azure DNS TXT record resource builder.</returns>
    public static IResourceBuilder<AzureDnsTxtRecordResource> AddTxtRecord(
        this IResourceBuilder<AzureDnsZoneResource> zone,
        string name,
        DnsRelativeName relativeName,
        object value)
    {
        ArgumentNullException.ThrowIfNull(zone);
        ArgumentNullException.ThrowIfNull(value);
        var valueKind = ValidateParameterValue(
            value,
            nameof(value));
        zone.ThrowIfRecordConflicts(
            relativeName,
            DnsRecordType.Txt);
        ValidateHostnameLength(
            zone.Resource,
            relativeName);
        var record = zone.ApplicationBuilder.AddResource(
            new AzureDnsTxtRecordResource(
                name,
                relativeName,
                zone.Resource,
                ConfigureTxtRecord));
        return AddTxtValue(record, value, valueKind);
    }

    /// <summary>
    /// Adds another value to an Azure DNS TXT record.
    /// </summary>
    /// <param name="record">The TXT record resource builder.</param>
    /// <param name="value">The TXT value or Aspire value reference.</param>
    /// <returns>The Azure DNS TXT record resource builder.</returns>
    public static IResourceBuilder<AzureDnsTxtRecordResource> WithValue(
        this IResourceBuilder<AzureDnsTxtRecordResource> record,
        object value)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(value);
        var valueKind = ValidateParameterValue(
            value,
            nameof(value));
        return AddTxtValue(record, value, valueKind);
    }

    /// <summary>
    /// Throws when a record would conflict with a modeled record.
    /// </summary>
    /// <param name="zone">The parent DNS zone.</param>
    /// <param name="relativeName">The record name relative to the zone.</param>
    /// <param name="recordType">The record type to add.</param>
    public static void ThrowIfRecordConflicts(
        this IResourceBuilder<AzureDnsZoneResource> zone,
        DnsRelativeName relativeName,
        DnsRecordType recordType)
    {
        ArgumentNullException.ThrowIfNull(zone);
        if (relativeName == default)
        {
            throw new ArgumentException(
                DnsRelativeName.UninitializedMessage,
                nameof(relativeName));
        }

        if (recordType == default)
        {
            throw new ArgumentException(
                DnsRecordType.UninitializedMessage,
                nameof(recordType));
        }

        var existing = zone.ApplicationBuilder.Resources
            .OfType<IAzureDnsRecordResource>()
            .Where(record =>
                ReferenceEquals(record.Parent, zone.Resource) &&
                record.RelativeName == relativeName)
            .ToArray();
        if (existing.FirstOrDefault(record =>
                record.RelativeName == default ||
                record.RecordType == default) is { } invalid)
        {
            throw new InvalidOperationException(
                $"Azure DNS resource '{invalid.Name}' has uninitialized identity.");
        }

        var conflict = existing.FirstOrDefault(record =>
                record.RecordType == recordType)
            ?? (recordType == DnsRecordType.Cname
                ? existing.FirstOrDefault()
                : existing.FirstOrDefault(record =>
                    record.RecordType == DnsRecordType.Cname));
        if (conflict is not null)
        {
            throw new InvalidOperationException(
                $"Azure DNS record '{relativeName.ToHostname(zone.Resource.ZoneName)}' is already written by resource '{conflict.Name}'.");
        }
    }

    /// <summary>
    /// Applies a parent DNS zone's Azure scope to a custom record resource.
    /// </summary>
    /// <param name="record">The custom DNS record resource.</param>
    /// <param name="infrastructure">
    /// The custom record's Azure infrastructure.
    /// </param>
    public static void ApplyAzureDnsZoneScope(
        this IAzureDnsRecordResource record,
        AzureResourceInfrastructure infrastructure)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(infrastructure);
        if (infrastructure.AspireResource is not
            AzureBicepResource azureResource ||
            !ReferenceEquals(
                infrastructure.AspireResource,
                record))
        {
            throw new ArgumentException(
                "The infrastructure must belong to the DNS record resource.",
                nameof(infrastructure));
        }

        var zone = record.Parent;
        var annotation = zone.Annotations
            .OfType<ExistingAzureResourceAnnotation>()
            .LastOrDefault();
        azureResource.Scope = annotation switch
        {
            {
                ResourceGroup: not null,
                Subscription: not null,
            } =>
                new AzureBicepResourceScope(
                    annotation.ResourceGroup,
                    annotation.Subscription),
            { ResourceGroup: not null } =>
                new AzureBicepResourceScope(
                    annotation.ResourceGroup),
            _ => zone.Scope,
        };
    }

    private static void ConfigureZone(
        AzureResourceInfrastructure infrastructure)
    {
        var resource = (AzureDnsZoneResource)
            infrastructure.AspireResource;
        ValidateExistingZoneIdentity(resource);
        var zone =
            AzureProvisioningResource
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
        AddIdOutput(infrastructure, zone.Id);
        infrastructure.Add(new ProvisioningOutput(
            AzureDnsZoneResource.NameOutputName,
            typeof(string))
        {
            Value = zone.Name,
        });
    }

    private static void ConfigureARecord(
        AzureResourceInfrastructure infrastructure)
    {
        var resource = (AzureDnsARecordResource)
            infrastructure.AspireResource;
        resource.ApplyAzureDnsZoneScope(infrastructure);
        var target = AddParameter<IPAddress>(
            infrastructure,
            TargetParameterName);
        var zone = (DnsZone)resource.Parent
            .AddAsExistingResource(infrastructure);
        var record = new DnsARecord(
            Infrastructure.NormalizeBicepIdentifier(resource.Name))
        {
            Parent = zone,
            Name = resource.RelativeName.Value,
            TtlInSeconds = DefaultTtlSeconds,
            ARecords =
            {
                new DnsARecordInfo
                {
                    Ipv4Address = target,
                },
            },
        };
        infrastructure.Add(record);
        AddIdOutput(infrastructure, record.Id);
    }

    private static void ConfigureCnameRecord(
        AzureResourceInfrastructure infrastructure)
    {
        var resource = (AzureDnsCnameRecordResource)
            infrastructure.AspireResource;
        resource.ApplyAzureDnsZoneScope(infrastructure);
        var target = AddParameter<string>(
            infrastructure,
            TargetParameterName);
        var zone = (DnsZone)resource.Parent
            .AddAsExistingResource(infrastructure);
        var record = new DnsCnameRecord(
            Infrastructure.NormalizeBicepIdentifier(resource.Name))
        {
            Parent = zone,
            Name = resource.RelativeName.Value,
            TtlInSeconds = DefaultTtlSeconds,
            Cname = target,
        };
        infrastructure.Add(record);
        AddIdOutput(infrastructure, record.Id);
    }

    private static void ConfigureTxtRecord(
        AzureResourceInfrastructure infrastructure)
    {
        var resource = (AzureDnsTxtRecordResource)
            infrastructure.AspireResource;
        resource.ApplyAzureDnsZoneScope(infrastructure);
        var zone = (DnsZone)resource.Parent
            .AddAsExistingResource(infrastructure);
        var record = new DnsTxtRecord(
            Infrastructure.NormalizeBicepIdentifier(resource.Name))
        {
            Parent = zone,
            Name = resource.RelativeName.Value,
            TtlInSeconds = DefaultTtlSeconds,
        };
        foreach (var value in resource.Annotations
            .OfType<AzureDnsTxtValueAnnotation>())
        {
            var parameter = AddParameter<string>(
                infrastructure,
                value.ParameterName);
            record.TxtRecords.Add(new DnsTxtRecordInfo
            {
                Values = { parameter },
            });
        }

        infrastructure.Add(record);
        AddIdOutput(infrastructure, record.Id);
    }

    private static IResourceBuilder<T> WithParameterValue<T>(
        this IResourceBuilder<T> resource,
        string name,
        object value,
        ParameterValueKind valueKind)
        where T : AzureBicepResource =>
        valueKind switch
        {
            ParameterValueKind.String =>
                resource.WithParameter(name, (string)value),
            ParameterValueKind.Parameter =>
                resource.WithParameter(
                    name,
                    (IResourceBuilder<ParameterResource>)value),
            ParameterValueKind.Output =>
                resource.WithParameter(
                    name,
                    (BicepOutputReference)value),
            ParameterValueKind.Expression =>
                resource.WithParameter(
                    name,
                    (ReferenceExpression)value),
            _ => throw new InvalidOperationException(),
        };

    private static ParameterValueKind ValidateParameterValue(
        object value,
        string parameterName) =>
        value switch
        {
            string => ParameterValueKind.String,
            IResourceBuilder<ParameterResource> =>
                ParameterValueKind.Parameter,
            BicepOutputReference => ParameterValueKind.Output,
            ReferenceExpression => ParameterValueKind.Expression,
            _ => throw new ArgumentException(
                $"Unsupported Bicep parameter value '{value.GetType().Name}'.",
                parameterName),
        };

    private static IResourceBuilder<AzureDnsTxtRecordResource>
        AddTxtValue(
            IResourceBuilder<AzureDnsTxtRecordResource> record,
            object value,
            ParameterValueKind valueKind)
    {
        var parameterName =
            $"value{record.Resource.Annotations.OfType<AzureDnsTxtValueAnnotation>().Count()}";
        return record
            .WithParameterValue(
                parameterName,
                value,
                valueKind)
            .WithAnnotation(
                new AzureDnsTxtValueAnnotation(parameterName),
                ResourceAnnotationMutationBehavior.Append);
    }

    private static void ValidateAddressLiteral(object address)
    {
        if (address is string value &&
            (!IPAddress.TryParse(value, out var parsed) ||
                parsed.AddressFamily is not
                    AddressFamily.InterNetwork))
        {
            throw new ArgumentException(
                $"'{value}' is not an IPv4 address.",
                nameof(address));
        }
    }

    internal static void ValidateExistingZoneIdentity(
        AzureDnsZoneResource zone)
    {
        var annotation = zone.Annotations
            .OfType<ExistingAzureResourceAnnotation>()
            .LastOrDefault();
        if (annotation is null)
        {
            return;
        }

        if (annotation.IsTenantScope ||
            annotation.Subscription is not null &&
            annotation.ResourceGroup is null)
        {
            throw new InvalidOperationException(
                "Azure DNS zones cannot use subscription or tenant scope.");
        }

        if (annotation.Name is not string name)
        {
            throw new InvalidOperationException(
                "Parameterized existing Azure DNS zone names are not supported.");
        }

        if (!string.Equals(
            name,
            zone.ZoneName,
            StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Existing Azure DNS zone name must be '{zone.ZoneName}'.");
        }
    }

    private static void ValidateHostnameLength(
        AzureDnsZoneResource zone,
        DnsRelativeName relativeName)
    {
        var hostname = relativeName.ToHostname(zone.ZoneName);
        if (hostname.Length > DnsRelativeName.MaxNameLength)
        {
            throw new ArgumentException(
                $"'{hostname}' exceeds the DNS name length limit.",
                nameof(relativeName));
        }
    }

    private static ProvisioningParameter AddParameter<T>(
        Infrastructure infrastructure,
        string name)
    {
        var parameter = new ProvisioningParameter(name, typeof(T));
        infrastructure.Add(parameter);
        return parameter;
    }

    private static void AddIdOutput(
        Infrastructure infrastructure,
        BicepValue<ResourceIdentifier> id) =>
        infrastructure.Add(new ProvisioningOutput(
            "id",
            typeof(string))
        {
            Value = id,
        });

    private enum ParameterValueKind
    {
        String,
        Parameter,
        Output,
        Expression,
    }
}

#pragma warning restore AZPROVISION001
