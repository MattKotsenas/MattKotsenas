# Container Apps migration checklist

This is the cleanup ledger for temporary migration accommodations. Source preparation is not evidence that a
live handoff or cutover happened. Preserve resource identities, records, values, TTLs, and the four public hostnames
until their specific migration step is approved. Changes to certificates, DNS/traffic, existing access, and the
workflow's disabled schedule remain separate rollout decisions.

| Item | Current accommodation | Removal or completion gate |
| --- | --- | --- |
| Deployment bootstrap | New native Aspire bootstrap replaces the Entra reconciliation engine | Merge/review source, explicitly approve the variable handoff, and validate a fresh main-only OIDC preview deployment; no live completion claimed |
| Old deployment identity and broad grants | Kept as recovery during handoff; bootstrap never revokes them | Inventory direct/inherited access, prove the new identity works, then explicitly approve removal of the superseded identity/grants; inherited grants may need action at their parent scope |
| Pinned deployment principal / custom ACR push grant | Removed from source; native runtime pull identity remains | Regenerate/merge artifacts, check effective registry mode, validate push and runtime pull with the new identity, then explicitly remove obsolete live push assignments and `AZURE_DEPLOYMENT_PRINCIPAL_ID` |
| Preview ACR compatibility | Native model assumes legacy registry RBAC; pinned SDK does not expose permissions mode | Read the effective mode before rollout. Legacy mode supports the chosen Contributor boundary. ABAC/unknown mode blocks rollout pending approved reconfiguration/recreation or a compatible role design; recreation also requires image republish and runtime-pull validation |
| DNS deployment authority | Bootstrap temporarily grants Template Spec Contributor in `dns` and DNS Zone Contributor on both existing zones | Transfer root website-record declarations to the shared DNS owner without changing values; decide the steady-state child-zone/record boundary. Remove each unused grant explicitly, then delete its `MigrationPermissions.cs` declaration; incremental deployment alone does not revoke it |
| Legacy Web App lookup | Application dynamically reads hostname/verification data; bootstrap grants site-scoped Reader | After certificate readiness and approved traffic cutover, remove the legacy lookup and its callers/parameters/artifacts; separately revoke Reader when no longer needed |
| Existing production DNS targets | Records still target App Service during preview | Audit all eight modeled record sets and delegation, preserve TTLs/verification values through ownership handoff, prepare certificates, then approve and verify the traffic change separately |
| Certificate preparation | Custom-domain binding stays disabled until a certificate name is supplied | Prepare and validate all four names before traffic changes; keep rollback targets. Manual HTTPS checks test current TLS/HTTP health, not certificate rollover |
| Legacy production deployment job | `deploy-legacy-web-app-until-container-cutover` continues serving production | After validated Container Apps cutover and agreed rollback window, remove the legacy job, publish-profile secret, and obsolete App Service resources only with explicit approval |
| Public HTTPS schedule | Workflow definition retained; GitHub schedule deliberately disabled | Re-enable only if requested. Local checks remain available; they do not prove fingerprint rollover |

## Verification at each handoff

- **Source:** Release build/tests, formatting, regenerated-artifact drift checks, and compilation of every Bicep file.
- **Identity:** fresh main-only GitHub OIDC, expected client/tenant/subscription, effective scoped permissions, and no
  reliance on the old principal's access. Role removal and any propagation/recovery checks are explicit operator work.
- **Hosting:** image publish and runtime pull, preview HTTP health, and unchanged public App Service targets.
- **Ownership/cutover:** capture before/after DNS values and delegation, prove certificate readiness, obtain approval,
  and test all public names after the change. Keep the rollback decision separate from deleting the legacy service.

Deleting or recreating the managed identity changes its principal; deleting a preview registry removes images and
may change registry identity. Neither is a generic retry. Record the affected resource IDs and validate dependent
assignments, repository variables, republishing, and runtime access before retiring recovery access.
