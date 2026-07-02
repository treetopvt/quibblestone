<!--
  Runbook: provisioning the keepsake-gallery/04 "shareable tale link" feature
  (the public read-only tale page + its Azure Table Storage). Owner-run handoff:
  the IaC is committed (I prep); the actual Azure provisioning + the pre-deploy
  security gate are yours to run.

  Prose uses hyphens, colons, parentheses - never em dashes.
-->

# Runbook: Provisioning the shareable tale link (keepsake-gallery/04)

Story 04 adds the feature's ONE server surface: a public, read-only tale page
(`GET /t/<slug>`) plus host-initiated publish/revoke (`POST` / `DELETE
/api/tales`). This runbook is the handoff to stand it up. The code + Bicep are
already committed; **nothing is live until you deploy and wire the connection
string**, and there is a hard security gate before it should ever face the
public internet (Part 0).

## What it provisions (no new resource)

Everything rides the **existing** Storage account (README section 9) - the same
one the telemetry serve-log uses. `infra/main.bicep` already declares:

- a `PublishedTales` table on the Storage account's Table service (alongside
  `StoryServes` / `StoryFeedback`);
- two App Service app settings, both composed at deploy time (never committed
  literals):
  - `PublishedTales__StorageConnectionString` - from `storage.listKeys()` (same
    posture as `Telemetry__StorageConnectionString`);
  - `PublishedTales__WebAppBaseUrl` - `https://<static-web-app-host>`, the target
    of the public page's "Play QuibbleStone" / "Start your own tale" CTAs.

**Feature switch:** the app reads `PublishedTales:StorageConnectionString`. With
it present the feature is ON; **absent, the feature is OFF** (a
`DisabledPublishedTaleStore` - `POST` returns 503, `GET /t/<slug>` returns a
friendly 404, and the web client silently falls back to the image/text share).
So a normal `main.bicep` deploy turns it ON automatically. To keep it OFF until
Part 0 is done, deploy but remove that one app setting (see Part 3).

## Part 0 - Pre-deploy security gate (do this BEFORE the public route is reachable)

`POST /api/tales` is an **open, anonymous, unauthenticated write endpoint**. The
server already: re-vets every published part through the content-safety filter
(no un-vetted content can reach the page, even from a lying client), HTML-encodes
all output, serves `noindex, nofollow`, mints an unguessable 12-char slug, and
expires tales after 30 days. What it does **not** yet have (security review
W-001):

- [ ] **A rate limit / quota on `POST /api/tales`.** Without one, a script can
  mass-create tales and bloat storage. **This is a hard requirement before the
  public URL is reachable.** Add ASP.NET Core's built-in rate limiting
  (`builder.Services.AddRateLimiter(...)` with a per-IP fixed/sliding window)
  scoped to the publish endpoint. Until it exists, keep the feature OFF (Part 3)
  or keep the environment non-public.
- [ ] **(Softer) A reaper / Storage lifecycle policy** for never-read expired
  rows. Expiry-on-read only deletes tales that get fetched; unread expired rows
  linger. At toy scale on cheap Table Storage this is fine, but a periodic sweep
  makes "does not accumulate unbounded" (AC-05) airtight.
- [ ] **(If a CDN / Front Door fronts `/t/*`)** confirm it preserves the
  `X-Robots-Tag: noindex, nofollow` response header and does not cache a tale
  past its TTL.

## Part 1 - Validate the IaC (no Azure login needed)

```bash
az bicep build --file infra/main.bicep
```

(Authored to mirror the proven `StoryServes` table + app-setting pattern; it
could not be validated in the build environment because the `az` CLI was
absent there - validate here before deploying.)

## Part 2 - Deploy

The `PublishedTales` table and both app settings deploy with the normal
footprint - there is no separate chore. Either:

- **Push-button:** GitHub -> Actions -> **Provision UAT** (or just merge to
  `main`, which auto-provisions + deploys), or
- **Locally:**
  ```bash
  az deployment group create \
    -g quibblestone-uat-rg \
    -f infra/main.bicep \
    -p infra/main.uat.bicepparam
  ```

The table is created by this deploy (and the store also `CreateIfNotExists`es it
on first write, so either path is safe).

## Part 3 - Turn the feature ON / OFF deliberately

- **ON:** the deploy above wires `PublishedTales__StorageConnectionString`, so
  the feature is on once Part 0 is satisfied.
- **OFF (kill switch):** remove that one app setting and the feature disables
  cleanly with no redeploy of code:
  ```bash
  az webapp config appsettings delete \
    -g quibblestone-uat-rg -n <api-app-name> \
    --setting-names PublishedTales__StorageConnectionString
  ```
  Publish then returns 503 and every `/t/<slug>` 404s. Re-add it to switch back
  on.

## Part 4 - Smoke-check

```bash
# 1. noindex header is present on the public route (even for a missing slug):
curl -sI https://<api-host>/t/ZZZZZZZZZZZZ | grep -i x-robots-tag
#   -> X-Robots-Tag: noindex, nofollow

# 2. A bad/expired slug returns a friendly 404 (not a 500):
curl -s -o /dev/null -w '%{http_code}\n' https://<api-host>/t/ZZZZZZZZZZZZ   # 404
```

Then end to end in the app: finish a group tale -> host taps "Share a public
link" -> open the returned `/t/<slug>` in a fresh browser (no session) -> confirm
the read-only tablet + gold "Play QuibbleStone" CTA render, the byline shows only
nicknames, and the CTA lands on the create/join flow. Finally revoke and confirm
the link stops resolving.

## Rollback

Removing the `PublishedTales__StorageConnectionString` app setting (Part 3) is
the instant kill switch. The `PublishedTales` table can be left in place (it is
tiny and TTL-bounded) or deleted from the Storage account's Table service if you
want a clean slate; no code change is needed either way.
