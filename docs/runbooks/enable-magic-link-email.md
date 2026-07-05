<!--
  Runbook: turning on magic-link email delivery (accounts-identity/04, #167) so a
  deployed environment actually emails the one-time sign-in / operator-login link.
  Owner-run handoff: the code + the deploy wiring are committed (I prep); the Azure
  Communication Services resource, the verified sender domain, the Key Vault secret
  values, and the repo variables are yours to set.

  Prose uses hyphens, colons, parentheses - never em dashes.
-->

# Runbook: Enabling magic-link email delivery

The whole email seam is **built and shipped** - it just ships **disabled**. With no
`Email:*` provider configured, the API registers the `NoOpEmailSender`, so a deployed
environment behaves exactly as before: the request endpoints still return the neutral
"check your inbox" acknowledgement, and `Development` still echoes the token so the
flow is walkable locally. "Turning it on" is therefore **configuration, not code**:
give the deployed API an Azure Communication Services (ACS) Email sender + a verified
from-address, and set a **durable** magic-link signing key.

Until this is done, magic-link sign-in (purchaser **and** operator) cannot complete
in a deployed environment - there is no path for the one-time link to reach the
inbox. That includes the operator back office (`sysadmin-console`), which is unusable
on UAT without delivery.

## What the code already does (why this is durable)

- **One seam, both flows.** `api/src/Accounts/IEmailSender.cs` is the single transport
  both `AccountsController.RequestLink` (purchaser) and
  `OperatorLoginController.RequestLink` (operator) deliver through. They differ only in
  the copy and the link path (`/account` vs `/admin/login`), never in how mail is sent.
- **Config-presence gate.** `Program.cs` reads the `Email` section once at startup:
  absent => `NoOpEmailSender`; present (a from-address plus an endpoint or a connection
  string) => the real `AcsEmailSender`. Exactly the `ITelemetrySink` / `IAiCompletionClient`
  idiom.
- **Fail-safe.** A provider error is caught in the controller, logged **without** the
  token / link / email / secret, and the endpoint still returns the neutral 200 - a
  delivery failure is never a 500 and never an account-existence oracle. The per-IP
  `SignInRateLimit` / `OperatorLoginRateLimit` policies remain the abuse boundary.
- **Minimal content.** The email carries only the one-time link + minimal copy, to the
  entered address only - no player nickname, room code, or session id.

Because `infra/main.bicep` **replaces** the API's `appSettings` array on every deploy,
the settings below are re-applied by the **"Wire magic-link email delivery (optional)"**
step in `.github/workflows/deploy.yml` after the provision, gated on
`vars.EMAIL_ENABLED == 'true'` (the same pattern as CORS / the AI gate / Stripe). Leave
`EMAIL_ENABLED` unset and everything here is skipped - email stays cleanly off.

## The two provider paths

| Path | What you set | Provider secret? |
|---|---|---|
| **Keyless (recommended)** | `Email__Endpoint` (the ACS resource endpoint) + the API's managed identity granted a send role on the ACS resource | **None** - the managed identity authenticates, so there is no secret to custody (AC-05) |
| **Keyed (fallback)** | `Email__ConnectionString` from Key Vault (contains an access key) | Yes - one Key Vault secret, never committed / never a `VITE_*` var |

Both need a **verified sender from-address** (`Email__FromAddress`) and both point the
link at the public web origin (`Email__LinkBaseUrl`, set automatically by the deploy
step to the custom domain if bound).

## Part 1 - Provision ACS Email + verify the sender domain (one time, in Azure)

1. **Create an Email Communication Service** and an **Azure Communication Service**
   resource in the resource group (Portal, or `az communication` / the ACS Bicep types).
   ACS Email is a **deliberate addition** to the five-resource footprint (README
   section 9), justified because magic-link is now load-bearing.
2. **Add and verify a custom sender domain** for `quibblestone.com` (or a subdomain):
   add the **SPF** and **DKIM** DNS records ACS gives you at your DNS host (Cloudflare,
   per the custom-domain runbook), and wait for ACS to show the domain **Verified**.
   Create the sender username `no-reply` so the from-address is
   `no-reply@quibblestone.com` (AC-09 - an unverified sender gets spam-filtered).
   - A quick alternative for a first smoke test is the ACS **Azure-managed domain**
     (a `...azurecomm.net` from-address, no DNS work), but move to the verified custom
     domain before relying on delivery.
3. **Connect** the Email Communication Service domain to the Communication Service
   resource (the "Domains" blade), so the endpoint can send from it.

## Part 2 - Grant the managed identity a send role (keyless path only)

Give the API App Service's system-assigned identity permission to send on the ACS
resource, so no connection string is needed:

```bash
rg=quibblestone-uat-rg
api="$(az webapp list -g "$rg" --query "[?tags.app=='quibblestone'].name" -o tsv)"
acs_id="$(az communication list -g "$rg" --query "[0].id" -o tsv)"
principal="$(az webapp identity show -g "$rg" -n "$api" --query principalId -o tsv)"

# "Communication and Email Service Owner" covers sending; scope it to the ACS resource.
az role assignment create \
  --assignee-object-id "$principal" --assignee-principal-type ServicePrincipal \
  --role "Communication and Email Service Owner" \
  --scope "$acs_id"
```

(Skip this on the keyed fallback path - the connection string carries its own auth.)

## Part 3 - Key Vault secret values (set once, out of band)

Set these **directly in Key Vault** (not through GitHub), so the workflow never sees
them and the values survive every deploy:

```bash
rg=quibblestone-uat-rg
vault="$(az keyvault list -g "$rg" --query "[0].name" -o tsv)"

# AC-07: a DURABLE magic-link signing key (a long random string YOU choose). Without
# this the app uses an ephemeral per-process key and a delivered link stops verifying
# the moment the App Service recycles or scales out.
az keyvault secret set --vault-name "$vault" --name AccountsTokenSigningKey --value "$(openssl rand -base64 48)"

# ONLY on the keyed fallback path (skip it on the keyless path):
az keyvault secret set --vault-name "$vault" --name EmailConnectionString --value 'endpoint=https://<acs>.communication.azure.com/;accesskey=...'
```

(Your own `az login` needs "Key Vault Secrets Officer" or similar on the vault to
write. The API identity already has read access - nothing to grant there.)

## Part 4 - Repo variables (the master switch + non-secret config)

Set these as **repository variables** (Settings -> Secrets and variables -> Actions ->
Variables) - none is secret:

| Variable | Value | Required |
|---|---|---|
| `EMAIL_ENABLED` | `true` | yes - the master switch |
| `EMAIL_FROM_ADDRESS` | `no-reply@quibblestone.com` (the verified sender) | yes when enabled |
| `EMAIL_ENDPOINT` | `https://<acs>.communication.azure.com` | yes on the keyless path; leave unset to use the keyed fallback |

```bash
gh variable set EMAIL_ENABLED --body true
gh variable set EMAIL_FROM_ADDRESS --body 'no-reply@quibblestone.com'
gh variable set EMAIL_ENDPOINT --body 'https://<acs>.communication.azure.com'
```

`Email__LinkBaseUrl` is **not** a variable - the deploy step derives it from the SWA's
bound custom domain (else the SWA default host) so the link points at the real site.

## Part 5 - Deploy and verify

1. Merge to `main` (or run the **Deploy** workflow manually). Confirm the "Wire
   magic-link email delivery (optional)" step ran (it is skipped unless
   `EMAIL_ENABLED=true`) and set `Accounts__TokenSigningKey`, `Email__FromAddress`,
   `Email__LinkBaseUrl`, and `Email__Endpoint` (or `Email__ConnectionString`).
2. On `quibblestone.com`, open the Account screen, enter your email, and request a
   link. Confirm the email arrives (check spam on the first send - if it lands there,
   the sender domain is not fully verified, revisit Part 1).
3. Follow the link and confirm sign-in completes.
4. **AC-07 check:** restart the App Service (`az webapp restart -g quibblestone-uat-rg
   -n "$api"`), request a fresh link, and confirm it still verifies after the recycle
   (with the ephemeral fallback it would not).
5. Repeat for the operator flow (`/admin/login`) with an allowlisted operator email.

## Rolling it back

Set `EMAIL_ENABLED` to anything but `true` (or delete the variable) and redeploy: the
wiring step is skipped, the Bicep provision leaves the `Email__*` settings out of the
fresh appSettings array, and the API falls back to the `NoOpEmailSender`. The Key Vault
secrets can stay - they are inert with nothing referencing them. For an **immediate**
kill without waiting for a deploy, delete the app settings by hand
(`az webapp config appsettings delete -g quibblestone-uat-rg -n "$api" --setting-names Email__Endpoint Email__ConnectionString Email__FromAddress`);
the next deploy makes it durable. Leave `Accounts__TokenSigningKey` in place - a durable
signing key is harmless (and desirable) even with delivery off.
