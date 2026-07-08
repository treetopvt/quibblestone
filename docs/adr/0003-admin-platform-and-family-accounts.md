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

**Retained on the play plane, exactly as ADR 0002 states it:** entitlement travels with the
session, not identity. Purchaser or family identity is resolved to capabilities at `CreateRoom` and
discarded at that boundary; only `SessionEntitlements` lands on `Room`; no PII, account id, or
device link ever lands on `Room`/`Player`, is broadcast, or is joined to a nickname *in the game
itself*. Kids join with a code + nickname, forever. Every story below is reviewed against this
exactly as before.

**Honest carve-out (added 2026-07-08 after the adversarial review - Decision 2/finding #5).** The
invariant governs the PLAY plane. It does NOT and cannot mean "a nickname string never co-resides
with an account anywhere," because the features the owner chose - family accounts, kid seat presets,
and vault claiming - deliberately place chosen nicknames under a family `AccountId` on the
ACCOUNT/consented plane (a preset is `accountId -> {nickname, variant}`; a claimed vault carries the
tale bylines). This is adult-owned, adult-consented household data, created only by an adult signing
in and claiming - never harvested from play, never surfaced to co-players, never on `Room`/`Player`.
The two planes and the firewall between them:

- **Play plane (the invariant, absolute):** `Room`/`Player`, broadcasts, telemetry, and the AI-gate
  attribution carry no identity and no account linkage. A preset join is byte-for-byte
  indistinguishable from a manual join. This is enforced structurally (no field exists to hold it).
- **Account plane (consented household data):** presets and claimed-vault bylines may associate a
  nickname with a family account, because an adult put it there. This is treated as household PII
  under the privacy posture, minimized and TTL'd, not as a violation of the play-plane invariant.
- **The firewall (structurally enforced, finding #2/#6):** nothing may bridge the two planes. In
  particular the operator support console (sysadmin-console/07) MUST NOT resolve a public slug or a
  vault claim code to an account email, and MUST NOT project nickname/byline/timestamp content -
  only counts and account-plane facts. That guard is a code-review-enforced structural rule, not an
  assertion (see the review-driven revisions below).

Do not, anywhere below, describe the invariant as "retained verbatim" - it is retained on the play
plane, with this explicit account-plane carve-out.

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

- **Teen-plus content is gated behind an affirmative adult signal (redesigned 2026-07-08 after the
  adversarial review - Decision 1/finding #1).** The review found the original kid-device design
  defended the wrong gate: the teen-plus tier is gated ONLY by the per-round `familySafe` flag and
  by NO entitlement (`TemplateCatalog.cs` / `FamilySafeContentSelector`), and the "lock" rode an
  optional, client-held, clearable device token - so an unsupervised kid could reach teen-plus
  simply by playing in a fresh/incognito browser (no token presented -> no forced state), at zero
  cost because the content was free. A lock that rides a credential its own holder can shed is not
  enforcement. The fix inverts the default posture: **teen-plus content requires an affirmative
  adult signal that a token-less session cannot obtain, and is family-safe by default otherwise.**
  Concretely (story `accounts-identity/09` + a child-safety touch to the content selector):
  - The teen-plus tier is served only when the room's session carries an explicit adult unlock -
    resolved server-side at `CreateRoom` from a signed-in adult credential or an adult-confirmed
    device, captured onto the room like an entitlement. A room with no such signal (anonymous play,
    incognito, cleared storage, a kid device) is family-safe regardless of any client `familySafe`
    value at `StartRound`.
  - Host-migration cannot open the gate: the adult-unlock state is a property of the room's
    captured session, not of whoever currently holds the host role, so promoting a kid to host
    (`EnsureHostLocked`) never flips it on.
  - A redeemed device defaults to family-safe-locked (`IsKidDevice` effectively defaults to the
    SAFE state); reaching teen-plus is an opt-IN an adult performs, not an opt-out a kid can skip.
  - The device flag still lives on the link (a device attribute), never on a player, so the
    play-plane invariant is untouched. What changed is that content safety no longer depends on the
    presence of a client-held token - the safe state is the default and the unlock is the exception.
  This is a deliberate scope addition to story 09 and a small child-safety change to the content
  selector; it supersedes the "force the flag on / override StartRound" mechanism described in the
  earlier draft of this section.
  - **Solo play is not yet covered (known gap, follow-up).** The `StartRound`-based enforcement is
    GROUP play. Solo play is client-driven with no server session today, so its teen-plus tier
    stays client-gated and bypassable (clear storage / modified client) - the same root cause on a
    surface no reviewer was pointed at. The group fix above does NOT close solo; the "can never
    reach teen-plus without an adult signal" guarantee is scoped to group play until a solo
    follow-up (move solo content selection server-side, gate the library download behind the adult
    signal, or mint a lightweight solo session - a real design choice) lands. Recorded so the
    guarantee is not overclaimed a second time.
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

**Canonical wave numbers.** The `Wave` column here is authoritative. Each feature's own
implementation.md MUST use these same numbers (or add an explicit `ADR-Wave` column that maps to
them) - do NOT let a feature renumber locally (the 2026-07-08 review found accounts-identity using
"waves 5-8" and billing "wave 7" for stories that are ADR Waves 1-3, which would mislead an
orchestrator grouping by the local number). sysadmin-console must also keep its historical 01-03
wave table out of the DAG-parsed section so "wave 1" is unambiguous.

| Wave | Stories (parallel within a wave unless noted) | Shared-file hazard |
|---|---|---|
| 1 | `accounts-identity/05` (AccountId spine), `keepsake-vault/01` (vault store + auto-save), `control-plane/01` (settings service), `sysadmin-console/04` (auth unification), `platform-devops/07` (key ring), `platform-devops/08` (second environment) | (a) all but 08 register services in `Program.cs` - separate small PRs, rebase serially, do not batch. (b) **07 and 08 both edit `.github/workflows/deploy.yml`** (07 a comment/wiring touch, 08 the target-environment input) - they are NOT disjoint; serialize them on that file (correcting the earlier "disjoint" claim). (c) `accounts/05` re-keys `api/src/Entitlements/StoredValueEntitlementService.cs` - see Wave 2 note. |
| 2 | `accounts-identity/06` (Decision F wiring), `accounts-identity/07` (free family account), `keepsake-vault/02` (gallery over vault), `control-plane/02` (capability scopes), `sysadmin-console/05` (jobs shell), `billing-entitlements/08` (grant metadata + resync) | **Corrected 2026-07-08:** the `api/src/Entitlements/` hotspot is NOT 06<->02. Story 06 only edits the `CreateRoom` call site in `GameHub.cs` (the `EvaluateForSession(purchaserIdentity, ...)` signature already accepts the arg) - it does not touch `api/src/Entitlements/`. The real chain on `StoredValueEntitlementService.cs` is **`accounts/05` (Wave 1 re-key) -> `control-plane/02` (Wave 2 system-flag composition)**, so control-plane/02 has a hard depends-on `accounts/05` (not just control-plane/01). Additionally **`billing/08` co-occupies the folder** (`EntitlementGrant.cs` + the grant store) - its record-shape change should land before or after control-plane/02's edit to that consumer, not concurrently. |
| 3 | `accounts-identity/08` (kid seat presets), `accounts-identity/09` (family device link + teen-plus gate + kid-device), `keepsake-vault/03` (claim + recovery), `keepsake-vault/04` (soft delete + restore), `control-plane/03` (knob migration - run alone in its slot), `sysadmin-console/06` (action log) | (a) **`Program.cs` is touched by FOUR W3 stories** (08 preset store, 09 device-token store, control-plane/03 limiter factories, sysadmin-console/06 log store) - the serial-merge rule applies here at higher concurrency than W1/W2. (b) `accounts/09` also edits **`web/src/App.tsx`** (a redeem route - add it to 09's footprint; it collides with `sysadmin-console/04`'s App.tsx route deletion if 09 is cut before 04 merges). (c) **`keepsake-vault/04` and `control-plane/03` both touch `api/src/PublishedTales/`** (04 changes `ConfirmHiddenAsync` to soft-delete; 03 migrates `AutoHideThreshold` and may touch `ReportedTalesController.cs`, the caller) - order 04's semantic change vs 03's read. |
| 4 | `sysadmin-console/07` (support lookup + verbs - consumes vault claim codes, grant metadata, the action log) | none new |

`Program.cs` is the one systemic hotspot: stories in Waves 1, 2, AND 3 add service registrations.
The rule for orchestration: stories touching `Program.cs` merge one at a time (small, rebased PRs),
even when everything else about them is parallel-safe.

## Security posture (from the 2026-07-08 adversarial review)

A five-lens adversarial review (invariant, abuse/security, wave-plan, scope, cold-builder) ran
against the plan before any code. Findings #1 (teen-plus gate) and #5 (nickname carve-out) are
resolved above; the wave-plan corrections are in the table above. The remaining findings are
binding requirements on the named stories - every affected story must satisfy the matching bullet:

- **Handles are secrets, and are treated as secrets (keepsake-vault/01, /03; accounts-identity/09).**
  A vault id, a claim code, and a family-device token are bearer credentials. Therefore:
  (a) they are carried in a request HEADER or BODY, never in the URL PATH (the existing
  `PiiScrubbingTelemetryInitializer` strips only the query string, so a path segment leaks to App
  Insights, access logs, `Referer`, and history); (b) reads are authorized by possession AND
  rate-limited (the vault READ endpoint, not just write); (c) ids/codes are server-minted with a
  crypto entropy floor (no `Math.random` fallback), and codes are single-use or short-TTL with
  rotation + revocation; (d) enumerable codes get a per-CODE attempt burn and a GLOBAL ceiling, not
  only a per-IP limiter (per-IP is defeated by IP rotation).
- **Identity is discarded at the boundary, structurally (accounts-identity/06, /09).** The
  per-connection resolution stores ONLY the resolved capability set (and the adult-unlock boolean),
  never the purchaser/family email or identity string. It is a singleton service (SignalR builds a
  fresh hub per invocation, so it cannot be a hub field - see the cold-builder note in that story),
  registered in `Program.cs`, cleared on disconnect. No identity string is ever keyed by
  `ConnectionId` alongside the roster.
- **The support console cannot bridge the planes (sysadmin-console/07).** Structural, not asserted:
  the support controller does not inject nickname/byline-bearing stores in a way that lets a lookup
  project them; slug/claim-code lookups resolve to account-plane facts and COUNTS only, never to a
  byline, a timestamp, or a room. Restoring a moderation takedown carries stronger friction than
  restoring a user's own delete.
- **The control plane cannot disable its own safety rails (control-plane/01, /03).** Every numeric
  setting has a min/max bound enforced on PUT (type-parse is not enough); rate-limit permits are
  clamped to a sane floor at the read site so a bad value can neither disable nor zero a limiter.
  The AI monthly spend ceiling and the `*.enabled` kill switches are NOT freely runtime-flippable to
  arbitrary values (bounded, and flips are confirmation-gated). Every settings change appends an
  action-log row NOW (this resolves the contradiction where control-plane/01 had the log
  out-of-scope while Amendment 2 requires it).
- **The action log is trustworthy dispute insurance (sysadmin-console/06).** The log row is written
  before (or transactionally with) the effectful action, not best-effort after, so an action cannot
  succeed with no trail. Retention is age-based with a hard floor that an operator setting cannot
  lower below (so it cannot be volume- or config-evicted by the party a dispute is about).
- **Stripe resync cannot corrupt grants (billing-entitlements/08).** The grant store is
  mode-aware (or resync refuses to write grants whose origin mode differs from the active mode), so
  a Test-mode resync can never overwrite live-derived grants; reconciliation keys by Stripe customer
  id / `AccountId`, not raw email; the resync endpoint is rate-limited/debounced.
- **Credentials survive scale-out safely (platform-devops/07, /08).** The durable signing key is
  generated from a CSPRNG (a `deploymentScripts` random value or an out-of-band Key Vault secret) -
  NEVER derived deterministically from `guid(resourceGroup().id, "<literal>")` (that is reproducible
  from public inputs and forges operator logins). The magic-link single-use nonce set moves to the
  same durable shared store as the key ring (a per-process set replays once per instance under
  scale-out). The key ring fails CLOSED in a deployed environment (refuse to start without durable
  backing) rather than silently reverting to per-instance keys. Each environment gets a DISTINCT
  key-ring backing store (a shared one would let a beta-minted credential validate in the platform
  environment). The new environment must sit behind the same single-hop trusted edge as beta, or
  the app's `X-Forwarded-For` trust (`KnownProxies`/`KnownNetworks` cleared, `ForwardLimit=1`) makes
  every per-IP limiter spoofable.
- **Telemetry knows the new identifiers (platform-devops or a child-safety touch).** Add
  `email`, `accountId`, `vaultId`, `claimCode`, `token`/`access_token`, `deviceToken` to
  `PiiScrubbingTelemetryInitializer`'s `SensitivePropertyKeys`, and forbid interpolating any of
  these into exception messages in the new Accounts/Vault/Support code (the scrubber cannot clean
  exception message text).

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
