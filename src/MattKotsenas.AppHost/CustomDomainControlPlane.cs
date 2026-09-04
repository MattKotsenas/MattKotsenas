using System.Net;

using Azure.Core;

namespace MattKotsenas.AppHost;

internal interface IContainerAppControlPlane
{
    Task<ContainerAppSnapshot?> GetAppAsync(
        ResourceIdentifier appId,
        CancellationToken cancellationToken);

    Task<ContainerAppEnvironmentSnapshot> GetEnvironmentAsync(
        ResourceIdentifier environmentId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ManagedCertificateSnapshot>>
        GetManagedCertificatesAsync(
            ResourceIdentifier environmentId,
            CancellationToken cancellationToken);

    Task DeleteManagedCertificateAsync(
        ResourceIdentifier certificateId,
        CancellationToken cancellationToken);
}

internal interface IDnsValidationRecords
{
    Task<bool> HasAnyValueAsync(
        DnsTxtRecordKey key,
        CancellationToken cancellationToken);

    Task EnsureValueAsync(
        DnsTxtRecordKey key,
        string value,
        TimeSpan defaultTtl,
        CancellationToken cancellationToken);

    Task RemoveValueAsync(
        DnsTxtRecordKey key,
        string value,
        bool keepEmptyRecordSet,
        CancellationToken cancellationToken);
}

internal interface IHttpsEndpointProbe
{
    Task<bool> IsHealthyAsync(
        string hostname,
        IPAddress address,
        CancellationToken cancellationToken);
}

internal sealed record ContainerAppSnapshot(
    ResourceIdentifier EnvironmentId,
    IReadOnlyList<CustomDomainBindingSnapshot> CustomDomains);

internal sealed record CustomDomainBindingSnapshot(
    string Hostname,
    CustomDomainBindingKind BindingKind,
    ResourceIdentifier? CertificateId);

internal enum CustomDomainBindingKind
{
    Disabled,
    Auto,
    SniEnabled,
    Unknown,
}

internal sealed record ContainerAppEnvironmentSnapshot(
    IPAddress StaticIp);

internal sealed record ManagedCertificateSnapshot(
    ResourceIdentifier Id,
    ManagedCertificateState State,
    string? ValidationToken,
    string? Error);

internal enum ManagedCertificateState
{
    Pending,
    Succeeded,
    Failed,
    Canceled,
    Deleting,
    DeleteFailed,
    Unknown,
}

internal sealed record DnsTxtRecordKey(
    string SubscriptionId,
    string ResourceGroup,
    string Zone,
    string RelativeName);
