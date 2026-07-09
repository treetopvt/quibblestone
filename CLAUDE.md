# CLAUDE.md - QuibbleStone

> Guidance for Claude Code working in this repository. The **`README.md` is the
> charter and the source of truth** for vision, stack, and architecture. If
> anything here conflicts with it, the README wins - flag the discrepancy.

QuibbleStone is a multiplayer, multi-device, fill-in-the-blank word game built for
"hilarity and easy fun" with friends and family - same car or different houses.
It is a **toy, not a system of record** (README section 4): most data is mutable,
sessions are ephemeral, and audit/immutability ceremony from prior apps does
**not** apply here.

This is a **solo, nights-and-weekends build (~10 hrs/week)** (README section 8).
The guiding constraint is: **do not lose momentum before something is fun.** Bias
toward the thin vertical slice over breadth.

---

## 0. Current status and roadmap (read first)

The thin vertical slice is **live and playable**. The running roadmap - what is
shipped, what is open, and the priority order - lives in
**[`docs/ROADMAP.md`](docs/ROADMAP.md)** (dated; update it as work lands). Start an
implementation session by picking an item there, opening its story under
`docs/features/`, and building it on its own branch.

Snapshot (2026-07-07 - see the roadmap for detail):

- **Shipped:** the whole alpha build - rooms/roster/avatars + host migration,
  solo + group play, 4 modes in solo and 3 in group (shared mode registry), the
  freshness arc (`story-selection/01-06`), Land the Laugh (`reveal-delight`),
  Spread the Word (deep-link join + keepsake gallery + public tale link behind a
  storage flag), Replay & Remix, reconnect hardening (`session-engine/07-11`),
  observability + anonymous usage (App Insights, PII-scrubbed), child safety
  (always-on filter + family-safe), the AI cost gate + Fresh Runes AI jumble,
  accounts (magic-link + real email) + billing (Stripe test mode, tip jar, gated
  purchase) + the operator/sys-admin console. Two cloud lanes (`platform-devops/07`):
  **qa** (auto-deployed on merge, `qa.quibblestone.com`, Playground/PAYG) in front of
  **beta** (`quibblestone.com`, promoted by a `v*` tag); no separate cloud dev.
- **Notable gap:** the "alpha gate" fix list in the roadmap (disconnect recovery,
  UAT SKU, grace-window tuning, error boundary) blocks the friends-and-family
  test; group play still lacks Progressive Story (needs its own broadcast story);
  the Playwright e2e suite has drifted (3 stale specs) and is not in CI.
- **Priority order:** (1) alpha-gate fixes, then **run the friends-and-family
  test** and watch the shipped telemetry; (2) polish from what the test surfaces +
  content velocity (more seeds or `ai-content-factory`); (3) the AI delight tier
  (voices next) behind the gate; (4) Stripe live + public-launch prep (brand
  clearance).
- **Load-bearing rule for AI:** the moment any AI call ships, it goes behind the
  cost gate (server-side proxy + entitlement-at-session-start + rate-limit/quota +
  spend circuit-breaker + moderation). The gate meters compute per session, not
  identity, so players stay anonymous. See `docs/ROADMAP.md` "The AI cost gate."

---

## 1. Repository layout

```
api/        ASP.NET Core app: REST API + SignalR hub in ONE project (section 4)
web/        React + Vite + TypeScript + MUI + FontAwesome client (PWA-first)
infra/      Bicep for the 5-resource dev footprint (section 9)
docs/features/   the backlog, as code (section 11) - feature.md + NN-story.md
.github/workflows/   CI (build both) + Deploy (manual, secret-gated)
.claude/    agents + skills for this repo
```

Each of `api/` and `web/` has its own `src/` and its own `README.md`.

## 2. Core architectural bet: one engine, many thin modes

The single most important decision (README section 4): every game variation is
the **same engine** - a template with typed blanks, a way to collect words, a way
to reveal - differing only on three axes:

1. **What the player sees:** nothing / subject only / progressive story
2. **How they answer:** free text / word bank
3. **When the reveal happens:** at the end / progressively

Build that abstraction once; each new mode is configuration, not a new engine. If
code forks the engine per mode, that is a smell.

## 3. Stack

| Layer | Tech |
|---|---|
| Web | React 19 + Vite 6, TypeScript strict |
| UI | Material UI 7 (`@mui/material`) - theme-driven; FontAwesome icons |
| Real-time | `@microsoft/signalr` 9 (one shared connection) |
| API | ASP.NET Core (`net10.0`), controllers + SignalR hub in one app |
| IaC | Bicep (Static Web App, App Service, Azure SignalR, Storage, Key Vault) |
| CI/CD | GitHub Actions |

**Deliberately NOT here** (do not add without asking): Azure Functions (section 4
explains why - revisit only for async AI jobs / Stripe webhooks), Redux/Zustand,
an i18n framework, AG Grid/Syncfusion, MSAL.

## 4. Conventions

- **Web styling lives in the MUI theme** (`web/src/theme.ts`). No hardcoded
  colors or pixel spacing in components - pull from the theme. New look-and-feel
  is a theme change (palette/typography/component overrides), per the design brief
  (section 10): bright, playful, chunky, high-contrast, **big tap targets**.
- **FontAwesome only** for icons (registered in `web/src/fontawesome.ts`).
- **One SignalR connection**, owned by a hook in `web/src/signalr/`. Hub/API URLs
  come from `import.meta.env` (`VITE_*`), never hardcoded. Secrets never go in
  `VITE_` vars (they ship to the browser).
- **TypeScript strict**: no `any`; guard instead of `!`.
- **API**: keep REST (controllers) and real-time (hubs) in their folders; shared
  logic in services. Async all the way; no secrets in committed config.
- **Verbose header comments on key files** so a new engineer orients fast
  (section 4 value). Match the comment density already in the tree.
- **Prose style**: hyphens, colons, or parentheses - not em dashes.

## 5. Child safety is a non-negotiable (section 6)

Designed in from the start, not bolted on:

- Profanity / safety filter on submitted free-text words **before** anyone sees them.
- A **family-safe toggle** that gates content and word banks.
- **Minimal data on minors**: players are anonymous (join code + nickname, no
  account, no PII). Only a purchaser gets a lightweight account, and only when
  they buy (section 3).

Any surface that submits or displays free text must respect this.

## 6. Monetization seam (section 3)

Monetization is a **thin entitlement check decided at session-creation time**, not
per-request. Free tier is generous (single-player + same-code group, base
content). Build the account/entitlement hooks in early even if the UI is minimal -
retrofitting auth onto an anonymous system later is painful. **Avoid ads.**

## 7. Slice discipline (sections 8, 12)

Build the **thin vertical slice** first ("my family is laughing in the car"):
session engine (create room, join code, roster), Classic blind mode only,
single-player + a 2-player group, a tiny hand-written library, text reveal only,
a basic word filter, no accounts/billing. **Park** every other great idea (AI,
more modes, monetize, voices/images) in the README backlog (section 12) until the
slice ships. The engine abstraction is what keeps parked ideas cheap to add later.

## 8. Agents and skills

| Agent (`.claude/agents/`) | Use for |
|---|---|
| `frontend-agent` | Web components, pages, hooks, real-time wiring, theming, forms |
| `story-agent` | Authoring/maintaining `docs/features/` stories (README section 11 templates) |
| `code-review` | Reviewing a diff against project values, stack rules, child safety, scope |
| `testing-agent` | Test strategy + specs (harnesses live: Vitest, xUnit, Playwright) |

| Skill (`.claude/skills/`) | Trigger |
|---|---|
| `commit` | "commit" - conventional commit with scope detection |
| `ci-check` | "ci check" / "run ci" - local API build + web build + Bicep validate |

## 9. Build / dev / test

```bash
# API
dotnet build QuibbleStone.slnx
dotnet run --project api/QuibbleStone.Api.csproj      # http://localhost:5180

# Web
cd web && npm install && npm run dev               # http://localhost:5173

# Infra (no Azure login needed)
az bicep build --file infra/main.bicep

# Tests (the harness lives in platform-devops/01)
cd web && npm run test:unit       # Vitest - pure engine + content logic (src/**/*.test.ts)
dotnet test QuibbleStone.slnx     # xUnit - API hub/rooms/safety/gate/billing (tests/QuibbleStone.Api.Tests)
cd web && npm run test:e2e        # Playwright - browser flows (smoke/routing/group-mode/reconnect)
```

The canonical test harness is **Vitest** (pure web logic, config
`web/vitest.config.ts`) plus **xUnit** (`tests/QuibbleStone.Api.Tests`, the API's
server-side suite) plus **Playwright** (browser flows, config
`playwright.config.ts` at the repo root, specs in `tests/`). CI
(`.github/workflows/ci.yml`) gates on `npm run test:unit`, the web build, and
`dotnet test`; **Playwright is NOT in CI** (it needs the full stack) and can
drift - run it locally for flow work. `npm run test:e2e` boots the web dev server
but the specs assert against the live hub, so the **API must be up on `:5180`**
(start it first); Playwright's Chromium is pre-provisioned (do **not** run
`playwright install`). See `.claude/agents/testing-agent.md` for strategy and
`web/README.md` for details.

### Deploying (qa auto, beta by tag)

Two cloud lanes (`platform-devops/07`): every merge to `main` auto-deploys to **qa**
(`qa.quibblestone.com`, Playground/PAYG); **beta** (`quibblestone.com`, the
friends-and-family site) deploys ONLY on a pushed `v*` tag. **Before any deploy or
promotion, read [`docs/runbooks/deploy-qa-and-promote-beta.md`](docs/runbooks/deploy-qa-and-promote-beta.md).**
Ship to qa: merge to `main`. Promote to beta:
`git tag vX.Y.Z <sha-validated-in-qa> && git push origin vX.Y.Z` (rollback: run
**Promote to Beta** with an older `ref`). `deploy-qa.yml` + `promote-beta.yml` call the
shared `deploy-env.yml` core; the old `deploy.yml` is retired.

## 10. Things that look wrong but aren't

- **`net10.0` on a preview SDK.** `.NET` 10 is the latest LTS; this machine
  currently has a 10.0 *preview* SDK. `/global.json` rolls forward to whatever
  10.0.x is installed (`allowPrerelease`), and CI installs the latest 10.0.x.
- **Free FontAwesome packs**, not a Pro kit - so `npm ci` needs no auth token.
  Swap the import source in `fontawesome.ts` if a Pro kit is adopted.
- **In-process SignalR hub, not Azure SignalR Service.** The skeleton runs the hub
  in-app for zero-setup local dev. Bicep still provisions Azure SignalR for
  production scale-out; wiring it is a `.AddAzureSignalR(...)` one-liner later
  (see `api/src/Program.cs`).
- **Storage + Key Vault are provisioned but unused** by the skeleton. They exist
  so the footprint is up early (section 9); features consume them later.
```
