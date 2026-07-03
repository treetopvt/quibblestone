<!--
  Implementation plan for the design-system feature. Bridges feature.md + stories to orchestration.
  Use hyphens/colons/parentheses, never em dashes.
-->

# Implementation Plan: Design System & UI Foundation

> The bridge between planning and orchestration. The **Wave Plan** below is DAG-ready: the `orchestrate-feature`
> skill's Phase 1 validates and adjusts it rather than deriving it. See
> [`docs/FEATURE_ORCHESTRATION_PLAYBOOK.md`](../../FEATURE_ORCHESTRATION_PLAYBOOK.md).

This is **the** web foundation feature: every screen story renders on top of it. `theme.ts` is the single highest-
contention file in the repo (CLAUDE.md section 4 - "web styling lives in the MUI theme"), so story 01 must land
before any consuming-web wave, and any later story that touches `theme.ts` (e.g. `session-engine/04`'s filled-purple
Share variant) serializes behind it.

## Reuse map

| Concern | Reuse | Where |
|---|---|---|
| Styling / theme tokens | the existing MUI theme (extend, do not fork) | `web/src/theme.ts` (exists) |
| Icons | FontAwesome, registered once (add app-bar icons here) | `web/src/fontawesome.ts` (exists) |
| Existing presentational pattern | the prop-driven, theme-only component style | `web/src/components/ConnectionStatus.tsx` |
| Design tokens (authoritative values) | the design pack - palette, type, radii, app-bar + button specs | `docs/design/README.md`, `docs/design/DESIGN_RULES.md` |
| Hero/guardian geometry (reference only) | the `.dc.html` design references (recreate, do not copy) | `docs/design/Home.dc.html`, `docs/design/Guardian.dc.html` |

What this feature **exports** that others import:
- The extended **MUI theme** (palette gradient, Fredoka/Nunito typography, radii, MuiAppBar + MuiButton overrides) -
  every web story inherits it automatically.
- `AppBar` and the gold-CTA / outlined-purple Button **contracts** - reused on every screen; never re-spec per screen.
- `BottomActionBar` - the pinned-action wrapper used by FillBlank, Reveal, Round Complete.
- `Guardian` (6 variants) - the avatar consumed by Join, Lobby, Waiting, Round Complete.
- The **hero mascot** asset - used by Home and Waiting.

## Wave Plan (DAG)

Sizing rule: a builder owns files **disjoint** from its concurrent siblings. Overlap on `theme.ts` means serialize.

| Story | Issue | Files it owns (footprint) | Depends-on | Can-run-with | Wave | Effort |
|---|---|---|---|---|---|---|
| 01 theme + app-shell | #16 | `web/src/theme.ts` (extend), `web/src/components/AppBar.tsx`, `web/src/components/BottomActionBar.tsx`, `web/index.html` (Google Fonts), `web/src/assets/HeroGuardian.tsx`; may add app-bar icons to `web/src/fontawesome.ts` | none | 02 (disjoint files), child-safety/01, platform-devops/01-02, template-model/01 | 1 | high |
| 02 guardian-component | #17 | `web/src/components/Guardian.tsx` | 01 (soft - project/theme exists; SVG colors are hardcoded per spec, not theme tokens) | 01 (footprints disjoint) | 1 | medium |
| 03 orientation-landscape | TBD | `web/public/manifest.webmanifest` (new), `web/index.html` (manifest link), `web/src/pages/Reveal.tsx` (landscape reflow of the story panel) | 01 (app shell, `index.html`, `BottomActionBar`), the-reveal/01 (the Reveal screen) | 02 (disjoint files) | post-slice-1 | low |
| 05 fit-to-viewport-declutter | TBD | `web/src/pages/Home.tsx`, `web/src/pages/Lobby.tsx`, new `web/src/components/GameSettingsSheet.tsx`, `web/src/pages/FillBlank.tsx`, `web/src/pages/Reveal.tsx` (layout only - reaction-row content itself is `reveal-delight/01`'s footprint), `web/src/fontawesome.ts` (new de-clutter icons) | 01 (theme + AppBar action-slot contract), the-reveal/01, session-engine/03, game-modes/02 | none - touches the same 4 page files `reveal-delight/01`'s revision touches; sequence, do not run concurrently with a `Reveal.tsx` editor | post-slice-1 | high |

**Concurrency per wave:** Wave 1 = 2 (stories 01 and 02 in parallel - their footprints are disjoint: 01 owns
`theme.ts`/`AppBar`/`index.html`/hero asset, 02 owns only `Guardian.tsx`). The dependency 02 declares on 01 is a
**soft** "the project must exist" - which it already does (the walking skeleton). If a single builder takes both, do
01 first. If 01 ends up adding app-bar icons to `fontawesome.ts`, keep that edit out of any concurrent story (no
sibling here touches it).

## Per-story tech notes

### 01 - MUI theme, AppBar, and Button shell
- **Approach:** extend the **existing** `web/src/theme.ts` (do not create a parallel theme file) with the design
  pack's authoritative tokens: the parchment background gradient (AC-01), Fredoka/Nunito typography loaded from
  Google Fonts via `index.html` (AC-02), and `components` overrides for `MuiAppBar` and `MuiButton` (gold
  `contained` CTA AC-04, outlined-purple secondary AC-05) so every usage inherits them with no per-instance styling.
  `BottomActionBar` is a thin layout wrapper (reserves height + applies the fade scrim, AC-06) - it is a component,
  not a theme override. The hero mascot (AC-08) is recreated from `docs/design/Home.dc.html` as an optimized
  SVG/component in `web/src/assets/`.
- **Key files it owns:** `theme.ts`, `components/AppBar.tsx`, `components/BottomActionBar.tsx`, `index.html`,
  `assets/HeroGuardian.tsx`.
- **Exports:** the theme, `<AppBar>`, the two Button variants (via theme overrides), `<BottomActionBar>`, the hero
  asset.
- **Gotchas:** trust the documented hex values over eyeballing the screenshots. No hardcoded color or raw-px spacing
  in any component (CLAUDE.md section 4) - tokens live in the theme. Entrance animations drive `transform: scale`
  only, never opacity keyframes on list items (design pack Gotchas). Out of scope: per-screen layouts, the Guardian
  avatar (story 02), the Phase-4 dyslexia-friendly / reduced-motion passes.

### 02 - Guardian avatar component (6 variants)
- **Approach:** one reusable inline-SVG component parameterised by `variant` (`purple | gold | coral | teal | sand
  | plum`) and `size` (AC-01 to AC-05). Common body (sandstone head, eyes, carved smile) plus a per-variant
  distinguishing feature; viewBox `0 0 56 56`, scales to the caller's `size`. Rendered as inline SVG so it works
  offline at any resolution (AC-05).
- **Key files it owns:** `web/src/components/Guardian.tsx`.
- **Exports:** `<Guardian variant size />` - imported by `session-engine/05` (selection grid), `session-engine/03`
  (roster tiles), `group-play/03` (waiting row), `group-play/04` (crew recap).
- **Gotchas:** recreate geometry from `docs/design/Guardian.dc.html` using React SVG idioms - **no**
  `dangerouslySetInnerHTML`, no inline HTML. SVG `fill`/`stroke` colors are hardcoded **on purpose** (the SVG is
  illustrative content, not theme chrome) - this is the one place hardcoded colors are correct, so the component
  does **not** depend on `theme.ts`. No idle/reaction animation here (the consuming screens own animation, keeping
  the component composable). Six variants only in Slice 1.

### 03 - Orientation: prefer portrait, stay readable in landscape
- **Approach:** two layers, per the "both" decision. (1) Add a `web/public/manifest.webmanifest`
  (`orientation: portrait`, name/short_name/start_url/display/theme_color=`#6C4BD8`/icons reusing existing
  `favicon.svg` + `apple-touch-icon.png`) linked from `index.html` - the "prefer portrait" layer for an installed
  PWA (AC-01). (2) Make landscape degrade gracefully (AC-02, AC-03): in `Reveal.tsx`, replace the fixed
  `maxHeight: '48vh'` story-scroll cap with an orientation-aware height, widen the `maxWidth: 430` container, and
  compact the celebration header + narration bar padding in landscape - all behind `@media (orientation: landscape)`
  so portrait is byte-for-byte unchanged (AC-04).
- **Key files it owns:** `web/public/manifest.webmanifest` (new), `web/index.html` (manifest `<link>` only), and the
  landscape-scoped `sx` in `web/src/pages/Reveal.tsx`.
- **Gotchas:** manifest `orientation` binds only an INSTALLED PWA and iOS Safari ignores it, so the graceful-landscape
  work is the actual fix, not optional. Mind the `BottomActionBar`/`BottomActionBarSpacer` reserved height so CTAs are
  never covered in the shorter landscape viewport. No hardcoded hex; big tap targets and `prefers-reduced-motion`
  preserved (AC-05). Out of scope: a full landscape/tablet redesign, an orientation LOCK / "please rotate" blocker, a
  service worker/offline pass, and reworking other screens beyond "not broken" (Reveal is the acceptance-critical one).
  This edits `index.html` (owned by story 01) - serialize behind 01, do not run concurrently with another `index.html`
  editor.

### 05 - Fit-to-viewport screen de-clutter
- **Approach:** apply one recipe - a fixed-height flex column root
  (`height: 100dvh`, `overflow: hidden`) with exactly one internal-scroll
  region where content is genuinely long - to Landing, Waiting room, Gameplay,
  and the Reveal, plus per-screen clarity cuts (Landing's utility icon bar,
  Lobby's `GameSettingsSheet`, Gameplay's tale-title pill + "Blind" chip, the
  Reveal's scrolling story card + app-bar Favorite star). No public prop
  contract changes on any of the four screens (AC-06).
- **Key files it owns:** `web/src/pages/Home.tsx`, `web/src/pages/Lobby.tsx`,
  new `web/src/components/GameSettingsSheet.tsx`, `web/src/pages/FillBlank.tsx`,
  `web/src/pages/Reveal.tsx` (layout regions only), `web/src/fontawesome.ts`
  (new icon registrations for this pass).
- **Exports:** the fixed-height-flex + single-internal-scroll pattern as the
  reusable recipe for any future full-screen page; `GameSettingsSheet` as a
  generic bottom-sheet chrome wrapper any future host-controls surface can
  reuse.
- **Gotchas:** this story's `Reveal.tsx` footprint is LAYOUT ONLY - the
  reaction row's three-reaction narrowing and one-per-user select/move/toggle
  rule is `reveal-delight/01`'s footprint (its own story, revised in place);
  the two changes shipped in the same pass and touch the same file, so
  sequence them rather than running concurrently. No hardcoded hex/raw-px;
  FontAwesome-only; no em dashes.

## Cross-cutting concerns

- **`theme.ts` is the single styling contract.** Only this feature's story 01 and `session-engine/04` (Share-button
  variant) edit it across the whole slice - serialize them (01 first). Every other web story consumes the theme; if
  a story needs a new look-and-feel, it is a theme change here, not an inline style there.
- **Inter-feature ordering:** `design-system/01` is foundation wave (alongside `child-safety/01`,
  `platform-devops/01-02`, `template-model/01`). `Guardian` (02) must exist before the avatar-consuming stories
  (`session-engine/03` + `/05`, `group-play/03` + `/04`).
- **FontAwesome only** (CLAUDE.md section 4) - never `@mui/icons-material`. App-bar icons register in
  `fontawesome.ts`.
- **No i18n** - user-facing strings are plain. **No em dashes** in authored content.
- **Big tap targets / family-friendly** chunky high-contrast UX is the brief (README section 10) - bake it into the
  theme so screens inherit it.
