using Aspire.Hosting.ApplicationModel;

namespace MattKotsenas.AppHost;

internal sealed class BlogCustomDomainResource(
    string name,
    string hostname,
    BlogDomainZone zone,
    string dnsRecordName,
    string ownershipRecordName,
    string validationRecordName,
    string certificateName,
    string dnsBicepIdentifier,
    string certificateBicepIdentifier)
    : Resource(name)
{
    internal string Hostname { get; } = hostname;

    internal BlogDomainZone Zone { get; } = zone;

    internal string DnsRecordName { get; } = dnsRecordName;

    internal string OwnershipRecordName { get; } =
        ownershipRecordName;

    internal string ValidationRecordName { get; } =
        validationRecordName;

    internal string CertificateName { get; } = certificateName;

    internal string DnsBicepIdentifier { get; } =
        dnsBicepIdentifier;

    internal string CertificateBicepIdentifier { get; } =
        certificateBicepIdentifier;

    internal bool IsApex => DnsRecordName == "@";

    internal string RecoverStepName => $"recover-{Name}";

    internal string PublishValidationStepName =>
        $"publish-validation-{Name}";

    internal string VerifyStepName => $"verify-{Name}";

    internal string CheckHttpsStepName => $"check-https-{Name}";

    internal static IReadOnlyList<BlogCustomDomainResource>
        CreateDefaults(string parentName) =>
        DefaultZones
            .SelectMany(zone => CreateDomains(parentName, zone))
            .ToArray();

    private static IReadOnlyList<BlogDomainZone> DefaultZones { get; } =
    [
        new(
            Name: "kotsenas.com",
            BicepIdentifier: "root",
            IsRoot: true),
        new(
            Name: "matt.kotsenas.com",
            BicepIdentifier: "blog",
            IsRoot: false),
    ];

    private static IEnumerable<BlogCustomDomainResource> CreateDomains(
        string parentName,
        BlogDomainZone zone)
    {
        yield return Create(parentName, zone, isWww: false);
        yield return Create(parentName, zone, isWww: true);
    }

    private static BlogCustomDomainResource Create(
        string parentName,
        BlogDomainZone zone,
        bool isWww)
    {
        var hostname = isWww
            ? $"www.{zone.Name}"
            : zone.Name;
        return new(
            name:
                $"{parentName}-domain-{hostname.Replace('.', '-')}",
            hostname,
            zone,
            dnsRecordName: isWww ? "www" : "@",
            ownershipRecordName:
                isWww ? "asuid.www" : "asuid",
            validationRecordName:
                isWww ? "_dnsauth.www" : "_dnsauth",
            certificateName:
                $"managed-{hostname.Replace('.', '-')}",
            dnsBicepIdentifier:
                $"{zone.BicepIdentifier}{(isWww ? "Www" : "Apex")}",
            certificateBicepIdentifier:
                $"{zone.BicepIdentifier}{(isWww ? "Www" : string.Empty)}Certificate");
    }
}

internal sealed record BlogDomainZone(
    string Name,
    string BicepIdentifier,
    bool IsRoot);

internal static class BlogCustomDomainResourceExtensions
{
    internal static IReadOnlyList<
        IResourceBuilder<BlogCustomDomainResource>>
        AddBlogCustomDomains<T>(
            this IResourceBuilder<T> blog,
            string azureSubscriptionId,
            string azureResourceGroup,
            string dnsResourceGroup)
        where T : IResource
    {
        var domains = BlogCustomDomainResource
            .CreateDefaults(blog.Resource.Name)
            .Select(domain => blog.ApplicationBuilder
                .AddResource(domain)
                .WithParentRelationship(blog.Resource)
                .ExcludeFromManifest())
            .ToArray();

        blog.WithBlogCustomDomainPipeline(
            domains,
            azureSubscriptionId,
            azureResourceGroup,
            dnsResourceGroup);

        return domains;
    }
}
