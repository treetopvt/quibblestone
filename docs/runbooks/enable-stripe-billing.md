<!--
  Runbook: turning on live Stripe billing (billing-entitlements/03) so the tip jar
  (the /support page) and the gated paywall actually charge. Owner-run handoff:
  the code + the deploy wiring are committed (I prep); the Stripe dashboard setup,
  the Key Vault secret values, and the repo variables are yours to set.

  Prose uses hyphens, colons, parentheses - never em dashes.
-->

# Runbook: Enabling live Stripe billing

The entire billing seam is **built and shipped** - it just ships **disabled**. With
no Stripe secret key configured, the API registers the no-op checkout service and
the webhook returns 503, so the `/support` tip jar shows its warm "not available
yet" state and free play is untouched (`api/src/Billing/StripeOptions.cs`,
`DisabledStripeCheckoutService.cs`). "Turning it on" is therefore **configuration,
not code**: give the deployed API real Stripe keys and set up the Stripe dashboard.

> **Do test mode first (the default path).** This is your live public site: deploy
> runs off every push to `main` into UAT, and `quibblestone.com` is bound to that
> Static Web App. So the safe sequence is two passes through the loop below:
>
> 1. **Test-mode dry run** - use Stripe **test** values (`sk_test_` key, a test
>    webhook, a test price) and pay with test card `4242 4242 4242 4242`. This
>    exercises 100% of the code path on the real deployed site with zero risk of a
>    real charge.
> 2. **Go live** - once the dry run is green, overwrite the two Key Vault secrets
>    with `sk_live_` values and swap the price-id repo vars to your live price ids,
>    then redeploy. No code or workflow change between the two passes.
>
> Everything below is written so the two passes are identical except for which
> Stripe mode you copy the values from.
>
> **One caveat for the dry run:** while test mode is enabled, `quibblestone.com/support`
> shows a *working* Stripe Checkout to the public, but it only accepts test cards - a
> real visitor's real card is declined. UAT traffic is near zero, but keep the
> test-mode window short (or run it at a quiet time) so nobody hits a dead checkout.

## How the switch works (why it is durable)

`infra/main.bicep` **replaces** the API's `appSettings` array on every deploy, so
any setting applied by hand is wiped on the next merge to `main` (the exact reason
CORS and the AI gate are re-applied by the deploy workflow). So the Stripe settings
are wired the same way: the **"Wire Stripe billing (optional)"** step in
`.github/workflows/deploy.yml` re-applies them after the Bicep provision on every
deploy, gated on `vars.STRIPE_ENABLED == 'true'`.

- The two **secrets** (secret key, webhook signing secret) live in **Key Vault** and
  are wired as `@Microsoft.KeyVault(...)` references - resolved at runtime by the
  API's managed identity (already granted "Key Vault Secrets User" on the vault). No
  Stripe secret ever touches GitHub, the workflow file, or a `VITE_` var.
- The **price ids** are not secret (env-specific) and come from repo variables.
- `Stripe__ClientBaseUrl` is set to the public web origin (the custom domain if one
  is bound, else the SWA default host) so Stripe's success/cancel redirects land on
  the real site, not `localhost`.

Leave `STRIPE_ENABLED` unset and everything above is skipped - billing stays cleanly
off. That is the only master switch.

## What you need to set (one time)

### Part 1 - Stripe dashboard (in the mode for this pass: Test first, then Live)

> Toggle the Stripe dashboard to **Test mode** for the dry run and copy the
> `sk_test_` / `whsec_` / test `price_` values; switch to **Live mode** and repeat
> for the go-live pass. Test and live objects are entirely separate in Stripe.

1. **Create the tip product + price.** Products -> add a product ("Buy the Guardians
   a coffee"), one-time, a fixed amount (e.g. $3). Copy its **Price ID** (`price_...`).
   The tip is a fixed-price one-time charge and grants nothing (entitlement-neutral by
   design) - it rides the same checkout plumbing as everything else.
2. *(Optional, only if you also want the paywall live)* Create the **Family Plan**
   (recurring subscription) and/or the **Spooky Pack** (one-time) products and copy
   their price ids. Skipping these just leaves those products shown-but-not-buyable.
3. **Get your secret key.** Developers -> API keys -> **Secret key** (`sk_live_...`).
4. **Register the webhook.** Developers -> Webhooks -> add endpoint:
   - **URL:** `https://<your-api-host>/api/stripe/webhook`
     (discover the host: `az webapp list -g quibblestone-uat-rg --query "[?tags.app=='quibblestone'].defaultHostName" -o tsv`)
   - **Events to send:** `checkout.session.completed` (required),
     and - only if you enabled the subscription plan - `invoice.paid`,
     `customer.subscription.updated`, `customer.subscription.deleted`.
   - After creating it, copy the **Signing secret** (`whsec_...`).

   > For the tip alone the webhook is not strictly required (the thank-you page is
   > driven by Stripe's success-redirect, and the tip grants nothing). But wire it
   > anyway: without a signing secret the webhook endpoint returns 503, and you will
   > need it the moment you sell the paywall plan/pack.

### Part 2 - Key Vault secret values (set once, out of band)

Find the vault, then set the two secret values. These are set **directly in Key
Vault** (not through GitHub), so the workflow never sees them and the values survive
every deploy:

```bash
rg=quibblestone-uat-rg
vault="$(az keyvault list -g "$rg" --query "[0].name" -o tsv)"

az keyvault secret set --vault-name "$vault" --name StripeSecretKey            --value 'sk_live_...'
az keyvault secret set --vault-name "$vault" --name StripeWebhookSigningSecret --value 'whsec_...'
```

(Your own `az login` needs "Key Vault Secrets Officer" or similar on the vault to
write. The API's identity already has read access - nothing to grant there.)

### Part 3 - Repo variables (the master switch + price ids)

Set these as **repository variables** (Settings -> Secrets and variables ->
Actions -> Variables) - they are not secret:

| Variable | Value | Required |
|---|---|---|
| `STRIPE_ENABLED` | `true` | yes - the master switch |
| `STRIPE_TIP_PRICE_ID` | `price_...` (the tip) | yes when enabled |
| `STRIPE_FAMILY_PLAN_PRICE_ID` | `price_...` | optional (paywall plan) |
| `STRIPE_PACK_SPOOKY_PRICE_ID` | `price_...` | optional (example pack) |

```bash
gh variable set STRIPE_ENABLED --body true
gh variable set STRIPE_TIP_PRICE_ID --body 'price_...'
```

### Part 4 - Deploy and verify

1. Merge this branch to `main` (or run the **Deploy** workflow manually). The "Wire
   Stripe billing (optional)" step applies the settings; confirm it did not skip.
2. Open `https://quibblestone.com/support`, tap **Buy the Guardians a coffee**, and
   confirm it redirects to Stripe Checkout (not the "not available yet" note).
3. Complete a real (or test-mode) payment and confirm the return to
   `/support?tip=success` shows the gold-Guardian thank-you.
4. In the Stripe dashboard, confirm the webhook delivery for
   `checkout.session.completed` returned `200`.

## Rolling it back

Set `STRIPE_ENABLED` to anything but `true` (or delete the variable) and redeploy:
the wiring step is skipped, the Bicep provision leaves the Stripe settings out of the
fresh appSettings array, and billing is off again on the next deploy. The Key Vault
secrets can stay - they are inert with nothing referencing them. For an **immediate**
kill without waiting for a deploy, delete the app settings by hand
(`az webapp config appsettings delete -g quibblestone-uat-rg -n <api> --setting-names Stripe__SecretKey Stripe__WebhookSigningSecret`);
the next deploy makes it durable.
