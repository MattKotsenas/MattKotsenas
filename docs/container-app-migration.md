# Container Apps migration checklist

Track temporary migration permissions and rollout gates here. The live identity handoff and traffic cutover
remain pending. Preserve resource identities, records, values, TTLs, and the four public hostnames until their
specific migration step is approved. Certificates, DNS/traffic, retirement of existing access, and the workflow's
disabled schedule each require a separate rollout decision.

| Item | Current state | Removal or completion gate |
| --- | --- | --- |
| Deployment bootstrap | The existing AppHost defines a native `bootstrap` target for the managed identity and GitHub variable handoff | Review/merge source, explicitly approve the handoff, and validate a fresh main-only OIDC preview deployment |
| Old deployment identity and subscription-wide grants | Retained for recovery during handoff | Inventory direct/inherited access, prove the new identity works, then separately approve retirement; inherited grants require action at their parent scope |
| Custom ACR push grant | Source uses Contributor-based deployment push and native runtime pull | Validate push and runtime pull with the new identity, then explicitly remove obsolete live push assignments and `AZURE_DEPLOYMENT_PRINCIPAL_ID` |
| Existing preview ACR | Read-only inventory shows `LegacyRegistryPermissions`; the separate runtime identity already has `AcrPull` | Preserve the registry and native pull identity; validate image push and runtime pull during the approved preview deployment |
| DNS deployment authority | Bootstrap temporarily grants Template Spec Contributor in `dns` and DNS Zone Contributor on both existing zones | Transfer root website-record declarations to the shared DNS owner while preserving values; decide the steady-state child-zone/record boundary. Explicitly revoke each unused grant and remove its `MigrationPermissions.cs` declaration |
| Legacy Web App lookup | Application reads hostname/verification data; bootstrap grants Reader on the exact `mattkotsenas` site in `Default-Web-WestUS` | After certificate readiness and approved traffic cutover, remove the lookup and its callers/parameters/artifacts; separately revoke Reader when unused |
| Existing production DNS targets | Records target App Service during preview | Audit all eight modeled record sets and delegation, preserve TTLs/verification values through ownership handoff, prepare certificates, then separately approve and verify the traffic change |
| Certificate preparation | Custom-domain binding stays disabled until a certificate name is supplied | Prepare and validate all four names before traffic changes; keep rollback targets. Manual HTTPS checks establish current TLS/HTTP health; certificate rollover needs separate verification |
| Legacy production deployment job | `deploy-legacy-web-app-until-container-cutover` continues serving production | After validated Container Apps cutover and an agreed rollback window, explicitly approve removal of the legacy job, publish-profile secret, and obsolete App Service resources |
| Public HTTPS schedule | Workflow definition retained; GitHub schedule disabled | Re-enable only on request. Local checks remain available; fingerprint rollover requires separate verification |

## Verification at each handoff

- Source: Release build/tests, formatting, `publish-and-diff` against `generated/azure`, and compilation of every
  Bicep file. The drift check regenerates output before reporting differences; review changed files after failure.
- Identity: fresh main-only GitHub OIDC, expected client/tenant/subscription, and effective scoped permissions.
  Validate the new identity independently of the old principal. Role removal and propagation/recovery checks
  require explicit operator action.
- Hosting: image publish and runtime pull, preview HTTP health, and unchanged public App Service targets.
- Ownership/cutover: capture before/after DNS values and delegation, prove certificate readiness, obtain approval,
  and test all public names after the change. Decide rollback and legacy-service deletion separately.

Incremental deployment preserves omitted Azure grants. Retiring subscription-wide grants and temporary scoped
grants requires explicit inventory and separately approved removal. Keep the recovery operator through validation.

Bootstrap can leave Azure resources/grants, OIDC settings, and earlier variable writes in place after a failure.
Follow the [recovery guidance](configure-azure-deployment.md#reruns-and-failures) before retrying. Recreating the
managed identity changes its principal; preserve its identity and validate dependent assignments and repository
variables before retiring recovery access.
