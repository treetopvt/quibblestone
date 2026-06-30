# Handoff: QuibbleStone — Core Game Loop (Design Pack)

## Overview
**QuibbleStone** is a mobile-first PWA: a multiplayer, fill-in-the-blank word game for families to play together (think Mad Libs, built for phones — great for car rides and kitchen tables). Players join a shared room, each contributes words to blanks **without seeing the story** ("blind mode"), and then the finished story is revealed — "carved" into an ancient glowing stone tablet.

The brand is **playful storybook-fantasy**: joyful, warm, welcoming to all ages. The recurring visual motif is a glowing carved **stone tablet**, plus a friendly **stone-guardian mascot**.

This pack covers the **complete core loop** across 7 screens plus 1 shared component:

`Home → Join → Lobby → Fill-in-the-blank (blind) → Waiting interstitial → Reveal → Round complete → (loop back to a new round)`

---

## About the Design Files
The files in this bundle are **design references created in HTML** — interactive prototypes that show the intended look, layout, motion, and behavior. **They are not production code to copy directly.**

- The HTML files use the `.dc.html` extension. Each is a self-contained "Design Component": a single HTML file with an inline-styled template plus a small logic class. They were authored in a design tool and rely on that tool's runtime (`support.js`) to render, so **opening them as plain files in a browser will not paint** — view them in the originating design tool, or treat **this README as the authoritative spec** (it is written to be self-sufficient).
- Your task: **recreate these designs in the target codebase's environment.** The intended production stack is **React + Material UI (MUI)** — the components were deliberately designed with MUI's vocabulary (app bars, cards, FABs, chips, dialogs, rounded buttons) so they translate cleanly. If you adopt a different stack, map to its established patterns.
- All styling in the prototypes is inline; that is an artifact of the design runtime. In production, use your normal theming/system (an MUI theme is the natural fit — token values are listed below).

## Fidelity
**High-fidelity (hifi).** Final colors, typography, spacing, corner radii, motion, and copy are all intentional and specified below. Recreate the UI faithfully using the codebase's libraries. Where a value here conflicts with your eyeballing of a screenshot, **trust the documented value.**

---

## Global System

### Device frame
Every screen is designed as a **portrait phone, 390 × 844 CSS px** (iPhone-class). The dark bezel + status bar in the prototypes is presentation chrome — **do not build the bezel**; it stands in for the real device. Build to a 390-wide portrait viewport, safe-area aware.

### Palette (design tokens)
| Token | Hex | Use |
|---|---|---|
| Parchment | `#F6EEDD` | App background (top→bottom subtle gradient: `#F8F1E2` → `#F6EEDD` → `#F0E6D0`) |
| Sandstone | `#E8DCC4` | Surfaces |
| Card | `#ECE2CC` | Card fill |
| Stone slot (carved input) | `#DCCFB0` / `#DFD2B4` | Inset "carved" fields & code slots |
| Tablet gradient | `#EFE3C7` → `#E3D2AC` → `#D6C194` (168°) | The stone-tablet motif |
| Text | `#2B2622` | Primary warm dark brown |
| Text muted | `rgba(43,38,34,.5–.66)` | Secondary text |
| **Primary** | `#6C4BD8` | Purple — brand, secondary buttons, accents |
| **CTA / Gold** | `#FFB22E` (gradient top `#FFC24E`) | **Always** the main call-to-action on a screen |
| Gold deep | `#E89A12` / `#B07908` | Gold icon strokes / gold text on light |
| Coral | `#FF6B57` | Accent; **filled-in story words are coral** |
| Teal | `#2FB8A0` (deep `#1f8a78`) | Accent; "ready"/"done" states |
| Stone edge | `#B49B6E` | Mascot/tile outlines, carved rims |

### Typography
- **Fredoka** (Google Font) — weights 500/600/700. Headings, wordmark, **all buttons**, prompts, names, numbers.
- **Nunito** (Google Font) — weights 400/600/700/800. Body, UI labels, captions, chips.
- Status-bar time: Nunito 800, 15px.

### Spacing / radius / shadow
- Screen content horizontal padding: **22px** (20px on Reveal).
- Card radius: **24px**; large rounded buttons: **20px**; chips/pills: **999px**; icon buttons: **14px**.
- Stone-tablet shape: arched top via asymmetric radius, e.g. `96px 96px 30px 30px` (hero) or `54–64px 54–64px 26–28px 26–28px` (smaller). Carved rim = an absolutely-positioned inset border `rgba(120,96,52,.26–.32)` with `inset 0 0 ~16px rgba(255,178,46,.2)` glow.
- Card shadow: `0 10px 24px -16px rgba(120,96,52,.6)`.
- Ambient glow (most screens): a radial behind content — `radial-gradient(circle, rgba(108,75,216,.2) 0%, rgba(255,178,46,.1) 45%, transparent 70%)`.

### CONSISTENT APP BAR (every screen with one — do not deviate)
- Container: `display:flex; align-items:center; gap:10px; padding:6px 16px 8px;`
- Icon buttons: **42×42**, `border:none; border-radius:14px; background:rgba(43,38,34,.07); cursor:pointer;`; icon `stroke:#2B2622; stroke-width:2.4`.
- Title: `flex:1; text-align:center; font-family:Fredoka; font-weight:600; font-size:21px; color:#2B2622;`.
- Always balance the right side with a matching 42px button **or** an empty `<div style="width:42px;height:42px">`.

### CONSISTENT BUTTONS (every screen — do not deviate)
- **Primary CTA (gold)** — the one main action per screen:
  `height:62px; border-radius:20px; gap:11px; background:linear-gradient(180deg,#FFC24E,#FFB22E); color:#2B2622; font-family:Fredoka; font-weight:600; font-size:20px; box-shadow:0 12px 22px -8px rgba(255,178,46,.85), inset 0 2px 0 rgba(255,255,255,.5);`
- **Secondary (outlined purple)**:
  `height:60px; border:2.5px solid #6C4BD8; border-radius:20px; gap:11px; background:rgba(108,75,216,.06); color:#6C4BD8; font-family:Fredoka; font-weight:600; font-size:20px;`
- Icon inside a button: 22px, stroke 2.6, color matches the button's text color.
- A purely passive screen (e.g. the waiting interstitial) legitimately has **no gold CTA** — don't invent one.

### Bottom action bar pattern
On screens with pinned actions, the button(s) sit in an absolutely-positioned bar at the bottom with a fade scrim: `background:linear-gradient(180deg, transparent 0%, #F2E8D2 ~30%)`, padding `12px 22px 22px`. Reserve vertical room for it so scrollable content doesn't hide behind it.

### iOS home indicator
A 128×5 pill, `rgba(43,38,34,.32)`, centered 8px from the bottom — presentation detail; omit in production (the OS draws it).

---

## Shared Component: `Guardian`
A reusable avatar of the stone-guardian mascot, rendered as inline SVG (viewBox `0 0 56 56`, scales to its box). One **`variant`** prop selects palette + a distinguishing feature. Build this as a single reusable component (`<Guardian variant size />`).

| variant | eye color | distinguishing feature |
|---|---|---|
| `purple` | `#6C4BD8` | small square block on top of head |
| `gold` | `#E89A12` | gold zig-zag crown |
| `coral` | `#FF6B57` | two small horns |
| `teal` | `#2FB8A0` | leaf sprout |
| `sand` | `#7C6442` | round stone ears on the sides |
| `plum` | `#9B7BE0` | single antenna with a glowing dot |

Common body (all variants): a sandstone rounded-square head (`#E0CDA0`, outline `#B49B6E`), two rounded-rect eyes, a curved carved smile (`#7C6442`). Used at: avatar grids (Join), player tiles (Lobby), recap rows (Waiting, RoundComplete). It is `Guardian.dc.html` in this bundle for reference.

The full-size hero mascot (Home, Waiting) is a larger, more detailed build of the same character (aura, raised/posed arms, moss accents, glowing forehead rune, cheeks) — recreate as an illustrated component or exported asset; see those two files.

### Canonical player set (used across screens)
`Pip` (teal, **host**), `Maple` (gold), `Bramble` (coral), `Wren` (plum), `Flint` (sand), `Juniper` (purple). Keep names↔variants consistent so the same person looks the same on every screen.

---

## Screens

### 1. Home — `Home.dc.html`
**Purpose:** Welcome / entry. No login.
**Layout (top→bottom, centered column, 26px padding):** kicker chip → stone-tablet hero → tagline → actions pinned toward the bottom.
- **Kicker chip:** pill, `rgba(108,75,216,.1)` fill + `rgba(108,75,216,.22)` border, teal glowing dot, text "FAMILY WORD QUEST" (Nunito 800, 12.5px, uppercase, letter-spacing 1.4px, `#6C4BD8`).
- **Stone-tablet hero:** ~296px wide, arched (`96px 96px 30px 30px`), tablet gradient, carved rim, glow shadow `0 26px 50px -22px rgba(108,75,216,.55)`. Contains: a 3-glyph rune inscription; the **wordmark** "QuibbleStone" (Fredoka 700, 39px) split two-tone — "Quibble" `#6C4BD8`, "Stone" `#FFB22E` — with a carved emboss text-shadow; a small caption "CARVE A SILLY TALE"; and the **hero mascot** (juggling-free idle pose) bobbing gently.
- **Tagline:** Nunito 600, 16px, `rgba(43,38,34,.78)`: "Fill in the blanks together and watch a wild story get carved into stone — perfect for car rides & kitchen tables."
- **Actions:** gold **"Create a game"** (primary CTA, "+" icon) and outlined-purple **"Join a game"** (enter/login-arrow icon). Below: reassurance row with teal check — "No account needed — just pick a name & play".
**Motion:** ambient glow; 3 twinkling sparkles (gold/teal/coral); mascot bob (`translateY -7px`, ~5s); eye/glow pulse.

### 2. Join — `Join.dc.html`
**Purpose:** Enter a room code, pick a display name, choose an avatar. Fully anonymous.
**App bar:** back arrow (left), title "Join a game", 42px spacer (right).
**Layout:** two stacked cards + pinned gold CTA.
- **Room-code card** (`#ECE2CC`): header row "ROOM CODE" (`#6C4BD8`, uppercase) + teal chip "from the host". Four **carved code slots** (flex, equal): each `height:64px`, `#DFD2B4`, radius 16, **inset** shadow `inset 0 3px 7px rgba(120,96,52,.45)`, Fredoka 600 32px `#6C4BD8`. Sample value `M O S S`.
- **Character card:** MUI **outlined text field** "Display name" — 56px, `#FBF6EA` fill, `2px solid #6C4BD8`, radius 16, notched floating label (`#6C4BD8`), person icon, live `n/14` counter, Fredoka 500 19px input. Then "Choose your guardian" (Fredoka 600 16px) and a **3-column avatar grid** of the 6 `Guardian` variants. Each tile 78×78, radius 22, tinted by its color. **Selected tile** gets a `3px solid #FFB22E` ring (inset -3px, radius 25) + a gold 24px check badge (top-right) that pops in.
- **Reassurance:** shield icon + "100% anonymous — no email, no account".
- **Pinned CTA:** gold **"Join MOSS →"**.
**State:** `name` (controlled input), `selectedVariant` (default `teal`). Tapping a tile selects it (single-select).

### 3. Lobby / Waiting room — `Lobby.dc.html`
**Purpose:** Gather players around a shared code; host starts the game.
**App bar:** leave "✕" (left), title "Waiting room", settings gear (right).
**Layout:**
- **Room-code / share tablet** (stone gradient, radius 26): left = "ROOM CODE" label + big carved code "MOSS" (Fredoka 700, 38px, `#6C4BD8`, letter-spacing 5px); right = stacked **Copy** (outlined purple; flips to a teal-check "Copied!" for ~1.8s on tap) and **Share** (filled purple, white text, share-nodes icon). Dashed divider + line: "Share this code so friends can gather round the stone".
- **Players header:** "Carvers gathered" (Fredoka 600 18px) + teal count chip "**x of 6**" (people icon).
- **Players grid:** 3-column. Each tile = 74px circle (`#FBF6EA`, `2.5px #E0CDA0`) holding a 52px `Guardian`, name (Fredoka 500 15px), and a role chip: **host** = gold "HOST" chip + a gold crown badge above the avatar + a pulsing gold ring (`@keyframes` ring 2.4s); others = teal "● READY" chip. **Empty slots** = dashed circle with 3 pulsing dots + "waiting…", border color pulses purple.
- **Pinned actions:** gold **"▶ Start game"** (host only) + host note "You're the host — start whenever your crew's ready" with a crown glyph.
**Behavior (live "gathering"):** starts with 4 players (Pip host, Maple, Bramble, Wren); after **2200ms** Flint joins, after **4400ms** Juniper joins — each fills an empty slot with a scale-pop (animate **transform/scale only, never opacity** — see Gotchas) and shows a transient dark toast bottom-center: "*Name* pulled up a stone" (2600ms, slide-up/out). Count chip updates to "6 of 6". Capacity = 6.

### 4. Fill-in-the-blank, blind mode — `FillBlank.dc.html`
**Purpose:** Ask the player for **one word at a time, with no story context.**
**App bar:** back (left), title "Your turn", help "?" (right).
**Layout:**
- **Progress:** row "Word **3** of 8" (chisel icon) + teal "5 to go". Below it an **8-segment bar** (flex, gap 5, each `height:9px` radius 5): completed/current = gold gradient, the **current segment glows** (`@keyframes` 1.8s); upcoming = `#DFD2B4`.
- **Stone-tablet prompt card** (arched `64px… 28px…`, carved rim, glow): centered **category chip** (purple pill, sparkle icon, uppercase — e.g. "Adjective"); **prompt** (Fredoka 600 29px, balanced) "Give me a **silly** describing word" (the category word colored `#6C4BD8`); sub-hint (Nunito 700 13.5px muted) "Something that describes a thing — anything goes!"; **carved input slot** (`#DCCFB0`, inset shadow, radius 18, 66px) with a chisel icon + Fredoka 500 24px text, placeholder "type a fun word…", maxLength 20; an example row "Need a spark?" + 3 tappable teal chips (`squishy`, `gigantic`, `sparkly`) that fill the input.
- **Blind-mode reassurance:** purple-tint panel, eye-off icon: "Blind mode — no peeking at the story. The big reveal comes at the end!"
- **Pinned:** gold **"Next word →"** + a low-pressure ghost link "Stuck? Skip this word" (`#6C4BD8`).
**State:** `word` (controlled). Example chips set `word`. (Categories drive prompt text + chip color in a full build — see Expansion.)
**Subtle motif:** a faint purple line-carved tablet SVG (opacity ~.06–.12, slow pulse) behind the card + two floating runes.

### 5. Waiting interstitial — `Waiting.dc.html`
**Purpose:** Shown after a player submits while others still write. Calm, low-pressure, **no countdown**.
**App bar:** leave "✕" (left), title "Your words are in!", spacer (right).
**Layout:**
- **Hero (flex, centered):** the **hero mascot juggling three glowing letter tiles** ("W·O·W") — arms raised, eyes looking up, happy open smile, one foot tapping (small motion arcs), a dotted juggling arc + glints. **This is a single static pose** that locks the idea; the real animation is a later build task. Caption (Fredoka 500 18px muted): "Juggling letters while the others carve…".
- **Status card** (`#ECE2CC`): header "**3** of 5 quibblers done" (teal check-circle icon) + "2 still writing". A **5-avatar row** of `Guardian`s, 54px circles: **done** players (Pip, Maple, Bramble) at full opacity with a teal check badge; **writing** players (Wren, Flint) dimmed (`opacity .55`, muted name) with a sandstone badge of 2 pulsing dots. Dashed divider + clock icon + "No rush — the stone waits for everyone."
- **Gentle action:** secondary outlined-purple **"Review my words"** (chisel icon). No gold CTA.

### 6. Reveal — `Reveal.dc.html`
**Purpose:** The payoff — the finished story is revealed, celebratory.
**App bar:** home (left), title "The Reveal", share-nodes (right).
**Layout:**
- **Confetti** (8 pieces, palette colors, gentle fall+spin, top 300px) + celebratory header "✦ Your tale is carved! ✦" (Fredoka 700 26px, twinkling stars) + byline "carved by Pip, Maple, Bramble & crew".
- **Glowing stone tablet** (flex, tablet gradient, radius `40…28`, **pulsing glow** `@keyframes` 4s alternating purple↔gold shadow):
  - **Narration bar** (top, purple-tint, bottom border): a 48px circular **purple play/pause FAB** (`#6C4BD8`); label that toggles "Hear it in the Guardian's voice" ↔ "Narrating in the Guardian's voice…"; a **12-bar animated waveform** (each bar `scaleY` `@keyframes` .9s, staggered delays, palette colors). Hooks to character-voice **TTS** narration in production.
  - **Story scroll** (overflow-y auto): title "The Wobbly Wizard & the Golden Sock" (Fredoka 700 23px `#6C4BD8`); body (Nunito 600 17.5px, line-height 1.72) where **every filled-in word is coral** — `color:#FF6B57; font-weight:800; border-bottom:2px solid rgba(255,107,87,.4)`.
- **Reaction row** (above the action bar): 4 equal pill buttons — Laugh (gold), Heart (coral), Wow/sparkle (teal), Star (purple) — each shows an icon + count (Fredoka 600 16px). Tapping **increments the count** and spawns a **floating icon** that rises ~62px and fades (`@keyframes` 1.1s). Starting counts: 14 / 9 / 6 / 11.
- **Pinned actions:** gold **"↺ Play another round"** + secondary outlined-purple **"Share the tale"**.
**State:** `playing` (bool), `counts {laugh,heart,wow,star}`, `floaters[]` (each removed after 1100ms).

### 7. Round complete (between rounds) — `RoundComplete.dc.html`
**Purpose:** Celebrate the round, recap contributors, offer replay. Default assumption: **the same group plays multiple rounds.**
**App bar:** home (left), title "Round complete", spacer (right).
**Layout:**
- **Confetti** + teal badge "✓ ROUND 1 CARVED" + header "✦ Round complete! ✦".
- **Stone-tablet keepsake** (tablet gradient, radius `54…26`, pulsing glow): "✦ YOUR TALE ◈" label; story **title** (Fredoka 700 22px `#6C4BD8`, balanced); a meta row of pills — "✎ 8 words", "👥 5 carvers", and a small outlined-purple **"⌁ Share"** affordance.
- **Crew recap:** header "Carved by your crew" + "all 5 staying on". A **5-avatar row** of `Guardian`s (56px), each with name (Fredoka 500 13px) and a teal **per-player word count** caption ("2 words", "1 word"; sums to 8).
- **Actions (flow, pinned to bottom via `margin-top:auto`):** gold **"↺ Play another round"** + secondary outlined-purple **"← Back to lobby"**.

---

## Interactions & Behavior (summary)
- **Navigation:** Home→(Create→Lobby | Join→Join→Lobby); Lobby Start→FillBlank; FillBlank Next×N→Waiting (when this player finishes before others)→Reveal (when all done); Reveal "Play another round"→RoundComplete or directly into the next FillBlank; RoundComplete "Play another round"→new round, "Back to lobby"→Lobby.
- **Selection:** single-select avatar grid (Join); tappable example chips (FillBlank).
- **Live updates:** players arriving (Lobby), completion progress (Waiting) — in production these are realtime (websocket/Firebase-style) events; the prototypes fake them with timers.
- **Copy/Share:** Copy code → "Copied!" confirmation; Share → Web Share API.
- **Reactions:** optimistic increment + ephemeral floating icon (Reveal).
- **Narration:** play/pause toggles TTS in a character voice; waveform animates while playing.

### Animation reference (durations / easing)
- Idle mascot bob: ~5s ease-in-out, `translateY` 6–7px.
- Twinkle/sparkle: 3.4–4.1s ease-in-out, opacity+scale.
- Confetti: 2.6–3.4s ease-in-out **alternate**, translateY + rotate.
- Player arrival pop: ~0.45s ease (**scale only**).
- Toast: 2.6s (slide-up in, hold, slide-out).
- Tablet glow pulse: 4–4.2s ease-in-out alternate.
- Waveform bar: 0.9s ease-in-out, staggered ~0.08s, `scaleY .32→1`.
- Reaction float: 1.1s ease-out, rise 62px + fade + scale 1.25.
- Selection ring/check pop: ~0.25s.

## State Management (for a real build)
- **Session/room:** roomCode, players[] (id, name, variant, isHost, isDone, wordsContributed), capacity (6), roundNumber, gamePhase (`lobby|prompting|waiting|reveal|roundComplete`).
- **Prompting:** prompts[] (category, blankIndex), currentBlankIndex, answers keyed by player+blank.
- **Story:** template with typed blanks; assembled story with per-word attribution for the reveal highlights and the recap counts.
- **Reveal:** narration playing state, reaction counts (per story), local "already reacted" guard if you de-dupe.
- **Data fetching:** realtime room sync; TTS audio for narration; share payload (story text/image).

## Assets
- **Fonts:** Google Fonts **Fredoka** & **Nunito** (link in each file's head). Use your app's font-loading.
- **Icons:** all inline SVG, **Lucide/Feather-style** strokes (stroke-width ~2.2–2.6, round caps). Swap for your icon library (Lucide recommended) — listed per screen above.
- **Mascot:** bespoke inline SVG (no external image). Either port the SVG into a component or export it as an optimized asset. Small avatars come from the shared `Guardian` component (6 variants).
- **No raster images, no third-party brand assets** are used.

## Files in this bundle
- `Home.dc.html`, `Join.dc.html`, `Lobby.dc.html`, `FillBlank.dc.html`, `Waiting.dc.html`, `Reveal.dc.html`, `RoundComplete.dc.html` — the 7 screens.
- `Guardian.dc.html` — shared avatar component (6 variants), referenced by several screens.
- `DESIGN_RULES.md` — the condensed brand/app-bar/button rules that govern all screens (the project's source-of-truth style contract).

## Implementation Gotchas (learned while building)
- **Never animate a player/avatar tile's `opacity` for entrance** — drive entrance pops with **`transform: scale` only**. An opacity-based keyframe with `fill-mode: both` can leave re-rendered list items stuck at `opacity:0`.
- Keep **names ↔ Guardian variants** consistent across screens (see canonical set).
- Reserve space for the pinned bottom action bar so scrollable content (e.g. the Reveal story, the player grid) never hides behind it.
- The gold gradient + shadow recipe and the app-bar recipe are **fixed contracts** — reuse a single Button and AppBar component; don't re-spec per screen.

---

## Suggested Expansion Areas (for the Claude Code review, before coding)
You asked to **review and expand features/stories before coding** — open threads the design pack intentionally leaves for discussion:
1. **Story library & blank typing.** A schema for story templates (title, body with typed blanks: adjective/noun/verb/name/place/exclamation/number/plural-noun…), difficulty/length tiers, and age-appropriate "kid" vs "anything goes" packs. The FillBlank screen already implies categories + per-category prompt copy and example sparks.
2. **Game modes.** Blind mode is built; consider a "story-visible" mode, solo mode, and a "host assigns blanks" vs "round-robin" distribution.
3. **Prompt-to-player assignment.** How 8 blanks map across 5 players (the recap shows uneven counts: 2/2/1/2/1). Define the allocation rule.
4. **Reveal enhancements.** Word-by-word "carving" reveal animation; the character-voice TTS pipeline; saving/sharing the finished tale as an image of the tablet.
5. **Persistence & accounts-lite.** Anonymous by design — but consider device-local profile (remembered name+avatar), room reconnection, and a private "tales we've carved" history.
6. **Realtime infra.** Room codes, presence, the live "player joined" / "x of n done" events, and host controls (kick, skip a slow writer, capacity > 6).
7. **Accessibility & all-ages.** Large hit targets are in (44px+); add dyslexia-friendly option, TTS for prompts (not just reveal), reduced-motion variants of every animation, and reading-level controls.
8. **Empty/edge states** not yet mocked: connection lost, host leaves, a player drops mid-round, profanity filtering for family-safety, a player who never submits (the Waiting screen's "no countdown" stance needs a host override).
