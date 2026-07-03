<!--
  Story 06 of the AI cost gate - the single-owner Bicep/IaC seam. "I prep the Bicep, you provision." No em dashes.
-->

# Story: IaC provisioning seam (Foundry + keyless MI RBAC + Content Safety + budget/action group)

**Feature:** AI Cost Gate  ·  **Status:** Complete  ·  **Issue:** #125

## Context
The gate's infrastructure, in one owner so a single story touches the AI provider
Bicep (two Bicep-editing stories would collide at merge - feature.md Decisions). It
preps the Azure resources the code stories consume: the Azure AI Foundry (Azure
OpenAI) resource + a model deployment, an OPTIONAL Azure AI Content Safety resource
(config-gated per ADR 0001 B), the keyless managed-identity grant the proxy calls
through, and the Azure Cost Management $20 budget + action-group email alert that is
the authoritative BACKSTOP to the app breaker (story 04). This is a "I prep the
Bicep, you run the Azure provisioning" hand-off (ROADMAP): the story lands the Bicep
+ docs; the owner runs the deployment and sets the deploy-time secrets/params.

**Deployed via PR #131 (2026-07-02).** The plan assumed this Bicep would land in
`infra/main.bicep`, co-located with the rest of the app footprint. That turned out to
be impossible: the app runs on an "Azure for Students" subscription, and that
subscription **cannot host Azure OpenAI** (the student offer + spending limit block
Cognitive Services OpenAI accounts outright, and real-time quota for the target model
family is 0). So the AI provider is authored as a **separate file, `infra/ai.bicep`**,
deployed to a **new resource group `quibblestone-ai-rg` on a Pay-As-You-Go
subscription** ("Playground", `52bec743-...`) in the same tenant (BullIT). The API's
existing App Service system-assigned managed identity reaches the OpenAI account
**cross-subscription, keyless**: a "Cognitive Services OpenAI User" role assignment
(created in the AI subscription) names the API identity's `principalId` (a GUID from
the app subscription) as a Bicep parameter - GUIDs resolve cross-subscription within
one tenant. The `Ai:Endpoint` / `Ai:Deployment` app settings are set on the API app as
a **post-deploy step** (from `ai.bicep` outputs), not inside `main.bicep`. See
[feature.md](./feature.md) and [ADR 0001](../../adr/0001-ai-provider.md).

## Acceptance Criteria
- [x] AC-01 (Foundry resource + deployment): Given **`infra/ai.bicep`** (a separate
      file/deployment from `infra/main.bicep` - see Context for why), then it
      provisions an Azure AI Foundry / Azure OpenAI account and a model deployment for
      the chosen model, with the model name/version/SKU as Bicep parameters
      (`aiModelName` / `aiModelVersion` / `aiDeploymentSku`) so a future model swap is
      config, not code, and the deployment name exposed as config the API reads
      (`Ai:Deployment`). Deployed: `gpt-5-mini` (2025-08-07), GlobalStandard, capacity
      10. The added footprint is documented in `infra/README.md` (README section 9 -
      keep it tiny, make it earn its place).
- [x] AC-02 (key in Key Vault, or managed identity): Given the provider credential,
      then EITHER the API's system-assigned managed identity is granted the Foundry
      data-plane role (preferred, keyless), OR the API key is stored as a Key Vault
      secret surfaced to the App Service as a KV-reference app setting - mirroring the
      existing `APPLICATIONINSIGHTS_CONNECTION_STRING` pattern. NEVER a `VITE_*` var,
      NEVER committed (CLAUDE.md section 4). The endpoint is config, not a secret.
      **Delivered via the preferred keyless path, made cross-subscription**: the AI
      account lives in a different subscription than the API app (see Context), so the
      "Cognitive Services OpenAI User" role assignment is created in the AI
      subscription and names the API identity's `principalId` (from the app
      subscription) as a plain Bicep parameter (`apiPrincipalId`, not a secret - an
      identity GUID resolves cross-subscription within one tenant). No key or Key
      Vault secret is needed for the model call.
- [x] AC-03 (optional Content Safety): Given the config-gated second moderation layer
      (story 05), then the Bicep can provision an Azure AI Content Safety resource and
      surface its endpoint/key the same KV-backed way - but it is OPTIONAL (a
      parameter/toggle), defaulting OFF for this slice so the footprint stays minimal
      (ADR 0001 B). When off, nothing is provisioned and story 05 no-ops.
      `deployContentSafety` defaults `false` and was left off for this deploy.
- [x] AC-04 (Cost Management budget): Given the $20/month hard business constraint,
      then the Bicep provisions a `Microsoft.Consumption/budgets` (resource-group
      scope) with `amount` = a parameter defaulting to 20, `timeGrain: Monthly`, and
      notification thresholds at 25 / 50 / 75 / 100% - the authoritative backstop that
      catches everything (AI + infra), reconciled against the app breaker (story 04).
      Deployed as `quibblestone-uat-ai-budget` (thresholds 25/50/75/100 + a
      Forecasted-100 notification).
- [x] AC-05 (action group email, param not literal): Given the budget notifications,
      then they are wired to a `Microsoft.Insights/actionGroups` with an email
      receiver whose address is a BICEP PARAMETER / deploy input (defaulting empty or
      to a placeholder), NEVER hardcoded into committed markdown or source. The owner
      supplies the real address at deploy time (the intended recipient is the project
      owner's email, provided as a deploy value). A forecasted-100% notification may
      also be added for earlier warning. Deployed as `quibblestone-uat-ai-budget-ag`
      (email supplied as a deploy input, not committed).
- [x] AC-06 (validates, minimal, documented): Given the Bicep, then `az bicep build`
      / validate passes, the new resources are the minimum needed (no speculative
      extras), and `infra/README.md` documents each addition and the provisioning
      hand-off steps (what the owner runs, which params/secrets to set). Local dev and
      the app still run with NONE of this provisioned (the code stories no-op without
      config, story 01 AC-04 / story 05 AC-05).
- [x] AC-07 (no secrets committed): Given the whole change, then no key, connection
      string, or email address is committed - grep-clean; secrets are Key Vault /
      deploy inputs, the email is a deploy parameter (README section 6 / CLAUDE.md
      section 4). `infra/ai.uat.bicepparam` sets no email/key; `apiPrincipalId` is a
      committed identity GUID, not a secret.

## Out of Scope
- The application code that consumes any of this - stories 01 (proxy), 04 (breaker/
  attribution), 05 (moderation). This story is `infra/ai.bicep` + `infra/README.md`
  (plus the small `Ai:*` app-setting hand-off on the existing `main.bicep`-managed
  API app) only.
- Running the actual Azure deployment / creating real resources - that is the owner's
  provisioning step (the hand-off); this story delivers validated Bicep + docs. (In
  practice PR #131 also carried out the deploy and verified the live resources.)
- Per-feature budgets, production hardening (private endpoints, separate prod
  resource), and PTU/committed-tier pricing - dev/UAT footprint only (feature.md
  Parked; README section 9).
- The app-level metric alert on the running estimate (story 04 AC-10) - that is App
  Insights wiring in the API/telemetry path, not this Bicep (though both target the
  same 25/50/75/100% thresholds; keep them coherent).
- Moving the app footprint (`main.bicep`) to a subscription that can host Azure
  OpenAI - out of scope; the cross-subscription split is the accepted shape, not a
  stopgap (see Context).

## Technical Notes
- **Why a separate file, not `infra/main.bicep`.** The plan assumed one owner in
  `main.bicep`; that is impossible because the app's subscription ("Azure for
  Students") cannot host Azure OpenAI at all (student offer + spending limit block
  Cognitive Services OpenAI accounts; the target model family shows 0 real-time
  quota there). `infra/ai.bicep` is deployed standalone to a new resource group
  `quibblestone-ai-rg` on a Pay-As-You-Go subscription ("Playground",
  `52bec743-...`) in the same tenant (BullIT). This *also* resolves the original
  "two Bicep-editing stories would collide in `main.bicep`" collision concern
  differently than planned - the split file means the AI footprint never touches
  `main.bicep` at all, so there is no shared-file collision to manage.
- **Mirror the existing footprint conventions** anyway: `baseName` + `uniqueString`
  suffix naming, `commonTags`, params with sane defaults - the same shape as
  `main.bicep`, just in its own file/resource group.
- **Foundry:** `Microsoft.CognitiveServices/accounts` (kind `AIServices`) + a
  `deployments` child, now named `gpt-5-mini` (superseded from ADR 0001's
  `gpt-4o-mini` pick - see the ADR's Update note: 4o-mini/4.1-mini are
  `Deprecating`, blocked for new deployments; 4.1-nano/5-nano/5.4-nano have 0
  real-time quota in eastus2). Model name/version/deployment SKU are Bicep
  parameters (`aiModelName`, `aiModelVersion`, `aiDeploymentSku`) so a future swap
  is config. Deployment SKU is `GlobalStandard` (capacity 10) - the current-gen mini
  models dropped the regional `Standard` SKU the old 4o-mini generation offered.
- **Cross-subscription keyless grant (AC-02).** The API's existing App Service
  system-assigned managed identity is granted the built-in `Cognitive Services
  OpenAI User` role (`5e0bd9bd-7b93-4f28-af87-19fc36ad61bd`), scoped to the Foundry
  account in the AI subscription. Because the identity lives in a different
  subscription, its `principalId` cannot be resolved as a resource reference - it is
  passed as a **plain Bicep parameter** (`apiPrincipalId`, a GUID, not a secret).
  Same-tenant GUIDs resolve across subscriptions, so the role assignment is valid.
  No key, no Key Vault secret, is needed for the model call itself.
- **Wiring the app (`Ai:Endpoint` / `Ai:Deployment`).** `ai.bicep` cannot set app
  settings on the API app - that app is owned by `main.bicep` in a different
  subscription/resource group. Instead `ai.bicep` outputs `aiEndpoint` and
  `aiDeploymentNameOut`; a post-deploy step (`az webapp config appsettings set`)
  applies them to the API app as `Ai__Endpoint` / `Ai__Deployment`. The app no-ops on
  absent `Ai:*` config regardless of ordering (story 01 AC-04).
- **Content Safety (optional):** `Microsoft.CognitiveServices/accounts` (kind
  `ContentSafety`) behind a `deployContentSafety` bool param defaulting false; not
  deployed for this slice.
- **Budget + action group:** `Microsoft.Insights/actionGroups` (email receiver =
  `alertEmail` param) + `Microsoft.Consumption/budgets` scoped to the AI resource
  group, `amount` = `monthlyBudgetUsd` param (default 20), `notifications` at the
  four Actual thresholds plus a Forecasted-100 notification, referencing the action
  group's resource id. Both resources are conditioned on `alertEmail` being
  non-empty, so a no-email deploy still stands the AI account up cleanly. Budget
  alerts are billing-driven and lag hours - the app breaker (story 04) is the
  real-time enforcer; the budget is the authoritative-but-slow backstop.
- **No secrets, no email, committed** (AC-07): `alertEmail` and any key are
  parameters/deploy inputs, never in the repo. `ai.uat.bicepparam` sets non-secret
  defaults (model, SKU, budget amount, `apiPrincipalId` as a plain GUID) but not the
  email or any secret.

## Tests
| AC | Test |
|---|---|
| AC-01 | `az bicep build --file infra/ai.bicep` + review: the Foundry account + `gpt-5-mini` deployment exist (`az cognitiveservices account deployment show`); deployment name is output/config |
| AC-02 | review: the cross-subscription "Cognitive Services OpenAI User" role assignment exists, scoped to the Foundry account, naming the API identity's `principalId`; nothing in VITE_* or committed |
| AC-03 | `az bicep build` with `deployContentSafety` true/false: the resource is present only when the param is set; confirmed off for this deploy |
| AC-04 | review: the deployed `quibblestone-uat-ai-budget` Consumption budget, amount 20, Monthly, thresholds 25/50/75/100% + Forecasted 100 |
| AC-05 | review: the deployed `quibblestone-uat-ai-budget-ag` action group with an email receiver sourced from the `alertEmail` deploy input, no hardcoded address in the repo |
| AC-06 | `az bicep build`/validate passes; `infra/README.md` documents the additions + provisioning hand-off; app runs with none provisioned |
| AC-07 | grep: no key/connection-string/email committed anywhere in the diff |

## Dependencies
- `infra` (the existing `main.bicep` footprint + Key Vault + Storage this sits
  alongside, on a different subscription).
- `platform-devops/04` (#106) - established the App Insights + Key Vault-secret +
  role-assignment Bicep patterns this mirrors (even though the AI footprint ended up
  keyless and standalone rather than extending `main.bicep` directly).
- Consumed by cost-gate/01 (Foundry config), 04 (budget is its backstop), 05
  (optional Content Safety config).
