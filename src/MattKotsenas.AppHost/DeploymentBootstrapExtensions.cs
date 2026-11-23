using Aspire.Hosting.Pipelines;
using Azure.Provisioning;
using Azure.Provisioning.Authorization;
using Azure.Provisioning.Expressions;
using Azure.Provisioning.Resources;
using Azure.Provisioning.Roles;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;

namespace MattKotsenas.AppHost;

#pragma warning disable ASPIREPIPELINES001
internal static class DeploymentBootstrapExtensions
{
    internal static IDistributedApplicationBuilder AddDeploymentBootstrap(
        this IDistributedApplicationBuilder builder,
        IResourceBuilder<ParameterResource> dnsGroup,
        IResourceBuilder<ParameterResource> legacyGroup)
    {
        builder.Configuration["Azure:ResourceGroup"] ??= BootstrapPreflight.ResourceGroup;
        string? verifiedTenantId = null;

        var bootstrap = builder.AddAzureInfrastructure("deployment-bootstrap", infrastructure =>
        {
            var identity = new UserAssignedIdentity("deploymentIdentity")
            {
                Name = BootstrapPreflight.IdentityName,
            };
            infrastructure.Add(identity);
            infrastructure.Add(new FederatedIdentityCredential("githubMain")
            {
                Parent = identity,
                Name = BootstrapPreflight.CredentialName,
                // Keep the issuer literal: Uri serialization appends a trailing slash.
                IssuerUri = new BicepValue<Uri>(new StringLiteralExpression(BootstrapPreflight.Issuer)),
                Audiences = [BootstrapPreflight.Audience],
                Subject = BootstrapPreflight.Subject,
            });
            foreach (var role in new[] { ArmBuiltInRole.Contributor, ArmBuiltInRole.RoleBasedAccessControlAdministrator })
            {
                infrastructure.Add(new RoleAssignment(ArmBuiltInRole.GetBuiltInRoleName(role))
                {
                    Name = BicepFunction.CreateGuid(BicepFunction.GetResourceGroup().Id, identity.Id, role.ToString()),
                    PrincipalId = identity.PrincipalId,
                    PrincipalType = RoleManagementPrincipalType.ServicePrincipal,
                    RoleDefinitionId = BicepFunction.GetSubscriptionResourceId("Microsoft.Authorization/roleDefinitions", role.ToString()),
                });
            }
            infrastructure.Add(new ProvisioningOutput("clientId", typeof(string)) { Value = identity.ClientId });
            infrastructure.Add(new ProvisioningOutput("principalId", typeof(string)) { Value = identity.PrincipalId });
            infrastructure.Add(new ProvisioningOutput("tenantId", typeof(string)) { Value = identity.TenantId });
            infrastructure.Add(new ProvisioningOutput("subscriptionId", typeof(string)) { Value = BicepFunction.GetSubscription().SubscriptionId });
        });

        var migrationResources = builder.AddMigrationPermissions(bootstrap.GetOutput("principalId"), dnsGroup, legacyGroup);
        AzureBicepResource[] bootstrapResources = [bootstrap.Resource, .. migrationResources];

        builder.Pipeline.AddStep(
            "bootstrap-preflight",
            async context =>
            {
                var provisionsBootstrap = context.PipelineContext.SelectedSteps.Any(step =>
                    step.Tags.Contains(WellKnownPipelineTags.ProvisionInfrastructure) &&
                    bootstrapResources.Any(resource => ReferenceEquals(resource, step.Resource)));
                if (!provisionsBootstrap)
                {
                    return;
                }

                verifiedTenantId = await BootstrapPreflight.VerifyAsync(builder.Configuration, [dnsGroup.Resource, legacyGroup.Resource], context.CancellationToken);
                context.Summary.Add("Bootstrap target", $"{builder.Configuration["Azure:SubscriptionId"]}/{BootstrapPreflight.ResourceGroup}");
                context.Summary.Add("GitHub subject", BootstrapPreflight.Subject);
            },
            requiredBy: WellKnownPipelineSteps.DeployPrereq);

        builder.Pipeline.AddStep(
            "bootstrap",
            async context =>
            {
                var cancellationToken = context.CancellationToken;
                var clientId = await bootstrap.GetOutput("clientId").GetValueAsync(cancellationToken);
                var tenantId = await bootstrap.GetOutput("tenantId").GetValueAsync(cancellationToken);
                var subscriptionId = await bootstrap.GetOutput("subscriptionId").GetValueAsync(cancellationToken);
                await GitHubBootstrap.ConfigureAsync(
                    clientId, tenantId, subscriptionId,
                    builder.Configuration["Azure:SubscriptionId"], verifiedTenantId, cancellationToken);
                context.Summary.Add("GitHub repository variables", "AZURE_CLIENT_ID, AZURE_TENANT_ID, AZURE_SUBSCRIPTION_ID");
            });

        bootstrap.WithPipelineConfiguration(context =>
        {
            var target = context.Steps.Single(step => step.Name == "bootstrap");
            foreach (var resource in bootstrapResources)
            {
                var provision = context.GetSteps(resource, WellKnownPipelineTags.ProvisionInfrastructure).Single();
                provision.RequiredBySteps.Remove(AzureEnvironmentResource.ProvisionInfrastructureStepName);
                target.DependsOn(provision);
            }
        });

        return builder;
    }
}
#pragma warning restore ASPIREPIPELINES001
