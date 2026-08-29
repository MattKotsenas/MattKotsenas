using Azure.Provisioning;
using Azure.Provisioning.AppService;

namespace MattKotsenas.AppHost;

internal static class LegacyWebAppInfrastructure
{
    public static void Configure(Infrastructure infrastructure)
    {
        ArgumentNullException.ThrowIfNull(infrastructure);

        var website = WebSite.FromExisting("website");
        website.Name = "mattkotsenas";
        infrastructure.Add(website);

        infrastructure.Add(
            new ProvisioningOutput("defaultHostName", typeof(string))
            {
                Value = website.DefaultHostName,
            });
        infrastructure.Add(
            new ProvisioningOutput(
                "customDomainVerificationId",
                typeof(string))
            {
                Value = website.CustomDomainVerificationId,
            });
    }
}
