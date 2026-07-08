<!--
  ADR 0003 - The admin platform: a stable identity spine, free family accounts (with kid seat
  presets), a server-side keepsake vault, a runtime control plane, and a reshaped operator console.
  This ADR AMENDS two stances from ADR 0002 (purchase-only accounts; no operator audit trail) and
  keeps its load-bearing invariant (no PII on the play plane) fully intact. It records the owner's
  decisions of 2026-07-08 and defines the cross-feature build order so the work can be orchestrated
  in parallel without file conflicts. Companion decompositions: accounts-identity (05-09),
  keepsake-vault (new), control-plane (new), sysadmin-console (04-07), billing-entitlements (08),
  platform-devops (07-08). Use hyphens/colons/parentheses, never em dashes.
-->

# ADR 0003: The admin platform - family accounts, the keepsake vault, the control plane, and the operator console

- **Status:** Accepted (owner resolved decisions 1-5 on 2026-07-08)
- **Date:** 2026-07-08
- **Context features:** `accounts-identity`, `billing-entitlements`, `keepsake-gallery`,
  `sysadmin-console`, `platform-devops`, plus two NEW features this ADR mints: `keepsake-vault`
  and `control-plane`.
- **Supersedes / superseded by:** amends ADR 0002 in two places (accounts are no longer
  purchase-only; a minimal operator action log now exists for money/moderation operations). ADR
  0002's load-bearing invariant - entitlement travels with the session, not identity; no PII ever
  lands on the play plane - is retained unchanged and remains the review guard.

## Context

A full audit of the admin experience (2026-07-08: the operator console, accounts/entitlements/
billing internals, and story persistence) found that the piecemeal shape of today's admin
capability will fail on exactly the two support scenarios the owner predicted:

1. **"Where are my saved stories?"** has no answer. The default save path is device-local
   IndexedDB (`web/src/gallery/localGallery.ts`): a 30-tale cap with silent oldest-first eviction,
   save errors swallowed, no server copy, and no key of any kind. Cloud sync is purchaser-only,
   manual, and cannot rescue tales saved before it existed (they lack `parts`). When a tale is
   gone, the operator console cannot even see that it - or the player - ever existed.
2. **Subscriptions will compound the pain.** There is no stable account id (SHA-256 of the email
   is the partition key in `PurchaserAccounts`, `EntitlementGrants`, and `CloudGalleryTales`, so an
   email change orphans everything); grant rows are bare `(CapabilityKey, ValidThrough?, Source)`
   leases with no plan id, Stripe subscription id, or history; webhooks are trusted with no
   reconciliation path; and `GameHub.CreateRoom` passes `purchaserIdentity: null` unconditionally
   (`GameHub.cs:536`), so ADR 0002 Decision F (host proves purchaser status via the hub access
   token) is decided but unbuilt - the family-plan bundle cannot actually unlock a session today.

Beyond the two scenarios, the audit found three structural gaps:

- **No runtime control plane.** Every operational knob is one of three ad-hoc mechanisms: a
  startup config-presence switch (redeploy to change), a hardcoded constant (report auto-hide = 3,
  AI per-IP = 30/min, seat grace, tale TTL = 30 days, gallery cap = 30), or the one bespoke
  persisted flag (Stripe mode - which is the right shape, built exactly once). Each new feature
  invents its own mechanism; that is the piecemeal engine.
- **Two operator auth schemes.** The real console (magic link + Key Vault allowlist, `/admin`
  bundle) coexists with the interim `X-Operator-Secret` shared-secret gate for the Stripe toggle,
  which lives at a link-less route inside the KID bundle (`/admin/billing-mode`).
- **No operator action trail.** Defensible for gameplay ("toy, not a system of record"), not for
  the moment real money is charged and a grant/revoke/takedown is disputed.

## Decisions (owner, 2026-07-08)

1. **Free family accounts, with kid seat presets built now.** The account decouples from purchase:
   an account is "an adult who wants things to persist"; a purchaser is an account that also holds
   paid grants. Same magic-link plumbing, email-only, adult-by-construction. Kid profiles ship in
   the same change as **seat presets** - named (nickname + Guardian variant) presets stored under
   the family account as a join-time convenience - with a firm boundary (below).
2. **Keepsakes go server-side by default.** Every completed reveal is auto-saved into a
   **keepsake vault**: an anonymous, server-side store keyed by a device-held random vault id, with
   a TTL for unclaimed vaults. Claiming a vault into a family account makes its tales permanent and
   cross-device; a human-friendly claim code allows recovery without an account. Tales are tied to
   the FAMILY, not to a kid profile.
3. **A minimal operator action log.** One append-only row (operator, action, target, timestamp)
   per money- or moderation-affecting operator action. This amends ADR 0002's "no audit ceremony"
   stance for the billing/moderation plane only; gameplay stays ceremony-free.
4. **UAT is rebadged beta-live; this work gets a second environment.** The existing UAT instance
   becomes the beta the friends-and-family test runs on; a new instance (provisioned from the same
   Bicep via a parameter set) hosts this platform work so the beta stays stable. The platform
   layers (identity spine, control plane) land BEFORE Stripe goes live.
5. **Entitlements stay session-captured (capture-once).** No mid-session refresh. A grant or
   revoke takes effect at the next session creation. See "How a child gets family entitlements"
   below for what this means for kids on a family plan - the **family device link** is the
   mechanism that makes a kid's own device count.

## The invariant retained, and the two amendments

**Retained, verbatim from ADR 0002:** entitlement travels with the session, not identity. Purchaser
or family identity is resolved to capabilities at `CreateRoom` and discarded at that boundary; only
`SessionEntitlements` lands on `Room`; no PII, account id, or device link ever lands on `Room`/
`Player`, is broadcast, or is joined to a nickname. Kids join with a code + nickname, forever. Every
story below is reviewed against this exactly as before.

**Amendment 1 - accounts are no longer purchase-only.** ADR 0002 Decision A's account ("created
only at purchase") becomes the **family account**: creatable free, still email + created-at (plus
the new stable id), still adult-anchored. Purchase adds grants to an account; it no longer creates
the account concept. Rationale: purchase-only accounts - not player anonymity - are what made
keepsakes unrecoverable and support blind. Player anonymity is untouched.

**Amendment 2 - a minimal action log exists for the money/moderation plane.** ADR 0002's "no
audit-trail ceremony" stance stands for gameplay and content, and the console still does not grow
role hierarchies or compliance dashboards. But operator grant/revoke, takedown/restore, Stripe mode
flips, settings changes, and support verbs each append one log row. This is dispute insurance, not
compliance ceremony: no immutability guarantees, no retention policy beyond a pragmatic cap.

### The kid-profile boundary (Decision 1, the firm edge)

A kid profile is a **seat preset, never an identity**:

- It is a named (nickname + Guardian variant) preset stored under the family account, offered as a
  one-tap choice in the join flow on a device that holds a family credential or device link.
- Selecting a preset is EXACTLY equivalent to typing that nickname and picking that Guardian by
  hand. Nothing preset-related lands on `Room` or `Player`; the server cannot tell a preset join
  from a manual join.
- No per-profile history, no per-profile gallery (the vault is family-level), no per-profile
  entitlements, no kid login, no kid PII (a preset name is a nickname and passes the same safety
  filter as any nickname).
- If a future feature wants per-kid anything, that is a new ADR, not a story-level slide.

### How a child gets family entitlements (Decision 5 clarified)

Entitlements unlock ROOMS, not people. The full chain once this ADR's work lands:

1. A room created from a device that presents a family credential (signed-in parent) or a **family
   device link token** (a kid's tablet the parent linked once) resolves that family's grants at
   `CreateRoom` - and the whole room, every player in it including anonymous friends, plays with
   the family capabilities for that session.
2. So yes: a child on a family plan gets the entitlements **at the start of the next game**,
   provided the room is created from a family-linked device. A grant or renewal mid-game changes
   nothing until the next room.
3. A child joining someone ELSE's room gets that room's capabilities, whatever they are. Kids
   never carry entitlements with them personally - that would require kid identity, which is
   exactly what the invariant forbids.

The **family device link** is the new mechanism making (1) cover kids' own devices: the parent
generates a short link code from the Account page; the kid's device redeems it once and stores a
long-lived, individually revocable family-device token; `CreateRoom` resolves it server-side to
capabilities the same way it resolves a purchaser credential (identity discarded at the boundary).
The Account page lists linked devices and can revoke any of them.

The explicit use case (owner, 2026-07-08): a parent buys a kid an add-on pack or capability and
then does NOT hand-hold - the kid plays independently on their own device. The link is
set-and-forget: buy once, link once, every later room the kid creates carries the family grants.
Two refinements follow from independent kid play:

- **The kid-device flag (in scope, story `accounts-identity/09`).** A linked device can be marked
  as a kid device by the parent, which locks the family-safe toggle ON for rooms that device
  creates (server-enforced, not a client hint: the forced state is captured at `CreateRoom` and
  overrides whatever `familySafe` value the client submits at `StartRound`, since family-safe is
  chosen per round today). Without it, an unsupervised kid
  host could flip family-safe off and reveal the teen-plus content tier - the gap independent play
  opens is content exposure, not capability misuse. The flag lives on the link (a device
  attribute), never on a player, so the invariant is untouched.
- **Per-device capability scoping (parked).** Letting a parent choose WHICH grants a linked device
  carries adds a second entitlement dimension for little value: the free tier is generous, packs
  applying family-wide cost nothing, and the AI cost gate bounds spend per session and per month
  regardless of who plays. Revisit only on a demonstrated need.

## The architecture: four layers

### Layer 0 - identity spine (`accounts-identity/05-09`, `platform-devops/07`)

- **Stable `AccountId` (GUID)** minted at account creation; email becomes a mutable login
  attribute. Grants, vault claims, and cloud tales key off `AccountId`; the email-hash lookup
  becomes an index to the account, not the primary key of everything.
- **ADR 0002 Decision F actually wired**: the hub connection carries the purchaser/family
  credential via `accessTokenFactory`; `CreateRoom` resolves it to capabilities; the `null`
  hardcode dies.
- **Free family account** (Amendment 1) + **kid seat presets** (boundary above) + the **family
  device link**.
- **Durable Data Protection key ring** (Key Vault / Blob backed) so purchaser and operator
  credentials survive restarts and scale-out.

### Layer 1 - control plane (`control-plane/01-03`)

- **One runtime settings service**: a typed key catalog with defaults in code, persisted overrides
  in Table Storage (the Stripe-mode store pattern, generalized), a short cache, a changed-by/at
  stamp, and one Operator-policy admin endpoint + console page. Config-presence switches remain
  ONLY for genuine infrastructure wiring (connection strings, endpoints).
- **Capability scopes**: the existing catalog gains a system scope, evaluated before account
  grants: system flag (kill switch / not-launched) -> account grant -> session snapshot. The
  `IEntitlementService` contract and the capture-once discipline do not change; only what feeds
  the evaluation does. System keys start with `publishing.enabled`, `ai.enabled`, `email.enabled`.
- **Knob migration**: the hardcoded constants (auto-hide threshold, AI per-IP limit, per-session
  quota, monthly ceiling, seat grace window, tale TTL, gallery cap) move to settings keys as they
  are touched.

### Layer 2 - recovery and support data (`keepsake-vault/01-04`, `billing-entitlements/08`)

- **The vault** (Decision 2): auto-save on reveal completion, anonymous vault id, TTL for
  unclaimed, claim-to-family, claim code, soft-delete with a restore window (takedowns become
  soft-deletes too). The device IndexedDB gallery becomes a cache/offline view over the vault.
- **Grant metadata + reconciliation**: `EntitlementGrant` gains a grant id, plan id, and Stripe
  subscription id; a per-account "resync from Stripe" service becomes the recovery path when a
  webhook was missed or a subscription was edited in the Stripe dashboard.

### Layer 3 - the console (`sysadmin-console/04-07`)

- **One bundle, one auth**: the Stripe toggle relocates into the operator console behind the real
  Operator policy; the interim `IOperatorGate` shared-secret scheme and the kid-bundle
  `/admin/billing-mode` page are deleted (closes the `billing-entitlements/07` follow-up).
- **Jobs, not feature tabs**: the shell reorganizes around Support (find a person, fix their
  problem), Content (moderation queue now; content-factory vetting and pack publishing later live
  in this same shell), and Operations (settings/flags, Stripe mode, AI spend snapshot - still
  linking out to App Insights rather than rebuilding dashboards).
- **Scoped authz now, RBAC later**: endpoints carry a scope (`support` / `content` / `ops`); the
  single operator holds all scopes; a future moderator is an allowlist entry with a scope list,
  not a rework.
- **The action log** (Decision 3) with a simple console view.
- **Support lookup + verbs**: lookup by email, claim code, or tale slug -> account, grants (with
  source and expiry), subscription state (plan, status, last webhook), vault/tale counts; verbs:
  resend magic link, extend a tale link TTL, restore a soft-deleted tale, comp/extend an
  entitlement, per-account Stripe resync.

## Cross-feature build order (for orchestration)

Decompositions live in each feature folder; every feature has its own DAG-ready implementation.md.
The cross-feature ordering constraints, chosen so parallel builders own disjoint files:

| Wave | Stories (parallel within a wave unless noted) | Shared-file hazard |
|---|---|---|
| 1 | `accounts-identity/05` (AccountId spine), `keepsake-vault/01` (vault store + auto-save), `control-plane/01` (settings service), `sysadmin-console/04` (auth unification), `platform-devops/07` (key ring), `platform-devops/08` (second environment) | all but 08 register services in `Program.cs` - land as separate small PRs, rebase serially; do not batch |
| 2 | `accounts-identity/06` (Decision F wiring), `accounts-identity/07` (free family account), `keepsake-vault/02` (gallery over vault), `control-plane/02` (capability scopes), `sysadmin-console/05` (jobs shell), `billing-entitlements/08` (grant metadata + resync) | 06 and control-plane/02 both touch `api/src/Entitlements/` - serialize those two |
| 3 | `accounts-identity/08` (kid seat presets), `accounts-identity/09` (family device link), `keepsake-vault/03` (claim + recovery), `keepsake-vault/04` (soft delete + restore), `control-plane/03` (knob migration - touches many files, run it alone in its slot), `sysadmin-console/06` (action log) | knob migration overlaps whatever else is in flight - schedule it when the tree is quiet |
| 4 | `sysadmin-console/07` (support lookup + verbs - consumes vault claim codes, grant metadata, the action log) | none new |

`Program.cs` is the one systemic hotspot: nearly every wave-1/2 story adds a service registration.
The rule for orchestration: stories touching `Program.cs` merge one at a time (small, rebased PRs),
even when everything else about them is parallel-safe.

## Consequences

- Two new feature folders exist (`keepsake-vault`, `control-plane`); `accounts-identity`,
  `sysadmin-console`, `billing-entitlements`, and `platform-devops` gain stories. Each carries its
  own implementation.md wave plan; this ADR owns only the cross-feature ordering.
- ADR 0002 gets a header note pointing here for the two amended stances; everything else there
  still stands, especially the invariant and Decision F's design (which this ADR finally builds).
- `keepsake-gallery/03` (device-local gallery) and `/05` (purchaser cloud gallery) are not
  reopened, but the vault supersedes their roles over time: the local gallery becomes a cache, and
  the manual purchaser upload is retired once vault claiming ships. Recorded in `keepsake-vault`'s
  feature.md rather than by editing shipped stories.
- The friends-and-family beta runs on the rebadged UAT instance unblocked; this work proceeds on
  the second environment; Stripe live waits for Layers 0-1.
- The anonymity posture is unchanged where it matters: no PII on the play plane, kids anonymous
  forever, vault ids and device-link tokens are random handles rather than identity. The only data
  posture change is that an adult may now hold an account without paying.
