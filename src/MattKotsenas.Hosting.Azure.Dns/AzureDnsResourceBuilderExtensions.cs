using System.Net;
using System.Net.Sockets;

using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
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
    private const int TtlInSeconds = 3600;
    private const string TargetParameterName = "target";

    /// <summary>
    /// Adds an existing Azure DNS zone.
    /// </summary>
    /// <param name="builder">The distributed application builder.</param>
    /// <param name="name">The Aspire resource name.</param>
    /// <param name="zoneName">The fully qualified DNS zone name.</param>
    /// <param name="resourceGroup">
    /// The parameter containing the Azure resource group name.
    /// </param>
    /// <returns>The Azure DNS zone resource builder.</returns>
    public static IResourceBuilder<AzureDnsZoneResource>
        AddAzureDnsZone(
            this IDistributedApplicationBuilder builder,
            string name,
            string zoneName,
            IResourceBuilder<ParameterResource> resourceGroup)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(resourceGroup);
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

        var zone = builder
            .AddResource(
                new AzureDnsZoneResource(
                    name,
                    normalizedZoneName))
            .WithAnnotation(
                new ExistingAzureResourceAnnotation(
                    normalizedZoneName,
                    resourceGroup.Resource))
            .ExcludeFromManifest();
        builder.AddAzureProvisioning();
        return zone;
    }

    /// <summary>
    /// Adds an Azure DNS A record.
    /// </summary>
    /// <param name="zone">The parent DNS zone.</param>
    /// <param name="name">The Aspire resource name.</param>
    /// <param name="relativeName">The record name relative to the zone.</param>
    /// <param name="address">The IPv4 address or Aspire value reference.</param>
    /// <returns>The Azure DNS A record resource builder.</returns>
    public static IResourceBuilder<AzureDnsARecordResource>
        AddARecord(
            this IResourceBuilder<AzureDnsZoneResource> zone,
            string name,
            DnsRelativeName relativeName,
            object address)
    {
        ArgumentNullException.ThrowIfNull(zone);
        ArgumentNullException.ThrowIfNull(relativeName);
        ArgumentNullException.ThrowIfNull(address);
        ValidateAddressLiteral(address);
        var valueKind = ValidateParameterValue(
            address,
            nameof(address));
        EnsureRecordAvailable<AzureDnsARecordResource>(
            zone.ApplicationBuilder,
            zone.Resource,
            relativeName);
        var record = zone.ApplicationBuilder.AddResource(
            new AzureDnsARecordResource(
                name,
                relativeName,
                zone.Resource,
                ConfigureARecord));
        ConfigureScope(record, zone.Resource);
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
        ArgumentNullException.ThrowIfNull(relativeName);
        ArgumentNullException.ThrowIfNull(target);
        if (relativeName.IsApex)
        {
            throw new ArgumentException(
                "A CNAME record cannot be created at the zone apex.",
                nameof(relativeName));
        }

        var valueKind = ValidateParameterValue(
            target,
            nameof(target));
        EnsureRecordAvailable<AzureDnsCnameRecordResource>(
            zone.ApplicationBuilder,
            zone.Resource,
            relativeName);
        var record = zone.ApplicationBuilder.AddResource(
            new AzureDnsCnameRecordResource(
                name,
                relativeName,
                zone.Resource,
                ConfigureCnameRecord));
        ConfigureScope(record, zone.Resource);
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
    public static IResourceBuilder<AzureDnsTxtRecordResource>
        AddTxtRecord(
            this IResourceBuilder<AzureDnsZoneResource> zone,
            string name,
            DnsRelativeName relativeName,
            object value)
    {
        ArgumentNullException.ThrowIfNull(zone);
        ArgumentNullException.ThrowIfNull(relativeName);
        ArgumentNullException.ThrowIfNull(value);
        var valueKind = ValidateParameterValue(
            value,
            nameof(value));
        EnsureRecordAvailable<AzureDnsTxtRecordResource>(
            zone.ApplicationBuilder,
            zone.Resource,
            relativeName);
        var record = zone.ApplicationBuilder.AddResource(
            new AzureDnsTxtRecordResource(
                name,
                relativeName,
                zone.Resource,
                ConfigureTxtRecord));
        ConfigureScope(record, zone.Resource);
        return AddTxtValue(record, value, valueKind);
    }

    /// <summary>
    /// Adds another value to an Azure DNS TXT record.
    /// </summary>
    /// <param name="record">The TXT record resource builder.</param>
    /// <param name="value">The TXT value or Aspire value reference.</param>
    /// <returns>The Azure DNS TXT record resource builder.</returns>
    public static IResourceBuilder<AzureDnsTxtRecordResource>
        WithValue(
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

    private static void ConfigureARecord(
        AzureResourceInfrastructure infrastructure)
    {
        var resource = (AzureDnsARecordResource)
            infrastructure.AspireResource;
        var target = AddParameter<IPAddress>(
            infrastructure,
            TargetParameterName);
        var zone = AddExistingZone(infrastructure, resource.Parent);
        var record = new DnsARecord(
            Infrastructure.NormalizeBicepIdentifier(resource.Name))
        {
            Parent = zone,
            Name = resource.RelativeName.Value,
            TtlInSeconds = TtlInSeconds,
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
        var target = AddParameter<string>(
            infrastructure,
            TargetParameterName);
        var zone = AddExistingZone(infrastructure, resource.Parent);
        var record = new DnsCnameRecord(
            Infrastructure.NormalizeBicepIdentifier(resource.Name))
        {
            Parent = zone,
            Name = resource.RelativeName.Value,
            TtlInSeconds = TtlInSeconds,
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
        var zone = AddExistingZone(infrastructure, resource.Parent);
        var record = new DnsTxtRecord(
            Infrastructure.NormalizeBicepIdentifier(resource.Name))
        {
            Parent = zone,
            Name = resource.RelativeName.Value,
            TtlInSeconds = TtlInSeconds,
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

    private static DnsZone AddExistingZone(
        Infrastructure infrastructure,
        AzureDnsZoneResource resource)
    {
        var zone = DnsZone.FromExisting(
            Infrastructure.NormalizeBicepIdentifier(resource.Name));
        zone.Name = resource.ZoneName;
        infrastructure.Add(zone);
        return zone;
    }

    private static void ConfigureScope<T>(
        IResourceBuilder<T> record,
        AzureDnsZoneResource zone)
        where T : AzureBicepResource
    {
        var existing = zone.Annotations
            .OfType<ExistingAzureResourceAnnotation>()
            .LastOrDefault()
            ?? throw new InvalidOperationException(
                $"Azure DNS zone '{zone.Name}' has no existing-resource annotation.");
        record.Resource.Scope =
            new AzureBicepResourceScope(
                existing.ResourceGroup!);
    }

    private static void EnsureRecordAvailable<TRecord>(
        IDistributedApplicationBuilder builder,
        AzureDnsZoneResource zone,
        DnsRelativeName relativeName)
        where TRecord : AzureProvisioningResource
    {
        var hostname = relativeName.ToHostname(zone.ZoneName);
        if (hostname.Length > 253)
        {
            throw new ArgumentException(
                $"'{hostname}' exceeds the DNS name length limit.",
                nameof(relativeName));
        }

        var existing = builder.Resources
            .OfType<IAzureDnsRecordResource>()
            .Where(record =>
                ReferenceEquals(record.Parent, zone) &&
                record.RelativeName == relativeName)
            .ToArray();
        var sameKind = existing.FirstOrDefault(record =>
            record is TRecord);
        if (sameKind is not null)
        {
            ThrowRecordConflict(hostname, sameKind);
        }

        if (typeof(TRecord) ==
                typeof(AzureDnsCnameRecordResource) &&
            existing.FirstOrDefault() is { } existingRecord)
        {
            ThrowRecordConflict(
                hostname,
                existingRecord);
        }

        if (existing
            .OfType<AzureDnsCnameRecordResource>()
            .FirstOrDefault() is { } existingCname)
        {
            ThrowRecordConflict(
                hostname,
                existingCname);
        }
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
        BicepValue<global::Azure.Core.ResourceIdentifier> id) =>
        infrastructure.Add(new ProvisioningOutput(
            "id",
            typeof(string))
        {
            Value = id,
        });

    private static void ThrowRecordConflict(
        string hostname,
        IAzureDnsRecordResource conflict) =>
        throw new InvalidOperationException(
            $"Azure DNS record '{hostname}' is already written by resource '{conflict.Name}'.");

    private enum ParameterValueKind
    {
        String,
        Parameter,
        Output,
        Expression,
    }
}

#pragma warning restore AZPROVISION001
