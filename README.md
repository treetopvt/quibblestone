# Quibbler

> **Working title — not final.** "Quibbler" is a placeholder pending a trademark
> and domain/app-store availability check (note: "The Quibbler" is a Harry Potter
> reference, so verify there's no conflict before committing). The name **must not**
> be "Mad Libs" or contain it; that is a registered trademark (Penguin Random House /
> Mattel). The fill-in-the-blank *mechanic* is not protectable, but the brand must be
> original.

A multiplayer, multi-device, fill-in-the-blank word game built for **hilarity and
easy fun** with friends and family — whether everyone is in the same car or in
different houses. Players are prompted for words (sometimes blind, sometimes from a
word bank), and the resulting story is revealed for everyone to laugh at together.

---

## 1. Vision & Use Cases

The core experience is the classic "give me a plural noun" surprise, modernized for
a generation that lives on screens rather than paper. The emotional target is loud,
silly, instant fun — the kind of thing a family pulls up on a road trip or during a
boring wait, and everyone is laughing within two minutes.

**Two equally-important use cases:**

- **Same location (car ride):** everyone on their own phone, one host narrating.
  Needs tolerance for brief connectivity drops (dead zones).
- **Different locations (remote):** the same session, players in separate places.
  Requires a cloud server in the middle — there is no local-only fallback.

Because remote play is first-class, the architecture is **cloud real-time**, not
peer-to-peer. The car case is handled by reconnect logic and light caching on top
of the same cloud backbone, not a separate system.

**Single-player and group play are both first-class.** Solo is the no-friction
entry point ("I'm bored in line") and the funnel into group play; group play is the
differentiator.

---

## 2. Market Positioning

The category exists (which validates demand) but has a clear, exploitable gap:

- The **official Mad Libs app** is the incumbent — freemium with paid story packs.
  Its reviews are dominated by one complaint: users run out of content and resent
  the paywall. Content velocity is its weakness.
- **Clones** (Fun Libs, Fast-Libs, etc.) lean on user-generated content or offer
  only *local* multiplayer (same device / same room). Several are criticized for
  intrusive ads.
- **Real-time, multi-device, remote-capable multiplayer is essentially unowned.**

**Our two-part edge:**

1. **Live cross-device multiplayer (incl. remote)** — the differentiator that gets
   us noticed.
2. **A bottomless, fresh, AI-generated content library** — directly attacks the #1
   complaint in the category (running out of stories) and is the retention engine.
3. **AI illustrations + character-voice narration** — the delight features that earn
   word-of-mouth (esp. the car-ride "phone reads the story in a pirate voice").

---

## 3. Monetization

Monetization is an early priority, layered as a thin entitlement check on top of the
core engine (decided at *session-creation* time, not per-request).

- **Free tier:** generous — single-player and same-code group play, base content.
- **Paid (leaning toward a "family plan" subscription):** the full content library,
  remote cross-device play, premium AI features (illustrations, voices, on-demand
  generation), larger groups.
- **Add-on packs:** themed content (holiday, sci-fi, road-trip edition) as an
  alternative or supplement to subscription. Same billing plumbing.

**Avoid ads**, especially given the kid-facing audience — ads are the single most
resented thing in this category.

**Identity model (tiered):**

- **Players are anonymous forever** — join with a code + nickname, no account, no
  PII. This is also the child-privacy posture (COPPA / GDPR-K): collect as little
  about minors as possible.
- **Only the purchaser gets a lightweight account**, and only when they buy.
  Free play requires no login. (The account *hooks* go in early even if the UI is
  minimal — retrofitting auth onto an anonymous system later is painful.)

---

## 4. Stack & Architecture

Built on the team's traditional stack. Deliberately **lighter on immutable/audit
reporting** than prior apps (e.g. COBRA, Cadence) — this is a toy, not a system of
record, so most data is mutable and sessions are ephemeral.

**Front end:** React + Vite, **Material UI**, FontAwesome. Delivered as a **PWA
first** (no app-store friction, instantly cross-platform, share via link, installable
to home screen). The same React codebase can later wrap into a native shell
(Capacitor) without a rewrite if store presence is wanted.

**Back end — one app to start:** a **single ASP.NET Core application** hosting both
the request/response API *and* the SignalR hub.

> **Why one ASP.NET Core app instead of Azure Functions (for now)?**
> Functions are a great fit for this bursty workload (scale-to-zero, first-class
> SignalR bindings), but for a solo, nights-and-weekends start they fight our values:
> a pile of loosely-related functions is harder to keep DRY and well-separated, cold
> starts add latency to a real-time game, and local debugging + integration testing
> (already a soft spot) are fiddlier. A single structured Web API gives us familiar
> DI, controllers, and services with one project, one deploy, and one debugging story.
> **We carve workloads out into Functions later, only when a real reason appears** —
> the natural first candidates are async AI generation jobs and Stripe webhooks,
> which genuinely benefit from event triggers and scale-to-zero.

**Real-time:** Azure SignalR Service for lobby, presence, live session state, and
reveal broadcast.

**Storage:** Azure Table Storage for templates and entitlements; Blob Storage for
AI-generated illustrations (later).

**Secrets:** Azure Key Vault (Stripe keys, AI provider keys).

**Source control & build:** GitHub, with GitHub Actions for CI/CD. Greenfield
scaffolding and IaC are well-suited to **Claude Code on the web** (self-contained,
reviewable as PRs) — which makes this charter load-bearing context to point it at.

### Core engineering principle: one engine, many thin modes

Every game variation is the **same underlying thing** — a template with typed blanks,
a way to collect words, and a way to reveal — differing only in *what context the
player sees* and *when*. Build that abstraction once and each new mode is days, not
weeks. The mode interface defines three axes:

1. **What the player sees:** nothing / subject only / progressive story
2. **How they answer:** free text / word bank
3. **When the reveal happens:** at the end / progressively

This is the single most important architectural decision for keeping scope sane.

---

## 5. Game Modes (variations on the same engine)

- **Classic blind** — no story context (maybe just the subject); fill blanks blind.
  *(First mode built.)*
- **Blind + word bank** — same, but answer from a provided list of words.
- **Progressive reveal** — story is revealed as it goes; you fill the current blank
  without knowing what comes next. With or without a word bank.
- **Owner-curated word bank** — the round's host supplies the word bank everyone
  draws from.
- *(More variations expected — they stay cheap to add on top of the engine.)*

---

## 6. Child Safety & Moderation (cross-cutting)

Non-negotiable, designed in from the start:

- Profanity / safety filter on submitted free-text words.
- A **family-safe toggle**.
- Content vetting for the library (and a strong moderation pipeline before any *live*
  AI generation is exposed to kids).
- Minimal data collection on minors (see identity model).

---

## 7. Epic Map

Grouped by build phase. Sizing is relative T-shirt (S/M/L/XL).

### Phase 0 — Foundation
- **Platform & DevOps** *(M)* — CI/CD, environments, observability, **IaC (Bicep)**.
  Keep the footprint tiny (see §8).
- **Session & Room Engine** *(L)* — SignalR backbone: create room, join code,
  roster/presence, reconnect tolerance. Everything rides on this.

### Phase 1 — Playable MVP (free, genuinely fun)
- **Template & Content Model** *(M)* — schema for templates, typed blanks, optional
  word banks, theme/age tags; an authoring format.
- **Game Modes Engine** *(L)* — the mode abstraction + Classic blind.
- **Single-Player Experience** *(M)*.
- **Group Play Experience** *(L)* — host controls, blank distribution, collection.
- **The Reveal** *(M)* — animated text reveal + host-read-aloud (grows later).
- **Child Safety & Moderation** *(M)*.

### Phase 2 — Monetize
- **Accounts & Identity** *(M)* — anonymous players; lightweight purchaser accounts.
- **Billing & Entitlements** *(L)* — Stripe, entitlement store, session-creation
  gating (supports both packs and subscription).
- **AI Content Factory — back office** *(M)* — batch-generate + vet + publish
  templates offline. The cheap version of the moat; seed content even in Phase 1.

### Phase 3 — Differentiate & Delight
- **AI Illustration** *(L)* — image of the finished story (share / keepsake hook).
- **AI Voice Narration** *(L)* — TTS character voices (the car-ride killer feature).
- **On-Demand AI Generation** *(XL)* — "a story about our dog Biscuit in space."
  Heaviest moderation burden, hence last.
- **Remaining Game Modes** *(S–M each)*.
- **Add-On Pack Catalog** *(M)*.

### Phase 4 — Scale & Polish
Content discovery/search, social sharing & saved-story keepsakes, optional UGC
template creation, analytics, leaderboards/replay. Demand-driven.

---

## 8. Roadmap — Thin Vertical Slice First

**Capacity assumption:** solo, nights & weekends (~10 effective hrs/week).

For a solo part-time build, the real enemy is **losing momentum before anything is
fun**. So do **not** build the phases horizontally. Cut a thin vertical slice that
crosses several epics shallowly and gets the family laughing fast — that captive test
audience is the best predictor of finishing.

### Slice 1 — "My family is laughing in the car" (~6–8 weeks)
- Session engine, minimum viable: create room, join code, roster. *(Skip
  reconnect-hardening for now.)*
- One mode only: **Classic blind**.
- Single-player **and** a 2-player group (proving real-time sync early de-risks the
  scariest part).
- A tiny hand-written library (10–15 stories, **no AI yet**).
- **Text reveal only.** No voices, no images.
- Basic word filter so it's safe to hand to kids.
- **No accounts, no billing.**

### Then, additive on a thing that already works:
1. **AI content factory (back office)** — hand-writing templates gets old fast; this
   keeps *you* unblocked on content. *(~weeks 8–12.)*
2. **More game modes + better reveal** — now that you know what's funniest.
3. **Monetize** — once there's enough content and modes to justify a pack/sub.
   *(~months 4–6.)*
4. **Delight tier** — illustrations, character voices, on-demand AI. *(~months 6+.)*

### Rough calendar (wide ranges — solo part-time variance is large)
- Playable: **~4–6 months**
- Taking money: **~6–8 months**
- Fully magical: **~9–12+ months**

---

## 9. IaC — Get It Up First, Keep It Tiny

IaC is a known soft spot, so do not let it become a week-2 wall. Target footprint —
five resources, short Bicep, generated by Claude Code, "deploys cleanly to dev" is
the bar (do not gold-plate):

- **Azure Static Web App** (or App Service) — React + Vite front end
- **Azure App Service** — the single ASP.NET Core app (API + SignalR hub)
- **Azure SignalR Service**
- **Azure Storage** — Table (templates, entitlements) + Blob (AI images, later)
- **Azure Key Vault** — secrets

Wire a GitHub Actions deploy workflow alongside.

---

## 10. Design Brief (for Claude Design)

**Emotional brief:** loud, silly, kid-and-family-friendly, instant fun, "everyone
laughing in a car." This is a deliberate departure from the restrained,
information-dense look of professional/financial tooling — reach for bright, playful,
chunky, high-contrast, **big tap targets**. A mascot/character direction is worth
exploring; a silly word game benefits from a character.

**Screens to explore first (highest leverage):**

1. **Lobby / join-code screen** — first impression, sets the tone.
2. **Word-entry prompt** — the most-repeated screen; must feel snappy and handle both
   free-text and word-bank modes gracefully.
3. **Reveal screen** — the payoff moment where the hilarity lands; deserves the most
   love.
4. **Visual identity starting point** — color, type, and mascot/character direction.

**Stack bridge:** the target is **Material UI**. Land the look-and-feel as an MUI
theme (palette, typography, shape/radius, component overrides) rather than a bespoke
design language, so the gap between design and code stays small — theme MUI to match
rather than fighting it.

---

## 11. Backlog & Docs Structure

The backlog lives in the repo as **docs-as-code** (a pattern proven on prior C5
prototypes), so stories version and travel through PRs alongside the code that
satisfies them. Each story file is a single PR-sized unit — the natural thing to hand
Claude Code one at a time.

### Layout

```
docs/
  features/
    session-engine/
      feature.md
      01-create-room.md
      02-join-with-code.md
      03-player-roster.md
    template-model/
      feature.md
      01-template-schema.md
      ...
```

- One folder per feature under `docs/features/`.
- Each feature folder has a `feature.md` plus one markdown file per story.
- **Story files are order-prefixed** (`01-`, `02-`, …) so a feature reads
  top-to-bottom in build sequence.

### Slice boundary

Only **Slice 1** features are fully specified with story files. Phase 2–4 features
exist as a `feature.md` **stub only** (no story files yet) — they get decomposed when
their phase comes up. This keeps the tree honest about what is actually ready to build.

### Cross-reference

Each `feature.md` links back to the relevant README section for the *why*; the
README's epic map (§7) points into `docs/features/` for the *how*.

### Template — `feature.md`

```markdown
# Feature: <name>

## Summary
<1–3 sentences: what this feature is and the value it delivers.>

## README reference
<Link to the relevant README section, e.g. §4 architecture / §7 epic.>

## Stories
- [ ] 01 — <story title>
- [ ] 02 — <story title>

## Dependencies
<Other features that must exist first, or "none".>

## Design notes
<Architecture/design considerations specific to this feature.>
```

### Template — story (`NN-<slug>.md`)

```markdown
# Story: <title>

**Feature:** <parent feature>  ·  **Status:** Not Started

## Context
<Why this story exists; what the user or system needs. Link to feature.md.>

## Acceptance Criteria
- [ ] <observable, testable outcome>
- [ ] <…>

## Out of Scope
<What this story deliberately does NOT do — guards against scope creep.>

## Technical Notes
<Stack-specific hints: relevant projects, patterns, libraries, gotchas.>

## Dependencies
<Stories that must land first, or "none".>
```

---

## 12. Open Decisions / Backlog

- Final **name** (+ trademark, domain, app-store availability check).
- **Subscription vs. packs vs. both** — pricing model (plumbing supports all).
- AI provider(s) for text, image, and voice generation.
- Moderation approach for **live** AI generation before exposing it to kids.
- Whether/when to wrap as a native app (Capacitor) for store presence.

> **Scope discipline:** this is an idea-rich concept. Park every great new idea here
> and refuse to pull it forward until the current slice ships. The engine abstraction
> is what keeps those ideas cheap to add later.
