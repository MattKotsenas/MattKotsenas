namespace MattKotsenas.AppHost;

internal static class BlogCustomDomains
{
    internal static BlogDomainZone RootZone { get; } =
        new(
            Name: "kotsenas.com",
            BicepIdentifier: "root");

    internal static IReadOnlyList<BlogDomainZone> Zones { get; } =
    [
        RootZone,
        new(
            Name: "matt.kotsenas.com",
            BicepIdentifier: "blog"),
    ];

    internal static IReadOnlyList<BlogCustomDomain> All { get; } =
        Zones.SelectMany(CreateDomains).ToArray();

    private static IEnumerable<BlogCustomDomain> CreateDomains(
        BlogDomainZone zone)
    {
        yield return CreateDomain(zone, isWww: false);
        yield return CreateDomain(zone, isWww: true);
    }

    private static BlogCustomDomain CreateDomain(
        BlogDomainZone zone,
        bool isWww)
    {
        var hostname = isWww
            ? $"www.{zone.Name}"
            : zone.Name;
        return new(
            Hostname: hostname,
            Zone: zone,
            ValidationRecordName:
                isWww ? "_dnsauth.www" : "_dnsauth",
            CertificateName:
                $"managed-{hostname.Replace('.', '-')}",
            BicepIdentifier:
                $"{zone.BicepIdentifier}{(isWww ? "Www" : string.Empty)}Certificate");
    }
}

internal sealed record BlogDomainZone(
    string Name,
    string BicepIdentifier);

internal sealed record BlogCustomDomain(
    string Hostname,
    BlogDomainZone Zone,
    string ValidationRecordName,
    string CertificateName,
    string BicepIdentifier);
