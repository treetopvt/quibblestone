<!--
  Runbook: the two-lane deployment model (platform-devops story 07). QA (auto from
  main) sits in front of BETA (the existing site, promoted only on a version tag).
  This is the OPERATING GUIDE - read the "How to deploy" section before shipping.
  Companion to docs/features/platform-devops/07-qa-promotion-lane.md and the original
  docs/runbooks/deploy-to-uat.md (the OIDC bootstrap in its Part 1 still applies).

  Prose uses hyphens, colons, parentheses - never em dashes.
-->

# Runbook: QA lane + tag-based promotion to beta

**Status: LIVE as of 2026-07-08.** Both lanes are running; beta was cut over to
tag-only (legacy `deploy.yml` removed, PR #200). The one-time bootstrap (Part 2) is
done - it is kept here for reproducibility. Day to day, you only need "How to deploy".

## The model

```
main merges ─(auto)─▶  QA  (Playground/PAYG: quibblestone-qa-rg westus2 + quibblestone-ai-qa-rg eastus2)
                        │                        https://qa.quibblestone.com  (F1, $0)
                  git tag v* ─(deliberate)─▶  BETA  (student sub: quibblestone-uat-rg eastus2)
                                                 https://quibblestone.com  (B1)
```

- **QA** auto-deploys on every push to `main` (`.github/workflows/deploy-qa.yml`). Its
  whole footprint is on the **Playground PAYG subscription** and fully isolated (own
  Storage / Key Vault / App Insights + own AI budget), so the infra overhaul is shaken
  out here and cannot touch testers.
- **BETA** is the existing site, physically unchanged (`environmentName=uat` in
  `quibblestone-uat-rg`, `quibblestone.com`). It deploys ONLY when you push a `v*` tag
  (`.github/workflows/promote-beta.yml`), which checks out the tagged commit - so beta
  ships exactly what you validated in QA, not whatever `main` is at promotion time.
- Both lanes share one engine, `.github/workflows/deploy-env.yml` (the reusable core),
  so the CORS / AI / Stripe / email wiring can never drift between them.

## How to deploy (quick reference)

- **Ship to QA** (staging / proving ground): just **merge to `main`**. `Deploy QA` runs
  automatically and updates `https://qa.quibblestone.com`. (F1 = ~40s cold start on the
  first hit after idle.)
- **Promote to BETA** (the friends-and-family site) - a deliberate release. Promote the
  exact commit you validated in QA:
  ```bash
  git tag v0.2.0 <sha-validated-in-qa>   # semver; bump from the last tag
  git push origin v0.2.0                 # fires "Promote to Beta"
  ```
  (Or cut a GitHub Release with a new `v*` tag - creating the tag fires the same run.)
  Then smoke-check `https://quibblestone.com` (`/` 200, hub shows "Connected") and the
  API `/health`.
- **Roll back / re-promote beta:** GitHub -> Actions -> **Promote to Beta** -> Run
  workflow -> set `ref` to an earlier tag/SHA (e.g. `v0.1.0`). Beta redeploys that ref;
  no tags move.
- **Scale a lane's SKU / region:** beta uses **Provision UAT** (`provision.yml`). QA's
  SKU is preserved across deploys, so change it with a one-off
  `az appservice plan update --sku B1 -g quibblestone-qa-rg -n quibblestone-qa-plan --subscription <playground>`.
- Last beta baseline tag: **`v0.1.0`** (2026-07-08).

## Cost at a glance

QA's app runs on the **Playground PAYG subscription**, not the student sub that hosts
beta - because the student sub caps App Service at ONE plan (B1 quota 1, held by beta;
F1 quota 0), so any second plan fails preflight with `SubscriptionIsOverQuotaForSku`. On
Playground, QA runs **`F1` (Free, $0)**. F1 has no Always On (cold starts) and a
5-WebSocket cap (a 6-player room cannot form) - fine for validating the lane, the
pipeline, and infra changes; bump `APP_SERVICE_PLAN_SKU` to `B1` (~$13/mo) for a real
6-player load test. QA AI has its own small budget (default `$10`/mo, separate from
beta's `$20`); everything else (SWA, SignalR, Storage, Key Vault) is Free/near-zero.

## Part 1 - Verify a QA deploy (after any merge)

- The `Deploy QA` run is green; `https://qa.quibblestone.com` loads and shows
  **Connected** (the SignalR hub handshake).
- `GET https://<qa-api-host>/health` returns healthy (the app route responds on any SKU;
  App Service's own health-check *monitoring* is paid-tier only, so on F1 the route works
  but Azure does not auto-probe it).
- CORS on the QA API lists the qa origins - `Cors__AllowedOrigins__0` = the raw SWA host,
  `__1` = `https://qa.quibblestone.com`. The "Set API CORS origins" step rebuilds this
  each run from the SWA default host + every Ready custom domain, so it self-maintains.

## Part 2 - One-time bootstrap (owner-only; DONE 2026-07-08, kept for reproducibility)

> **OUTSTANDING (added 2026-07-09, story 08):** the original 2026-07-08 bootstrap did NOT
> register `Microsoft.ContainerInstance` (story 08's `deploymentScripts` signing-key
> provisioner did not exist yet). Until it is registered on the qa PAYG subscription, every
> QA deploy after platform-devops/08 (#207) FAILS at the signing-key step. Run the single
> `az provider register -n Microsoft.ContainerInstance` command in step 1c below (and the
> same on the beta subscription before promoting Wave 1). Everything else in Part 2 is done.

Prerequisite: the original OIDC bootstrap from
[`deploy-to-uat.md`](./deploy-to-uat.md) Part 1 (the app registration
`quibblestone-github-oidc` + the three repo secrets). QA adds two resource groups on
the **Playground PAYG sub**, the role grants there, one federated subject, a provider
registration, and the QA GitHub Environment. Beta is untouched.

> Windows note: with two subscriptions in play, run `az` from PowerShell if any argument
> starts with `/` (Git Bash mangles those), and beware the shared active-subscription -
> pass `--subscription` explicitly. See memory note `az-cli-on-windows-gotchas`.

> **App Service quota on Playground is REGIONAL** (even though the preflight error shows
> a blank location): `eastus2`/`eastus` = 0, but **westus2, centralus, westeurope,
> eastasia** have quota. QA's app RG is therefore in **westus2**, and an RG must be
> created in the SAME region as its resources (else `az group create -l westus2` fails
> "RG already exists in eastus2"). The AI RG stays in **eastus2** (gpt-5-mini
> GlobalStandard has Cognitive-Services quota there - a separate quota from App Service).

```bash
APP_ID="$(az ad app list --display-name quibblestone-github-oidc --query '[0].appId' -o tsv)"
SP_ID="$(az ad sp show --id "$APP_ID" --query id -o tsv)"
PLAY="52bec743-..."   # Playground (PAYG) - hosts the ENTIRE QA footprint (app + AI)

# 1a. QA app footprint RG - WESTUS2 (App Service quota; see the note above)
az group create -n quibblestone-qa-rg -l westus2 --subscription "$PLAY"
# Contributor to create resources; RBAC-admin because main.bicep assigns the API
# identity "Key Vault Secrets User" (a role assignment needs role-grant rights).
az role assignment create --assignee-object-id "$SP_ID" --assignee-principal-type ServicePrincipal \
  --role "Contributor" --scope "/subscriptions/${PLAY}/resourceGroups/quibblestone-qa-rg"
az role assignment create --assignee-object-id "$SP_ID" --assignee-principal-type ServicePrincipal \
  --role "Role Based Access Control Administrator" --scope "/subscriptions/${PLAY}/resourceGroups/quibblestone-qa-rg"

# 1b. QA AI cost-gate RG - EASTUS2 (Cognitive Services quota for the model)
az group create -n quibblestone-ai-qa-rg -l eastus2 --subscription "$PLAY"
az role assignment create --assignee-object-id "$SP_ID" --assignee-principal-type ServicePrincipal \
  --role "Contributor" --scope "/subscriptions/${PLAY}/resourceGroups/quibblestone-ai-qa-rg"
az role assignment create --assignee-object-id "$SP_ID" --assignee-principal-type ServicePrincipal \
  --role "Role Based Access Control Administrator" --scope "/subscriptions/${PLAY}/resourceGroups/quibblestone-ai-qa-rg"

# 1c. Register the resource providers Playground was missing. main.bicep provisions Azure
#     SignalR, and (platform-devops/08, ADR 0003) a Microsoft.Resources/deploymentScripts
#     that mints the durable magic-link signing key - deploymentScripts run inside an Azure
#     Container Instance, so Microsoft.ContainerInstance MUST be registered or the deploy
#     fails with "does not have authorization to perform action
#     'Microsoft.ContainerInstance/register/action'" (the CI OIDC identity cannot self-register
#     at subscription scope). The rest (Storage/KeyVault/Insights/OperationalInsights/Web/
#     CognitiveServices) were already registered. Registration is async (~1-2 min to
#     "Registered"); it is permanent per subscription.
az provider register -n Microsoft.SignalRService --subscription "$PLAY"
az provider register -n Microsoft.ContainerInstance --subscription "$PLAY"
# NOTE: the BETA lane's subscription (the student sub, $SUB) needs the SAME
# Microsoft.ContainerInstance registration before Wave 1 (story 08) is promoted to beta,
# or the first beta promotion carrying the deploymentScript fails identically:
#   az provider register -n Microsoft.ContainerInstance --subscription "$SUB"

# 1d. Trust GitHub Actions running in the QA environment (beta reuses the existing
#     environment:uat subject, so it needs no new subject).
az ad app federated-credential create --id "$APP_ID" --parameters '{
  "name": "quibblestone-env-qa",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:treetopvt/quibblestone:environment:qa",
  "audiences": ["api://AzureADTokenExchange"]
}'
```

### 1e. The QA GitHub Environment + its vars

GitHub -> Settings -> Environments -> **New environment** `qa`, then add these
**Environment variables** (they override the repo-level defaults for the QA lane only):

| Variable | Value | Why |
|---|---|---|
| `AZURE_LOCATION` | `westus2` | Playground App Service quota is regional; westus2 has it (eastus2 = 0) |
| `APP_SERVICE_PLAN_SKU` | `F1` | Free/$0. Bump to `B1` (~$13/mo) for a real 6-player load test |
| `STRIPE_ENABLED` | `false` | QA stays minimal - overrides the repo `true` so QA needs no Stripe KV secrets |
| `EMAIL_ENABLED` | `false` | Same - no magic-link/ACS email footprint in QA |
| `AI_MONTHLY_BUDGET_USD` | `10` | QA's own AI budget, isolated from beta's $20 |
| `VITE_GA4_MEASUREMENT_ID` | `" "` (single space) | Disables GA4 on qa (`readId` trims it to empty) so qa test traffic never pollutes analytics |
| `VITE_CLARITY_PROJECT_ID` | `" "` (single space) | Same - disables Clarity on qa |

Everything else (`AZURE_*` secrets, `AI_SUBSCRIPTION_ID`, `AI_ALERT_EMAIL`) is repo-level
and shared - QA inherits it. `deploy-qa.yml` fixes the app + AI subscription/RG for the
lane (Playground), so those are not vars.

## Part 3 - The cutover (DONE 2026-07-08)

Beta was cut over to tag-only by removing the legacy `deploy.yml` (PR #200), after the
QA lane was proven and the first `v0.1.0` tag deployed beta cleanly via `promote-beta`.
`main` now deploys ONLY to QA; beta moves ONLY on a `v*` tag. `provision.yml` remains
beta's SKU/scale lever. (To reverse: restore a push-triggered deploy for the uat RG.)

## The QA custom domain (qa.quibblestone.com) - DONE 2026-07-08

DNS is at Cloudflare (zone `7231a83313b728556cf469546c8df29a`); it was added via the CF
API (token in memory note `cloudflare-dns-token`). To add another subdomain (`play.`,
`dev.`, ...) later, the recipe is:

```bash
# 1. Cloudflare CNAME, DNS-only (grey cloud - required for SWA's managed cert):
curl -sS -X POST "https://api.cloudflare.com/client/v4/zones/7231a83313b728556cf469546c8df29a/dns_records" \
  -H "Authorization: Bearer <cf-token>" -H "Content-Type: application/json" \
  --data '{"type":"CNAME","name":"<sub>","content":"<swa-default-host>","ttl":1,"proxied":false}'
# 2. Bind + validate (SWA issues the TLS cert; goes Validating -> Ready in a few min):
az staticwebapp hostname set -n <swa-name> -g <rg> --subscription <sub> --hostname <sub>.quibblestone.com
# 3. CORS: the deploy auto-discovers Ready SWA custom domains, or set it directly:
az webapp config appsettings set -g <rg> -n <api-name> --subscription <sub> \
  --settings Cors__AllowedOrigins__1=https://<sub>.quibblestone.com
```

Free SWA = 2 custom-domain slots, but PER-SWA: qa's SWA (1 used) is independent of beta's
(apex + www, both used).

## Notes / gotchas

- **AI principal is discovered, not committed.** The core reads the lane's API managed
  identity at deploy time and passes it to `ai.bicep`, so QA and beta grant their own
  identities with no hardcoded GUID.
- **QA AI quota fits.** gpt-5-mini GlobalStandard = 500 TPM on the PAYG sub; beta + QA
  each deploy capacity 10.
- **Turning QA knobs on later.** To test Stripe/email in QA, flip that lane's
  `STRIPE_ENABLED`/`EMAIL_ENABLED` var to `true` and add the secrets to QA's OWN Key
  Vault (see `enable-stripe-billing.md` / `enable-magic-link-email.md`).
- **Rename beta later (optional).** Beta is still physically `environmentName=uat`;
  renaming to `beta` means re-provisioning + re-binding `quibblestone.com`, so it was
  deferred. Do it in a quiet window if the naming bothers you.
