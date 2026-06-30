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
