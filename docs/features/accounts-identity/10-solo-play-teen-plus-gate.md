# Story: Solo play's teen-plus gate

**Feature:** Accounts & Identity  ·  **Status:** Not Started  ·  **Issue:** #247

## Context
accounts-identity/09 built the child-safety fix for GROUP play: the teen-plus
content tier is gated behind an affirmative, server-resolved adult-unlock
signal captured onto `Room` at `CreateRoom`, and `GameHub.StartRound` computes
`effectiveFamilySafe = room.AdultUnlocked ? familySafe : true` - the client's
own `familySafe` value is honored only once an adult signal has already been
resolved, never before. Story 09's own Out of Scope section named the gap this
story closes, in its own words: **"Solo play's teen-plus gate (SCOPED OUT
here - needs its own follow-up)"** - `AC-07` is enforced at `GameHub.StartRound`,
which is GROUP play, and solo play is client-driven with no server session
(`GameHub.cs`'s own comment: "Solo play has no server session ... it is
client-driven"), so solo's teen-plus tier stays gated client-side and is NOT
closed by story 09. [ADR 0003](../../adr/0003-admin-platform-and-family-accounts.md)
records the identical deferral under "Solo play is not yet covered (known
gap, follow-up)" and this feature's own `feature.md` Parked section carries it
as "Solo-play teen-plus gate (FOLLOW-UP, surfaced 2026-07-08 review)." This
story is that follow-up.

**THE PROBLEM, stated plainly.** Solo play's whole seed library - every
template, INCLUDING the ~14 teen-plus ones (mild dating/office/party
innuendo, tagged `familySafe: false` in `seedLibrary.ts`) - is bundled into
the web build and cached offline (PWA-first, README section 4). Template
selection is pure client-side JS: `web/src/pages/Solo.tsx` holds the
`familySafe` toggle as local React state (`useState(FAMILY_SAFE_DEFAULT)`)
and passes it straight into `mode.eligibleTemplates(seedLibrary, familySafe)`,
which bottoms out in `web/src/content/familySafe.ts`'s `selectTemplates` - a
pure, honest function that does exactly what it is told and nothing more, per
its own header: **"Default posture ... is a caller concern, not something
this module enforces by itself - there is no hidden global toggle here."**
The toggle is UI-only state with ZERO server backing. A kid on a family
tablet can flip the toggle off (via the on-screen control, or via DevTools
against a cached bundle) and reach teen-plus with no identity check
whatsoever - the exact same root cause story 09's adversarial-review finding
#1 named for group play, left open here because solo has no session to
attach a captured signal to.

**THE CHOSEN APPROACH ("C-lite," owner decision 2026-07-10).** Rather than
the heavier alternatives (moving solo selection fully server-side, gating the
library download itself, or minting a lightweight solo server session - all
explicitly set aside, see Out of Scope), this story builds a small,
READ-ONLY, anonymous-friendly endpoint that resolves the SAME adult-unlock
signal `GameHub.OnConnectedAsync` already resolves for group play, using the
SAME resolver (purchaser credential -> family-device token's
`IsAdultConfirmedDevice` -> neither, `false`), and returns just the boolean.
Solo calls it once on mount and honors the family-safe toggle ONLY when that
boolean is `true` - mirroring group's `room.AdultUnlocked ? familySafe : true`
formula client-side, because solo has no `Room` to capture it onto. This is
**an identity-aware nudge, not a structural "can never"** - see the honest
limits below, which this story does not paper over.

See [feature.md](./feature.md), the ADR's Security posture (the identity-
discarded-at-the-boundary rule this endpoint must also uphold), and
accounts-identity/09 (the resolver and the device-link/purchaser-credential
plumbing this story reuses rather than forks).

## Acceptance Criteria
- [ ] AC-01 (anonymous solo is safe by default): Given a device with no
      purchaser session and no family-device token (the overwhelming common
      case - a kid's own tablet, a fresh browser, an incognito window), when
      solo play starts, then the resolved adult signal is `false` and solo is
      served family-safe content REGARDLESS of the family-safe toggle's
      position - the toggle cannot, by itself, unlock teen-plus on a device
      with no adult signal at all.
- [ ] AC-02 (an adult signal unlocks the toggle): Given a device carrying
      EITHER a signed-in purchaser session (accounts-identity/03,
      adult-by-construction) OR a family-device token whose row has
      `IsAdultConfirmedDevice = true` (accounts-identity/09, an adult opt-in
      from the Account page), when solo play starts and the resolved adult
      signal is `true`, then the family-safe toggle behaves exactly as it
      does today - the player's own toggle position is honored, on or off.
- [ ] AC-03 (a linked-but-unconfirmed device stays safe): Given a device
      holding a family-device token whose row has `IsAdultConfirmedDevice =
      false` (the default for every freshly redeemed device, accounts-
      identity/09 AC-02), when solo play starts, then the resolved adult
      signal is `false` and solo is family-safe regardless of the toggle -
      the same "redeemed device defaults to SAFE" posture story 09's AC-07b
      established for group play, mirrored here rather than re-decided.
- [ ] AC-04 (fail-safe, non-negotiable): Given the adult-signal endpoint is
      unreachable, times out, or returns an error (offline play, a transient
      network failure, a cold PWA cache with no connectivity), when solo
      resolves its adult signal, then it treats the signal as `false` and
      serves family-safe content - an absent or failed resolution NEVER
      defaults to teen-plus. Only a POSITIVE, freshly resolved `true` value
      unlocks the toggle; every other outcome (miss, error, timeout, offline)
      is treated identically to "no adult signal."
- [ ] AC-05 (no PII, server-resolved only): Given the adult-signal endpoint,
      then its response body carries EXACTLY `{ adultUnlocked: boolean }` and
      nothing else - no account id, no email, no device-token identifier, no
      capability list. The value is computed entirely server-side from
      whatever credential the request presents; there is no request
      parameter or body field the client can set to directly assert
      `adultUnlocked` - the boolean is resolved, never accepted as input.
- [ ] AC-06 (one resolver, not a fork): Given the adult-signal endpoint's
      resolution logic, then it is the SAME resolver `GameHub.OnConnectedAsync`
      already calls for group play (purchaser credential first, adult-by-
      construction; else a family-device token's `IsAdultConfirmedDevice`;
      else `false`) - not a second, parallel implementation that could drift
      from it. A code-level check confirms both call sites route through one
      shared resolution path.
- [ ] AC-07 (honest scope - a nudge, not a structural fix): Given this
      story's design, then it is documented, in-product-comment and here,
      as an identity-aware CLIENT nudge rather than story 09's structural
      group-play "can never" guarantee: the teen-plus templates remain
      bundled in the web build and cached offline regardless of any signal,
      so a determined, technically capable kid can still reach them by
      overriding the client-held boolean or reading the cached bundle
      directly - the same bundled-content caveat group play's `Room`-based
      gate does not have to make (its GATE is server-side even though its
      catalog also ships client-visible template metadata to the host).
      Closing that residual gap is out of scope here (see Out of Scope's
      escalation trigger) and this AC exists so the guarantee is never
      overclaimed a second time.

## Out of Scope
- **The Option-B content-supply gate (the escalation trigger, permanently
  out of scope HERE).** If the teen-plus tier ever grows beyond MILD content
  (its current ~14 templates are dating/office/party innuendo, nothing
  stronger), escalate to an adult-signal-gated CONTENT SUPPLY: move the
  teen-plus templates out of the bundle entirely and serve them only to a
  request that presents a resolved adult signal, so a no-signal device never
  RECEIVES the prose at all - the only truly un-spoofable fix, because there
  is nothing cached to override. That escalation is a real, and heavier,
  design change (a new content-delivery endpoint, cache-busting
  implications for the PWA, a different relationship between the seed
  library and the build) and is deliberately not built here; this story is
  recorded as the trigger for it, not a substitute.
- **Any change to group play's AC-07.** Story 09's server-enforced,
  `Room`-captured gate is already shipped and already correct for group play;
  this story does not touch `GameHub.StartRound`'s effective-`familySafe`
  computation, `Room.CaptureAdultUnlocked`, or host-migration's guard
  (AC-08 there).
- **Any new sign-in requirement for solo.** Solo stays anonymous-capable
  exactly as today - this story adds an optional signal a device MAY carry,
  never a login solo requires to play at all.
- **Moving solo template selection fully server-side, or minting a solo
  server session.** Both were considered (ADR 0003's own list of solo
  follow-up options: "move solo content selection server-side ... or mint a
  lightweight solo session") and set aside by the owner in favor of the
  lighter client-nudge shape here. Revisit only if the escalation trigger
  above fires.
- **Per-kid-profile anything.** Out of bounds per ADR 0003 Decision 1's kid-
  profile boundary (a seat preset is never an identity) - this story resolves
  a DEVICE-level signal, exactly like group play's `Room.AdultUnlocked`, never
  a per-kid one.
- **Any change to `familySafe.ts`'s `selectTemplates`/`isFamilySafe`
  functions.** They stay exactly as documented in their own header - pure,
  callee-agnostic, "the default posture is a caller concern." This story
  changes the CALLER (`Solo.tsx`), never the selector.

## Technical Notes
- **api - the new endpoint:** a small, anonymous-accessible, READ-ONLY
  endpoint on the existing `AccountsController` (`api/src/Controllers/
  AccountsController.cs`, base route `api/accounts`) - e.g.
  `GET /api/accounts/adult-signal`. It reads the SAME credential the hub's
  `accessTokenFactory` prefers (purchaser session credential, else a stored
  family-device token, else nothing) via `AccountsController`'s EXISTING
  `ReadCredential()` helper - the `Authorization: Bearer` header, falling back
  to the HttpOnly cookie - exactly as the other account endpoints do. NEVER a
  query-string or URL path segment: a bearer credential in a URL leaks to
  access logs / App Insights / the Referer header (the ADR's "handles are
  secrets" rule), so this endpoint takes it only from the header/cookie. It
  returns `{ adultUnlocked: boolean }`. No request body, no PII in, no PII out.
- **Reuse, don't fork the resolver (AC-06).** `GameHub.OnConnectedAsync`
  (`api/src/Hubs/GameHub.cs`, lines ~524-633) already implements the exact
  three-step resolution this endpoint needs: try `_purchaserCredentials
  .ResolvePurchaserEmail(token)` first (adult-by-construction on a hit); else
  try `_deviceLinks.ResolveAsync(token, ...)` and read its
  `IsAdultConfirmedDevice` flag; else `false`. Extract that resolution into a
  small shared service (e.g. `IAdultSignalResolver` /
  `AdultSignalResolutionService`) both `OnConnectedAsync` and the new
  controller action call, rather than duplicating the branching logic inline
  in the controller - this is the concrete mechanism behind AC-06's "one
  resolver" requirement, and keeps solo and group from ever diverging on what
  "adult signal" means. Both callers still discard the resolved identity
  string at the boundary (purchaser email / family account email) - the
  service returns only the bool, exactly like `ResolvedConnectionIdentity`
  does today, per the ADR's "identity is discarded at the boundary,
  structurally" security-posture bullet.
- **web - applying the gate:** `web/src/pages/Solo.tsx` calls the new
  endpoint once on mount (a small new client module, e.g.
  `web/src/account/adultSignalClient.ts`, mirroring the shape of the
  existing `deviceRedeemClient.ts`/`familyDeviceToken.ts` pair story 09
  built) and holds the resolved bool in state, defaulting to `false` until
  (and unless) a positive response arrives (AC-04's fail-safe - the default
  state IS the safe state, so a slow/failed/offline call never has to
  actively "turn off" anything). Where `Solo.tsx` currently passes its raw
  `familySafe` state straight into `mode.eligibleTemplates(seedLibrary,
  familySafe)` (line ~517), it instead computes an EFFECTIVE value first -
  `const effectiveFamilySafe = adultUnlocked ? familySafe : true;` - the
  client-side mirror of `GameHub.StartRound`'s `room.AdultUnlocked ?
  familySafe : true` - and passes THAT into `eligibleTemplates`/
  `selectTemplates`. `familySafe.ts` itself is untouched (Out of Scope); only
  its caller's input changes.
- **The toggle, when there is no adult signal, is informational/disabled -
  never the source of truth** - the same posture story 09's AC-07 states for
  group play's client-visible toggle ("if shown at all for a session with no
  adult-unlock signal, is informational/disabled - never the source of
  truth"). Consider visually reflecting this in `FamilySafeToggle`'s usage on
  the solo setup screen (a disabled/locked affordance when `adultUnlocked` is
  `false`) as a UX nicety - the enforcement itself is the effective-value
  computation above, which holds even if the toggle is not visually altered
  at all.
- **The endpoint's credential-read plumbing already exists on the client
  side:** `useGameHub.ts`'s `accessTokenFactory` (`purchaserCredentialRef
  .current ?? familyDeviceTokenRef.current ?? ''`) is the precedent for
  "what credential does this device currently hold" - the new
  `adultSignalClient.ts` module should read from the SAME two sources
  (`usePurchaserSession()`'s credential, `loadFamilyDeviceToken()`) rather
  than inventing a third way to ask "what does this device have."
- **Files:** `api/src/Accounts/` or a new small file (e.g.
  `AdultSignalResolutionService.cs`) housing the extracted shared resolver;
  `api/src/Hubs/GameHub.cs` (`OnConnectedAsync` calls the extracted service
  instead of its inline branching); `api/src/Controllers/
  AccountsController.cs` (the new `GET /api/accounts/adult-signal` action);
  `web/src/account/adultSignalClient.ts` (new); `web/src/pages/Solo.tsx`
  (mount-time call + the effective-value computation before
  `eligibleTemplates`/`selectTemplates`).
- Dependencies (accounts-identity/09's device link + resolver,
  accounts-identity/03's purchaser credential) are already merged, so no
  dependency-tolerant/degraded-path note is needed here.

## Tests
| AC | Test |
|---|---|
| AC-01 | `tests/QuibbleStone.Api.Tests/Accounts/AdultSignalTests.cs (new): a request with no credential resolves adultUnlocked=false.` |
| AC-02 | `tests/QuibbleStone.Api.Tests/Accounts/AdultSignalTests.cs: a request carrying a valid purchaser session credential resolves adultUnlocked=true; a request carrying a family-device token with IsAdultConfirmedDevice=true resolves adultUnlocked=true.` |
| AC-03 | `tests/QuibbleStone.Api.Tests/Accounts/AdultSignalTests.cs: a request carrying a family-device token with IsAdultConfirmedDevice=false resolves adultUnlocked=false.` |
| AC-04 | `web/src/account/adultSignalClient.test.ts (new, Vitest): a network error, a timeout, and a non-2xx response each resolve to false (never throw uncaught, never default true); manual: solo play with the API stopped/offline serves family-safe content.` |
| AC-05 | `tests/QuibbleStone.Api.Tests/Accounts/AdultSignalTests.cs: the response body deserializes to exactly one boolean field; assert no other property is present. Manual: attempt to pass an `adultUnlocked` value in the request and confirm it has no effect on the resolved response.` |
| AC-06 | `code-level check (reviewed in PR, not a runtime test): GameHub.OnConnectedAsync and the new controller action both call the SAME extracted resolver type - confirm via a shared-service reference, not two copies of the branching logic.` |
| AC-07 | `static intent (reviewed in PR, no automated check): the story's own Context/Out of Scope sections and an in-code comment on the new endpoint/client module state the client-nudge scope plainly; no test asserts a bundled-content guarantee this story does not make.` |

## Dependencies
- accounts-identity/09 (#229, Complete) - the family-device link, the
  `IsAdultConfirmedDevice` flag, and the `OnConnectedAsync` resolver this
  story extracts and reuses rather than duplicates.
- accounts-identity/03 (the purchaser session credential the resolver's
  first branch checks).
- [ADR 0003](../../adr/0003-admin-platform-and-family-accounts.md)'s Security
  posture - "identity is discarded at the boundary, structurally" applies to
  this new endpoint exactly as it does to `OnConnectedAsync`.
