using Azure;
using Azure.ResourceManager;
using Azure.ResourceManager.Dns;
using Azure.ResourceManager.Dns.Models;

namespace MattKotsenas.AppHost;

internal sealed class AzureDnsValidationRecords
    : IDnsValidationRecords
{
    private const int MaximumAttempts = 5;
    private readonly Func<
        DnsTxtRecordKey,
        CancellationToken,
        Task<DnsTxtRecordData?>> get;
    private readonly Func<
        DnsTxtRecordKey,
        DnsTxtRecordData,
        CancellationToken,
        Task> create;
    private readonly Func<
        DnsTxtRecordKey,
        DnsTxtRecordData,
        ETag?,
        CancellationToken,
        Task> update;
    private readonly Func<
        DnsTxtRecordKey,
        ETag?,
        CancellationToken,
        Task> delete;

    public AzureDnsValidationRecords(ArmClient armClient)
        : this(
            (key, cancellationToken) =>
                GetAsync(armClient, key, cancellationToken),
            (key, data, cancellationToken) =>
                CreateAsync(
                    armClient,
                    key,
                    data,
                    cancellationToken),
            (key, data, etag, cancellationToken) =>
                UpdateAsync(
                    armClient,
                    key,
                    data,
                    etag,
                    cancellationToken),
            (key, etag, cancellationToken) =>
                DeleteAsync(
                    armClient,
                    key,
                    etag,
                    cancellationToken))
    {
    }

    internal AzureDnsValidationRecords(
        Func<
            DnsTxtRecordKey,
            CancellationToken,
            Task<DnsTxtRecordData?>> get,
        Func<
            DnsTxtRecordKey,
            DnsTxtRecordData,
            CancellationToken,
            Task> create,
        Func<
            DnsTxtRecordKey,
            DnsTxtRecordData,
            ETag?,
            CancellationToken,
            Task> update,
        Func<
            DnsTxtRecordKey,
            ETag?,
            CancellationToken,
            Task> delete)
    {
        this.get = get;
        this.create = create;
        this.update = update;
        this.delete = delete;
    }

    public async Task<bool> HasAnyValueAsync(
        DnsTxtRecordKey key,
        CancellationToken cancellationToken)
    {
        var record = await get(key, cancellationToken);
        return record?.DnsTxtRecords
            .SelectMany(value => value.Values)
            .Any(value => !string.IsNullOrWhiteSpace(value))
            is true;
    }

    public Task EnsureValueAsync(
        DnsTxtRecordKey key,
        string value,
        TimeSpan defaultTtl,
        CancellationToken cancellationToken) =>
        ReconcileAsync(
            key,
            current =>
            {
                if (current is not null &&
                    current.DnsTxtRecords.Any(record =>
                        record.Values.Count == 1 &&
                        record.Values[0] == value))
                {
                    return null;
                }

                var replacement = Copy(current, defaultTtl);
                var record = new DnsTxtRecordInfo();
                record.Values.Add(value);
                replacement.DnsTxtRecords.Add(record);
                return replacement;
            },
            cancellationToken);

    public Task RemoveValueAsync(
        DnsTxtRecordKey key,
        string value,
        bool keepEmptyRecordSet,
        CancellationToken cancellationToken) =>
        ReconcileAsync(
            key,
            current =>
            {
                if (current is null ||
                    !current.DnsTxtRecords.Any(record =>
                        record.Values.Count == 1 &&
                        record.Values[0] == value))
                {
                    return null;
                }

                var replacement = Copy(
                    current,
                    TimeSpan.Zero,
                    value);
                return !keepEmptyRecordSet &&
                    replacement.DnsTxtRecords.Count == 0
                        ? DeleteRecord.Instance
                        : replacement;
            },
            cancellationToken);

    internal static DnsTxtRecordData Copy(
        DnsTxtRecordData? source,
        TimeSpan defaultTtl,
        string? excludedValue = null)
    {
        var replacement = new DnsTxtRecordData
        {
            TtlInSeconds = source?.TtlInSeconds ??
                (long)defaultTtl.TotalSeconds,
        };
        if (source is not null)
        {
            foreach (var metadata in source.Metadata)
            {
                replacement.Metadata.Add(metadata);
            }
        }

        replacement.DnsTxtRecords.Clear();
        foreach (var existing in source?.DnsTxtRecords ?? [])
        {
            if (existing.Values.Count == 1 &&
                existing.Values[0] == excludedValue)
            {
                continue;
            }

            var copy = new DnsTxtRecordInfo();
            foreach (var chunk in existing.Values)
            {
                copy.Values.Add(chunk);
            }

            replacement.DnsTxtRecords.Add(copy);
        }

        return replacement;
    }

    private async Task ReconcileAsync(
        DnsTxtRecordKey key,
        Func<DnsTxtRecordData?, object?> transform,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < MaximumAttempts; attempt++)
        {
            var current = await get(key, cancellationToken);
            var replacement = transform(current);
            if (replacement is null)
            {
                return;
            }

            try
            {
                if (replacement is DeleteRecord)
                {
                    await delete(
                        key,
                        current!.ETag,
                        cancellationToken);
                }
                else if (current is null)
                {
                    await create(
                        key,
                        (DnsTxtRecordData)replacement,
                        cancellationToken);
                }
                else
                {
                    await update(
                        key,
                        (DnsTxtRecordData)replacement,
                        current.ETag,
                        cancellationToken);
                }

                return;
            }
            catch (RequestFailedException exception)
                when (exception.Status is 404 or 412)
            {
                if (attempt == MaximumAttempts - 1)
                {
                    break;
                }
            }
        }

        throw new InvalidOperationException(
            $"TXT record '{key.RelativeName}.{key.Zone}' remained concurrently modified.");
    }

    private static async Task<DnsTxtRecordData?> GetAsync(
        ArmClient armClient,
        DnsTxtRecordKey key,
        CancellationToken cancellationToken)
    {
        try
        {
            return (await GetZone(armClient, key)
                .GetDnsTxtRecordAsync(
                    key.RelativeName,
                    cancellationToken))
                .Value
                .Data;
        }
        catch (RequestFailedException exception)
            when (exception.Status == 404)
        {
            return null;
        }
    }

    private static async Task CreateAsync(
        ArmClient armClient,
        DnsTxtRecordKey key,
        DnsTxtRecordData data,
        CancellationToken cancellationToken)
    {
        await GetZone(armClient, key)
            .GetDnsTxtRecords()
            .CreateOrUpdateAsync(
                WaitUntil.Completed,
                key.RelativeName,
                data,
                ifMatch: null,
                ifNoneMatch: "*",
                cancellationToken);
    }

    private static async Task UpdateAsync(
        ArmClient armClient,
        DnsTxtRecordKey key,
        DnsTxtRecordData data,
        ETag? etag,
        CancellationToken cancellationToken)
    {
        var id = DnsTxtRecordResource.CreateResourceIdentifier(
            key.SubscriptionId,
            key.ResourceGroup,
            key.Zone,
            key.RelativeName);
        await armClient
            .GetDnsTxtRecordResource(id)
            .UpdateAsync(
                data,
                etag,
                cancellationToken);
    }

    private static async Task DeleteAsync(
        ArmClient armClient,
        DnsTxtRecordKey key,
        ETag? etag,
        CancellationToken cancellationToken)
    {
        var id = DnsTxtRecordResource.CreateResourceIdentifier(
            key.SubscriptionId,
            key.ResourceGroup,
            key.Zone,
            key.RelativeName);
        await armClient
            .GetDnsTxtRecordResource(id)
            .DeleteAsync(
                WaitUntil.Completed,
                etag,
                cancellationToken);
    }

    private static DnsZoneResource GetZone(
        ArmClient armClient,
        DnsTxtRecordKey key)
    {
        var id = DnsZoneResource.CreateResourceIdentifier(
            key.SubscriptionId,
            key.ResourceGroup,
            key.Zone);
        return armClient.GetDnsZoneResource(id);
    }

    private sealed class DeleteRecord
    {
        internal static DeleteRecord Instance { get; } = new();
    }
}
