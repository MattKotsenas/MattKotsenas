using Azure.Core;
using Azure.Provisioning;
using Azure.Provisioning.AppService;

namespace MattKotsenas.AppHost;

public static class BlogInfrastructure
{
    public static void Configure(Infrastructure infrastructure)
    {
        ArgumentNullException.ThrowIfNull(infrastructure);

        var plan = new AppServicePlan("plan")
        {
            Name = "DefaultServerFarm",
            Location = new AzureLocation("westus"),
            Kind = "app",
            Sku = new AppServiceSkuDescription
            {
                Name = "B1",
                Tier = "Basic",
                Size = "B1",
                Family = "B",
                Capacity = 1,
            },
        };
        infrastructure.Add(plan);

        var website = new WebSite("website")
        {
            Name = "mattkotsenas",
            Location = new AzureLocation("westus"),
            Kind = "app",
            AppServicePlanId = plan.Id,
            IsClientAffinityEnabled = true,
            IsClientCertEnabled = false,
            ClientCertMode = ClientCertMode.Required,
            IsEnabled = true,
            IsHttpsOnly = true,
            IsReserved = false,
            PublicNetworkAccess = "Enabled",
            SiteConfig = new SiteConfigProperties
            {
                IsLocalMySqlEnabled = false,
                NetFrameworkVersion = "v4.0",
            },
        };
        infrastructure.Add(website);

        AddHostNameBinding(infrastructure, website, "root", "kotsenas.com");
        AddHostNameBinding(infrastructure, website, "rootWww", "www.kotsenas.com");
        AddHostNameBinding(infrastructure, website, "blog", "matt.kotsenas.com");
        AddHostNameBinding(infrastructure, website, "blogWww", "www.matt.kotsenas.com");

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

    private static void AddHostNameBinding(
        Infrastructure infrastructure,
        WebSite website,
        string identifierPrefix,
        string hostName)
    {
        var certificate = AppCertificate.FromExisting(
            $"{identifierPrefix}Certificate");
        certificate.Name = $"{hostName}-mattkotsenas";
        infrastructure.Add(certificate);

        var binding = new SiteHostNameBinding($"{identifierPrefix}Binding")
        {
            Parent = website,
            Name = hostName,
            HostNameType = AppServiceHostNameType.Verified,
            SslState = HostNameBindingSslState.SniEnabled,
            ThumbprintString = certificate.ThumbprintString,
        };
        infrastructure.Add(binding);
    }
}
