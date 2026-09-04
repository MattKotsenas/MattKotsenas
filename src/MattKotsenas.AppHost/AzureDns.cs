using System.Net;

using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using Azure.Provisioning;
using Azure.Provisioning.Dns;

namespace MattKotsenas.AppHost;

internal sealed class AzureDnsZoneResource(
    string name,
    string zoneName)
    : Resource(name)
{
    internal string ZoneName { get; } = zoneName;
}

internal interface IAzureDnsRecordResource
    : IResourceWithParent<AzureDnsZoneResource>
{
    DnsRelativeName RelativeName { get; }

    string Hostname { get; }

    AzureDnsRecordKind RecordKind { get; }
}

internal interface IAzureDnsRoutingRecordResource
    : IAzureDnsRecordResource;

internal enum AzureDnsRecordKind
{
    A,
    Cname,
    Txt,
}

internal sealed class AzureDnsARecordResource(
    string name,
    DnsRelativeName relativeName,
    AzureDnsZoneResource parent,
    Action<AzureResourceInfrastructure> configure)
    : AzureProvisioningResource(
        name,
        configure),
      IAzureDnsRoutingRecordResource
{
    public AzureDnsZoneResource Parent { get; } = parent;

    public DnsRelativeName RelativeName { get; } = relativeName;

    public string Hostname => RelativeName.ToHostname(Parent.ZoneName);

    public AzureDnsRecordKind RecordKind => AzureDnsRecordKind.A;
}

internal sealed class AzureDnsCnameRecordResource(
    string name,
    DnsRelativeName relativeName,
    AzureDnsZoneResource parent,
    Action<AzureResourceInfrastructure> configure)
    : AzureProvisioningResource(
        name,
        configure),
      IAzureDnsRoutingRecordResource
{
    public AzureDnsZoneResource Parent { get; } = parent;

    public DnsRelativeName RelativeName { get; } = relativeName;

    public string Hostname => RelativeName.ToHostname(Parent.ZoneName);

    public AzureDnsRecordKind RecordKind =>
        AzureDnsRecordKind.Cname;
}

internal sealed class AzureDnsTxtRecordResource(
    string name,
    DnsRelativeName relativeName,
    AzureDnsZoneResource parent,
    Action<AzureResourceInfrastructure> configure)
    : AzureProvisioningResource(
        name,
        configure),
      IAzureDnsRecordResource
{
    public AzureDnsZoneResource Parent { get; } = parent;

    public DnsRelativeName RelativeName { get; } = relativeName;

    public string Hostname => RelativeName.ToHostname(Parent.ZoneName);

    public AzureDnsRecordKind RecordKind => AzureDnsRecordKind.Txt;
}

internal sealed class AzureDnsDynamicTxtRecordResource(
    string name,
    DnsRelativeName relativeName,
    AzureDnsZoneResource parent)
    : Resource(name),
      IAzureDnsRecordResource
{
    public AzureDnsZoneResource Parent { get; } = parent;

    public DnsRelativeName RelativeName { get; } = relativeName;

    public string Hostname => RelativeName.ToHostname(Parent.ZoneName);

    public AzureDnsRecordKind RecordKind => AzureDnsRecordKind.Txt;
}

internal sealed record DnsRelativeName
{
    private DnsRelativeName(string value)
    {
        Value = value;
    }

    internal string Value { get; }

    internal bool IsApex => Value == "@";

    internal static DnsRelativeName Apex { get; } = new("@");

    internal static DnsRelativeName From(string value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length > 253 ||
            value.StartsWith('.') ||
            value.EndsWith('.') ||
            value.Split('.').Any(label =>
                label.Length is 0 or > 63 ||
                label.Any(character =>
                    !char.IsAsciiLetterOrDigit(character) &&
                    character is not '-' and not '_' and not '*')))
        {
            throw new ArgumentException(
                $"'{value}' is not a relative DNS name.",
                nameof(value));
        }

        return new(value.ToLowerInvariant());
    }

    internal string ToHostname(string zoneName) =>
        IsApex ? zoneName : $"{Value}.{zoneName}";

    public override string ToString() => Value;
}

internal sealed record AzureDnsTxtValueAnnotation(
    string ParameterName)
    : IResourceAnnotation;

// Azure.Provisioning.Dns is prerelease and marks its entire API as experimental.
#pragma warning disable AZPROVISION001

internal static class AzureDnsResourceExtensions
{
    private const int TtlInSeconds = 3600;
    private const string TargetParameterName = "target";

    internal static IResourceBuilder<AzureDnsZoneResource>
        AddAzureDnsZone(
            this IDistributedApplicationBuilder builder,
            string name,
            string zoneName,
            string resourceGroup)
    {
        if (Uri.CheckHostName(zoneName) is not UriHostNameType.Dns)
        {
            throw new ArgumentException(
                $"'{zoneName}' is not a DNS zone name.",
                nameof(zoneName));
        }

        var subscription = builder.Configuration[
            "Azure:SubscriptionId"]
            ?? throw new InvalidOperationException(
                "Azure:SubscriptionId is required.");
        var normalizedZoneName = zoneName.ToLowerInvariant();
        if (builder.Resources
            .OfType<AzureDnsZoneResource>()
            .Any(zone =>
            {
                var existing = zone.Annotations
                    .OfType<ExistingAzureResourceAnnotation>()
                    .Single();
                return string.Equals(
                        zone.ZoneName,
                        normalizedZoneName,
                        StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(
                        existing.ResourceGroup as string,
                        resourceGroup,
                        StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(
                        existing.Subscription as string,
                        subscription,
                        StringComparison.OrdinalIgnoreCase);
            }))
        {
            throw new InvalidOperationException(
                $"Azure DNS zone '{normalizedZoneName}' is already registered from resource group '{resourceGroup}'.");
        }

        var zone = new AzureDnsZoneResource(
            name,
            normalizedZoneName);

        return builder
            .AddResource(zone)
            .WithAnnotation(
                new ExistingAzureResourceAnnotation(
                    zone.ZoneName,
                    resourceGroup,
                    subscription))
            .ExcludeFromManifest();
    }

    internal static IResourceBuilder<AzureDnsARecordResource>
        AddARecord(
            this IResourceBuilder<AzureDnsZoneResource> zone,
            string name,
            DnsRelativeName relativeName,
            object address)
    {
        EnsureRecordAvailable(
            zone.ApplicationBuilder,
            zone.Resource,
            relativeName,
            AzureDnsRecordKind.A);
        var record = zone.ApplicationBuilder.AddResource(
            new AzureDnsARecordResource(
                name,
                relativeName,
                zone.Resource,
                ConfigureARecord));
        ConfigureScope(record, zone.Resource);
        return WithParameterValue(
            record,
            TargetParameterName,
            address);
    }

    internal static IResourceBuilder<AzureDnsCnameRecordResource>
        AddCnameRecord(
            this IResourceBuilder<AzureDnsZoneResource> zone,
            string name,
            string relativeName,
            object target) =>
        zone.AddCnameRecord(
            name,
            DnsRelativeName.From(relativeName),
            target);

    internal static IResourceBuilder<AzureDnsCnameRecordResource>
        AddCnameRecord(
            this IResourceBuilder<AzureDnsZoneResource> zone,
            string name,
            DnsRelativeName relativeName,
            object target)
    {
        EnsureRecordAvailable(
            zone.ApplicationBuilder,
            zone.Resource,
            relativeName,
            AzureDnsRecordKind.Cname);
        var record = zone.ApplicationBuilder.AddResource(
            new AzureDnsCnameRecordResource(
                name,
                relativeName,
                zone.Resource,
                ConfigureCnameRecord));
        ConfigureScope(record, zone.Resource);
        return WithParameterValue(
            record,
            TargetParameterName,
            target);
    }

    internal static IResourceBuilder<AzureDnsTxtRecordResource>
        AddTxtRecord(
            this IResourceBuilder<AzureDnsZoneResource> zone,
            string name,
            DnsRelativeName relativeName) =>
        AddTxtRecord(
            zone.ApplicationBuilder,
            zone.Resource,
            name,
            relativeName);

    internal static IResourceBuilder<AzureDnsTxtRecordResource>
        AddTxtRecord(
            this IDistributedApplicationBuilder builder,
            AzureDnsZoneResource zone,
            string name,
            DnsRelativeName relativeName)
    {
        EnsureRecordAvailable(
            builder,
            zone,
            relativeName,
            AzureDnsRecordKind.Txt);
        var record = builder.AddResource(
            new AzureDnsTxtRecordResource(
                name,
                relativeName,
                zone,
                ConfigureTxtRecord));
        ConfigureScope(record, zone);
        return record;
    }

    internal static IResourceBuilder<AzureDnsTxtRecordResource>
        WithValue(
            this IResourceBuilder<AzureDnsTxtRecordResource> record,
            object value)
    {
        var parameterName =
            $"value{record.Resource.Annotations.OfType<AzureDnsTxtValueAnnotation>().Count()}";
        return record
            .WithParameterValue(parameterName, value)
            .WithAnnotation(
                new AzureDnsTxtValueAnnotation(parameterName),
                ResourceAnnotationMutationBehavior.Append);
    }

    internal static IResourceBuilder<
        AzureDnsDynamicTxtRecordResource>
        AddDynamicTxtRecord(
            this IResourceBuilder<AzureDnsZoneResource> zone,
            string name,
            DnsRelativeName relativeName) =>
        AddDynamicTxtRecord(
            zone.ApplicationBuilder,
            zone.Resource,
            name,
            relativeName);

    internal static IResourceBuilder<
        AzureDnsDynamicTxtRecordResource>
        AddDynamicTxtRecord(
            this IDistributedApplicationBuilder builder,
            AzureDnsZoneResource zone,
            string name,
            DnsRelativeName relativeName)
    {
        EnsureRecordAvailable(
            builder,
            zone,
            relativeName,
            AzureDnsRecordKind.Txt);
        return builder
            .AddResource(
                new AzureDnsDynamicTxtRecordResource(
                    name,
                    relativeName,
                    zone))
            .ExcludeFromManifest();
    }

    internal static void ConfigureARecord(
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

    internal static void ConfigureCnameRecord(
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

    internal static void ConfigureTxtRecord(
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
            .Single();
        record.Resource.Scope =
            existing.Subscription is null
                ? new(existing.ResourceGroup!)
                : new(
                    existing.ResourceGroup!,
                    existing.Subscription);
    }

    private static void EnsureRecordAvailable(
        IDistributedApplicationBuilder builder,
        AzureDnsZoneResource zone,
        DnsRelativeName relativeName,
        AzureDnsRecordKind recordKind)
    {
        var existing = builder.Resources
            .OfType<IAzureDnsRecordResource>()
            .Where(record =>
                ReferenceEquals(record.Parent, zone) &&
                record.RelativeName == relativeName)
            .ToArray();
        if (existing.Any(record =>
                record.RecordKind == recordKind) ||
            existing.Length > 0 &&
            (recordKind is AzureDnsRecordKind.Cname ||
                existing.Any(record =>
                    record.RecordKind is
                        AzureDnsRecordKind.Cname)))
        {
            throw new InvalidOperationException(
                $"Azure DNS record '{relativeName}.{zone.ZoneName}' already has a conflicting {recordKind} writer.");
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

    private static IResourceBuilder<T> WithParameterValue<T>(
        this IResourceBuilder<T> resource,
        string name,
        object value)
        where T : AzureBicepResource =>
        value switch
        {
            string text =>
                resource.WithParameter(name, text),
            IResourceBuilder<ParameterResource> parameter =>
                resource.WithParameter(name, parameter),
            BicepOutputReference output =>
                resource.WithParameter(name, output),
            ReferenceExpression expression =>
                resource.WithParameter(name, expression),
            _ => throw new ArgumentException(
                $"Unsupported Bicep parameter value '{value.GetType().Name}'.",
                nameof(value)),
        };

    private static void AddIdOutput(
        Infrastructure infrastructure,
        BicepValue<Azure.Core.ResourceIdentifier> id) =>
        infrastructure.Add(new ProvisioningOutput(
            "id",
            typeof(string))
        {
            Value = id,
        });
}

#pragma warning restore AZPROVISION001
