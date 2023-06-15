using System.Collections.Generic;
using Pulumi;
using Pulumi.Azure.Compute;
using Pulumi.Docker;
using Azure = Pulumi.Azure;
using Image = Pulumi.Docker.Image;
using ImageArgs = Pulumi.Docker.ImageArgs;

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
        AdminEnabled = true, // TODO: Find a way to do this via AAD / MSI
    });

    var acrCredentials = new ImageRegistry
    {
        Server = acr.LoginServer,
        Username = acr.AdminUsername,
        Password = acr.AdminPassword,
    };

    var image = new Image("docpad", new ImageArgs
    {
        Build = new DockerBuild
        {
            Context = "..",
            Dockerfile = "../build/Dockerfile",
        },
        ImageName = Output.Format($"{acr.LoginServer}/docpad:latest"),
        Registry = acrCredentials,
    });

    return new Dictionary<string, object?>
    {
        ["ResourceName"] = acr.GetResourceName(),
        ["Name"] = acr.Name,
        ["FullImageName"] = image.ImageName,
        ["ImageHash"] = image.ImageName.Apply(x => x.Split(":")[1]),
    };
});
