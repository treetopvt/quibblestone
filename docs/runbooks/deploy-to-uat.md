<!--
  Runbook: continuous delivery to UAT (platform-devops story 03). This is the
  owner-run, one-time setup that turns the committed Bicep + GitHub workflows
  into a self-deploying UAT environment.

  Everything CI/CD-related runs in GitHub Actions with OIDC (no stored
  credentials). The ONLY manual, owner-only step is the credential bootstrap in
  Part 1 - it needs Azure Owner/Administrator rights, so it cannot run in CI.
  After that, provisioning is a button and deploys are automatic on merge.

  Prose uses hyphens, colons, parentheses - never em dashes.
-->

# Runbook: Continuous delivery to UAT

Stands up UAT and makes `main` deploy itself to it. Three parts, run in order:

1. **Bootstrap** the OIDC federated credential + set three repo secrets (Part 1).
   One-time, owner-only (needs Azure Owner rights).
2. **Provision** the Azure footprint from GitHub - a button, no local `az`
   (Part 2).
3. **Deploy** happens automatically on every merge to `main`; smoke-check it
   (Parts 3-4).

## Cost at a glance

Everything is Free/near-zero except the App Service Plan, and that defaults to
**F1 (Free, $0)** - so standing up UAT costs nothing.

| Resource | SKU | Cost |
|---|---|---|
| Static Web App | Free | $0 |
| Azure SignalR | Free_F1 | $0 |
| Storage | Standard_LRS | pennies (near-zero at rest) |
| Key Vault | standard | pennies (per-operation) |
| **App Service Plan** | **F1 (default)** | **$0** |

F1 has no Always On (the app sleeps when idle), a 60 CPU-min/day cap, and limited
WebSockets - fine for kicking the tyres, but the real-time flow may drop
connections. When you want a solid demo, **scale up to B1** (~$13/mo, Always On +
reliable WebSockets) by re-running Provision UAT with `sku=B1` (Part 2). No code
change; switch back to F1 the same way to stop paying.

## Part 1 - One-time OIDC bootstrap (owner-only)

This is the only step that needs your own Azure credentials. It creates an app
registration GitHub can log in as (via a short-lived federated token - no stored
password), scopes it to your subscription, and tells it to trust this repo.

```bash
az login
SUBSCRIPTION_ID="$(az account show --query id -o tsv)"
TENANT_ID="$(az account show --query tenantId -o tsv)"

# 1. Create the app registration + service principal GitHub will act as.
APP_ID="$(az ad app create --display-name quibblestone-github-oidc --query appId -o tsv)"
az ad sp create --id "$APP_ID"

# 2. Trust GitHub Actions from THIS repo. One federated credential per subject.
#    a) pushes/merges to main (the Deploy workflow):
az ad app federated-credential create --id "$APP_ID" --parameters '{
  "name": "quibblestone-main",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:treetopvt/quibblestone:ref:refs/heads/main",
  "audiences": ["api://AzureADTokenExchange"]
}'
#    b) the uat environment (Provision + Deploy both target environment: uat):
az ad app federated-credential create --id "$APP_ID" --parameters '{
  "name": "quibblestone-env-uat",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:treetopvt/quibblestone:environment:uat",
  "audiences": ["api://AzureADTokenExchange"]
}'

# 3. Let that identity create + manage resources in the subscription.
#    (Scope to a pre-created resource group instead if you prefer least-privilege.)
az role assignment create \
  --assignee "$APP_ID" \
  --role Contributor \
  --scope "/subscriptions/${SUBSCRIPTION_ID}"

echo "AZURE_CLIENT_ID       = $APP_ID"
echo "AZURE_TENANT_ID       = $TENANT_ID"
echo "AZURE_SUBSCRIPTION_ID = $SUBSCRIPTION_ID"
```

Then set those three as repo **secrets** (Settings -> Secrets and variables ->
Actions -> Secrets): `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`,
`AZURE_SUBSCRIPTION_ID`. They are identifiers, not passwords, but keeping them as
secrets is the convention.

Optional: set the variable `AZURE_RESOURCE_GROUP` if you want a group name other
than the default `quibblestone-uat-rg`.

> Note on federated subjects: the Deploy job runs on a push to `main` **and** in
> the `uat` environment, so both credentials above are created. If you later add
> a required reviewer to the `uat` environment, the `environment:uat` subject is
> the one that authorizes it.

`Part 1 met` when the three secrets exist and the app registration has the two
federated credentials + the Contributor assignment.

## Part 2 - Provision the footprint (a button, no local az)

GitHub -> Actions -> **Provision UAT** -> Run workflow. Inputs:

- `sku` - App Service Plan tier. `F1` (default, Free/$0) or `B1`/`B2`/`S1`.
- `location` - region (default `eastus`).
- `resource_group` - group name (default `quibblestone-uat-rg`, created if absent).

It logs in via OIDC and runs `infra/main.bicep`. Idempotent: re-run any time to
**scale the plan** (pick a different `sku`), change region, or apply infra
changes. It never deletes anything.

`Part 2 met` when the run is green (the job summary lists the group + SKU).

Prefer local `az`? The equivalent is still available:

```bash
az group create -n quibblestone-uat-rg -l eastus
az deployment group create -g quibblestone-uat-rg \
  -f infra/main.bicep -p infra/main.uat.bicepparam
```

## Part 3 - Deploy (automatic on merge to main)

Nothing to run. When an approved PR merges to `main`, the **Deploy** workflow:

1. builds + tests both projects (a broken tree never deploys),
2. logs into Azure via OIDC,
3. **discovers** the API + web resources in the group (tagged
   `app=quibblestone`) - so there are no publish profiles, tokens, or URLs to
   copy anywhere,
4. sets the API's CORS origin to the web origin (required for the SignalR hub -
   see `api/src/Program.cs`),
5. deploys the API to App Service, builds the web bundle pointed at the
   discovered API URL, and deploys it to the Static Web App.

To re-run without a merge: Actions -> **Deploy** -> Run workflow.

## Part 4 - Smoke check

1. **API health** - `curl -s https://<api-host>/health` -> HTTP 200 and a small
   JSON body. (Find `<api-host>` in the Deploy run log's "Discover Azure
   resources" step, or `az webapp list -g quibblestone-uat-rg -o table`.)
2. **Web reaches the hub** - open the Static Web App URL (also in the Deploy log,
   and linked on the `uat` environment in GitHub). The `ConnectionStatus` chip
   should read **"Connected"** (it reads "Disconnected" if the hub handshake
   fails - usually CORS, which Part 3 step 4 sets automatically).

Done when `/health` returns 200 and the web client shows "Connected".

## Teardown

```bash
az group delete -n quibblestone-uat-rg --yes --no-wait
```

Removes all resources (Key Vault has soft-delete on, so its name is reserved
until purged or the retention window lapses).

## Deliberately deferred (do not gold-plate)

Environments beyond UAT, a manual approval gate (add a required reviewer on the
`uat` GitHub Environment - no code change), production hardening (VNet, private
endpoints, diagnostics), wiring the app to the provisioned Azure SignalR Service,
and custom domains. See `infra/README.md`.
