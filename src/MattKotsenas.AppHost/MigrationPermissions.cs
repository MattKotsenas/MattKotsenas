using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using Azure.Provisioning;
using Azure.Provisioning.AppService;
using Azure.Provisioning.Authorization;
using Azure.Provisioning.Dns;
using Azure.Provisioning.Expressions;

namespace MattKotsenas.AppHost;

// Temporary permissions for the existing DNS and legacy-site model. Removal gates: docs/container-app-migration.md.
// The DNS SDK marks its API experimental, including references to existing zones.
#pragma warning disable AZPROVISION001
internal static class MigrationPermissions
{
    internal static AzureBicepResource[] AddMigrationPermissions(
        this IDistributedApplicationBuilder builder,
        BicepOutputReference principal,
        IResourceBuilder<ParameterResource> dnsGroup,
        IResourceBuilder<ParameterResource> legacyGroup)
    {
        var dns = builder.AddAzureInfrastructure("migration-dns-permissions", infrastructure =>
        {
            var principalId = principal.AsProvisioningParameter(infrastructure);
            // Template Spec Contributor includes the ARM deployment operations used by the DNS modules.
            infrastructure.Add(CreateRole("dnsDeployments", principalId, "1c9b6475-caf0-4164-b5a1-2142a7116f4b"));
            foreach (var (symbol, name) in new[] { ("rootZone", "kotsenas.com"), ("blogZone", "matt.kotsenas.com") })
            {
                var zone = DnsZone.FromExisting(symbol);
                zone.Name = name;
                infrastructure.Add(zone);
                var assignment = CreateRole(symbol + "Contributor", principalId, "befefa01-2a29-4197-83a8-272ff33ce314");
                assignment.Name = BicepFunction.CreateGuid(zone.Id, principalId, assignment.RoleDefinitionId);
                assignment.Scope = new BicepValue<string>(new IdentifierExpression(zone.BicepIdentifier));
                infrastructure.Add(assignment);
            }
        });
        dns.Resource.Scope = new AzureBicepResourceScope(dnsGroup.Resource);
        var legacy = builder.AddAzureInfrastructure("migration-legacy-reader", infrastructure =>
        {
            var site = WebSite.FromExisting("legacySite");
            site.Name = "mattkotsenas";
            infrastructure.Add(site);
            var principalId = principal.AsProvisioningParameter(infrastructure);
            var assignment = CreateRole("legacyReader", principalId, "acdd72a7-3385-48ef-bd42-f606fba81ae7");
            assignment.Name = BicepFunction.CreateGuid(site.Id, principalId, assignment.RoleDefinitionId);
            assignment.Scope = new BicepValue<string>(new IdentifierExpression(site.BicepIdentifier));
            infrastructure.Add(assignment);
        });
        legacy.Resource.Scope = new AzureBicepResourceScope(legacyGroup.Resource);
        return [dns.Resource, legacy.Resource];
    }

    private static RoleAssignment CreateRole(string name, ProvisioningParameter principal, string role) => new(name)
    {
        Name = BicepFunction.CreateGuid(BicepFunction.GetResourceGroup().Id, principal, role),
        PrincipalId = principal,
        PrincipalType = RoleManagementPrincipalType.ServicePrincipal,
        RoleDefinitionId = BicepFunction.GetSubscriptionResourceId("Microsoft.Authorization/roleDefinitions", role),
    };
}
#pragma warning restore AZPROVISION001
