# Configure Azure deployment

An authorized operator runs `dotnet aspire do bootstrap` to provision the GitHub deployment identity and
configure this repository's Azure variables. The existing AppHost in `src/MattKotsenas.AppHost` defines both
bootstrap and application resources. Native pipeline targets select which resources to provision.

## Authority and prerequisites

Bootstrap changes Azure resources, role assignments, GitHub OIDC configuration, and repository variables.
Obtain approval for the identity and variable handoff before running it. Retain a recovery operator and the
old deployment identity until the new identity has passed an approved deployment validation.

Authenticate Azure CLI and GitHub CLI as the authorized operator. The operator needs Azure deployment and
role-assignment authority at the scopes below, plus permission to configure this repository's Actions OIDC
subject and variables. Before starting, verify the credential can update repository OIDC customization and write
repository variables. The user-assigned managed identity uses ARM provisioning; Graph application-administration
consent is unnecessary.

The subscription must be explicit and the hosting resource group is fixed to `blog`. Bootstrap can create
`blog`. The external `dns` and `Default-Web-WestUS` groups and their referenced zones/site must already exist.

| Scope | Identity grants | Purpose |
| --- | --- | --- |
| `blog` resource group | Contributor; Role Based Access Control Administrator | Aspire manages the application, registry, runtime identities, and assignments |
| `dns` resource group | Template Spec Contributor | Temporary deployment authority for the existing DNS modules |
| `kotsenas.com` and `matt.kotsenas.com` zones in `dns` | DNS Zone Contributor on each zone | Temporary record-update authority |
| `mattkotsenas` Web App in `Default-Web-WestUS` | Reader on the exact site | Temporary legacy hostname/verification lookup |

The `blog` roles grant application-level authority. External grants are isolated in `MigrationPermissions.cs`;
see their [removal gates](container-app-migration.md).

## Run bootstrap

From the repository root, set `SUBSCRIPTION_ID` to the approved subscription GUID, review the rollout gates
below, then run:

```bash
dotnet tool restore
dotnet aspire do bootstrap \
  --environment preview \
  -- \
  --Azure:SubscriptionId="$SUBSCRIPTION_ID" \
  --Azure:Location=westus3
```

`aspire.config.json` selects the AppHost. Bootstrap preflight runs before native Azure authentication/context
setup and provisioning. It verifies the target subscription and repository metadata, reads the required
external groups, and inspects any existing federated credentials on the named managed identity. Only selected
bootstrap-resource targets run the GitHub/Azure CLI preflight.

After native Azure provisioning, bootstrap enables and verifies immutable GitHub OIDC and writes
`AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, and `AZURE_SUBSCRIPTION_ID`. The identity is `github-MattKotsenas-blog`;
its declared federated credential is `github-main-immutable`. Its issuer is exactly
`https://token.actions.githubusercontent.com`, its audience is `api://AzureADTokenExchange`, and its subject is
`repo:MattKotsenas@51421/MattKotsenas@8395662:ref:refs/heads/main`. Only the main branch matches this subject.

Preflight rejects an existing identity with unexpected federated credentials before applying new grants.
An identity with no credentials can converge after a partial creation. Effective and inherited permissions
require a separate inventory. Separately authorized writers retain the ability to change trust.

Group reads check existence at preflight time; concurrent deletion remains possible. Aspire 17's native deploy
path can create resource groups, including when a subsequent group read fails. Effective Azure RBAC defines
the authority boundary. Retiring superseded subscription-wide grants requires a separately approved action.

## Publish and deploy

Preserve local work before generating artifacts. Review the generated diff before rerunning checks or deploying.
Publish exports the full application and bootstrap model to one `generated/azure` tree:

```bash
dotnet aspire publish --output-path generated/azure --environment preview --non-interactive
```

Run the appropriate native target for the intended operation. Routine application deployment uses:

```bash
dotnet aspire deploy --environment preview --non-interactive
```

Routine deployment provisions application resources and leaves bootstrap modules and GitHub settings actions
unselected. CI uses this native deploy command for the preview workflow.

CI checks generated output through `WithOutputDiffing` and the `publish-and-diff` target:

```bash
dotnet aspire do publish-and-diff --output-path generated/azure --environment preview --non-interactive
```

This command regenerates files, then fails if selected output differs. It changes the output tree even on a
drift failure. Review those changes, regenerate intentionally, and commit approved artifacts before rerunning
the check.

## Controlled rollout

1. Review the generated templates and inventory the existing deployment identity, direct/inherited grants,
   repository variables, and OIDC configuration. Retain recovery access through validation.
2. The read-only Azure inventory shows the existing registry uses `LegacyRegistryPermissions`, compatible
   with Contributor-based deployment push. The runtime has a separate identity with `AcrPull` already configured.
   Preserve native runtime pull and the existing registry.
3. Obtain approval for the identity handoff, then run bootstrap. It replaces the three GitHub Azure variables.
   Existing access remains in place. Enabling immutable subjects affects other workflows using this repository's
   OIDC configuration; verify their trust configuration before proceeding.
4. Review and commit regenerated artifacts, then approve and run a fresh main-only OIDC preview deployment
   using the new identity. Confirm provisioning, image push/runtime pull, DNS-module updates, and legacy
   lookups. Confirm public records still point to App Service.
5. After successful validation, obtain separate approval to remove superseded subscription-wide grants,
   the old identity, obsolete custom push assignments, and `AZURE_DEPLOYMENT_PRINCIPAL_ID`. Inventory grants
   at their actual scopes. Incremental deployment preserves grants omitted from templates.

The live identity handoff remains pending. This source work authorizes no Azure changes. Certificate and
traffic changes have their own approval gates in the [migration checklist](container-app-migration.md).

## Reruns and failures

Native incremental deployment converges declared resources. Undeclared credentials, grants, identities, and
external resources remain until an explicit cleanup operation removes them.

Partial effects persist: Azure resources and grants can remain after deployment failure. GitHub OIDC can be
enabled while readback or a variable write fails; earlier variable writes remain. Stop competing deployments,
inspect the CLI error and current Azure/GitHub state, and understand the cause before rerunning. Recovery
requires inspection and explicit repair; automatic rollback is unavailable.

Preserve the managed identity during recovery. Recreating it changes its principal while existing role-assignment
names can remain, requiring explicit stale-assignment cleanup and another approved variable handoff.
