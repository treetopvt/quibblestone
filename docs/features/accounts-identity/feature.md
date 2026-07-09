# Feature: Accounts & Identity

## Summary
Formalizes and extends QuibbleStone's tiered identity model: players stay
anonymous forever (join code + nickname + Guardian, no PII); a lightweight
account exists for anyone who wants things to persist, and a purchaser is an
account that also holds paid grants. This is the auth seam that
`billing-entitlements` hangs off of.

**Amended 2026-07-08 ([ADR 0003](../../adr/0003-admin-platform-and-family-accounts.md),
Layer 0 - the identity spine):** the "account exists only at purchase" framing
above predates ADR 0003 and is superseded, not erased - see the Decisions log.
Stories 01-04 (Complete) still stand exactly as shipped; stories 05-09 add a
stable `AccountId` spine, finally wire ADR 0002 Decision F's purchaser proof at
`CreateRoom`, decouple account creation from purchase into a free "family
account," and add kid seat presets + a family device link on top of it -
without ever touching player anonymity.

## README reference
README section 3 (Monetization - "Identity model (tiered)": players anonymous
forever; only the purchaser gets a lightweight account, and only when they buy;
account hooks go in early even with a minimal UI) and section 7 (Epic Map -
Phase 2, "Accounts & Identity (M) - anonymous players; lightweight purchaser
accounts"). Child-privacy posture: section 6 (minimal data on minors) and
section 3 (COPPA / GDPR-K). CLAUDE.md section 6 (Monetization seam).

## Stories
<!-- Status: Not Started | In Progress | Complete | Blocked | Dropped -->
| Story | Issue | Title | Status |
|---|---|---|---|
| 01 | #67 | Anonymous player, forever | Complete |
| 02 | #68 | Lightweight purchaser account | Complete |
| 03 | #69 | Sign-in and restore on a new device | Complete |
| 04 | #167 | Magic-link email delivery | Complete |
| 05 | #195 | Stable account id spine | Complete |
| 06 | #210 | Purchaser proof at CreateRoom (ADR 0002 Decision F, finally wired) | Complete |
| 07 | #211 | The free family account | Complete |
| 08 | #228 | Kid seat presets | Not Started |
| 09 | #229 | Family device link | Not Started |

## Dependencies
- session-engine (the existing anonymous join contract this feature formalizes:
  nickname + Guardian variant at join, no account).
- child-safety (nicknames remain free text and stay filtered regardless of
  whether the session's purchaser is signed in).
- billing-entitlements (the purchaser account exists to hold entitlements;
  story 03 restore is the read side of billing-entitlements/05).

## Design notes
- **No new identity system for players.** This feature does not touch the
  player join flow (nickname + Guardian, session-engine/02) at all - story 01
  is documentation-and-hardening of a contract that is already true, so the
  session-creation code path can assume "no account" without a special case.
  Adding accounts must be additive, never a prerequisite for play.
- **One account type, one purpose.** The account exists solely to own
  entitlements (when any) and let a purchase, or simply a wish to persist
  something, survive a device change. It is not a player profile, not a
  friends list, not a leaderboard identity - none of that is in scope for
  Phase 2. **Amended 2026-07-08 (ADR 0003 Amendment 1):** "own entitlements"
  is no longer the account's ONLY reason to exist - an account with zero
  grants (a free family account, story 07) is now a normal, expected shape,
  not a transitional or degenerate one.
- **Minimum viable account.** An email (the magic-link identity, ADR 0002
  Decision A - resolved 2026-07-03) is the only required field. No display
  name, no avatar, no date of birth, no address, no password, no OAuth SDK.
  The purchaser is assumed to be an adult (they are the one completing a
  checkout), so no age-gating flow is needed for the account itself - the
  age/safety posture that matters is about the **kids playing**, who never
  touch this account.
- **The account belongs to the adult, not the kids playing.** A parent buys
  the family plan (or simply creates a free family account) on their own
  phone; the kids in the back seat still join with just a code and a
  nickname. The account holder's sign-in state never flows down into a
  player's join experience - a signed-out room plays exactly like today.
  **Amended 2026-07-08 (ADR 0003):** a kid's OWN device can also carry the
  family's capabilities, via the family device link (story 09) - but that
  device link is an attribute of the DEVICE, never an identity the kid holds
  or logs into; the child remains anonymous, forever (accounts-identity/01).
- **Where it lives:** the account record is a small row, keyed by a stable
  `AccountId` (story 05) with the email identity as a mutable lookup index
  (magic-link, ADR 0002 Decision A), stored alongside entitlements in Azure
  Table Storage (README section 4). No new datastore is introduced.
- **Session-creation is still the only place identity matters for gameplay.**
  A room does not care who is signed in mid-session; the signed-in state is
  read once, at session-creation, by the entitlement check in
  billing-entitlements/01 - never per-request or per-answer.

## Parked - Phase 3+
- Player-side profiles (a PLAYER remembering their own nickname/Guardian
  across devices, a friends/repeat-crew list, per-player history) - explicitly
  out of scope; players stay anonymous. NOTE (2026-07-08): this is distinct
  from story 08's kid seat presets, which are a FAMILY-ACCOUNT-held join-time
  convenience, never a player identity - see story 08's "kid-profile boundary"
  for the line between the two.
- Social/OAuth sign-in of any kind (ADR 0002 Decision A resolved the identity
  provider to magic-link email only) - and, within that, multi-provider
  linking or account merge, should the mechanism ever change.
- Household / multi-purchaser management (e.g. co-parents sharing one plan
  with separate logins) - Phase 2 ships one purchaser, one account.
- Any UI beyond the minimum checkout/sign-in/restore flow (account settings
  page, purchase history beyond "what's unlocked" - see billing-entitlements/05).
- **Per-device capability scoping (PARKED 2026-07-08, ADR 0003).** Letting a
  parent choose WHICH capabilities a linked device (story 09) carries -
  rejected for now: the free tier is generous, add-on packs apply family-wide
  at no extra cost, and the AI cost gate bounds spend per session/month
  regardless of who plays. A linked device always carries the WHOLE family
  grant set; only the family-safe content state is ever device-scoped (story
  09's kid-device flag, a content-safety concern, not an entitlement).
  Revisit only on a demonstrated need.
- **Solo-play teen-plus gate (FOLLOW-UP, surfaced 2026-07-08 review).** Story
  09's AC-07 gates the teen-plus content tier behind an affirmative adult signal
  server-side at `GameHub.StartRound` - which is GROUP play only. Solo play is
  client-driven with no server session today, so its teen-plus tier stays
  client-gated and bypassable (clear storage / modified client), the same root
  cause finding #1 named. This is a KNOWN, recorded gap, not covered by story
  09: closing it is a real design choice (move solo content selection
  server-side, gate the library download behind the adult signal, or mint a
  lightweight solo session) and wants its own story. Story 09's guarantee is
  scoped to group play until it lands. Not pulled into the current slice; sized
  when the alpha shows whether solo teen-plus exposure is a real risk.

## Decisions
- 2026-07-01: Scoped to exactly the three stories above (anonymous contract,
  purchaser account, sign-in/restore) rather than a general auth system,
  because the only thing an account needs to do in this product is hold
  entitlements and survive a device change. Anything broader is scope creep
  against README section 3's "lightweight" instruction. Recorded during the
  look-ahead planning pass ahead of Slice 1 shipping.
- 2026-07-03: [ADR 0002](../../adr/0002-accounts-subscriptions-and-admin.md)
  (accounts, subscriptions, sys-admin surface) states the load-bearing invariant
  this feature's purchaser account must uphold - "entitlement travels with the
  session, not identity": the host's purchaser identity is resolved to
  capabilities at `GameHub.CreateRoom` and discarded at that boundary, so only
  the resolved capability set (never a purchaser id) lands on `Room`. The host
  proves purchaser status by supplying the magic-link session token to the hub
  via SignalR's `accessTokenFactory` (ADR 0002 Decision F).
- 2026-07-03: **Identity provider resolved (ADR 0002 Decision A): magic-link
  email.** Story 02's open provider choice is closed - purchasers sign in via an
  emailed one-time link (no password, no OAuth SDK). The same one-time-token
  issue/verify plumbing is reused for the sys-admin back office's operator login
  (`sysadmin-console/01`) against a SEPARATE operator allowlist in config / Key
  Vault; admin authorization is allowlist membership resolved at verify time and
  is never inferred from being a purchaser (`purchaser == admin` is the bug to
  prevent). Consistent with story 02 AC-01 (email or one identity, nothing more).
- 2026-07-03: **Built via `/orchestrate-feature` (all three stories on the
  `claude/orchestrate-accounts-identity-a373p2` umbrella).** Notes from the build:
  - Story 02's magic-link token verifier originally compared DECODED signature
    bytes, which accepted a padding-equivalent final base64url char (~6% of
    mutations) and flaked its tamper test. Fixed to compare the CANONICAL
    base64url strings via constant-time `FixedTimeEquals`, with an exhaustive
    regression test. Live-verified: replaying a consumed token returns
    `link-invalid` (single-use holds).
  - The purchaser session credential (story 03) uses ASP.NET Data Protection
    (`ITimeLimitedDataProtector`, 12h TTL, purpose `QuibbleStone.PurchaserSession`)
    - no new dependency, no hand-rolled crypto. The current key ring is the
    framework default (per-process, not durable); a Key Vault-backed shared key
    ring is a **billing-entitlements deployment follow-up**, not this slice.
  - AC-05 no-enumeration is structural: the request endpoint never reads the
    account store (identical neutral response for any email); the dev-only token
    echo is gated on `IsDevelopment()`. Live-verified end to end.
  - The `signed-in` happy path is not UI-drivable yet (account creation lives in
    the not-yet-built billing purchase flow; no seed endpoint) - it is covered by
    `SignInTests.cs`. Verification drove request -> verify (`no-account`),
    single-use replay, and confirmed free play (hub negotiate + the 2-device
    group-mode e2e) is unaffected by the added `/account` route.
- 2026-07-04: **Email delivery decomposed into story 04 (Not Started).** The
  magic-link transport was carried as a deferred aside in stories 02/03 and
  implementation.md ("later, the email-delivery provider's key"), never its own
  story. It is decomposed now because it is on the critical path: verified on UAT
  (after `sysadmin-console` #158 shipped) that neither purchaser sign-in nor
  operator console login can complete in a deployed environment - the API runs as
  Production (no dev-token echo), no provider is wired, and
  `Accounts:TokenSigningKey` is unset (ephemeral). Token issuance shipped (02);
  delivery is the half that stayed unbuilt (ADR 0002 Decision A named both). OPEN
  decision: provider is Azure Communication Services Email (RECOMMENDED) vs SendGrid (story 04
  Technical Notes) - ACS is Azure-native but adds an Email Communication Service +
  a verified domain to the footprint; SendGrid avoids the resource but adds a
  third-party dependency. Story 04 also promotes `Accounts:TokenSigningKey` to a
  durable Key Vault secret so a delivered link survives an app recycle.
- 2026-07-07: **Story 04 shipped (Complete)** via the PR #169-#172 chain (issue
  #167 closed). The open provider decision resolved to Azure Communication
  Services Email: the one `IEmailSender` seam (`AcsEmailSender` /
  `NoOpEmailSender`, config-presence) delivers both the purchaser and operator
  links, `infra/main.bicep` provisions the ACS Email footprint behind
  `enableEmail` (deploy-wired, with an `EMAIL_ENDPOINT` external override that
  skips provisioning), the durable `Accounts__TokenSigningKey` is Key
  Vault-backed, and the web surfaces complete sign-in from the followed email
  link. Note: the 2026-07-01 "exactly the three stories above" scoping decision
  predates story 04's decomposition - the feature is now four stories, all
  Complete.
- 2026-07-08: **[ADR 0003](../../adr/0003-admin-platform-and-family-accounts.md)
  (the admin platform: identity spine, free family accounts, the keepsake
  vault, the control plane, the operator console) accepted - Layer 0 decomposed
  into stories 05-09.** ADR 0003 amends ADR 0002 in exactly one place this
  feature owns: **Amendment 1, accounts are no longer purchase-only.** The
  account becomes "an adult who wants things to persist"; a purchaser is an
  account that ALSO holds paid grants. This does NOT reopen anything else ADR
  0002 decided (the load-bearing invariant, Decision F's design, the magic-link
  provider choice) - it only widens WHO may hold an account and WHEN it is
  created. Five new stories:
  - **05 - Stable account id spine (foundation).** Mints a durable `AccountId`
    (GUID); email becomes a mutable login attribute; `PurchaserAccounts`,
    `EntitlementGrants`, and `CloudGalleryTales` re-key off it. Trivial UAT-only
    migration (near-zero real rows).
  - **06 - Purchaser proof at CreateRoom, finally wired.** ADR 0002 Decision F
    was decided 2026-07-03 but never built (`GameHub.CreateRoom` still passes
    `purchaserIdentity: null` unconditionally) - this story closes that gap
    using plumbing that already exists.
  - **07 - The free family account.** Amendment 1's actual account-creation
    change: a sign-up flow decoupled from purchase, same magic-link plumbing,
    zero grants.
  - **08 - Kid seat presets.** A named (nickname + Guardian variant) preset
    picker, held to a firm boundary: exactly equivalent to typing a nickname
    by hand, never a kid identity (see story 08's quoted "kid-profile
    boundary"). Ships with a documented degraded path (parent's device only)
    until 09 lands.
  - **09 - Family device link.** The mechanism making a kid's OWN device count
    toward family entitlements, PLUS (owner refinement, same date) the
    kid-device flag that server-enforces family-safe content on a room created
    from a flagged device - the content-exposure gap independent kid play
    opens. Per-device capability scoping is explicitly parked (see Parked
    section) - a linked device always carries the whole family grant set.
  This feature's `implementation.md` is updated with the reuse map and Wave
  Plan for all five; see ADR 0003's "Cross-feature build order" for how this
  feature's stories interleave with the new `keepsake-vault` and
  `control-plane` features.
