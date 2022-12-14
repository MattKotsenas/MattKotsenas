using System.Collections.Generic;
using Pulumi;
using Azure = Pulumi.Azure;

return await Deployment.RunAsync(() =>
{
    var example = new Azure.Core.ResourceGroup("personal-site", new()
    {
        Location = Constants.Location,
    });

    var acr = new Azure.ContainerService.Registry("acr", new()
    {
        ResourceGroupName = example.Name,
        Location = example.Location,
        Sku = "Basic",
        AdminEnabled = false
    });

    return new Dictionary<string, object?>
    {
        ["ResourceName"] = acr.GetResourceName(),
        ["Name"] = acr.Name,
    };
});
