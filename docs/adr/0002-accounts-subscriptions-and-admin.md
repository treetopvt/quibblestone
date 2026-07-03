<!--
  ADR 0002 - How QuibbleStone introduces a purchaser identity, a family-plan subscription, and a
  sys-admin surface WITHOUT deanonymizing players. This is an exploration/position record: it maps
  the tension in README sections 3 + 6 against what is already designed (accounts-identity,
  billing-entitlements, ai-cost-gate), states the one load-bearing invariant that resolves the
  tension, and SURFACES the open decisions. The owner resolved all six (A-F) on 2026-07-03 - see the
  Decision section; Status is now Accepted. Companion feature exploration:
  docs/features/sysadmin-console/feature.md.
  Use hyphens/colons/parentheses, never em dashes.
-->

# ADR 0002: Purchaser accounts, family subscriptions, and the sys-admin surface (keeping players anonymous)

- **Status:** Accepted (exploration; the owner resolved open decisions A-F on 2026-07-03 - see Decision)
- **Date:** 2026-07-03
- **Context features:** `accounts-identity` (#67-69), `billing-entitlements` (#70-74),
  `ai-cost-gate` (#121), and a NEW `sysadmin-console` exploration (this ADR's companion)
- **Supersedes / superseded by:** nothing; extends the identity + monetization posture the two
  Phase-2 features already committed to. Does not reopen ADR 0001 (the AI cost gate).

## Context

The product cannot stay purely anonymous forever: paid families will unlock new content and
features, and the moment money changes hands there is, by necessity, *someone* the app knows -
the purchaser. README section 3 already anticipated this and drew the line precisely:

- **Players are anonymous forever** - join with a code + nickname, no account, no PII. This is
  also the child-privacy posture (COPPA / GDPR-K): collect as little about minors as possible
  (README section 6).
- **Only the purchaser gets a lightweight account**, and only when they buy. Free play requires
  no login.
- **Monetization is a thin entitlement check decided at session-creation time, not per-request**;
  the free tier is generous; **avoid ads**.

The tension this ADR resolves: **keep every player anonymous while a purchasing adult has an
account whose entitlements unlock content and features for their sessions.** Two planes - an
anonymous play plane and a single purchaser-identity plane - that must meet at exactly one point
without the identity plane ever touching a player.

Most of the machinery for this is already *designed* (though not yet built - see "State of the
tree" below):

- `accounts-identity` - the tiered identity model as three stories: anonymous-forever contract
  (#67), a lightweight purchaser account created only at purchase (#68, email OR a single OAuth
  identity, provider explicitly TBD), and sign-in/restore on a new device (#69).
- `billing-entitlements` - the `IEntitlementService` seam evaluated once at session-creation
  (#70), a tip jar (#71), shared Stripe plumbing + entitlement store (#72), a gated purchase
  flow (#73), and purchaser-facing restore/manage (#74). Its feature.md already commits to
  **"one billing plumbing, two purchase shapes"** (a one-time add-on pack AND a recurring
  family-plan subscription) and to a default-unlocked capability-key catalog
  (`library.full`, `play.remote`, `play.largeGroup`, `ai.*`, `pack.<id>`).
- `ai-cost-gate/02` (#121) - the first consumer of the #70 seam, capturing entitlements once at
  `GameHub.CreateRoom` and establishing the framing that matters here: **meter compute per
  session, not identity.**

What is genuinely *unexplored* is the third leg the task names - a **sys-admin surface** - and the
subscription-specific glue (recurring lifecycle, plan-to-capability mapping) that the existing
billing stories name but do not decompose. This ADR covers all three: (a) the purchaser account +
login, (b) the sys-admin surface, (c) how a family subscription unlocks capabilities through the
existing seam.

### State of the tree (an honest correction)

The framing that prompted this exploration described `IEntitlementService` as already shipped
(`api/src/Entitlements/`, async `ValueTask`, default-unlocked, captured at `GameHub.CreateRoom`).
**It is not in the tree yet.** Both `billing-entitlements/01` (#70) and `ai-cost-gate/02` (#121)
are Status: Not Started; there is no `api/src/Entitlements/` or `api/src/Accounts/` folder, and
`GameHub.CreateRoom` currently makes no entitlement call. What *did* ship for the cost gate is the
AI-provider **infrastructure** (Bicep, PR #131) and the ADR-0001 decisions - not the C# seam. This
matters for slice planning: the entitlement seam is a **specified contract with an agreed call site
(`CreateRoom`)**, not running code. This ADR assumes it lands as `billing-entitlements/01`
specifies and deliberately does **not** reopen its shape (`EvaluateForSession(purchaser?) ->
SessionEntitlements`, default-unlocked). Everything below is designed to consume that contract, so
that whether #70 lands before or alongside this work, the consumers do not change.

## The load-bearing invariant (what resolves the tension)

**Entitlement travels with the session, not with identity.**

This is the same shape ADR 0001 already committed to for cost ("meter compute per session, not
identity"), applied to unlocking: the purchaser's subscription unlocks **the session**, not **the
players**. Concretely:

1. The host device *optionally* has a signed-in purchaser (the adult who bought the family plan,
   on their own phone). The kids in the back seat still join with a code + nickname and never touch
   an account.
2. At `GameHub.CreateRoom` - the single point where the two planes meet - the server resolves the
   host's purchaser identity to a capability set **before** anything lands on the room, and stashes
   **only the resolved `SessionEntitlements`** on `Room`. The purchaser identity is used and
   discarded at that boundary; it is never persisted on `Room`, never broadcast, never joined to a
   player's nickname or connection.
3. The room then plays with those capabilities unlocked **for everyone in it**, for the session's
   lifetime, with no further checks (README section 3: not per-request).
4. Signed-out host, or a lapsed subscription -> `EvaluateForSession(null)` returns the
   default-unlocked free set -> the room plays exactly as it does today. Free play is unchanged and
   requires no login.

The privacy firewall is the *ordering*: **purchaser identity resolves to capabilities on one side
of `CreateRoom`; only capabilities cross to `Room` on the other side.** `Room` never gains a
purchaser-id field, so no telemetry, share link, or bug ever ties a child's session back to a real
adult. This is the single claim everything below hangs on, and it is the thing a reviewer should
guard most fiercely.

## (a) The purchaser account + login

Already scoped by `accounts-identity/02` (#68): created only at purchase, holds only an email or a
single OAuth-subject plus created-at, no player data ever, treated as adult data (checkout is
itself evidence of an adult). This ADR adds only the **provider question** and how the account maps
to entitlements without deanonymizing players.

**How it maps to entitlements (no player ever appears in the chain):**

```
purchaser identity (email / OAuth subject)   [adult plane]
        |  resolved at CreateRoom, then discarded at the boundary
        v
  EntitlementGrant rows in Table Storage  ->  SessionEntitlements (capability keys only)
        |
        v
  Room.SessionEntitlements                     [play plane - anonymous]
```

The grant lookup is keyed by a hash of the purchaser identity (`accounts-identity/02` AC, #68) -
never by anything about a player. The player side of the chain is empty by construction.

**Identity-provider options (OPEN - see Open Decisions A):** the AC-01 contract in
`accounts-identity/02` ("email OR a single OAuth identity, nothing more") holds regardless of which
is chosen. The realistic candidates for a solo, nights-and-weekends build:

| Option | What it is | Pull | Push |
|---|---|---|---|
| Magic-link email | Email a one-time sign-in link; no password stored | Minimal PII, no password custody, no third-party SDK | You own token issuance + email delivery |
| Single OAuth (Google / Apple) | Delegate identity to one provider | No password custody; fast trust; Apple/Google cover most families | Adds an SDK + client config; provider lock-in for restore |
| Stripe Customer as the identity | Reuse the email Stripe already collects at checkout as the account key; a magic link proves ownership for restore | Almost zero net-new identity infra - Stripe is already the billing plumbing (#72) | Couples identity to the billing provider; restore still needs an email-proof step |
| Entra External ID (Azure AD B2C) | Azure-native managed identity | Stays inside the Azure footprint | Heavyweight for "one email field" on a toy |

The **Stripe-Customer-as-identity** option is worth calling out because it minimizes net-new
surface: the first thing that ever needs a purchaser record is a purchase, the purchase already
mints a Stripe Customer with an email, and `billing-entitlements/03` already owns that Stripe
integration. It is not obviously right (it couples identity to the billing vendor and still needs
a proof-of-ownership step for cross-device restore, `accounts-identity/03`), but it is the thinnest.
This is the owner's call, not this ADR's.

## (b) The sys-admin surface - what it is actually for, and where it lives

The most important finding: **the "sys-admin site" is not one thing to build.** It is an umbrella
over several distinct back-office needs, and most of them are already latent inside other features
or already served by Azure. Enumerated against what exists:

| Admin need | Already served by | Genuinely new / admin-only? |
|---|---|---|
| Cost & abuse oversight | App Insights (`platform-devops/04`, #106) + Azure Cost Management budget emails (ADR 0001) + the gate's per-call attribution telemetry + the spend circuit-breaker (the enforcement, `ai-cost-gate/04`) | **No.** Observation is covered; enforcement is automatic. Humans need to *see*, and Azure already shows it. |
| AI content vetting (generate -> vet -> publish) | `ai-content-factory` (#78-80) already designs an internal review queue as its "UI" | **No** - it belongs to that feature, not a separate console. |
| Library / pack content management | `ai-content-factory/03` (publish) + `story-packs` | **Mostly no** - back-office of those features. |
| Purchaser / subscription operator support (look up a stuck customer by email, manually grant / revoke an entitlement, reconcile a refund or chargeback, ban an abuser) | Stripe's own dashboard (refunds) + hand-editing Table Storage grants | **YES.** No feature owns "operator helps a paying customer." Arrives *with real charging*. |
| Moderation review / takedown of PUBLIC content (a shared keepsake tale is reported) | Nothing - the filter is a pre-display gate, there is no human review or takedown path | **YES.** Arrives *with public tales* (keepsake-gallery already shipped public tale links). |

So the two genuinely-new, admin-only responsibilities are **purchaser/subscription operator
support** (triggered by real charging going live) and **a report/takedown path for public content**
(triggered by tales being public). Everything else the phrase "sys-admin site" evokes is either
already designed inside its owning feature or already handled by an Azure surface.

**Where it lives - in-app vs separate tool (OPEN - see Open Decisions B):**

- **A. A separate, access-controlled back-office surface**, never part of the anonymous kid-facing
  PWA (separate bundle/route tree, its own auth). Pro: keeps operator + PII-adjacent purchaser data
  entirely out of the app children use - the kid app's attack surface and bundle stay minimal,
  consistent with kid-safety-by-construction (CLAUDE.md). Con: more to stand up.
- **B. An in-app admin area** behind an auth gate in the same PWA. Pro: reuses theme/components.
  Con: couples the kid app to an operator surface and widens the anonymous app's blast radius -
  against the section-6 posture. Not recommended.
- **C. No bespoke tool yet** - the solo operator uses App Insights, Cost Management, Stripe's
  dashboard, and Table Storage tooling (Storage Explorer / `az`) directly, and a bespoke admin
  surface is minted only when a specific need cannot be met that way.

## (c) How a family subscription unlocks content + features through the existing seam

The seam already carries this; the subscription adds three specific things on top of the one-time
pack shape `billing-entitlements` already models:

1. **A recurring Stripe object, not a one-time charge.** A family plan is a Stripe *Subscription*
   (Checkout in subscription mode), whose lifecycle (created, renewed = `invoice.paid`, `past_due`,
   `canceled`, expired) drives entitlement state via webhook. `billing-entitlements/03` already
   ships the shared Stripe plumbing and an in-app webhook handler isolated enough to lift into a
   Function later; this is that handler carrying subscription events.

2. **A lease-shaped grant, not a permanent one.** A one-time pack grant is forever; a subscription
   grant is **valid-through a date, refreshed on each successful renewal webhook and expired on
   cancel.** This means the `EntitlementGrant` row needs (at least) an optional `validThrough` and a
   `source` (subscription vs one-time), so the session-creation check can answer "is this
   subscription currently active" from a single Table Storage read - no call to Stripe on the play
   path. (A grace window for `past_due`, so a lapsed card does not lock a family mid-car-ride, is an
   open decision - see D.)

3. **A plan -> capability-bundle mapping.** A "family plan" is exactly "grant this bundle of
   capability keys while active" - the README section 3 paid tier: `library.full`, `play.remote`,
   `play.largeGroup`, and the `ai.*` keys once those features exist. Defining that named bundle is
   the one small new artifact; the keys themselves already live in the #70 catalog. Degradation is
   graceful by design: a lapsed subscription falls back to the *generous* free tier, not a wall.

The unlock itself is unchanged from the invariant above: captured once at `CreateRoom`, applied to
the whole room, never per-request, never per-player.

## Recommendation (the thinnest first slice)

Slice discipline (CLAUDE.md section 7) says: build none of this as a standalone "site" for alpha,
and let each admin capability be minted by the feature that creates its need.

1. **Foundation is the seam, not the site.** The first thing to land is `billing-entitlements/01`
   (#70) - the `IEntitlementService` seam + capability catalog, default-unlocked, captured at
   `CreateRoom` - because `ai-cost-gate/02` already needs it and accounts + subscriptions consume
   it. This changes zero observed behavior and unblocks everything else. (It is currently unbuilt;
   see "State of the tree.")
2. **Purchaser account + subscription** ride the existing `accounts-identity` + `billing-entitlements`
   stories, adding only: the lease-shaped grant (`validThrough` + `source`), the subscription
   webhook cases, and the family-plan capability bundle. No new feature folder needed for this leg.
3. **The sys-admin surface starts at option C (no bespoke tool).** For a ~50-sessions/month alpha,
   cost/abuse oversight is App Insights + budget emails (already there), content vetting is the
   content-factory queue (when built), refunds are Stripe's dashboard, and grants are Table Storage.
   *(Superseded by Decision B: the owner elected to stand up the separate back office now, with
   magic-link operator login + grant/revoke + the report/takedown queue as its first jobs. Cost
   oversight still stays on App Insights + Cost Management - the back office does not rebuild it.)*
4. **The first bespoke admin sliver is a single authenticated operator action - "grant/revoke an
   entitlement by purchaser email"** - and it is justified only when real charging goes live (so you
   can unstick a paying customer without hand-editing storage). Build it as option A (a separate,
   auth-gated surface), the thinnest possible version: one or two protected endpoints + a minimal
   internal page, reusing the theme.
5. **A public-content report/takedown path is the second candidate sliver** - and because keepsake
   tales are *already public*, whether it is needed *now* is a live safety question worth raising
   with the owner rather than deferring silently (Open Decision E).

The companion exploration `docs/features/sysadmin-console/feature.md` captures the admin surface as
a deferred umbrella whose stories activate on their trigger features, with candidate stories and the
recommended first sliver.

## Open decisions (surfaced here; all resolved in the Decision section below on 2026-07-03)

- **A - Purchaser identity provider:** magic-link email vs a single OAuth provider (Google/Apple)
  vs Stripe-Customer-as-identity (thinnest) vs Entra External ID (Azure-native). Contract
  (`accounts-identity/02` AC-01: email or one OAuth identity, nothing more) holds either way.
- **B - Where the admin surface lives:** separate auth-gated back-office (A, recommended when a
  bespoke need arises) vs in-app admin area (B, not recommended) vs no-bespoke-tool-yet (C,
  recommended for alpha).
- **C - Subscription entitlement model:** confirm the lease shape (`validThrough` + `source` on the
  grant) and the plan -> capability-bundle mapping for "family plan." Which keys are in the bundle;
  is "family" the only plan, with add-on packs separate.
- **D - Dunning grace window:** how long a `past_due` subscription keeps unlocking before it falls
  back to free (so a failed card does not lock a family mid-session). `billing-entitlements` parks
  full dunning; this is only the minimal "grace vs instant-lock" call.
- **E - Public-content moderation (a safety question, worth surfacing now):** keepsake tales are
  already public. Is a report/hide/takedown path needed in alpha, or does low volume + the
  pre-publish filter suffice for now? This one has a child-safety dimension, so it should be an
  explicit owner call, not a silent deferral.
- **F - How the host proves purchaser status at `CreateRoom`:** how a signed-in purchaser's proof
  reaches the SignalR hub (bearer token on the hub connection, resolved server-side) **without** any
  identity landing on `Room` - the mechanism that must uphold the load-bearing invariant.

## Decision

Resolved by the owner on 2026-07-03:

- **A - Purchaser identity: magic-link email.** Purchasers sign in via an emailed one-time link (no
  password, no OAuth SDK). The same one-time-token issue/verify plumbing is **reused for operator
  login** to the back office (Decision B), against a **separate operator allowlist** held in config
  / Key Vault. Admin authorization is membership in that allowlist, resolved at verify time, and is
  NEVER inferred from being a purchaser - `purchaser == admin` is the bug to prevent. The purchaser
  account holds email + created-at + Stripe customer id + entitlement summary, and nothing about any
  player. Records the choice `accounts-identity/02` left open (its AC-01 is unchanged).

- **B - Admin surface: a separate, auth-gated back office, built now (option A).** Not deferred to
  "when a need arises," and never in the kid PWA (option B rejected). Because a back office must
  justify itself with real work, its auth boundary and its first capabilities land together (the
  `sysadmin-console` stories): magic-link operator login (01), grant/revoke-by-email (02), and the
  report/takedown queue (03, Decision E). Sequencing reality: grant/revoke only becomes meaningful
  once real charging is live (`billing-entitlements/03-04`), so story 02 pairs with that; the
  report/takedown queue (03) is actionable immediately, because public tales already exist.

- **C - Family plan = the full paid-tier bundle; add-on packs sold separately.** The subscription
  grants the README section 3 paid bundle - `library.full`, `play.remote`, `play.largeGroup`, and
  the `ai.*` keys once those features exist - while active. Add-on packs stay a separate one-time
  purchase (`pack.<id>`) on the same billing plumbing. The grant is **lease-shaped**: the
  `EntitlementGrant` row carries a `validThrough` and a `source` (subscription vs one-time),
  refreshed on each renewal webhook, so the session-creation check answers "active?" from a single
  Table Storage read with no call to Stripe on the play path.

- **D - Dunning: a ~7-day grace window on `past_due`.** A failed card keeps the family unlocked for
  about a week (the grant's `validThrough` is extended a grace period on `past_due`, not immediately
  expired), then falls back to the generous free tier. Avoids locking a family mid-car-ride over a
  transient card failure; full dunning/retry stays parked (`billing-entitlements`).

- **E - Public content: report -> auto-hide-after-N -> operator review.** A "report this tale"
  control on public keepsake tales; reports accumulate, a tale auto-hides at a threshold N, and the
  operator confirms or restores it from the back office (Decision B). Closes the child-safety gap on
  already-public content without letting a single bad actor unilaterally suppress a tale (the
  threshold) and without waiting on always-on human moderation (the auto-hide). N and the review
  queue live in `sysadmin-console` (story 03).

- **F - Proof at CreateRoom: an access token on the SignalR hub connection.** The host's purchaser
  session token (from the magic-link session, Decision A) is supplied to the hub via SignalR's
  standard `accessTokenFactory`; the server validates it at connect and, at `CreateRoom`, resolves
  the active subscription to a capability set and stores **only** `SessionEntitlements` on `Room`.
  One shared connection (CLAUDE.md), no cross-domain cookie handling; anonymous players supply no
  token and hit the default-unlocked path unchanged. Here the invariant is upheld by **discipline** -
  `CreateRoom` sees the resolved identity briefly and must discard it, keeping only capabilities - so
  the review guard on `CreateRoom` (no purchaser id ever assigned to `Room`) is the enforcement. The
  structurally-enforced REST mint-session alternative was considered and set aside as more moving
  parts than a solo alpha needs.

These six turn the exploration into a buildable shape. None of them changes the `billing-entitlements/01`
seam contract; decomposition into stories follows.

## Consequences

- A new `sysadmin-console` feature folder exists (feature.md exploration). Per Decision B it is **no
  longer a deferred umbrella**: the owner elected to build the separate back office now, so its first
  stories (magic-link operator login, grant/revoke, report/takedown) are near-term rather than
  minted-on-need - though grant/revoke still pairs with real charging going live.
- `accounts-identity` and `billing-entitlements` gain small, additive scope (lease-shaped grants,
  subscription webhook cases, the family-plan bundle) recorded as Decisions entries pointing here -
  no reshaping of the #70 seam contract.
- The load-bearing invariant ("entitlement travels with the session, not identity; purchaser
  identity resolves to capabilities *before* touching `Room`") becomes the thing every account /
  subscription / admin story must be reviewed against, exactly as "meter compute per session, not
  identity" already is for the cost gate.
- Nothing here is built yet: this is a written exploration that surfaces the decisions in Open
  Decisions for the owner. Decomposition into build stories follows once A-F are resolved.
