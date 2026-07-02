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
