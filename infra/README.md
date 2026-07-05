# infra - QuibbleStone infrastructure (Bicep)

The tiny footprint from README section 9. The bar is **"deploys cleanly to dev"** -
this is intentionally not gold-plated.

## Resources

| # | Resource | Type | Purpose |
|---|----------|------|---------|
| 1 | Static Web App | `Microsoft.Web/staticSites` | Hosts the React + Vite web client |
| 2 | App Service (+ Plan) | `Microsoft.Web/sites` (+ `serverfarms`) | Hosts the single ASP.NET Core app (API + SignalR hub) |
| 3 | Azure SignalR Service | `Microsoft.SignalRService/signalR` | Real-time backplane for production scale-out |
| 4 | Storage Account | `Microsoft.Storage/storageAccounts` | Table: `StoryServes` / `StoryFeedback` (telemetry) + `PublishedTales` (keepsake-gallery/04 public tale links); Blob (AI images, later) |
| 5 | Key Vault | `Microsoft.KeyVault/vaults` | Secrets (Stripe, AI provider keys); now also holds the App Insights connection string |
| 6 | Application Insights (+ Log Analytics workspace) | `Microsoft.Insights/components` (+ `Microsoft.OperationalInsights/workspaces`) | Operational telemetry for the API (exceptions, failed requests, latency, dependencies) - `platform-devops/04` |
| 7 | ACS Email (**optional**, `enableEmail`) | `Microsoft.Communication/emailServices` (+ `/domains`) and `Microsoft.Communication/communicationServices` | Magic-link sign-in / operator-login email delivery - `accounts-identity/04`. **OFF by default** (the core footprint is unchanged); provisioned only when `enableEmail=true` (the deploy flips this from `vars.EMAIL_ENABLED`) |

The App Service Plan is part of resource #2 (App Service cannot run without a
plan). Application Insights is workspace-based, so it needs the Log Analytics
workspace alongside it (two ARM resources, one concern). Resource #7 (ACS Email)
is the only gated part of the footprint: with `enableEmail=false` (the default)
none of its resources exist, so a fresh clone or a UAT that has not turned email
on stays at the tiny core set (README section 9).

## Observability (platform-devops/04)

Application Insights is the API's **operational** telemetry - "find and fix bugs",
strictly no-PII / no-content (README section 6). It is deliberately tiny:

- **Two resources** - the App Insights component and the Log Analytics workspace
  it requires (workspace-based; PerGB2018, 30-day retention).
- **Connection string via Key Vault.** The component's connection string is stored
  as the `AppInsightsConnectionString` Key Vault secret and surfaced to the API as
  the `APPLICATIONINSIGHTS_CONNECTION_STRING` app setting via a **Key Vault
  reference** (`@Microsoft.KeyVault(SecretUri=...)`) - never a committed literal,
  never a `VITE_*` var. This is the first real consumer of the Key Vault.
- **RBAC, not an access policy.** The vault has `enableRbacAuthorization=true`, so
  the API App Service's SystemAssigned identity is granted the built-in **Key Vault
  Secrets User** role (`4633458b-17de-408a-b874-0445c86b69e6`) scoped to the vault,
  which is what lets the KV reference resolve at runtime.
  - **First-deploy note:** an RBAC grant can take a few minutes to propagate, so on
    the very first deploy the KV reference may not resolve immediately - App Service
    re-resolves references periodically, and until it does the SDK simply no-ops
    (App Insights is unconfigured, no error). A single app restart settles it if you
    do not want to wait. No code change is needed (an explicit `dependsOn` from the
    app to the role assignment would create a cycle).
- **Clean no-op locally.** With no connection string configured (local dev, CI),
  the SDK emits nothing and errors nothing - no key is ever committed.

### Alert seam (AC-07 - documented, not provisioned)

To keep the footprint tiny (README section 9) we do NOT provision an action group
or a full alert stack. Wiring a signal is a one-step, push-button action in the
portal (App Insights -> Alerts -> Create -> Custom log search) or one CLI call.
Two signals worth wiring first, with their KQL:

- **Server-exception spike:**

  ```kusto
  exceptions | where timestamp > ago(5m) | summarize count()
  ```

  Alert when the count over 5 minutes crosses a small threshold (e.g. > 5).

- **Failed-request spike:**

  ```kusto
  requests | where timestamp > ago(5m) and success == false | summarize count()
  ```

  Alert when the count over 5 minutes crosses a small threshold (e.g. > 5).

One step to wire either: create a scheduled-query-rule alert on the App Insights
resource with the KQL above and attach an email action group, e.g.

```bash
az monitor scheduled-query create \
  -g <rg> -n "server-exception-spike" \
  --scopes <appInsightsResourceId> \
  --condition "count 'exceptions' > 5" \
  --condition-query exceptions="exceptions | where timestamp > ago(5m)"
```

## Product usage (platform-devops/05)

Beyond "is it broken" (04), anonymous PRODUCT-USAGE events answer "how is the toy
actually used" - which modes get played, how long a session lasts, and roughly how
many distinct devices play. They ride this SAME App Insights pipeline as **custom
events** (no new resource, no dashboard, no third telemetry stack): the group hub
emits them server-side and the solo client forwards through `/api/usage`, and both
flow through the same PII scrubber. Anonymous by construction (README sections 3 +
6): the only fields are `mode`, `context` (solo/group), a `playerCount`, a
`durationMs` metric, and - for solo - an anonymous `deviceId` (a device-local
random GUID, an APPROXIMATE device count, never a verified person; it resets on
storage clear, and unique-person identity is deferred to accounts / Phase 2).

The three headline questions are each a one-line KQL query (App Insights ->
Logs) - no dashboard or workbook (demand-driven, README section 12):

- **Which modes get played, over time (AC-01):**

  ```kusto
  customEvents
  | where name == "RoundStarted"
  | summarize plays = count() by mode = tostring(customDimensions.mode), bin(timestamp, 1d)
  | order by timestamp asc
  ```

- **Median session length (AC-02):**

  ```kusto
  customEvents
  | where name == "RoundCompleted"
  | summarize medianMs = percentile(todouble(customMeasurements.durationMs), 50)
  ```

- **Approximate active-device count (AC-03 - a DEVICE count, never unique people):**

  ```kusto
  customEvents
  | where name in ("RoundStarted", "RoundCompleted") and isnotempty(tostring(customDimensions.deviceId))
  | summarize approxDevices = dcount(tostring(customDimensions.deviceId))
  ```

  Note the honest limit: `deviceId` rides SOLO events only (a solo tab has an
  anonymous device-local id). GROUP rounds are server-side and carry no per-device
  id, so they contribute `RoundStarted` volume and `context == "group"` but not
  device reach - true cross-device / unique-person counting needs accounts (Phase 2,
  out of scope). Split solo vs group with `context = tostring(customDimensions.context)`.

## Magic-link email delivery (accounts-identity/04)

Purchaser sign-in and operator login use a one-time magic link that has to be
**emailed**. The transport is Azure Communication Services (ACS) Email, and the app
sends **keyless** via its existing App Service managed identity (no provider secret -
see `api/src/Accounts/AcsEmailSender.cs`). The footprint is **gated behind
`enableEmail`** (default `false`), so it is inert until email is turned on.

- **What `enableEmail=true` provisions.** An Email Communication Service, an
  **Azure-managed domain** (a `*.azurecomm.net` sender Azure creates and verifies for
  you - **no SPF/DKIM DNS work**), and the Communication Services resource the app's
  `EmailClient` targets (linked to the domain). The managed-domain sender is Azure's
  **fixed `DoNotReply@<generated>.azurecomm.net`** - managed domains do not allow custom
  sender usernames, so a friendly `no-reply@…` needs the custom-domain upgrade below.
  This is a
  deliberate addition to the core footprint (README section 9), justified because
  magic-link is now load-bearing - and it stays out of the footprint entirely until
  you opt in.
- **Keyless send-role grant, automated.** The deploy workflow grants the API's managed
  identity the **Communication and Email Service Owner** role on the Communication
  Services resource, so the keyless path works end to end with no hand-run role
  assignment. (The role is name-resolved by the workflow, so no role GUID is hardcoded
  in Bicep; the grant is idempotent.)
- **How the app is wired.** `main.bicep` outputs the ACS endpoint (`emailAcsEndpoint`)
  and the Azure-managed sender address (`emailSenderAddress`); the deploy workflow's
  "Wire magic-link email delivery" step applies them to the API as `Email__Endpoint` /
  `Email__FromAddress`, plus a durable Key Vault-backed `Accounts__TokenSigningKey`
  (AC-07) and the derived `Email__LinkBaseUrl`. No secret is committed and nothing goes
  into a `VITE_*` var. With `enableEmail=false` those outputs are empty and the wiring
  step is skipped, so the API runs on the `NoOpEmailSender` exactly as before.
- **Production deliverability is an operator upgrade (not automated).** The
  `*.azurecomm.net` managed domain is perfect for UAT/alpha, but a **verified custom
  domain** (`no-reply@quibblestone.com` with real SPF/DKIM) needs DNS records and a
  verification wait that cannot live in Bicep. Set it up in ACS and point
  `vars.EMAIL_FROM_ADDRESS` (and, for a separate ACS, `vars.EMAIL_ENDPOINT`) at it -
  both override the provisioned defaults. See the turn-on procedure in the runbook:
  [`docs/runbooks/enable-magic-link-email.md`](../docs/runbooks/enable-magic-link-email.md).

The one still-manual, out-of-band step is the **durable signing key** Key Vault secret
(`AccountsTokenSigningKey`) - a value you choose, never in GitHub - so a delivered link
survives an app recycle. The runbook covers it.

## AI cost gate provider footprint (`infra/ai.bicep`, ai-cost-gate/06)

The AI provider (Azure OpenAI) is **not** part of `main.bicep` and does **not**
deploy to the app's resource group or subscription. It lives in its own file,
`infra/ai.bicep`, deployed to a **separate resource group** (`quibblestone-ai-rg`)
on a **different subscription** than everything above.

**Why a different subscription.** The app runs on an "Azure for Students"
subscription, and that subscription cannot host Azure OpenAI at all - the student
offer + spending limit block Cognitive Services OpenAI accounts, and the target
model family shows 0 real-time quota there. The AI account instead lives on a
Pay-As-You-Go subscription ("Playground") in the same tenant. The app's existing
App Service system-assigned managed identity still reaches it, **cross-subscription
and keyless**: a "Cognitive Services OpenAI User" role assignment, created in the AI
subscription, names the API identity's `principalId` (a GUID from the app
subscription) as a Bicep parameter - identity GUIDs resolve across subscriptions
within one tenant, so no key ever needs to exist.

Footprint (deployed to `quibblestone-ai-rg`):

| Resource | Type | Purpose |
|---|---|---|
| Azure AI Foundry (Azure OpenAI) account | `Microsoft.CognitiveServices/accounts` (kind `AIServices`) | Hosts the chat model deployment the AI cost gate calls |
| Model deployment | `Microsoft.CognitiveServices/accounts/deployments` | The deployed model - name/version/SKU are Bicep params (`aiModelName` / `aiModelVersion` / `aiDeploymentSku`), currently `gpt-5-mini` (2025-08-07), `GlobalStandard`, capacity 10 |
| Cross-sub role assignment | `Microsoft.Authorization/roleAssignments` | Grants the API's managed identity `Cognitive Services OpenAI User` (keyless) |
| Content Safety account (optional) | `Microsoft.CognitiveServices/accounts` (kind `ContentSafety`) | The config-gated second moderation layer; behind `deployContentSafety`, defaults `false` (not deployed) |
| Budget + action group (optional) | `Microsoft.Consumption/budgets` + `Microsoft.Insights/actionGroups` | $20/month backstop (param `monthlyBudgetUsd`), alerts at 25/50/75/100% Actual + Forecasted 100%; provisioned only when an `alertEmail` is supplied at deploy time |

**Model choice is a live decision, not a one-time pick.** ADR 0001 originally chose
`gpt-4o-mini`; by deploy time the whole gpt-4o/gpt-4.1 mini family was
`Deprecating` (Azure blocks new deployments of a deprecating model) and the cheaper
nano models had zero real-time quota in the target region, so `gpt-5-mini` was
deployed instead. Model name/version/SKU are Bicep parameters specifically so the
next availability shift is a config change. See
[`docs/adr/0001-ai-provider.md`](../docs/adr/0001-ai-provider.md) (Update note) and
[`docs/features/ai-cost-gate/06-iac-provisioning-seam.md`](../docs/features/ai-cost-gate/06-iac-provisioning-seam.md).

**Wiring the app.** `ai.bicep` cannot set app settings on the API app directly (it
is a different subscription/resource group, owned by `main.bicep`). It outputs the
endpoint and deployment name; a post-deploy step applies them to the API app:

```bash
az webapp config appsettings set -g quibblestone-uat-rg \
  -n <apiAppName> --settings Ai__Endpoint=<aiEndpoint> Ai__Deployment=<aiDeploymentName>
```

The app no-ops cleanly on absent `Ai:*` config, so this ordering is not fragile.

**Provisioning hand-off** (no secrets committed - the alert email is a deploy input,
never in `infra/ai.uat.bicepparam`):

```bash
az bicep build --file infra/ai.bicep   # validate, no Azure login needed

az deployment group create -g quibblestone-ai-rg -f infra/ai.bicep \
  -p infra/ai.uat.bicepparam -p alertEmail='owner@example.com'
```

Omit `alertEmail` and the account + model deployment + keyless grant still deploy;
only the budget + action group are skipped.

## Shareable tale link (keepsake-gallery/04)

The public read-only tale page (`GET /t/<slug>`) + host-initiated publish/revoke
ride the **existing** Storage account: `main.bicep` declares the `PublishedTales`
table and two deploy-composed app settings (`PublishedTales__StorageConnectionString`
from `storage.listKeys()`, `PublishedTales__WebAppBaseUrl` from the SWA host) -
no new resource, no committed secret. The feature is **OFF without the connection
string** (a disabled store: publish 503, page 404), exactly like the telemetry
NoOp fallback, so it needs zero setup locally and can be kept dark in a deployed
environment by omitting that one setting.

**Before it faces the public internet** there is a hard security gate (an
unauthenticated write endpoint needs a rate limit) plus a smoke-check and a kill
switch - the full turnkey procedure is the runbook:
[`docs/runbooks/keepsake-published-tales.md`](../docs/runbooks/keepsake-published-tales.md).

## Cost lever: `appServicePlanSku`

Everything above is on Free/near-zero SKUs except the App Service Plan, so that
plan is the only real cost. It is a parameter (`appServicePlanSku`) that
**defaults to `F1` (Free, $0)** - standing up UAT costs nothing. Scale up when
you want Always On + reliable WebSockets (a solid real-time demo):

| SKU | Tier | Always On | Cost | Use |
|---|---|---|---|---|
| `F1` (default) | Free | no | $0 | kick the tyres; app sleeps when idle |
| `B1` | Basic | yes | ~$13/mo | realistic floor for live multiplayer |
| `B2` / `S1` | Basic / Standard | yes | more | headroom, slots, autoscale |

`alwaysOn` and the `/health` check turn on automatically on paid tiers (they are
rejected on Free F1). WebSockets are enabled on every tier for the SignalR hub.

## Validate (no Azure needed)

```bash
az bicep build --file infra/main.bicep
```

## Provision UAT

Push-button (no local `az`): GitHub -> Actions -> **Provision UAT** -> Run
workflow (pick the `sku`). Or locally:

```bash
az group create -n quibblestone-uat-rg -l eastus2  # a Static-Web-Apps region (not eastus)
az deployment group create \
  -g quibblestone-uat-rg \
  -f infra/main.bicep \
  -p infra/main.uat.bicepparam
# scale up later: add -p appServicePlanSku=B1  (or re-run Provision UAT with sku=B1)
```

For the full end-to-end procedure (one-time OIDC bootstrap, provision,
auto-deploy on merge to main, and the `/health` + "Connected" smoke check), see
the runbook:
[`docs/runbooks/deploy-to-uat.md`](../docs/runbooks/deploy-to-uat.md). The
earlier dev-environment procedure is preserved at
[`docs/runbooks/deploy-to-dev.md`](../docs/runbooks/deploy-to-dev.md).

## Deliberately deferred (do not gold-plate yet)

- VNet / private endpoints / diagnostic settings.
- Wiring the API to **Azure SignalR Service** (the skeleton uses the in-process
  hub; production chains `.AddAzureSignalR(...)` with the connection string from
  Key Vault - see api/src/Program.cs).
- A provisioned **alert action group / alert stack** for App Insights - the alert
  seam is documented above (`platform-devops/04` AC-07), not provisioned.
- Environments beyond UAT, and a manual approval gate before UAT deploy (add a
  required reviewer on the `uat` GitHub Environment - no code change).

The Static Web App / App Service are now bound to the GitHub deploy workflow
(`.github/workflows/deploy.yml`, OIDC, auto on merge to main).
