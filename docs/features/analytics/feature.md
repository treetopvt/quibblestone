# Feature: Product Analytics (GA4 + Clarity)

## Summary
Client-side product analytics for the alpha: Google Analytics 4 (event funnels,
route/page views, engagement) plus Microsoft Clarity (session replay, heatmaps,
rage/dead-click detection) so the "how do people actually move through the toy,
and where do they get stuck" questions are answerable before and during the
friends-and-family test. Both are **off by construction** until measurement IDs
are configured, both are **consent-gated**, and Clarity **masks all text** so a
child's typed words are never recorded.

## README reference
README section 12 (Open Decisions / Backlog - "analytics" as demand-driven) and
section 6 (Child Safety - the non-negotiable this feature is scoped around). The
epic map (section 7, Phase 4) lists analytics as demand-driven polish; this is
that demand, pulled forward deliberately to steer the alpha.

## Relationship to platform-devops/04-05 (read this first)
`platform-devops/05` (anonymous product-usage metrics) deliberately scoped
**third-party analytics SDKs, session replay, and heatmaps OUT** - "App Insights
custom events only, anonymous. (Some of those also raise child-privacy concerns
this app must avoid.)" This feature consciously **revisits that boundary** at the
product owner's request, because App Insights custom events answer "what modes,
how long, roughly how many devices" but NOT "which button did they hesitate on,
where did they rage-click, what does the funnel drop-off look like" - the exact
"frustrations and headscratchers" the alpha needs.

The child-privacy concern that got these parked is **addressed, not ignored**:

- **Anonymous by construction still holds.** No nickname, join code, player
  session id, submitted word, or story text is ever sent to GA4. Event params are
  an allowlist of the same anonymous, enum-ish facts `platform-devops/05` already
  emits (mode id, solo/group, route template).
- **Clarity masks ALL text** (`data-clarity-mask` / mask mode "all"), so typed
  free-text words - the one real child-data surface - are never captured in a
  replay. Masking is on by default and cannot be relaxed per-element.
- **Consent-gated.** Analytics default to DENIED (Google Consent Mode v2); nothing
  loads or fires until consent is granted. A single documented constant flips the
  default for a closed test.
- **Env-gated / no-op when unconfigured.** Exactly the `platform-devops/04`
  posture: with no `VITE_GA4_MEASUREMENT_ID` / `VITE_CLARITY_PROJECT_ID`, the
  whole feature is inert (no scripts, no banner, no events) - so merging it changes
  nothing until the operator opts in.

Operational health (`platform-devops/04`), content-curation serve counts
(`story-selection/04-05`), anonymous usage (`platform-devops/05`), and now product
analytics (this) stay complementary - each a clear purpose, no duplicated plumbing.

## Operator steps before go-live (config-side, not code)

Two guarantees live in the provider dashboards, not the app, so they must be set
when the measurement ids are turned on (`web/src/telemetry/analytics.ts` documents
both):

1. **Clarity Masking = "Mask" (strict)** - the real "never record typed words"
   guarantee (AC-03). The app also tags the word-entry field with
   `data-clarity-mask` as a code-level second layer, but it cannot force
   project-level masking from JS.
2. **GA4: turn OFF Enhanced Measurement "page changes based on browser history
   events"** (AC-06). The app sends its own scrubbed page_views; leaving GA4's
   automatic SPA page tracking on would double-count and leak a `/join/:code`
   deep link via `page_referrer`. Disabling it leaves only the scrubbed sends
   (the app also scrubs `page_referrer` defensively).

## Stories
| Story | Title | Status |
|---|---|---|
| 01 | GA4 + Clarity, consent-gated and anonymous | In Progress |

## Dependencies
- **child-safety/01-02** (the no-PII / no-content posture and the family-safe
  toggle this feature must not undermine) - the hard constraint.
- **platform-devops/05** (the anonymous event philosophy + the `deviceId.ts`
  device-local id posture this mirrors; the coordination note above).
- **design-system/01** (the MUI theme the consent banner is styled from).
- None blocking - the app runs identically with the feature unconfigured.

## Design notes
- **Client-side, not server-side.** "Click patterns" and rage/dead-click signals
  only exist in the browser, so this is deliberately a client concern (gtag.js +
  Clarity's snippet), unlike the server-side App Insights pipeline. The two do NOT
  overlap: App Insights stays the operational/usage sink; GA4/Clarity are the
  product-behavior sink.
- **One small module owns it** (`web/src/telemetry/analytics.ts`), mirroring the
  fire-and-forget, no-PII, no-op-when-unconfigured discipline of
  `errorBeacon.ts` / `usageBeacon.ts` / `serveLog.ts`. Event params run through a
  pure allowlist builder so "no PII" is a unit-tested guarantee, not a comment.
- **No CSP today** (verified repo-wide), so the scripts are not blocked; a
  hardening follow-up could add a CSP allowlisting only Google/Clarity/Fonts/API.
- **Measurement IDs are public** (not secrets), so they are fine as `VITE_` vars
  baked into the bundle - unlike connection strings, which stay server-side.
- **PWA note:** scripts load async and are best-effort; a blocked/slow analytics
  host never delays first paint or gameplay.
