using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using Azure.Provisioning;
using Azure.Provisioning.AppService;
using Azure.Provisioning.Expressions;

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
                var legacyResourceGroup = resourceGroup.Resource
                    .AsProvisioningParameter(infrastructure);
                var websiteId = new FunctionCallExpression(
                    new IdentifierExpression("resourceId"),
                    new MemberExpression(
                        new FunctionCallExpression(new IdentifierExpression("subscription")),
                        "subscriptionId"),
                    new IdentifierExpression(legacyResourceGroup.BicepIdentifier),
                    website.ResourceType.ToString(),
                    "mattkotsenas");
                var websiteProperties = new FunctionCallExpression(
                    new IdentifierExpression("reference"),
                    websiteId,
                    website.ResourceVersion!);

                infrastructure.Add(
                    new ProvisioningOutput(
                        "defaultHostName",
                        typeof(string))
                    {
                        Value = new MemberExpression(websiteProperties, "defaultHostName"),
                    });
                infrastructure.Add(
                    new ProvisioningOutput(
                        "customDomainVerificationId",
                        typeof(string))
                    {
                        Value = new MemberExpression(websiteProperties, "customDomainVerificationId"),
                    });
            });

        return legacyWeb;
    }
}
