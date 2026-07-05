<!--
  Runbook: turning on magic-link email delivery (accounts-identity/04, #167) so a
  deployed environment actually emails the one-time sign-in / operator-login link.
  The code, the ACS Email infrastructure (infra/main.bicep, behind enableEmail), and
  the deploy wiring are ALL committed - so "turning it on" is now mostly ONE repo
  variable plus one Key Vault secret. A verified custom sender domain is an optional
  production upgrade (the one part that cannot be automated).

  Prose uses hyphens, colons, parentheses - never em dashes.
-->

# Runbook: Enabling magic-link email delivery

The whole email path is **built and shipped** - code, infrastructure, and deploy
wiring - it just ships **disabled**. With no `Email:*` provider configured, the API
registers the `NoOpEmailSender`, so a deployed environment behaves exactly as before:
the request endpoints still return the neutral "check your inbox" acknowledgement, and
`Development` still echoes the token so the flow is walkable locally.

"Turning it on" is a **repo variable plus one Key Vault secret**: set
`EMAIL_ENABLED=true` (which both PROVISIONS the Azure Communication Services (ACS) Email
footprint AND wires the API to it) and set a **durable** magic-link signing key. The
recommended keyless path needs no provider secret and no manual Azure resource creation.

Until this is done, magic-link sign-in (purchaser **and** operator) cannot complete in a
deployed environment - there is no path for the one-time link to reach the inbox. That
includes the operator back office (`sysadmin-console`), which is unusable on UAT without
delivery.

## What the code + infra already do (why turn-on is small)

- **One seam, both flows.** `api/src/Accounts/IEmailSender.cs` is the single transport
  both `AccountsController.RequestLink` (purchaser) and
  `OperatorLoginController.RequestLink` (operator) deliver through. They differ only in
  the copy and the link path (`/account` vs `/admin/login`), never in how mail is sent.
- **Config-presence gate.** `Program.cs` reads the `Email` section once at startup:
  absent => `NoOpEmailSender`; present (a from-address plus an endpoint or a connection
  string) => the real `AcsEmailSender`. Exactly the `ITelemetrySink` / `IAiCompletionClient`
  idiom.
- **Infrastructure, in Bicep (behind `enableEmail`).** `infra/main.bicep` provisions the
  ACS Email footprint - an Email Communication Service, an **Azure-managed domain** (a
  `*.azurecomm.net` sender Azure verifies for you, no DNS work), a `no-reply` sender, and
  the Communication Services resource - only when `enableEmail=true`. The deploy sets that
  from `vars.EMAIL_ENABLED`, so the same switch provisions AND wires.
- **Keyless, no secret.** The deploy grants the API's managed identity the
  **Communication and Email Service Owner** role on the ACS resource, so the app sends
  keyless (AC-05) - there is no provider secret to custody on the recommended path.
- **Fail-safe.** A provider error is caught in the controller, logged **without** the
  token / link / email / secret, and the endpoint still returns the neutral 200 - a
  delivery failure is never a 500 and never an account-existence oracle. The per-IP
  `SignInRateLimit` / `OperatorLoginRateLimit` policies remain the abuse boundary.
- **Minimal content.** The email carries only the one-time link + minimal copy, to the
  entered address only - no player nickname, room code, or session id.

Because `infra/main.bicep` **replaces** the API's `appSettings` array on every deploy,
the settings are re-applied by the **"Wire magic-link email delivery (optional)"** step
in `.github/workflows/deploy.yml` after the provision, gated on
`vars.EMAIL_ENABLED == 'true'` (the same pattern as CORS / the AI gate / Stripe). Leave
`EMAIL_ENABLED` unset and both the provisioning and the wiring are skipped - email stays
cleanly off.

## The two provider paths

| Path | What you set | Provider secret? |
|---|---|---|
| **Keyless (recommended, auto-provisioned)** | just `EMAIL_ENABLED=true` - the deploy provisions the ACS resource, grants the send role, and wires the endpoint | **None** - the managed identity authenticates (AC-05) |
| **Keyed (fallback / external ACS)** | `Email__ConnectionString` from Key Vault (contains an access key), with `EMAIL_ENDPOINT` left unset | Yes - one Key Vault secret, never committed / never a `VITE_*` var |

Both send from a **verified sender from-address**. On the keyless auto-provisioned path
the deploy derives the from-address from the Azure-managed domain
(`no-reply@<...>.azurecomm.net`); override it with `EMAIL_FROM_ADDRESS` once you move to a
verified custom domain. The link points at the public web origin automatically
(`Email__LinkBaseUrl`, the bound custom domain if there is one).

## Part 1 - Turn it on (the fast path)

1. **Set the durable signing key** in Key Vault (out of band, so the workflow never sees
   it and it survives every deploy):

   ```bash
   rg=quibblestone-uat-rg
   vault="$(az keyvault list -g "$rg" --query "[0].name" -o tsv)"

   # AC-07: a DURABLE magic-link signing key (a long random string YOU choose). Without
   # this the app uses an ephemeral per-process key and a delivered link stops verifying
   # the moment the App Service recycles or scales out.
   az keyvault secret set --vault-name "$vault" --name AccountsTokenSigningKey --value "$(openssl rand -base64 48)"
   ```

   (Your own `az login` needs "Key Vault Secrets Officer" or similar on the vault to
   write. The API identity already has read access - nothing to grant there.)

2. **Flip the master switch:**

   ```bash
   gh variable set EMAIL_ENABLED --body true
   ```

3. **Deploy** (merge to `main`, or run the **Deploy** workflow manually). On that run:
   - the **"Ensure UAT is provisioned"** step stands up the ACS Email footprint
     (`enableEmail=true`) and outputs the endpoint + Azure-managed sender address;
   - the **"Wire magic-link email delivery (optional)"** step grants the API identity the
     ACS send role and sets `Accounts__TokenSigningKey`, `Email__FromAddress`,
     `Email__LinkBaseUrl`, and `Email__Endpoint`.

That is the whole keyless turn-on. Skip to **Part 4 - Verify**. Parts 2 and 3 are the
optional custom-domain and keyed-fallback paths.

## Part 2 - (optional) Verified custom sender domain for production deliverability

The Azure-managed `*.azurecomm.net` domain is fine for UAT and first smoke tests, but its
deliverability and reputation are limited. For production, verify a **custom** domain -
this is the one part that cannot be automated (it needs DNS records + a verification wait,
AC-09):

1. In the provisioned Email Communication Service, **add a custom domain** for
   `quibblestone.com` (or a subdomain) and add the **SPF** and **DKIM** DNS records ACS
   gives you at your DNS host (Cloudflare, per the custom-domain runbook). Wait for ACS to
   show the domain **Verified**, and create the `no-reply` sender so the from-address is
   `no-reply@quibblestone.com` (an unverified sender gets spam-filtered).
2. **Connect** the custom domain to the Communication Services resource (the "Domains"
   blade), so the endpoint can send from it.
3. Point the deploy at it by setting the from-address override (the provisioned ACS
   endpoint is unchanged, so `EMAIL_ENDPOINT` is only needed for a *different* ACS
   resource):

   ```bash
   gh variable set EMAIL_FROM_ADDRESS --body 'no-reply@quibblestone.com'
   ```

   Redeploy; the wiring step now uses the custom from-address over the Azure-managed one.

## Part 3 - (optional) Keyed / external-ACS fallback

If you would rather not use the managed identity (or you point at an ACS resource outside
this resource group), use a connection string instead of the keyless endpoint:

```bash
rg=quibblestone-uat-rg
vault="$(az keyvault list -g "$rg" --query "[0].name" -o tsv)"

# The ACS connection string (endpoint=...;accesskey=...). The ONLY secret on this path.
az keyvault secret set --vault-name "$vault" --name EmailConnectionString --value 'endpoint=https://<acs>.communication.azure.com/;accesskey=...'
```

Leave `EMAIL_ENDPOINT` unset and set `EMAIL_FROM_ADDRESS` (the keyed path has no
Azure-managed domain to derive one from); the wiring step then uses
`Email__ConnectionString` from Key Vault. To instead point the KEYLESS path at an
**external** ACS you manage, set `EMAIL_ENDPOINT` to its endpoint and grant that
resource's send role yourself (the automated grant only targets the ACS this deploy
provisioned).

## Part 4 - Verify

1. Confirm the deploy's **"Ensure UAT is provisioned"** step reported `email=true`, and
   the **"Wire magic-link email delivery (optional)"** step ran and set the `Email__*` +
   `Accounts__TokenSigningKey` settings.
2. On `quibblestone.com`, open the Account screen, enter your email, and request a link.
   Confirm the email arrives (check spam on the first send from the managed domain - if it
   lands there, move to a verified custom domain, Part 2).
3. Follow the link and confirm sign-in completes.
4. **AC-07 check:** restart the App Service and confirm a fresh link still verifies after
   the recycle (with the ephemeral fallback it would not):

   ```bash
   rg=quibblestone-uat-rg
   api="$(az webapp list -g "$rg" --query "[?tags.app=='quibblestone'].name" -o tsv)"
   az webapp restart -g "$rg" -n "$api"
   ```

5. Repeat for the operator flow (`/admin/login`) with an allowlisted operator email.

## Rolling it back

Set `EMAIL_ENABLED` to anything but `true` (or delete the variable) and redeploy: the
provision runs with `enableEmail=false`, the wiring step is skipped, the fresh appSettings
array omits `Email__*`, and the API falls back to the `NoOpEmailSender`. The ACS resources
themselves **linger** - an incremental deploy does not delete resources dropped from the
template - but they are free at rest and unreferenced; delete them by hand if you want
them gone. The Key Vault secrets can stay - inert with nothing referencing them. For an
**immediate** kill without waiting for a deploy, delete the app settings by hand:

```bash
rg=quibblestone-uat-rg
api="$(az webapp list -g "$rg" --query "[?tags.app=='quibblestone'].name" -o tsv)"
az webapp config appsettings delete -g "$rg" -n "$api" \
  --setting-names Email__Endpoint Email__ConnectionString Email__FromAddress
```

The next deploy makes it durable. Leave `Accounts__TokenSigningKey` in place - a durable
signing key is harmless (and desirable) even with delivery off.
