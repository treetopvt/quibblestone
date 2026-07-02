<!--
  Story 06 of the AI cost gate - the single-owner Bicep/IaC seam. "I prep the Bicep, you provision." No em dashes.
-->

# Story: IaC provisioning seam (Foundry + Content Safety + KV secret + budget/action group)

**Feature:** AI Cost Gate  ·  **Status:** Not Started  ·  **Issue:** #125

## Context
The gate's infrastructure, in one owner so a single story touches `infra/main.bicep`
(two Bicep-editing stories would collide at merge - feature.md Decisions). It preps
the Azure resources the code stories consume: the Azure AI Foundry (Azure OpenAI)
resource + a `gpt-4o-mini` deployment, an OPTIONAL Azure AI Content Safety resource
(config-gated per ADR 0001 B), the Key Vault secret (or managed-identity RBAC) the
proxy reads, and the Azure Cost Management $20 budget + action-group email alert
that is the authoritative BACKSTOP to the app breaker (story 04). This is a "I prep
the Bicep, you run the Azure provisioning" hand-off (ROADMAP): the story lands the
Bicep + docs; the owner runs the deployment and sets the deploy-time secrets/params.
See [feature.md](./feature.md) and [ADR 0001](../../adr/0001-ai-provider.md).

## Acceptance Criteria
- [ ] AC-01 (Foundry resource + deployment): Given `infra/main.bicep`, then it
      provisions an Azure AI Foundry / Azure OpenAI account and a model deployment for
      the chosen model (`gpt-4o-mini`), with the deployment name exposed as config the
      API reads (`Ai:Deployment`). The added footprint is documented in
      `infra/README.md` (README section 9 - keep it tiny, make it earn its place).
- [ ] AC-02 (key in Key Vault, or managed identity): Given the provider credential,
      then EITHER the API's system-assigned managed identity is granted the Foundry
      data-plane role (preferred, keyless), OR the API key is stored as a Key Vault
      secret surfaced to the App Service as a KV-reference app setting - mirroring the
      existing `APPLICATIONINSIGHTS_CONNECTION_STRING` pattern. NEVER a `VITE_*` var,
      NEVER committed (CLAUDE.md section 4). The endpoint is config, not a secret.
- [ ] AC-03 (optional Content Safety): Given the config-gated second moderation layer
      (story 05), then the Bicep can provision an Azure AI Content Safety resource and
      surface its endpoint/key the same KV-backed way - but it is OPTIONAL (a
      parameter/toggle), defaulting OFF for this slice so the footprint stays minimal
      (ADR 0001 B). When off, nothing is provisioned and story 05 no-ops.
- [ ] AC-04 (Cost Management budget): Given the $20/month hard business constraint,
      then the Bicep provisions a `Microsoft.Consumption/budgets` (resource-group
      scope) with `amount` = a parameter defaulting to 20, `timeGrain: Monthly`, and
      notification thresholds at 25 / 50 / 75 / 100% - the authoritative backstop that
      catches everything (AI + infra), reconciled against the app breaker (story 04).
- [ ] AC-05 (action group email, param not literal): Given the budget notifications,
      then they are wired to a `Microsoft.Insights/actionGroups` with an email
      receiver whose address is a BICEP PARAMETER / deploy input (defaulting empty or
      to a placeholder), NEVER hardcoded into committed markdown or source. The owner
      supplies the real address at deploy time (the intended recipient is the project
      owner's email, provided as a deploy value). A forecasted-100% notification may
      also be added for earlier warning.
- [ ] AC-06 (validates, minimal, documented): Given the Bicep, then `az bicep build`
      / validate passes, the new resources are the minimum needed (no speculative
      extras), and `infra/README.md` documents each addition and the provisioning
      hand-off steps (what the owner runs, which params/secrets to set). Local dev and
      the app still run with NONE of this provisioned (the code stories no-op without
      config, story 01 AC-04 / story 05 AC-05).
- [ ] AC-07 (no secrets committed): Given the whole change, then no key, connection
      string, or email address is committed - grep-clean; secrets are Key Vault /
      deploy inputs, the email is a deploy parameter (README section 6 / CLAUDE.md
      section 4).

## Out of Scope
- The application code that consumes any of this - stories 01 (proxy), 04 (breaker/
  attribution), 05 (moderation). This story is `infra/` + `infra/README.md` only.
- Running the actual Azure deployment / creating real resources - that is the owner's
  provisioning step (the hand-off); this story delivers validated Bicep + docs.
- Per-feature budgets, production hardening (private endpoints, separate prod
  resource), and PTU/committed-tier pricing - dev/UAT footprint only (feature.md
  Parked; README section 9).
- The app-level metric alert on the running estimate (story 04 AC-10) - that is App
  Insights wiring in the API/telemetry path, not this Bicep (though both target the
  same 25/50/75/100% thresholds; keep them coherent).

## Technical Notes
- **Mirror the existing footprint conventions** in `infra/main.bicep`: `baseName` +
  `uniqueString` suffix naming, `commonTags`, params with sane defaults. The App
  Insights + Key Vault + Storage wiring already there is the template - especially the
  KV-reference app-setting (`@Microsoft.KeyVault(SecretUri=...)`) and the
  role-assignment pattern granting the API identity a Key Vault / resource role.
- **Foundry:** `Microsoft.CognitiveServices/accounts` (kind `AIServices` or
  `OpenAI`) + a `deployments` child for `gpt-4o-mini`. Prefer granting the App
  Service managed identity the `Cognitive Services OpenAI User` role (keyless) over a
  key; if a key is used, store it as a KV secret like the App Insights conn string.
- **Content Safety (optional):** `Microsoft.CognitiveServices/accounts` (kind
  `ContentSafety`) behind a `deployContentSafety` bool param defaulting false.
- **Budget + action group:** `Microsoft.Insights/actionGroups` (email receiver =
  `alertEmail` param) + `Microsoft.Consumption/budgets` scoped to the resource group,
  `amount` = `monthlyBudgetUsd` param (default 20), `notifications` at the four
  thresholds referencing the action group's resource id. Note in `infra/README.md`
  that budget alerts are billing-driven and lag hours - the app breaker (story 04) is
  the real-time enforcer; the budget is the authoritative-but-slow backstop.
- **No secrets, no email, committed** (AC-07): `alertEmail` and any key are
  parameters/KV, never in the repo. `main.bicepparam` may set non-secret defaults
  (env name, budget amount) but not the email or any secret.

## Tests
| AC | Test |
|---|---|
| AC-01 | `az bicep build` + review: the Foundry account + gpt-4o-mini deployment exist; deployment name is output/config |
| AC-02 | review: managed-identity role assignment OR KV-secret + KV-reference app setting; nothing in VITE_* or committed |
| AC-03 | `az bicep build` with `deployContentSafety` true/false: the resource is present only when the param is set |
| AC-04 | review: a Consumption budget, amount param default 20, Monthly, thresholds 25/50/75/100% |
| AC-05 | review: an action group with an email receiver sourced from the `alertEmail` param, no hardcoded address |
| AC-06 | `az bicep build`/validate passes; `infra/README.md` documents the additions + provisioning hand-off; app runs with none provisioned |
| AC-07 | grep: no key/connection-string/email committed anywhere in the diff |

## Dependencies
- `infra` (the existing `main.bicep` footprint + Key Vault + Storage this extends).
- `platform-devops/04` (#106) - established the App Insights + Key Vault-secret +
  role-assignment Bicep patterns to mirror.
- Consumed by cost-gate/01 (Foundry config), 04 (budget is its backstop), 05
  (optional Content Safety config).
