# Story: GA4 + Clarity, consent-gated and anonymous

**Feature:** Product Analytics  ·  **Status:** In Progress  <!-- Not Started | In Progress | Complete | Blocked | Dropped -->

## Context
We are at v1 and about to hand the toy to friends and family for feedback. App
Insights tells us "is it broken" and "what modes, how long, roughly how many
devices" (`platform-devops/04-05`), but not "where in the flow do people hesitate,
mis-tap, or bail" - the click patterns and frustrations that turn an alpha into a
fun v2. GA4 gives event funnels + route views + engagement; Clarity gives session
replay + heatmaps + rage/dead-click detection. This story adds both, scoped hard so
the child-safety non-negotiable (README section 6) is honored by construction. See
[feature.md](./feature.md) (esp. "Relationship to platform-devops/04-05").

## Acceptance Criteria
- [ ] AC-01 (env-gated / no-op): Given no `VITE_GA4_MEASUREMENT_ID` and no
      `VITE_CLARITY_PROJECT_ID`, then NOTHING loads or fires - no gtag/Clarity
      script, no consent banner, no events - and the app behaves exactly as before.
      Each provider activates independently when its own id is present.
- [ ] AC-02 (anonymous by construction / PII, non-negotiable): Given any GA4 event,
      then it carries ONLY allowlisted anonymous facts - the event name plus
      enum-ish params (mode id, solo/group context, normalized route template,
      player-count bucket) - and NEVER a nickname, join code, player/session id,
      submitted word, or story text. The pure param builder is unit-tested to prove
      no field can carry PII (mirrors `usageBeacon.test.ts`).
- [ ] AC-03 (Clarity never records typed text): Given Clarity is active, then it is
      initialized to mask ALL text (mask mode "all"), so a child's typed free-text
      words are never captured in a replay or heatmap. Masking cannot be relaxed
      per-element from app code.
- [ ] AC-04 (GA4 privacy hardening): Given GA4 is active, then it runs with IP
      anonymization, ad personalization signals OFF, and Google Signals OFF - so no
      cross-site/advertising profile is built on a kid-facing property.
- [ ] AC-05 (consent-gated): Given analytics is configured, then it defaults to
      DENIED (Google Consent Mode v2 default) and neither provider sends data until
      consent is granted. A lightweight, theme-driven banner captures the choice;
      the choice persists device-local (versioned `localStorage`, mirroring
      `identity.ts`). A single documented constant (`ANALYTICS_DEFAULT_CONSENT`)
      lets the operator start GRANTED for a closed friends-and-family test.
- [ ] AC-06 (route views): Given the SPA changes route (react-router), then a GA4
      page_view is sent with the NORMALIZED route template only (e.g. `/join/ABCD`
      -> `/join`, reusing `errorBeacon.ts`'s `normalizeRoutePath`) so a join code
      never rides the analytics path.
- [ ] AC-07 (funnel + frustration events): Given key moments, then anonymous GA4
      events fire at the funnel points that answer the alpha's questions - room
      created / joined, round started (mode + context), reveal reached, reaction
      tapped, invite/share tapped, and the reconnect/"seat timed out" states (the
      frustration signals) - co-located with the existing usage-beacon call sites so
      there is one event philosophy, not two.
- [ ] AC-08 (fails soft, never blocks): Given an analytics outage or a blocked
      third-party host, then it is fire-and-forget - a failed load or send never
      blocks, delays, or errors a round, and never surfaces to a player (same
      posture as `errorBeacon.ts` / `serveLog.ts`). Scripts load async.
- [ ] AC-09 (no entitlement, free/paid identical): Given analytics is internal, then
      there is no entitlement gate and it is not a feature a player pays for - free
      and paid sessions are measured identically and anonymously.
- [ ] AC-10 (operator config): Given deployment, then the two measurement IDs are
      wired as `VITE_` vars in the deploy workflow's "Build web" step (public, not
      secrets), documented in `web/.env.development` and `vite-env.d.ts`, so turning
      analytics on is a config change, not a code change.

## Out of Scope
- Server-side GA4 (Measurement Protocol) - client gtag is required for click/scroll
  behavior; the server-side App Insights pipeline already covers server facts.
- A custom analytics dashboard, cohort/retention modeling, or scheduled reports -
  read GA4 + Clarity's own dashboards (demand-driven, README section 12).
- Tracking the operator/admin bundle (`admin.html`) - kid-app only for now.
- A full cookie-preferences center / granular per-category consent - one
  analytics-consent choice, revisited at public-launch/brand-clearance time.
- Replacing or duplicating App Insights (`platform-devops/04-05`) or the
  content-curation serve log (`story-selection/04-05`) - those stay as-is.
- A CSP (none exists today) - noted as a hardening follow-up in feature.md.

## Technical Notes
- **Module:** `web/src/telemetry/analytics.ts` - reads `import.meta.env`, installs
  Consent Mode v2 defaults, async-injects gtag + Clarity ONLY when the matching id
  is set, exposes `initAnalytics()`, `trackEvent(name, params)`, `grantConsent()` /
  `denyConsent()`. Mirror the header-comment density + fire-and-forget of
  `errorBeacon.ts`. TS strict: type the `gtag`/`clarity` globals, no `any`, no `!`.
- **Consent store:** `web/src/telemetry/consent.ts` - versioned `localStorage`,
  try/catch on every access, validated on load (mirror `identity.ts` /
  `deviceId.ts`). `ANALYTICS_DEFAULT_CONSENT` constant mirrors `FAMILY_SAFE_DEFAULT`.
- **Event vocabulary:** a small enum-ish list + a pure `buildEventParams` allowlist
  so AC-02 is testable (mirror `buildUsagePayload`).
- **Banner:** `web/src/components/ConsentBanner.tsx` - MUI theme tokens only (no
  hardcoded hex/px), FontAwesome icons, big tap targets; re-export from
  `components/index.ts`. Only rendered when analytics is configured AND consent is
  unset.
- **Wiring:** `initAnalytics()` in `main.tsx` (beside `installErrorBeacon()`);
  page_view effect on `useLocation()` in `App.tsx`; `trackEvent` calls beside the
  existing `recordSolo*` sites (`Solo.tsx`) and at the group transitions in
  `App.tsx` (room set, reveal set, reaction, share, reconnect notice).
- **Route normalization:** reuse `normalizeRoutePath` from `errorBeacon.ts` (do not
  re-implement) so the join-code-stripping guarantee is shared.
- Prose: hyphens / colons / parentheses, never em dashes.

## Tests
| AC | Test |
|---|---|
| AC-01 | Vitest: with env ids unset, `initAnalytics()` injects no script and `trackEvent` is a no-op; manual: unconfigured build behaves as before |
| AC-02 | Vitest: `buildEventParams` output has only allowlisted keys; feeding it PII-shaped input drops it (mirror `usageBeacon.test.ts`) |
| AC-03 | code review + manual: Clarity init call sets mask mode "all"; a replay of a fill screen shows masked words |
| AC-04 | code review: gtag config sets `anonymize_ip`, `allow_google_signals:false`, `allow_ad_personalization_signals:false` |
| AC-05 | Vitest: default consent is DENIED; `grantConsent()` persists + flips gtag consent; manual: banner appears only when configured + unset |
| AC-06 | Vitest: page_view path is normalized (`/join/ABCD` -> `/join`); manual: GA4 realtime shows route templates, no codes |
| AC-07 | manual: play solo + a 2-device group; confirm the funnel events appear in GA4 realtime with correct anonymous params |
| AC-08 | Vitest + manual: a throwing/blocked send is swallowed; rounds start/finish normally with analytics unreachable |
| AC-09 | code review: no entitlement key, no player-facing on/off beyond consent |
| AC-10 | code review: `deploy.yml` Build-web step + `.env.development` + `vite-env.d.ts` carry the two ids |

## Dependencies
- child-safety/01-profanity-filter + child-safety/02-family-safe (the no-PII /
  no-content posture; Clarity masking is the replay-surface equivalent)
- platform-devops/04-operational-observability + 05-anonymous-usage-metrics (the
  anonymous event philosophy + `deviceId.ts` posture this mirrors and coordinates with)
- design-system/01-mui-theme (the consent banner's styling source)
- session-engine + single-player + group-play (the route/round lifecycle the
  events hook into)
