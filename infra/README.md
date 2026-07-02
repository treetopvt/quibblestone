# infra - QuibbleStone infrastructure (Bicep)

The tiny footprint from README section 9. The bar is **"deploys cleanly to dev"** -
this is intentionally not gold-plated.

## Resources

| # | Resource | Type | Purpose |
|---|----------|------|---------|
| 1 | Static Web App | `Microsoft.Web/staticSites` | Hosts the React + Vite web client |
| 2 | App Service (+ Plan) | `Microsoft.Web/sites` (+ `serverfarms`) | Hosts the single ASP.NET Core app (API + SignalR hub) |
| 3 | Azure SignalR Service | `Microsoft.SignalRService/signalR` | Real-time backplane for production scale-out |
| 4 | Storage Account | `Microsoft.Storage/storageAccounts` | Table (templates, entitlements) + Blob (AI images, later) |
| 5 | Key Vault | `Microsoft.KeyVault/vaults` | Secrets (Stripe, AI provider keys); now also holds the App Insights connection string |
| 6 | Application Insights (+ Log Analytics workspace) | `Microsoft.Insights/components` (+ `Microsoft.OperationalInsights/workspaces`) | Operational telemetry for the API (exceptions, failed requests, latency, dependencies) - `platform-devops/04` |
| 7 | AI Foundry (Azure OpenAI) + `gpt-4o-mini` deployment | `Microsoft.CognitiveServices/accounts` (kind `AIServices`) + `.../deployments` | The AI provider the cost-gate proxy calls - `ai-cost-gate/06` |
| 8 | Azure AI Content Safety (OPTIONAL, off by default) | `Microsoft.CognitiveServices/accounts` (kind `ContentSafety`) | The optional second moderation layer, `deployContentSafety` param - `ai-cost-gate/06` |
| 9 | Cost Management budget + action group (only when `alertEmail` set) | `Microsoft.Consumption/budgets` + `Microsoft.Insights/actionGroups` | The billing backstop to the app spend breaker - `ai-cost-gate/06` |

The App Service Plan is part of resource #2 (App Service cannot run without a
plan). Application Insights is workspace-based, so it needs the Log Analytics
workspace alongside it (two ARM resources, one concern).

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

## AI cost gate (ai-cost-gate/06)

The infrastructure seam for the AI cost gate (ADR 0001). This story preps the Bicep;
the owner runs the provisioning ("I prep the Bicep, you provision"). Everything here
has safe defaults so `az bicep build` and an AI-free deploy still work, and - crucially
- **the app still runs with NONE of this provisioned**: the AI proxy (story 01) and the
moderation seam (story 05) no-op when their `Ai:*` / `ContentSafety:*` config is absent.

What it adds:

- **AI Foundry (Azure OpenAI) account + `gpt-4o-mini` deployment** (`resource 7`). The
  provider the server-side proxy (story 01) calls. The endpoint and deployment name are
  surfaced to the API as the `Ai__Endpoint` and `Ai__Deployment` app settings
  (double-underscore = ASP.NET Core's `Ai:Endpoint` / `Ai:Deployment` nested-key
  convention). Both are **config, not secrets** (the endpoint is a public URL).
- **Keyless credential (preferred).** The API App Service's SystemAssigned identity is
  granted the built-in **Cognitive Services OpenAI User** role
  (`5e0bd9bd-7b93-4f28-af87-19fc36ad61bd`) scoped to the Foundry account - the same
  role-assignment pattern as the Key Vault grant. No API key is stored or committed; the
  identity authenticates against the data plane via Azure AD (the account sets a
  `customSubDomainName`, which token auth requires). NEVER a `VITE_*` var.
- **Optional Azure AI Content Safety** (`resource 8`) behind the `deployContentSafety`
  bool param, **defaulting `false`**. When off, nothing is provisioned and story 05
  no-ops. When on, its key is stored as the `ContentSafetyKey` Key Vault secret and
  surfaced to the API as the `ContentSafety__Key` **Key Vault reference** app setting -
  the same secret-safe path as the App Insights connection string.
- **Cost Management budget + email action group** (`resource 9`), provisioned **only
  when `alertEmail` is supplied**. A resource-group-scoped `Microsoft.Consumption/budgets`
  at `monthlyBudgetUsd` (default **$20**), `Monthly` grain, with notifications at **25 /
  50 / 75 / 100%** Actual plus a **Forecasted 100%** early warning, wired to a
  `Microsoft.Insights/actionGroups` email receiver.

**Budget lag - why the app breaker is the real enforcer.** Azure budget evaluation is
billing-driven and **lags hours** (typically evaluated every 8-24h). So the budget is the
authoritative-but-slow **backstop** that catches everything (AI + infra); the **app spend
breaker (story 04) is the real-time enforcer** that stops AI calls the moment the running
estimate hits the ceiling. The two target the same 25/50/75/100% thresholds and are
reconciled periodically - do not rely on the budget alone to stop a runaway.

### Provisioning hand-off (what the owner runs)

The email is a **deploy input, never committed** (AC-05/AC-07). Provision (or
re-provision) with it:

```bash
az deployment group create \
  -g quibblestone-uat-rg \
  -f infra/main.bicep \
  -p infra/main.uat.bicepparam \
  -p alertEmail='you@example.com'          # arms the budget + action group
# optional extras:
#   -p deployContentSafety=true            # add the second moderation layer
#   -p monthlyBudgetUsd=20                 # override the $20 default ceiling
#   -p budgetStartDate='2026-07-01'        # first of the month you provision in
```

- **Leave `alertEmail` unset** and the budget + action group are simply skipped - the
  Foundry + deployment + keyless role still deploy, and the app runs; only the billing
  backstop is absent (the story-04 app breaker still enforces in real time).
- **No secrets or keys** are passed on the command line: the Foundry credential is
  keyless (managed identity), and the optional Content Safety key is read into Key Vault
  at deploy time - never a committed literal.
- **RBAC propagation:** as with the Key Vault grant, the OpenAI-User role can take a few
  minutes to propagate on first deploy; until it does the proxy fails soft (typed
  "unavailable", degrades to the deterministic fallback) rather than erroring.

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
