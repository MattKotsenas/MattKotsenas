using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using Azure.Provisioning;
using Azure.Provisioning.AppService;

namespace MattKotsenas.AppHost;

internal static class LegacyWebAppExtensions
{
    public static IResourceBuilder<AzureBicepResource> AddLegacyWebAppReference(
        this IDistributedApplicationBuilder builder)
    {
        var resourceGroup = builder.AddParameter(
            "legacyWebResourceGroupName",
            "Default-Web-WestUS",
            publishValueAsDefault: true);
        var legacyWeb = builder.AddAzureInfrastructure(
            "legacy-web",
            infrastructure =>
            {
                var website = WebSite.FromExisting("website");
                website.Name = "mattkotsenas";
                infrastructure.Add(website);

                infrastructure.Add(
                    new ProvisioningOutput(
                        "defaultHostName",
                        typeof(string))
                    {
                        Value = website.DefaultHostName,
                    });
            });
        legacyWeb.Resource.Scope = new(resourceGroup.Resource);

        return legacyWeb;
    }
}
