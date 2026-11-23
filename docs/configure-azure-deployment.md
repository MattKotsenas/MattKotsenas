# Configure Azure deployment

Bootstrap creates a user-assigned managed identity for this repository, gives it authority over the blog
application, and hands its IDs to GitHub Actions. Native Aspire/Azure.Provisioning resources replace the
Entra application reconciliation script. Routine deployment remains `aspire deploy`.

## Authority and prerequisites

Run bootstrap as an authorized operator, **not** as the CI identity it creates. Authenticate Azure CLI and GitHub
CLI first. The operator needs Azure resource/deployment and role-assignment authority at the scopes below,
plus permission to configure this repository's Actions OIDC subject and variables. Bootstrap does not create an
Entra application or require Graph application-administration consent.

The selected subscription is explicit; the hosting resource group is fixed to `blog`. Bootstrap may create that
group. Existing `dns` and `Default-Web-WestUS` groups and their referenced zones/site must already exist. Routine
CI checks that `blog` exists before invoking deployment and must not run bootstrap.

| Scope | Identity grants | Purpose |
| --- | --- | --- |
| `blog` resource group | Contributor; Role Based Access Control Administrator | Aspire manages the application, registry, runtime identities, and assignments |
| `dns` resource group | Template Spec Contributor | Temporary deployment authority for the existing DNS modules |
| `kotsenas.com` and `matt.kotsenas.com` zones in `dns` | DNS Zone Contributor on each zone | Temporary record-update authority |
| `mattkotsenas` Web App in `Default-Web-WestUS` | Reader on the site | Temporary legacy hostname/verification lookup |

The two `blog` roles deliberately grant application-level authority, not a resource-by-resource allowlist. External
grants are isolated in `MigrationPermissions.cs`; see their [removal gates](container-app-migration.md).

## Operator command

From the repository root, after reviewing the target and the rollout gates below:

```bash
dotnet tool restore
dotnet aspire do bootstrap \
  --apphost src/MattKotsenas.Bootstrap.AppHost \
  --environment preview \
  -- \
  --Azure:SubscriptionId="$SUBSCRIPTION_ID" \
  --Azure:Location=westus3
```

**This command provisions resources and changes repository settings.** To list the pipeline without executing it,
add `--list-steps` before `--`. Listing is not an Azure what-if or an authorization check. To generate templates
without applying them, use `dotnet aspire publish --apphost src/MattKotsenas.Bootstrap.AppHost`.

The pipeline verifies the subscription/repository metadata, reads the required external groups, and inspects any
existing federated credentials on the named managed identity. It then applies the native Azure deployment, enables
and verifies immutable GitHub OIDC, and writes `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, and `AZURE_SUBSCRIPTION_ID`.
The identity is `github-MattKotsenas-blog`; its only declared federated credential is `github-main-immutable`.
Its issuer is exactly `https://token.actions.githubusercontent.com`, audience `api://AzureADTokenExchange`, and
subject `repo:MattKotsenas@51421/MattKotsenas@8395662:ref:refs/heads/main`. PRs and other branches do not match.

An existing identity with unexpected federated credentials is rejected before new grants. An identity with no
credentials can converge after a partial creation. These checks do not inventory all effective/inherited permissions
or prevent a separately authorized writer from changing trust later.

Group-existence checks are preflight checks, not Azure authorization constraints. Aspire 17 overrides
`AllowResourceGroupCreation` during deploy and can attempt creation if a subsequent group read fails. Do not rely
on that option. The CI wrapper stops before Aspire when its initial `blog` read fails; bootstrap similarly checks
its external groups before provisioning. These checks do not lock the groups against concurrent deletion. Effective
Azure permissions remain the authority boundary; keep routine CI scoped and retire superseded broad grants through
the approved handoff.

## Controlled rollout

1. Review both generated template trees and inventory the existing deployment identity, direct/inherited grants,
   repository variables, and OIDC configuration. Retain a recovery operator and the old identity until validation.
2. **Check the existing ACR permissions mode before deployment.** This model uses native Aspire 17 registry/runtime
   roles. In `LegacyRegistryPermissions`, inherited Contributor permits deployment push/pull; in
   `AbacRepositoryPermissions`, Contributor and legacy `AcrPull`/`AcrPush` do not provide repository data access.
   Inspect with `az acr show --name "$REGISTRY_NAME" --resource-group blog --subscription "$SUBSCRIPTION_ID" --query roleAssignmentMode`.
   The pinned provisioning SDK cannot declare this mode. An unread/unknown/ABAC mode blocks rollout: explicitly
   approve a mode change or preview-registry recreation, or design compatible repository roles first. Do not restore
   the old pinned-principal push workaround. Fresh registry creation uses the service's legacy-RBAC default.
3. With the identity handoff approved, run bootstrap. It replaces the three GitHub Azure variables; it does not
   revoke old access. If GitHub OIDC was mutable, enabling immutable subjects also affects other workflows using it;
   verify their trust configuration before proceeding.
4. Review and commit regenerated application artifacts, then run a fresh main-only OIDC deployment using the new
   identity. Confirm native resource provisioning, image push/pull, DNS-module updates, and legacy lookups. Public
   records must still point to App Service. This is a preview deployment, not traffic cutover.
5. Only after approved validation, remove superseded broad grants/old identity and the unused
   `AZURE_DEPLOYMENT_PRINCIPAL_ID` variable. Keep the recovery operator. Removing a declaration from an incremental
   deployment does **not** remove its Azure grant; cleanup needs an explicit inventory and separately approved action.

No live bootstrap or narrow-only deployment has been validated by this source change. Follow the
[migration checklist](container-app-migration.md) for certificate and traffic changes; neither is part of bootstrap.

## Reruns and failures

Native incremental deployment owns resource convergence. It does not delete undeclared credentials, grants,
identities, or external resources. Bootstrap deliberately does not discover/create Entra apps or revoke access.

The operation is **not transactional**. Azure resources/grants can remain after deployment failure. GitHub OIDC
may be enabled while a readback or variable write fails; earlier variable writes remain. Stop competing deployments,
inspect the CLI error and current Azure/GitHub state, and rerun only after the cause is understood. There is no
rollback promise. Never delete/recreate the identity as a blind retry: its principal changes while existing role
assignment names may remain, requiring explicit stale-assignment cleanup and another controlled variable handoff.
