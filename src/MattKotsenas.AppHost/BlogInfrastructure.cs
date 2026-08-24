using Azure.Provisioning;
using Azure.Provisioning.AppService;

namespace MattKotsenas.AppHost;

public static class BlogInfrastructure
{
    public static void ConfigureExisting(Infrastructure infrastructure)
    {
        ArgumentNullException.ThrowIfNull(infrastructure);

        var plan = AppServicePlan.FromExisting("plan");
        plan.Name = "DefaultServerFarm";
        infrastructure.Add(plan);

        var website = WebSite.FromExisting("website");
        website.Name = "mattkotsenas";
        infrastructure.Add(website);

        infrastructure.Add(
            new ProvisioningOutput("websiteId", typeof(string))
            {
                Value = website.Id,
            });
        infrastructure.Add(
            new ProvisioningOutput("defaultHostName", typeof(string))
            {
                Value = website.DefaultHostName,
            });
        infrastructure.Add(
            new ProvisioningOutput("customDomainVerificationId", typeof(string))
            {
                Value = website.CustomDomainVerificationId,
            });
        infrastructure.Add(
            new ProvisioningOutput("appServicePlanId", typeof(string))
            {
                Value = plan.Id,
            });
    }
}
