# Story: The second environment (beta rebadge + platform instance)

**Feature:** Platform & DevOps  ·  **Status:** Not Started  ·  **Issue:** #TBD

## Context
[ADR 0003](../../adr/0003-admin-platform-and-family-accounts.md) Decision 4: "UAT is rebadged
beta-live; this work gets a second environment." The existing UAT instance
(`quibblestone-uat-rg`, provisioned by `platform-devops/03` + `infra/main.uat.bicepparam`) is
where the friends-and-family test runs (per `docs/ROADMAP.md`'s priority order); ADR 0003's
identity-spine, keepsake-vault, and control-plane work (`accounts-identity/05-09`,
`keepsake-vault/01-04`, `control-plane/01-03`, this feature's own `platform-devops/07`) should not
land on that same instance mid-test and risk destabilizing it. The ADR's answer is not a new
Bicep fork - `main.bicep` already parameterizes `environmentName`, and the deploy/provision
workflows already discover resources by tag rather than by hardcoded name - so a second,
independent instance is a parameter set away, not a new template.

This story does two things: (1) a naming/docs-only rebadge of the existing UAT instance as
"beta" (the friends-and-family test's home, undisturbed), and (2) a genuinely new, independent
resource group provisioned from the SAME `infra/main.bicep`, at the cheapest SKU, that hosts the
platform-layer work until it is ready to promote toward the beta/production line. See
[feature.md](./feature.md) and ADR 0003 Decision 4 + Consequences.

## Acceptance Criteria
- [ ] AC-01 (beta rebadge, zero resource churn): Given the existing `quibblestone-uat-rg`
      instance, then it is documented (README/runbook naming only - no Bicep, workflow, or DNS
      change) as "beta" - the environment the friends-and-family test runs on. Its running
      resources, deploy target (`main` branch -> this resource group, unchanged), and public URL
      are untouched by this story.
- [ ] AC-02 (a second, independent instance provisions cleanly): Given a new environment name
      (this story picks one, e.g. `plat`, and records the choice here) and a new resource group
      (e.g. `quibblestone-plat-rg`), when `infra/main.bicep` is deployed with that parameter set,
      then a fully independent set of resources (App Service, Static Web App, SignalR, Storage,
      Key Vault, App Insights) stands up from the SAME template - not a forked copy - at the
      cheapest App Service Plan SKU (F1).
- [ ] AC-03 (deploy targeting, specified mechanism): Given a push to `main`, then the existing
      `deploy.yml` behavior is UNCHANGED - it deploys to beta (`quibblestone-uat-rg`) exactly as
      today. Given an operator runs `deploy.yml` via `workflow_dispatch` with a new `target`
      (or `environment`) input set to the platform environment's name, then it deploys to the
      new resource group instead - reusing the SAME build/publish steps, with only the resource
      group / GitHub Environment (and therefore its `vars`/`secrets`) resolved from the input.
- [ ] AC-04 (config isolation): Given the two environments, then each has its OWN resource group,
      Storage Account, Key Vault, and app settings - no connection string, secret, or Key Vault
      reference from one is ever wired into the other's App Service.
- [ ] AC-05 (runbook-level notes): Given an operator stands up or maintains the platform
      instance, then a documented, per-environment checklist exists (in this story's Technical
      Notes or a linked runbook) of what each environment needs configured independently: the
      OIDC federated-credential scoping (or a shared one scoped broadly enough to cover both
      resource groups - documented either way), `AZURE_RESOURCE_GROUP` (or equivalent), ACS email
      (`enableEmail` + its repo vars), the operator allowlist (`OperatorAllowedEmails` Key Vault
      secret), and Stripe wiring (left OFF on the platform instance per Out of Scope, but the
      checklist says so explicitly).
- [ ] AC-06 (cost floor, documented): Given the platform instance exists to host pre-launch
      platform work (not beta/live traffic), then its App Service Plan SKU defaults to F1 (Free,
      $0) and this is stated explicitly (not left to whoever runs Provision next to guess) - no
      accidental cost creep from copying beta's SKU.

## Out of Scope
- Any resource-level change to the beta/UAT instance itself - AC-01 is naming/docs only.
- Stripe billing on the platform instance - ADR 0003 Decision 4 explicitly sequences "the platform
  layers land BEFORE Stripe goes live"; leave `STRIPE_ENABLED` off there (the existing
  config-presence gate already makes this a no-op, not a new switch).
- A production/live environment, a custom domain for the platform instance, or any go-live
  readiness work - this is a pre-launch working environment, not a third public-facing tier.
- Automatic promotion/sync of data (accounts, grants, tales) from the platform instance to
  beta/production when this work eventually ships there - that is a deliberate, separate cutover
  decision for whoever ships the identity-spine/control-plane work, not this story.
- A branch-per-environment GitOps setup, environment-specific Bicep forks, or a third-party
  deployment tool - the mechanism is a parameter to the SAME workflow/template (README section 9:
  keep it tiny).
- Tearing down or renaming the `uat` `environmentName`/resource-group value anywhere in code -
  Decision 4's "rebadge" is explicitly cosmetic/documentation, not a rename that would force a
  redeploy of beta.

## Technical Notes
- **Naming choice (record it here):** environment name `plat`, resource group
  `quibblestone-plat-rg`, GitHub Environment name `platform`. (If the builder picks a different
  name at build time, update this line and `infra/main.plat.bicepparam`'s header comment to
  match - the point of recording it here is so the next story/PR does not have to guess.)
- **New bicepparam file:** `infra/main.plat.bicepparam`, mirroring `infra/main.uat.bicepparam`'s
  shape exactly:
  ```
  using './main.bicep'
  param namePrefix = 'quibblestone'
  param environmentName = 'plat'
  param appServicePlanSku = 'F1'
  ```
  No template changes needed - `main.bicep` already derives every resource name from
  `namePrefix`/`environmentName`/a `uniqueString(resourceGroup().id)` suffix (AC-02 - this is
  exactly why a "second instance is a parameter set away").
- **Provision workflow:** `provision.yml` already accepts `resource_group` and `sku` as
  `workflow_dispatch` inputs and passes `environmentName=uat` hardcoded in its `az deployment
  group create` call. Add an `environment_name` input (default `uat`, so today's usage is
  unchanged) and thread it into that `-p environmentName=...` line - this is the smallest edit
  that makes provisioning the platform instance a matter of running the SAME workflow with
  `resource_group=quibblestone-plat-rg`, `environment_name=plat`.
- **Deploy workflow, targeting mechanism (AC-03):** `deploy.yml` today hardcodes
  `AZURE_RESOURCE_GROUP: ${{ vars.AZURE_RESOURCE_GROUP || 'quibblestone-uat-rg' }}` and
  `environment: name: uat` (the GitHub Environment, which scopes `vars`/`secrets`). The
  manually-targeted mechanism this story specifies (chosen over a branch-push trigger, since the
  ADR only requires the platform work to land somewhere stable, not a permanent auto-deploying
  branch):
  1. Add a `workflow_dispatch` input `target_environment` (`choice`: `uat` / `plat`, default
     `uat`).
  2. The `push: branches: [main]` trigger keeps deploying to `uat` exactly as today (the input
     only matters for a manual dispatch - a push event has no input, so it resolves to the
     default).
  3. `environment: name: ${{ inputs.target_environment || 'uat' }}` - this is what makes AC-04's
     config isolation free: GitHub Environments already scope `vars`/`secrets` per environment, so
     a `platform` GitHub Environment with ITS OWN `AZURE_RESOURCE_GROUP=quibblestone-plat-rg` (and
     its own `EMAIL_ENABLED`, etc., left unset/off per Out of Scope) means the SAME job steps
     naturally read the right resource group and the right (absent) optional config - no `if`
     branching inside the job body beyond what already exists.
  4. If a future story wants the platform environment to deploy automatically on push to a
     `platform` branch (rather than manual dispatch only), that is an ADDITIVE trigger
     (`push: branches: [main, platform]` plus a step that maps branch -> `target_environment`) -
     not required by this story's ACs, noted here as the natural next step per the ADR's "branch-
     aware or manually-targeted" framing.
- **Runbook checklist (AC-05), per environment:**
  | Config | Beta (uat) | Platform (plat) |
  |---|---|---|
  | `AZURE_RESOURCE_GROUP` | `quibblestone-uat-rg` (default) | `quibblestone-plat-rg` (new GitHub Environment var) |
  | OIDC federated credential | scoped to `quibblestone-uat-rg` (or the subscription) | must also reach `quibblestone-plat-rg` - broaden the existing federated credential's scope or add a second one; document whichever is chosen |
  | `EMAIL_ENABLED` / ACS email | on (friends-and-family test needs real magic links) | leave OFF (`NoOpEmailSender`) unless platform work specifically needs it - keep the instance cheap and quiet |
  | `Operator__AllowedEmails` (Key Vault `OperatorAllowedEmails`) | the real operator's email | the same operator's email (or none, if the platform instance's admin work is tested unauthenticated locally instead) |
  | `STRIPE_ENABLED` | as configured today | OFF (Out of Scope) |
  | `platform-devops/07`'s Blob container + Key Vault key | provisioned by `main.bicep` automatically in both, since both deploy from the same template | same |
- **Reuse, do not reinvent:** the SAME `main.bicep`, the SAME `deploy.yml`/`provision.yml`
  structure (parameterized, not forked), the SAME resource-discovery-by-tag pattern
  (`az webapp list --query "[?tags.app=='quibblestone']"`) - tag-based discovery already works
  per-resource-group, so it needs no change to work against a second group.

## Tests
| AC | Test |
|---|---|
| AC-01 | `manual: confirm no diff to infra/main.bicep, infra/main.uat.bicepparam, or deploy.yml's default (unparameterized) behavior; the rebadge is docs-only.` |
| AC-02 | `az bicep build`/validate + manual: az deployment group create against infra/main.plat.bicepparam succeeds and produces a distinct, independent resource set (different uniqueString suffix, different resource group).` |
| AC-03 | `manual: a push to main deploys to quibblestone-uat-rg (unchanged); a workflow_dispatch run with target_environment=plat deploys to quibblestone-plat-rg.` |
| AC-04 | `manual: inspect each App Service's app settings - confirm no cross-environment connection string, Key Vault reference, or secret appears in the other's settings.` |
| AC-05 | `manual: a fresh operator follows the Technical Notes checklist end to end on a clean plat resource group and reaches a working, reachable deployed app.` |
| AC-06 | `manual/code review: infra/main.plat.bicepparam's appServicePlanSku is F1; this is stated in the file's header comment.` |

## Dependencies
- none (independent of every other ADR 0003 feature per the ADR's Wave 1 table; the only shared
  hazard is that this story's own feature-mate, `platform-devops/07`, should ideally land first or
  alongside so the platform instance provisions with the durable key ring from day one - not a
  hard blocker either way, since `platform-devops/07`'s Bicep addition applies automatically to
  whichever environment is deployed).
