# Configure Azure deployment

An authorized operator runs **Configure Container App deployment** on the AppHost's `blog` resource to configure
GitHub-to-Azure OIDC and scoped deployment roles. The command can create a deployment application or reuse a
verified application. It changes the application's federated credential, the repository OIDC configuration,
repository variables, and Azure role assignments. It does not deploy the website or revoke existing access.

## Prerequisites

Sign in with the Azure and GitHub CLIs using an operator account authorized to manage the Entra application,
assign roles at the scopes below, and update the repository's OIDC settings and Actions variables.
The GitHub deployment identity does not need these operator-level bootstrap permissions.

The following resources must already exist in the selected subscription:

- Resource groups `blog` and `dns`.
- DNS zones `kotsenas.com` and `matt.kotsenas.com` in `dns`.
- Web App `mattkotsenas` in `Default-Web-WestUS`.
- The container registry used by the preview deployment, in `blog`.

The operator also registers `Microsoft.App`, `Microsoft.ContainerRegistry`, `Microsoft.ManagedIdentity`,
`Microsoft.OperationalInsights`, `Microsoft.Network`, and `Microsoft.Web` before deployment. Provider registration
and resource-group creation are bootstrap operations; this command does not perform them. The deployment
workflow disables resource-group creation. Bootstrap a fresh environment separately under operator authority.

These scopes match the AppHost's default resource names. If deployment parameters select different DNS zones
or resource groups, an operator must review and configure the corresponding permissions separately.

## Configure the identity

Inspect the selected account and available registry IDs with these read-only commands:

```bash
az account show --query '{subscription:id,tenant:tenantId}' --output json
az acr list --resource-group blog --query '[].{name:name,id:id}' --output json
```

Set `RegistryResourceId` to the verified resource ID of the preview registry from that output. Setup validates
its subscription, resource group, resource type, and existence. It also checks both resource groups before
making identity changes. It does not guess a registry name or choose the first registry returned.

For an existing deployment application, verify its ownership and set `ApplicationId` to its client/application ID.
Do not substitute its object ID or the service principal's object ID. In Bash, run:

```bash
(
  set -e
  dotnet tool restore
  trap 'dotnet aspire stop' EXIT
  dotnet aspire start
  dotnet aspire resource blog configure-container-app-deployment -- \
    --registryResourceId "$RegistryResourceId" --applicationId "$ApplicationId"
)
```

For a new application, omit `--applicationId`. If an application with the configured display name already exists,
setup stops and asks for its verified application ID. Stop the AppHost even when setup fails. The dashboard
command accepts the same registry and application inputs.

Setup is not transactional: a failure can leave identity settings or some grants updated. Inspect the failure,
correct its cause, and rerun with the same verified application ID. Setup does not roll back or delete grants.

Setup verifies immutable GitHub OIDC claims and sets `AZURE_CLIENT_ID`, `AZURE_DEPLOYMENT_PRINCIPAL_ID`,
`AZURE_TENANT_ID`, and `AZURE_SUBSCRIPTION_ID`. It rechecks the application identity before each role grant.
If the service principal changes, regenerate the Azure publication artifacts with its new object ID in
`DeploymentPrincipalId`, then review and commit them before deployment.

## Deployment permissions

All scopes below are within the selected subscription. Setup grants built-in roles by their definition IDs and
recognizes an existing grant only when its role-definition ID and exact scope match.

| Role | Scope | Purpose |
|---|---|---|
| Contributor | `blog` resource group | Hosting resources and their ARM deployments |
| Template Spec Contributor | `dns` resource group | ARM deployment operations and resource-group reads for the DNS modules |
| DNS Zone Contributor | `kotsenas.com` zone | Root website record sets |
| DNS Zone Contributor | `matt.kotsenas.com` zone | Blog website record sets |
| Reader | Existing `mattkotsenas` Web App | Dynamic hostname and domain-verification properties |
| Role Based Access Control Administrator | Verified preview registry | Role assignments declared by the hosting templates |

[Template Spec Contributor](https://learn.microsoft.com/azure/role-based-access-control/built-in-roles/management-and-governance#template-spec-contributor)
includes ARM deployment operations and also permits template-spec management within `dns`. It avoids a custom
role while keeping DNS write authority limited to the two zone scopes. Zone-level DNS grants alone cannot
submit ARM deployments at resource-group scope.

Registry-scoped RBAC Administrator permits all role-assignment management at that registry. The generated
hosting templates use it for `AcrPush` and `AcrPull`; the grant is not conditioned to those two roles.
Setup requests no subscription-scoped Contributor or RBAC Administrator grant.

The `legacy-web` module deploys in `blog` and reads the existing website across scope through an ARM reference.
It does not submit a deployment in `Default-Web-WestUS` or create/update that website. Retire its Reader grant
when the legacy lookup is removed. Root DNS record ownership and default custom-domain binding behavior are
unchanged by this preparation.

## Replace broad assignments safely

A setup-code change does not revoke existing access. An authorized operator performs this rollout separately
from merging code:

1. Record the deployment service principal's current direct and inherited role assignments, including assignment
   IDs, role-definition IDs, principal IDs, and scopes. Keep an independently authorized operator available for
   recovery. Confirm provider registrations and the existing resources above.
2. Run setup with the verified existing application and registry IDs. Inspect the resulting six scoped grants.
   Adding them while broad grants remain does not prove that they are sufficient.
3. With explicit approval, remove only the identified old subscription-scoped Contributor and RBAC Administrator
   assignments for this deployment principal. Review any other inherited access separately. Setup never deletes
   assignments automatically, and rerunning this version cannot restore those subscription-wide grants.
4. Allow RBAC propagation and start a fresh GitHub Actions/OIDC session. After confirming broad inherited access
   is absent, run the manual **Build and Deploy** workflow with deployment approval. Require a successful
   Container App deployment, unchanged DNS record values, and a healthy application.
5. If deployment is denied, retain the failed operation and scope as evidence. Use the authorized operator for
   recovery and correct the missing scoped permission. Do not treat an automatic broad-role fallback as success.

Local tests verify setup decisions and generated infrastructure. They do not establish effective Azure access.
The narrowed role set remains unverified in Azure until the controlled deployment runs without broad grants.
