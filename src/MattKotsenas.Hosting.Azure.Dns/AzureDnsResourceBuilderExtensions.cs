using System.Net;

using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using Azure.Provisioning;
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
        AddOutput(infrastructure, "id", zone.Id);
        infrastructure.Add(new ProvisioningOutput(
            "name",
            typeof(string))
        {
            Value = zone.Name,
        });
    }

    /// <summary>Adds an Azure DNS A record.</summary>
    public static IResourceBuilder<AzureDnsARecordResource> AddARecord(
        this IResourceBuilder<AzureDnsZoneResource> zone,
        string name,
        string relativeName,
        object address,
        TimeSpan? timeToLive = null)
    {
        relativeName = NormalizeRelativeName(relativeName);
        ThrowIfDuplicate<AzureDnsARecordResource>(zone, relativeName);
        var record = zone.ApplicationBuilder.AddResource(
            new AzureDnsARecordResource(
                name,
                relativeName,
                zone.Resource,
                ValidateTtl(timeToLive),
                ConfigureARecord));
        return SetParameter(record, "target", address);
    }

    /// <summary>Adds an Azure DNS CNAME record.</summary>
    public static IResourceBuilder<AzureDnsCnameRecordResource>
        AddCnameRecord(
            this IResourceBuilder<AzureDnsZoneResource> zone,
            string name,
            string relativeName,
            object target,
            TimeSpan? timeToLive = null)
    {
        relativeName = NormalizeRelativeName(relativeName);
        ThrowIfDuplicate<AzureDnsCnameRecordResource>(zone, relativeName);
        var record = zone.ApplicationBuilder.AddResource(
            new AzureDnsCnameRecordResource(
                name,
                relativeName,
                zone.Resource,
                ValidateTtl(timeToLive),
                ConfigureCnameRecord));
        return SetParameter(record, "target", target);
    }

    /// <summary>Adds an Azure DNS TXT record.</summary>
    public static IResourceBuilder<AzureDnsTxtRecordResource> AddTxtRecord(
        this IResourceBuilder<AzureDnsZoneResource> zone,
        string name,
        string relativeName,
        object value,
        TimeSpan? timeToLive = null)
    {
        relativeName = NormalizeRelativeName(relativeName);
        ThrowIfDuplicate<AzureDnsTxtRecordResource>(zone, relativeName);
        var record = zone.ApplicationBuilder.AddResource(
            new AzureDnsTxtRecordResource(
                name,
                relativeName,
                zone.Resource,
                ValidateTtl(timeToLive),
                ConfigureTxtRecord));
        return record.WithValue(value);
    }

    /// <summary>Adds another TXT value.</summary>
    public static IResourceBuilder<AzureDnsTxtRecordResource> WithValue(
        this IResourceBuilder<AzureDnsTxtRecordResource> record,
        object value)
    {
        var parameterName =
            $"value{record.Resource.Annotations.OfType<AzureDnsTxtValueAnnotation>().Count()}";
        return SetParameter(record, parameterName, value)
            .WithAnnotation(
                new AzureDnsTxtValueAnnotation(parameterName),
                ResourceAnnotationMutationBehavior.Append);
    }

    private static void ConfigureARecord(
        AzureResourceInfrastructure infrastructure)
    {
        var resource = (AzureDnsARecordResource)infrastructure.AspireResource;
        ApplyParentScope(resource);
        var target = AddParameter<IPAddress>(infrastructure, "target");
        var zone = (DnsZone)resource.Parent.AddAsExistingResource(infrastructure);
        var record = new DnsARecord(resource.GetBicepIdentifier())
        {
            Parent = zone,
            Name = resource.RelativeName,
            TtlInSeconds = (int)resource.TimeToLive.TotalSeconds,
            ARecords = { new DnsARecordInfo { Ipv4Address = target } },
        };
        infrastructure.Add(record);
        AddOutput(infrastructure, "id", record.Id);
    }

    private static void ConfigureCnameRecord(
        AzureResourceInfrastructure infrastructure)
    {
        var resource = (AzureDnsCnameRecordResource)infrastructure.AspireResource;
        ApplyParentScope(resource);
        var target = AddParameter<string>(infrastructure, "target");
        var zone = (DnsZone)resource.Parent.AddAsExistingResource(infrastructure);
        var record = new DnsCnameRecord(resource.GetBicepIdentifier())
        {
            Parent = zone,
            Name = resource.RelativeName,
            TtlInSeconds = (int)resource.TimeToLive.TotalSeconds,
            Cname = target,
        };
        infrastructure.Add(record);
        AddOutput(infrastructure, "id", record.Id);
    }

    private static void ConfigureTxtRecord(
        AzureResourceInfrastructure infrastructure)
    {
        var resource = (AzureDnsTxtRecordResource)infrastructure.AspireResource;
        ApplyParentScope(resource);
        var zone = (DnsZone)resource.Parent.AddAsExistingResource(infrastructure);
        var record = new DnsTxtRecord(resource.GetBicepIdentifier())
        {
            Parent = zone,
            Name = resource.RelativeName,
            TtlInSeconds = (int)resource.TimeToLive.TotalSeconds,
        };
        foreach (var value in resource.Annotations.OfType<AzureDnsTxtValueAnnotation>())
        {
            var parameter = AddParameter<string>(infrastructure, value.ParameterName);
            record.TxtRecords.Add(new DnsTxtRecordInfo
            {
                Values = { parameter },
            });
        }
        infrastructure.Add(record);
        AddOutput(infrastructure, "id", record.Id);
    }

    private static string NormalizeRelativeName(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return value.ToLowerInvariant();
    }

    private static void ApplyParentScope(
        AzureBicepResource record)
    {
        var dnsRecord = (AzureDnsRecordResource)record;
        var annotation = dnsRecord.Parent.Annotations
            .OfType<ExistingAzureResourceAnnotation>()
            .LastOrDefault();
        record.Scope = dnsRecord.Parent.Scope ??
            (annotation switch
            {
                {
                    ResourceGroup: not null,
                    Subscription: not null,
                } => new AzureBicepResourceScope(
                    annotation.ResourceGroup,
                    annotation.Subscription),
                { ResourceGroup: not null } =>
                    new AzureBicepResourceScope(
                        annotation.ResourceGroup),
                _ => null,
            });
    }

    private static TimeSpan ValidateTtl(TimeSpan? timeToLive)
    {
        var ttl = timeToLive ?? DefaultTimeToLive;
        if (ttl <= TimeSpan.Zero ||
            ttl.Ticks % TimeSpan.TicksPerSecond != 0 ||
            ttl.TotalSeconds > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(timeToLive));
        }
        return ttl;
    }

    private static void ThrowIfDuplicate<T>(
        IResourceBuilder<AzureDnsZoneResource> zone,
        string relativeName)
        where T : AzureDnsRecordResource
    {
        if (zone.ApplicationBuilder.Resources
            .OfType<T>()
            .Any(record =>
                ReferenceEquals(record.Parent, zone.Resource) &&
                record.RelativeName == relativeName))
        {
            throw new InvalidOperationException(
                $"DNS record '{relativeName}' is already registered.");
        }
    }

    private static IResourceBuilder<T> SetParameter<T>(
        IResourceBuilder<T> resource,
        string name,
        object value)
        where T : AzureBicepResource =>
        value switch
        {
            string text => resource.WithParameter(name, text),
            IResourceBuilder<ParameterResource> parameter =>
                resource.WithParameter(name, parameter),
            BicepOutputReference output =>
                resource.WithParameter(name, output),
            ReferenceExpression expression =>
                resource.WithParameter(name, expression),
            _ => throw new ArgumentException(
                $"Unsupported parameter value '{value.GetType().Name}'.",
                nameof(value)),
        };

    private static ProvisioningParameter AddParameter<T>(
        Infrastructure infrastructure,
        string name)
    {
        var parameter = new ProvisioningParameter(name, typeof(T));
        infrastructure.Add(parameter);
        return parameter;
    }

    private static void AddOutput(
        Infrastructure infrastructure,
        string name,
        BicepValue<ResourceIdentifier> value) =>
        infrastructure.Add(new ProvisioningOutput(name, typeof(string))
        {
            Value = value,
        });
}

#pragma warning restore AZPROVISION001
