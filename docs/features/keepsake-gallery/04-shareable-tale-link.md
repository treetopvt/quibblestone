# Story: Shareable tale link (the back-link growth loop)

**Feature:** Keepsake Gallery  ·  **Status:** Complete  ·  **Issue:** #66

> Code Complete and merged (PR #130): the server surface, host publish/revoke, the
> public read-only tale page, server-side re-vet, unguessable slug, noindex, TTL,
> and the per-IP publish rate limit all ship. The public surface stays DISABLED
> until Azure is provisioned (the connection string is wired) - see
> `docs/runbooks/keepsake-published-tales.md`. The deploy-gated / on-device manual
> ACs (AC-02 visual, AC-06 phone PWA hand-off, AC-05 live TTL eviction) are
> verified during that provisioning pass, not in CI.

## Context
A shared tale should pull new players IN, not just show a pretty picture. Today's
share (story 02) exports a watermarked image - a soft brand impression, but a
dead end: whoever receives it has no way to reach QuibbleStone or see the tale in
motion. This story adds a real back-link: a short, unguessable public URL that
opens a lightweight, read-only "tale" page (the carved tablet) with a prominent
"Play QuibbleStone" call to action. That closes the growth loop this whole feature
exists to serve (README section 2 - word-of-mouth is the growth lever). It is the
ONE part of Keepsake Gallery that needs a small SERVER surface (a public tale
route + a stored tale), so it is called out explicitly and kept behind strict
child-safety and privacy guardrails. See [feature.md](./feature.md).

## Acceptance Criteria
- [x] AC-01: Given a finished, family-safe tale, when the host chooses "Share the
      tale", then the share payload includes a short, unguessable public link
      (e.g. `https://<app>/t/<slug>`) in addition to the watermarked image from
      story 02 - and where a browser cannot share an image/file, the link alone
      is the share (this is story 02 AC-01's text/link fallback made concrete).
- [x] AC-02: Given someone opens the link (no app install, no account, no login),
      then they see a lightweight, read-only public "tale" page - the carved
      stone-tablet with the coral filled-in words and the "carved by [names] &
      crew" byline - and a prominent gold "Play QuibbleStone" CTA (plus a
      secondary "Start your own tale"); this is the adoption hook.
- [x] AC-03 (child-safety / privacy, non-negotiable): Given any tale published to
      a link, then ONLY already-vetted, family-safe content is ever published (a
      tale from a family-safe session stays family-safe on the page; there is no
      un-vetted publish path); the only identity shown is the in-session
      nickname(s) + Guardian variant(s), never any PII; the slug is unguessable
      (not a sequential id); the page is served `noindex, nofollow` (never
      search-indexed); and publishing is HOST-INITIATED (opt-in per tale), never
      automatic.
- [x] AC-04 (entitlement): Given sharing a tale link, then it is FREE - it is a
      growth loop, not a gated capability; no entitlement check gates it at
      session-creation and it consumes no billing-entitlements capability key
      (README section 3 - the free tier is generous, and this is how new players
      arrive; gating the growth loop would defeat its purpose).
- [x] AC-05: Given a published tale, then it is stored server-side as the
      assembled, already-filtered story text + minimal metadata (byline names,
      Guardian variants, created date) behind the slug - never raw per-player
      submissions and never PII - and it has an eviction/expiry policy (a TTL,
      mirroring story 03's local-history cap) so published tales do not accumulate
      unbounded (README section 4 - an ephemeral toy, not a system of record).
- [ ] AC-06: Given the public tale page on a phone, when the visitor taps "Play
      QuibbleStone", then they enter the normal create/join flow (and, as a PWA,
      are offered add-to-home-screen) - so a received link converts directly into
      a new session and the loop closes.
- [x] AC-07: Given a host wants to stop sharing a tale, then they can revoke it
      (the link stops resolving) - a simple, low-ceremony delete, consistent with
      "most data is mutable" (README section 4).

## Out of Scope
- A public gallery / discovery / browse of other people's tales - each link is
  private-by-obscurity to whoever the host shared it with; there is no directory.
- Comments, reactions, or accounts on the public page.
- Share/click analytics or a referral-rewards system (no analytics infra yet; a
  rewards system is a separate monetization idea - the entitlement seam is
  untouched here).
- Editing the tale from the public page - it is read-only; in-session
  editing/remix is `replay-remix`'s job.
- Server-side image rendering - the page renders the tablet from stored TEXT with
  the same theme tokens; the shared IMAGE stays story 01/02's client-side render.

## Technical Notes
- **This story adds the feature's only server surface** - a public, read-only
  route (e.g. `GET /t/<slug>` served by the ASP.NET Core app, or a Static Web App
  route hydrating from a small API) plus storage of the published tale. This is
  the documented exception to Keepsake Gallery's otherwise client-only boundary
  (see feature.md Design notes / Decisions): stories 01-03 stay `web/`-only; only
  04 touches the server. Keep it isolated (a thin, dedicated controller/service),
  not woven into `GameHub.cs` or the round lifecycle.
- **Storage:** a small "published tale" record in Azure Table Storage (assembled
  text + byline metadata), partitioned by slug for a single-lookup read; reuse the
  Storage account the footprint already provisions (README section 9) - no new
  resource. Apply the TTL/eviction from AC-05.
- **Slug:** unguessable, short, human-shareable - longer than the 4-char join
  code (it must resist enumeration), reusing the no-ambiguous-glyph alphabet from
  `session-engine`'s join codes at a length that makes guessing infeasible.
  **Chosen values (recorded per the story process):** slug length **12**
  (`SlugGenerator.SlugLength`) over the 31-glyph alphabet
  `ABCDEFGHJKMNPQRSTUVWXYZ23456789` (`SlugGenerator.Alphabet`) -> a ~7.9e17
  keyspace, minted with `RandomNumberGenerator.GetInt32` (never sequential). TTL
  **30 days** (`PublishedTalesController.TaleTtl`), stamped on publish and applied
  as lazy expiry-on-read (`PublishedTale.IsExpired`).
- **noindex:** send `X-Robots-Tag: noindex, nofollow` and a
  `<meta name="robots" content="noindex">` on the page (AC-03).
- **Content safety (AC-03):** the published text is the SAME already-filtered
  `AssembledStory` the reveal showed - publishing opens no new free-text path and
  never re-runs collection. Do not publish anything a family-safe session would
  not have shown.
- **Web Share hand-off:** story 02's `handleShare` includes this link in the share
  payload, feature-detecting the same way story 02 does; when file/image share is
  unsupported, the link is the entire share (AC-01).
- **The "Play QuibbleStone" CTA** reuses the gold-CTA `Button` contract
  (design-system) and routes into the existing Home / create-join flow; theme
  tokens (`web/src/theme.ts`) render the read-only tablet, FontAwesome for chrome.

## Tests
| AC | Test |
|---|---|
| AC-01 | manual: trigger share; confirm the payload contains the public link (and the link alone when image share is unsupported) |
| AC-02 | manual: open the link in a fresh browser (no session); confirm the read-only tablet + gold "Play QuibbleStone" CTA render |
| AC-03 | code review + manual: unguessable slug, `noindex` headers/meta, only nickname + Guardian shown, family-safe content only, publish is opt-in |
| AC-04 | code review: no entitlement/capability check gates the share-link path |
| AC-05 | integration: a published tale stores only filtered text + metadata (no PII); a TTL/eviction policy exists and is exercised |
| AC-06 | manual (phone): the CTA enters the create/join flow; the PWA offers add-to-home-screen |
| AC-07 | manual: revoking a tale makes the link stop resolving |

## Implementation notes (2026-07-02, In Progress)
- **Server (isolated, no GameHub touch):** new `api/src/PublishedTales/` -
  `PublishedTale`/`TalePart` (stored record), `SlugGenerator`, `IPublishedTaleStore`
  + `TableStoragePublishedTaleStore` (mirrors `TableStorageTelemetrySink`;
  PartitionKey = RowKey = slug; parts serialized to one JSON property; lazy
  expiry-on-read) + `DisabledPublishedTaleStore` (NoOp-when-no-connection-string),
  and `PublishedTalesController` (`POST /api/tales`, `DELETE /api/tales/{slug}`,
  public `GET /t/{slug}`). Registered in `Program.cs` connection-string-gated,
  exactly like the telemetry sink. NO new NuGet (`Azure.Data.Tables` already
  referenced).
- **Server-side re-vet (AC-03):** EVERY non-empty part - coral words AND "literal"
  template runs - plus the byline run through the injected `IContentSafetyFilter`
  on publish; any failure rejects the whole publish (400) with a generic message.
  All stored content is HTML-encoded on render (XSS defense in depth).
  - **Security-review fix (CR-001, 2026-07-02):** the first cut re-vetted only the
    parts the CLIENT flagged `IsWord=true` and trusted "literal" parts verbatim.
    That was an exploitable child-safety bypass: a crafted anonymous POST could
    mark unfiltered text as a `literal` part and land it on the public,
    child-visible page (HTML-encoding stops script injection but not unsafe content
    DISPLAY). Fixed to re-vet every non-empty part regardless of the client's
    classification - the server has no template to verify a "literal" claim against
    and the endpoint is untrusted. Genuine author template text is
    false-positive-resistant and passes. Covered by
    `PublishedTalesControllerTests.Publish_rejects_an_unsafe_LITERAL_part_a_lying_client_tags_as_not_a_word`.
- **Open, anonymous write endpoint - rate limited (security-review W-001, now
  implemented):** `POST /api/tales` is throttled by a per-IP fixed window
  (`PublishTalesRateLimit`: 8 publishes / minute / client IP, `429` on reject) via
  ASP.NET Core's built-in rate limiting (no new dependency) - registered in
  `Program.cs` (`AddRateLimiter` + `UseRateLimiter`), opted into by the publish
  action alone (`[EnableRateLimiting]`), so the hub/health/moderation routes are
  untouched. Size caps still bound a single payload; the limiter bounds volume. A
  real family sharing a few tales never hits it. Covered by
  `PublishTalesRateLimitTests` (per-IP partitioning; fails closed to a shared
  bucket when no IP is available).
  - **Deploy hardening note:** behind App Service the true client IP arrives in
    `X-Forwarded-For`; wire ForwardedHeaders middleware so
    `Connection.RemoteIpAddress` reflects it (else all callers may share the proxy
    bucket) - a one-liner recorded on the provisioning runbook. A periodic reaper /
    Storage lifecycle policy for never-read expired rows is a softer follow-up
    (lazy expiry-on-read only deletes rows that get fetched).
- **noindex (AC-03):** `X-Robots-Tag: noindex, nofollow` header on every `/t/{slug}`
  response plus `<meta name="robots" content="noindex, nofollow">`.
- **Web (host-gated, opt-in):** `web/src/gallery/publishTale.ts`
  (`publishTale`/`revokeTale`/`slugFromTaleUrl`, mirrors `checkWord.ts`). Reveal
  gains an optional `publicShare` prop (host-only "Share a public link" +
  "Stop sharing this link"); the returned link threads into the EXISTING
  `shareImage`/`shareText` payload (Web Share `url` slot), never a forked flow.
  App.tsx `GroupReveal` supplies `publicShare` only when `isHost`.
- **Infra:** `PublishedTales` table + `PublishedTales__StorageConnectionString`
  (same storage account as telemetry) + `PublishedTales__WebAppBaseUrl` (from the
  Static Web App host name) added to `infra/main.bicep`. `az bicep build` could NOT
  be validated locally - the az CLI is absent on this machine.
- **Disabled-without-connection-string:** with no `PublishedTales:StorageConnectionString`
  (local dev / CI / fresh clone), the disabled store is registered: `POST /api/tales`
  returns 503 "not available" and `GET /t/{slug}` returns the friendly 404 page - the
  app builds and runs with the feature simply OFF and zero Azure setup.
- **Tests added (`tests/QuibbleStone.Api.Tests/`):** `PublishedTaleTests`
  (slug alphabet/length/non-repetition, `IsExpired`), `PublishedTalesControllerTests`
  (re-vet rejection stores nothing, clean publish returns unguessable slug + url +
  TTL, noindex header/meta, HTML-encoding, 404 on missing/expired/disabled, revoke).
  Vitest `web/src/gallery/publishTale.test.ts` (endpoint/body, graceful failure,
  slug extraction). Full Table Storage round-trip is not integration-tested (needs
  an emulated Storage account, same as the telemetry sink).
- **Manual / not-yet-verified:** AC-02 final visual polish of the public page,
  AC-06 (phone: CTA into create/join + PWA add-to-home-screen), and the live
  end-to-end share on a real device remain manual checks.

## Dependencies
- keepsake-gallery/01-save-reveal-as-image (the rendered tablet/tale this publishes)
- keepsake-gallery/02-share-with-watermark (the share action this adds the link to)
- the-reveal/01-text-reveal (the assembled, already-filtered tale)
- session-engine (the create/join flow the CTA routes into; the slug alphabet)
- child-safety (the family-safe + no-PII posture the public page must honor)
- infra (Azure Table Storage for the published tale - README section 9)
