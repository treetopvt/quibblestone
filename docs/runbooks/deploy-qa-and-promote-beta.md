<!--
  Runbook: the two-lane deployment model (platform-devops story 07). Turns the one
  auto-deploying UAT environment into: QA (auto from main) in front of BETA (the
  existing site, promoted only on a version tag). Companion to the story
  docs/features/platform-devops/07-qa-promotion-lane.md and the original
  docs/runbooks/deploy-to-uat.md (Part 1 OIDC bootstrap still applies).

  Owner-run parts (Azure Owner/RBAC-admin rights) are Part 1 only; after that QA is
  automatic and beta is a tag push. Prose uses hyphens, colons, parentheses - never
  em dashes.
-->

# Runbook: QA lane + tag-based promotion to beta

## The model

```
main merges ─(auto)─▶  QA  (quibblestone-qa-rg, its own data + AI + budget)
                        │
                  git tag v* ─(deliberate)─▶  BETA  (quibblestone-uat-rg + quibblestone.com)
```

- **QA** auto-deploys on every push to `main` (`.github/workflows/deploy-qa.yml`).
  Its own resource group, so its Storage / Key Vault / App Insights and its own AI
  cost-gate RG + budget are fully isolated - the infra overhaul is shaken out here
  and cannot touch testers.
- **BETA** is today's site, physically unchanged (still `environmentName=uat` in
  `quibblestone-uat-rg`, keeps `quibblestone.com`). It deploys ONLY when you push a
  `v*` tag (`.github/workflows/promote-beta.yml`), which checks out the tagged
  commit - so beta ships exactly what you validated in QA, not whatever `main` is
  at promotion time.
- Both lanes share one engine, `.github/workflows/deploy-env.yml` (the reusable
  core), so the CORS / AI / Stripe / email wiring can never drift between them.

> **Do Part 1 BEFORE merging this branch.** Until the QA resource groups, the CI
> role grants, and the `environment:qa` OIDC subject exist, the first
> `Deploy QA` run will fail at Azure login (harmlessly - it never touches beta).
> Beta keeps auto-deploying from `main` via the legacy `deploy.yml` until you do
> the cutover in Part 4, so nothing about the testers' site changes until you say so.

## Cost at a glance

QA roughly **doubles hosting** while it is scaled up: the only real cost is the
App Service Plan, and QA defaults to `B1` (~$13/mo) so multiplayer is realistic
during testing. Drop QA to `F1` ($0) when idle (set its `APP_SERVICE_PLAN_SKU` var
to `F1` and re-run, or use Provision UAT-style scaling). QA AI has its own small
budget (default `$10`/mo, separate from beta's `$20`). Everything else (SWA,
SignalR, Storage, Key Vault) is Free/near-zero per lane.

## Part 1 - One-time QA bootstrap (owner-only)

Prerequisite: the original OIDC bootstrap from
[`deploy-to-uat.md`](./deploy-to-uat.md) Part 1 is already done (the app
registration `quibblestone-github-oidc`, the three repo secrets, subscription
Contributor). We are only adding QA's resource groups, role grants, and one
federated subject.

> Windows note: with two subscriptions in play, isolate the local `az` session so a
> parallel shell cannot flip your active subscription mid-run
> (`AZURE_CONFIG_DIR=$(mktemp -d)` before `az login`), and run `az` from PowerShell
> if any argument starts with `/` (Git Bash mangles those). See the memory note
> `az-cli-on-windows-gotchas`.

```bash
# The app registration GitHub logs in as (its appId == the AZURE_CLIENT_ID secret).
APP_ID="$(az ad app list --display-name quibblestone-github-oidc --query '[0].appId' -o tsv)"

# --- 1a. QA app footprint (student/app subscription - the default sub) ---------
APP_SUB="$(az account show --query id -o tsv)"   # af7f9e54... (Azure for Students)
az group create -n quibblestone-qa-rg -l eastus2

# The identity already has subscription Contributor (from the original bootstrap),
# which covers creating the QA RG + its resources. main.bicep ALSO creates a role
# assignment (the API identity -> Key Vault Secrets User), which needs role-grant
# rights - add them, scoped to the QA RG (least privilege, mirrors the UAT RG):
az role assignment create --assignee "$APP_ID" \
  --role "User Access Administrator" \
  --scope "/subscriptions/${APP_SUB}/resourceGroups/quibblestone-qa-rg"

# Trust GitHub Actions running in the QA environment (beta reuses the existing
# environment:uat subject, so no new subject is needed for it).
az ad app federated-credential create --id "$APP_ID" --parameters '{
  "name": "quibblestone-env-qa",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:treetopvt/quibblestone:environment:qa",
  "audiences": ["api://AzureADTokenExchange"]
}'

# --- 1b. QA AI cost-gate footprint (PAYG "Playground" subscription) ------------
AI_SUB="52bec743-..."   # the Pay-As-You-Go sub that hosts Azure OpenAI
az account set -s "$AI_SUB"
az group create -n quibblestone-ai-qa-rg -l eastus2
# Contributor to create the Azure OpenAI account + model deployment; role-grant
# rights for the cross-sub "Cognitive Services OpenAI User" assignment ai.bicep makes.
az role assignment create --assignee "$APP_ID" --role Contributor \
  --scope "/subscriptions/${AI_SUB}/resourceGroups/quibblestone-ai-qa-rg"
az role assignment create --assignee "$APP_ID" --role "User Access Administrator" \
  --scope "/subscriptions/${AI_SUB}/resourceGroups/quibblestone-ai-qa-rg"
az account set -s "$APP_SUB"   # back to the app sub
```

### 1c. The QA GitHub Environment + its vars

GitHub -> Settings -> Environments -> **New environment** named `qa`. Then add
these **Environment variables** (Settings -> Environments -> qa -> Environment
variables). They override the repo-level defaults ONLY for the QA lane:

| Variable | Value | Why |
|---|---|---|
| `APP_SERVICE_PLAN_SKU` | `B1` | Always On + WebSockets so QA multiplayer is realistic (set `F1` to park at $0) |
| `STRIPE_ENABLED` | `false` | QA stays minimal - overrides the repo-level `true` so QA needs no Stripe Key Vault secrets |
| `EMAIL_ENABLED` | `false` | Same - no magic-link/ACS email footprint in QA (overrides repo `true`) |
| `AI_MONTHLY_BUDGET_USD` | `10` | QA's own smaller AI budget, isolated from beta's $20 |

Everything else (the three `AZURE_*` secrets, `AI_SUBSCRIPTION_ID`,
`AI_ALERT_EMAIL`) is repo-level and shared - QA inherits it automatically. The QA
AI RG is fixed by the workflow (`quibblestone-ai-qa-rg`), not a var.

**Part 1 met** when: both QA resource groups exist, the CI identity has the grants
above, the `environment:qa` federated credential exists, and the `qa` Environment
has its four vars.

## Part 2 - First QA deploy + smoke check

Trigger it: merge to `main`, or GitHub -> Actions -> **Deploy QA** -> Run workflow.
The first run auto-provisions the whole QA footprint (same first-deploy behavior
UAT had) and deploys. Then check:

- The `Deploy QA` run is green; its environment URL (the QA SWA hostname) loads and
  shows **Connected** (the SignalR hub handshake).
- `GET https://<qa-api-host>/health` returns healthy (B1+ only).
- Two phones/tabs can create + join a QA room and complete a round.
- **Verify CORS** on the QA API (a saved note once claimed only `__0` was set):
  ```bash
  az webapp config appsettings list -g quibblestone-qa-rg -n <qa-api-name> \
    --query "[?starts_with(name,'Cors__AllowedOrigins__')].{n:name,v:value}" -o table
  ```
  Expect the QA SWA origin (and any bound custom domain). If only `__0` shows,
  update the memory note / the CORS step - do not carry the stale assumption into beta.

From here, every merge to `main` redeploys QA automatically.

## Part 3 - Promote a release to beta

Once a `main` commit is proven in QA, promote that exact commit:

```bash
git tag v1.0.0 <sha-validated-in-qa>
git push origin v1.0.0
```

(or cut a GitHub Release with a new `v*` tag - creating the tag fires the same
workflow). **Promote to Beta** runs against the tagged commit and ships beta. It
reuses the existing `uat` Environment + physical names, so beta behaves exactly as
it does today - just gated behind your deliberate tag instead of every merge.

Smoke-check beta the same way (its URL is `quibblestone.com`).

**Rollback / re-promote:** GitHub -> Actions -> **Promote to Beta** -> Run workflow
-> set `ref` to an earlier tag or SHA (e.g. `v0.9.0`). Beta redeploys that ref; no
tags move.

## Part 4 - Cutover: freeze beta from `main` (do this last)

This is the switch that actually protects testers from the overhaul. Until now,
the legacy `.github/workflows/deploy.yml` still auto-deploys beta on every merge
(so nothing regressed while you built the QA lane). Once QA is proven AND you have
cut at least one `v*` tag through **Promote to Beta** (so beta's promote path is
exercised), retire the legacy auto-deploy:

```bash
git rm .github/workflows/deploy.yml   # promote-beta.yml now owns beta (tag-gated)
```

After this: `main` -> QA only (auto); beta -> `v*` tags only (deliberate). Start the
infra overhaul. Provision UAT (`provision.yml`) still works as the beta SKU/scale
button and is unaffected.

## Notes / gotchas

- **Why beta was not renamed.** Renaming to `environmentName=beta` would re-provision
  new resources and force re-binding `quibblestone.com` + re-seeding Key Vault right
  before testers arrive. "beta" is a lane label; the site stays physically `uat`.
  Rename later in a quiet window if desired.
- **AI principal is discovered, not committed.** The core reads the lane's API
  managed-identity principal at deploy time and passes it to `ai.bicep`, so QA and
  beta grant their own identities with no hardcoded GUID (`ai.qa.bicepparam` omits it).
- **QA quota fits.** GlobalStandard gpt-5-mini quota is 500 TPM on the PAYG sub;
  beta + QA each deploy capacity 10, so 20 total - no conflict.
- **Turning QA knobs on later.** To test Stripe or email in QA, set that lane's
  `STRIPE_ENABLED`/`EMAIL_ENABLED` var to `true` and add the required secrets to
  QA's OWN Key Vault (see `enable-stripe-billing.md` / `enable-magic-link-email.md`).
- **A pretty QA URL** (`qa.quibblestone.com`) is optional: bind it as a custom domain
  on the QA SWA (Cloudflare CNAME, DNS-only) - CORS auto-discovers it, no code change.
