<!--
  QuibbleStone roadmap - the living view of where the build is and what comes next.
  Companion to docs/features/ (the backlog as code): every item here traces to a
  story there. Update the "as of" date and the Shipped / Open sections as work lands.
  Use hyphens/colons/parentheses, never em dashes.
-->

# QuibbleStone Roadmap

**As of 2026-07-07** (after a full audit: stories vs GitHub issues vs code vs a
green build). The alpha build phase is essentially over: everything from the thin
slice through reconnect hardening, observability, accounts + billing, and the
sys-admin console is merged, and the tree is green (clean `dotnet build`, 523 xUnit
+ 363 Vitest tests passing). The bar is no longer "build the alpha" - it is **run
the alpha**, and the alpha gate is fully closed: B2 (the UAT SKU) is confirmed
live, B1/B3/B4/B5 merged via
[PR #175](https://github.com/treetopvt/quibblestone/pull/175) and are deploying
to UAT, and W5 (ACS email) is live and tested via the magic-link flow. **Nothing
left blocks inviting the friends-and-family testers** - go watch the telemetry
that now exists. (Update 2026-07-08: session-engine/12 (#180) now adds a THIRD invite
channel - email a friend the room's join link directly, reusing the ACS email seam via
its own game-invite method behind an availability probe + a per-IP rate limit. It
renders only where an email provider is configured; copy-link and the OS/browser share
sheet, `web/src/pages/useRoomInvite.ts`, remain the always-on channels.) Every path
below traces to a written story in
[`docs/features/`](./features/); this file is the map over that backlog, not new
scope.

(The interactive visual version of this map at
https://claude.ai/code/artifact/2e5c39ac-98e9-4afc-b7d4-1c06fbf677bd is a snapshot
of the 2026-07-04 view and predates this update.)

## Guiding compass

- **Do not lose momentum before something is fun** (README section 8). The slice
  is fun now, so the bar shifts to "more laughs per round" and "does it survive
  real phones in a real living room."
- **Keep it a toy** - ephemeral, anonymous, family-safe by construction (README
  sections 4, 6).
- **Every AI call rides the gate.** The gate is built and proven; new AI features
  inherit it, never bypass it (see "The AI cost gate" below).

## Where we are (shipped)

- **The core game.** Rooms, join codes, roster, Guardian avatars; solo + group
  play end to end; the coral text reveal + round-complete recap; host migration
  when the host leaves (server promotes a remaining player and broadcasts it).
- **One engine, many thin modes** - the three-axis mode abstraction with a shared
  registry consumed by solo AND group. Solo offers all four modes (mode picker,
  PR #97); **group play offers Classic Blind, Word Bank, and Progressive Reveal**
  via the host's mode pick (`group-play/05`, PR #116). Progressive Story stays
  solo-only (group needs a live "story so far" broadcast - its own story).
- **Keep It Fresh** - the whole `story-selection` arc (01-06): length classes +
  the one selection pipeline, quick-story option, no-repeats rotation, the
  anonymous serve log, like/dislike tale feedback, and device-local
  favorite-a-story replay.
- **Land the Laugh** (`reveal-delight/01-04`, PR #112): the reaction row (v2:
  three pills, one pick per player), the word-by-word carving animation, the
  Golden Guardian funniest-word vote + next-round crown, and per-word "carved by"
  attribution.
- **Spread the Word** (`session-engine/06` + `keepsake-gallery/01-05`, PRs #130 +
  #157): tappable `/join/:code` deep-link share, save-the-reveal as a stone-tablet
  image, watermarked share, the device-local "Tales we've carved" gallery, the
  host-opt-in public tale link (re-vetted server-side, unguessable slug, noindex,
  TTL, per-IP rate limit; ships disabled until Table Storage is configured), and
  the purchaser-gated cloud gallery.
- **Replay & Remix** (`replay-remix/01-03`, PR #162): "Carve it again" same-crew
  replay, "Remix a word" one-blank re-reveal synced to the room, and "Pass the
  chisel" between-rounds host handoff (host-only, server-enforced).
- **Don't Lose the Room** - reconnect hardening (`session-engine/07-11`, PR #151
  + fixes #161/#166): a 30s disconnect grace window that holds the seat
  (`SeatGraceService`), a `Rejoin` hub method with full round-state rehydration,
  the web reconnect token + auto-rejoin (on reconnect and on page load), the
  "resuming your game" screen instead of bouncing Home, and the wired "+ invite"
  roster slot. An e2e spec (`tests/reconnect.spec.ts`) covers the loop.
- **Eyes on the alpha** - observability + anonymous usage
  (`platform-devops/04-05`, PR #110): server-side App Insights with a PII-scrubbing
  choke point and hub-exception filter, round-completed usage events, and
  anonymous client error/usage beacons flowing through the same scrubber. Bicep
  provisions Log Analytics + App Insights; the app no-ops cleanly without a
  connection string.
- **Deployed** - one UAT environment (`quibblestone-uat-rg`), auto-delivered on
  every merge to main with first-deploy auto-provision from Bicep (OIDC, no stored
  publish secrets). There is deliberately no separate cloud "dev" - local is dev,
  UAT is the cloud. `provision.yml` is the push-button SKU/scale lever.
- **Child safety, always on** - the profanity/safety filter runs server-side on
  every free-text path (names, words, remix, tip message, publish re-vet incl.
  byline; hardened for compound obscenities in PR #155) plus the family-safe
  toggle and safe-by-default content selection.
- **The AI cost gate** (`ai-cost-gate/01-06`, PRs #132 app + #131 IaC): server-side
  proxy (keyless managed identity, key never in the browser), entitlement captured
  once at `CreateRoom`, per-session quota + per-IP limiter + the "N fresh runes
  left" meter, the **$20/UTC-month** spend circuit-breaker with anonymous cost
  attribution (fail-closed if the spend store is unreadable), moderate-before-
  display, and the IaC seam. Zero AI config degrades to the deterministic
  fallback at every stage.
- **The first AI slice - Fresh Runes** (`game-modes/07` free reshuffle +
  `ai-on-demand-generation/05` AI jumble + `/02` moderation; PR #140, transport
  fixes #146/#148, enrichment #149): the free deterministic reshuffle always
  works; the AI jumble rides the gate (`feature=jumble`), verified live on UAT,
  with a soft theme steer and a cheeky grown-up word set when family-safe is off
  (still behind the always-on profanity filter).
- **Accounts + entitlements, built end to end** (`accounts-identity/01-04`, PRs
  #147 + #169-#172; `billing-entitlements/01-07`, PRs #152 + #160/#163/#164):
  anonymous-forever players, the magic-link purchaser account **with real email
  delivery** (Azure Communication Services, keyless, provisioned by the deploy
  workflow, sign-in completes from the emailed link), sign-in/restore, the
  session-creation entitlement seam with the real catalog + grant store, Stripe
  checkout + webhook (idempotent), tip jar, gated purchase, restore/manage, and
  the Stripe **live/test mode toggle** with its operator page (interim gate - see
  Open below).
- **The sys-admin console** (`sysadmin-console/01-03`, PR #158 + wiring
  #163/#164/#170-#172): a separate admin bundle (`/admin`, own Vite entry, no
  imports from the kid app), operator magic-link login with a Key Vault-backed
  allowlist (Bearer-first auth, allowlist re-checked per request), operator
  grant/revoke of entitlements, and the public-tale report -> auto-hide-after-N ->
  review/restore queue.
- **Harness + CI** - Vitest (44 files / 363 tests) + xUnit
  (`tests/QuibbleStone.Api.Tests`, 523 tests) both gate CI alongside the web
  build; Playwright (4 specs) covers smoke/routing/group-mode/reconnect but is
  **not in CI** and has drifted (see Open).

## Open / near-done

| Item | Story / ref | Note |
|---|---|---|
| Alpha-gate fixes | [PR #175](https://github.com/treetopvt/quibblestone/pull/175) | B1/B3/B4/B5 merged, auto-deploying to UAT; B2 (UAT SKU) already bumped + confirmed live |
| Product analytics (GA4 + Clarity) | `analytics/01` (branch `claude/ga4-analytics-stress-test-7tjpar`) | Built: consent-gated, anonymous by construction, env-gated (no-op until ids set). Monitoring LIVE on rollout, one-time banner deferred behind a flag. Two operator steps before go-live: Clarity Masking = "Mask" (strict), GA4 Enhanced Measurement OFF (else a /join/:code leaks via page_referrer) - see [analytics/feature.md](./features/analytics/feature.md) |
| Load / stress test | [`docs/load-testing/findings.md`](./load-testing/findings.md) + the `/load` harness | **UAT run done (2026-07-07):** B1 holds far past alpha scale - 3,000 concurrent conns at 100% completion, <=49% CPU; connection *count* is not the wall (the single load client tops out first). Server limits are connect/disconnect **storms** (~98-99% CPU - the post-restart reconnect herd) and **memory** (~4-5k conns est.); a true server ceiling needs distributed load. Single in-process instance / no Azure SignalR backplane stays the architectural ceiling (F1). W2 6-player cap is **merged** (#181), bounding the join race + O(N^2) roster fan-out. Cheap near-term win: add **jitter** to the client reconnect backoff (`useGameHub.ts`) to de-sync the herd |
| ~~Orientation / landscape readability~~ (done) | `design-system/03` | **Shipped** (commit ead9ae4); story + feature status trued up 2026-07-08. PWA manifest prefers portrait + the Reveal reflows in landscape (the `48vh` cap is gone). A manual landscape spot-check on a real phone is still worth doing before the beta. |
| Billing-mode toggle relocation | `billing-entitlements/07` follow-up | move `/admin/billing-mode` out of the kid bundle into the operator console, behind the real Operator scheme |
| E2E suite repair + CI | `platform-devops/06` (written 2026-07-08) | 3 of 8 Playwright specs fail on stale selectors (mode picker moved into Game settings; Home play-solo pill dropped its "Or " prefix); e2e is not in CI so drift goes unnoticed. The story empirically reproduced all 3 failures and specs the per-spec repair + a readiness-gated CI job (boot API on :5180, gate on `/health`, no `playwright install`) |
| Public-tale TTL sweep | #150 | reap never-read expired tales; low urgency by design |
| Group Progressive Story | `game-modes/05` x group | deferred: needs the live cross-player "story so far" broadcast (its own story) |
| Bicep validation gap | carry-forward | `az bicep build` is not gated in CI (local `ci-check` skill covers it); serve-log client (`serveLog.ts`) still has no Vitest spec |

## The alpha gate - fix before inviting the family (audited 2026-07-07)

A code-level release-readiness pass found the game logic solid (server-authoritative
rounds, disciplined locking, the filter on every text path, zero placeholder code)
but flagged the mobile-reality layer. Blockers first; all are small, located fixes -
roughly a day or two of work total. **Status as of 2026-07-07 evening: every
blocker/high item (B1-B5) and W5 are resolved - the alpha gate is closed.**

| # | Severity | Problem | Fix shape | Status |
|---|---|---|---|---|
| B1 | Blocker | No recovery from a terminal SignalR disconnect: default retry policy gives up in ~40s, `onclose` never restarts, Home's CTAs silently dim with no copy or retry (`useGameHub.ts`, `App.tsx`) | restart on `onclose` / `visibilitychange` / `online` with backoff (or infinite retry array); visible "Reconnecting - tap to retry" state | Merged - PR #175 |
| B2 | Blocker | UAT runs the checked-in **F1 Free** SKU: 5 concurrent WebSockets (a 6-player room cannot form), no Always On (cold starts), 60 CPU-min/day (`infra/main.uat.bicepparam`) | run Provision with `appServicePlanSku=B1` for the test window; verify the live plan before invites | **Resolved** - `az appservice plan list` confirms `quibblestone-uat-plan` is `B1`/`Basic` |
| B3 | Blocker | 30s seat grace + abort-on-eviction: early finishers' phones auto-lock on the Waiting screen, and ~60s later the whole round aborts - even when the evicted player had no blanks left (`SeatGraceService.cs`, `GameHub.cs` eviction epilogue) | raise `DefaultGraceWindow` to 2-5 min; only abort when the departed seat has unsubmitted blanks | Merged - PR #175 (raised to 3 min; abort now conditional on outstanding blanks) |
| B4 | High | A rejected `Rejoin` (seat expired, server restarted) leaves zombie room state: frozen screen, no broadcasts, submits refused forever (`useGameHub.ts` rejoin-fail path) | on rejected rejoin, clear local room state + show the friendly "seat timed out" notice | Merged - PR #175 |
| B5 | High | No React error boundary: any render error is a permanent white screen (the error beacon reports it; the family sees blank) | one ErrorBoundary around `<App/>` with a "Something went off" + reload button | Merged - PR #175 |
| W1 | Warn | 30-min idle sweep deletes rooms under still-connected players (nothing bumps `LastActiveUtc` for connected sockets) | exempt rooms with connected seats or lengthen; clear client state on "room not found" | Open - specced in `session-engine/13` (2026-07-08) |
| W2 | Warn | Lobby says "n of 6" but the server never enforces a cap (a 7th joiner shows "7 of 6"; on F1 they cannot connect at all) | enforce the cap in `JoinRoom` with a friendly "room's full" | **Merged** (#181) - `Room.AddPlayer` caps at `Room.MaxPlayers`=6 (host incl.), atomic under the room lock; `JoinRoom` returns the friendly full message. Surfaced + verified by the load test (F2); re-confirmed absent on UAT at 6 players/room |
| W3 | Warn | `StartRound` has no phase guard: a host double-tap mid-deal re-deals everyone and discards in-flight words | reject StartRound while phase is "prompting" (mirror PassHost's gate) | Open - specced in `session-engine/13` (2026-07-08) |
| W4 | Warn | Mid-round joiners get yanked into a reveal they did not play (no phase check on `JoinRoom`) | block joins during "prompting" with a friendly wait message, or mark spectators | Open - specced in `session-engine/13` (2026-07-08) |
| W5 | Warn | With no `Email:*` config, magic-link flows silently send nothing - so the operator console (incl. the tale review queue) is unreachable on UAT | configure ACS email per the runbook before the test, or accept no admin console during it | **Resolved** - ACS email is live on UAT, confirmed via a tested magic-link round trip |

Notes-tier items (hub-method rate limits, tale-link revoke ownership, quota-key
rotation, unmetered telemetry endpoints, Google Fonts as the one external runtime
dependency) are recorded in the audit and can ride until after the test.

## The paths, by horizon

### 1. Run the alpha (now)
- **The alpha gate is closed.** B1/B3/B4/B5 merged -
  [#175](https://github.com/treetopvt/quibblestone/pull/175), auto-deployed to
  UAT; B2 (UAT SKU) confirmed live; W5 (ACS email) live and tested via a
  magic-link round trip. W1-W4 stay fast-follows, not blockers.
- **Invite friends and family** - nothing left blocks it. Watch App Insights
  (crashes, hub errors, round completions, AI spend) and the tale feedback +
  reactions data - the telemetry to see how the alpha actually plays is already
  shipped.
- **Repair the e2e suite** (3 stale specs) and put Playwright in CI so UI drift
  gets caught, not discovered - now specced in `platform-devops/06` (2026-07-08).
- **Shipped 2026-07-08**: session-engine/12 (#180) adds the dedicated "email a player
  an invite" action - a third channel alongside copy-link and the OS/browser share
  sheet (`useRoomInvite.ts`), reusing the ACS email seam behind the availability probe
  + per-IP rate limit. It appears only where an email provider is configured, so
  copy-link/share stay the always-on default.

### 2. Polish the laughs (during / after the test)
- Triage what the telemetry and the family actually surface - this list is a
  guess until then.
- **Content depth**: the seed library is now 54 templates across two tiers - 40
  family-safe plus a 14-story grown-up set (`familySafe: false` / `teen-plus`)
  the family-safe toggle reveals only when a host turns it off, so the toggle now
  gates real library content, not just the AI jumble's grown-up mode. Keep
  authoring seeds, or pull `ai-content-factory/01-03` forward (batch generate ->
  vet -> publish is the content-velocity moat, and its stories are written:
  #78-#80).
- **Group Progressive Story** - the one missing mode in group; write + build the
  "story so far" broadcast story.
- Any W-tier paper cuts that actually bit testers. (Orientation/landscape,
  `design-system/03`, has since shipped - status trued up 2026-07-08.)
- Decide on the parked **Versus/Duel** mode (#55) once `vote.ts` + group modes
  have proven out - it is the engine's one real stretch.

### 3. Spread the word + charge for it (for real)
- **Provision Table Storage for the public tale page** (it ships disabled behind
  the connection-string flag) + the TTL sweep (#150); the report/takedown queue
  is already live.
- **Go live on Stripe**: real keys + price ids via the runbook (the dual-mode
  toggle and the operator console exist); relocate the billing-mode toggle into
  the console; operator grant/revoke is already there for support.
- **Brand clearance** ([checklist](./launch-readiness/brand-clearance-checklist.md))
  before any public listing or marketing - explicitly not a friends-and-family
  blocker.
- Keepsake/share loop tuning informed by real usage (the `/join/:code` +
  watermark + public-tale funnel is built end to end).

### 4. The AI delight tier (the gate is waiting)
- **Character voices** (`ai-voice-narration` - story 02 is already filed): the
  car-ride killer feature and the natural next thin slice behind the same gate.
- **AI illustration** (`ai-illustration` sketch) - the keepsake hook.
- **On-demand tales** (`ai-on-demand-generation/01/03/04` sketches) - heaviest
  moderation burden, ships last, but the jumble already proved the
  generate -> moderate -> display pipeline it will reuse.
- **Packs** (`story-packs/01-03`, #75-#77) once the content factory feeds them.

## The AI cost gate

The hard part of AI is not any one feature - it is the shared plumbing (a provider,
moderation, cost control). The gate is **built, shipped, and proven by a live
consumer**; every AI feature inherits it.

1. **Server-side only** - the provider credential lives with the app (keyless
   managed identity preferred); the browser never calls AI directly.
2. **Entitlement at session start** - one check when the room is created decides
   what this session gets (the `billing-entitlements` seam, real catalog + grants).
3. **Rate limit + quota** - per-session quota, per-IP limiter, and the "N calls
   left" meter, so even an allowed session cannot spam.
4. **Spend circuit-breaker** - a **$20/UTC-month** ceiling; cross it and AI
   degrades gracefully to the deterministic fallback (fail-closed if the spend
   store is unreadable). Covers bugs and abuse alike.
5. **Moderate before display** - AI output passes the safety filter + family-safe
   before any child sees it. Non-negotiable (README section 6).

Players stay anonymous - the gate meters **compute per session, not identity**.
Provider/model + cost decisions: [ADR 0001](./adr/0001-ai-provider.md) (Azure AI
Foundry, gpt-5-mini, in-app proxy, existing filter now + Content Safety seam
later, AI jumble free-for-all in alpha behind quota + breaker). First consumer:
Fresh Runes (PR #140, verified on UAT with a `feature=jumble` cost event). The
cross-feature build order lives in
[`ai-cost-gate/implementation.md`](./features/ai-cost-gate/implementation.md).

## Recommended sequence

1. **Done** - the slice, deploy + auto-UAT, routing, all solo modes + three group
   modes, the freshness arc, Land the Laugh, Spread the Word, Replay & Remix,
   reconnect hardening, observability + usage metrics, child-safety hardening,
   the AI cost gate + Fresh Runes, accounts + billing (test mode) + the sys-admin
   console.
2. **Now** - the alpha gate is fully closed (B1-B5 and W5 all resolved); **invite
   the family**; repair the e2e suite + add it to CI.
3. **Next** - polish from telemetry + feedback; content velocity (more seeds or
   the content factory); group Progressive Story.
4. **Later** - public tale page provisioning, Stripe live, brand clearance, then
   the AI delight tier (voices -> illustration -> on-demand) and packs - all
   riding the same gate.

## Using this in an implementation session

1. Pick a card / row above and open its story under `docs/features/<feature>/`.
2. Branch per the git workflow in `CLAUDE.md` (a new branch per unit of work).
3. Build to the story's acceptance criteria; keep to the stack conventions.
4. Verify (`npm run build`, `npm run test:unit`, `dotnet test`, and
   `npm run test:e2e` where a flow is involved) before opening the PR.
