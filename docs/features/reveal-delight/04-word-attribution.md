# Story: Show who submitted each word on the reveal (group play)

**Feature:** Reveal Delight  ·  **Status:** Complete  ·  **Issue:** #105

## Context
Half the laugh in group play is finding out WHO wrote the word that broke the room.
Today's reveal (`the-reveal/01`) highlights every filled-in word coral but says
nothing about who contributed it - the punchline lands, but the "wait, YOU wrote
that?!" beat is missing. The good news: attribution is already tracked end to end.
`assemble()` records a `playerSessionId` on every `FilledBlank`, and
`buildRevealParts()` already carries it onto each `RevealWordPart` - it is simply
never shown. This story surfaces that existing data as a light, tap-to-reveal
attribution on the group reveal, mapping each coral word back to the contributor's
nickname + Guardian from the roster. It is additive presentation over `Reveal.tsx`,
no engine change (see [feature.md](./feature.md) and the reuse map note "Assembled
story / attribution (unchanged by this feature)").

## Acceptance Criteria
- [ ] AC-01: Given a group reveal (a room with more than one player), when the story
      is fully shown, then each coral filled-in word can reveal its contributor - the
      in-session nickname (and their Guardian variant) of the player who submitted it -
      sourced by mapping the word's existing `playerSessionId` to the roster player.
- [ ] AC-02: Given the attribution surface, then it is DELIGHT, not clutter: it does
      not force a name onto every word inline by default (that would drown the coral
      contrast the reveal depends on). A tap/press on a coral word reveals "carved by
      [nickname]" with their Guardian, and/or a per-contributor color/legend keyed to
      each player's Guardian variant - the exact treatment is a build-time design
      choice, but the coral highlight and body text stay readable either way.
- [ ] AC-03: Given a word that was left blank (no submission - `playerSessionId` is
      `undefined`, per `assemble()`'s empty-fill rule), then it shows no contributor
      (it is attributed to no one) and never renders "carved by undefined" or a broken
      tile - the unattributed case is handled explicitly.
- [ ] AC-04: Given I am playing solo (no room / a single-player session), then
      per-word attribution is not shown at all - every word is mine, so naming a
      contributor is noise. This mechanic is inherently group-shaped (README section
      1: it is about the shared "you wrote THAT?" laugh), and is simply absent solo.
- [ ] AC-05 (child-safety / privacy, non-negotiable): Given attribution, then the
      only identity shown is the in-session nickname + Guardian variant already on the
      roster (exactly what `session-engine/03` displays) - never any PII, never a
      device id. It introduces no new free-text entry point: both the word and the
      nickname already passed the safety filter upstream (`the-reveal/01` AC-04,
      `session-engine/02` AC-03), so there is nothing new here for the filter to check.
- [ ] AC-06: Given a shared, real-time reveal, then attribution is derived purely from
      data every client already holds (the assembled story's `playerSessionId` per
      word + the roster the room already broadcasts) - this story adds NO new hub
      message and no second SignalR connection; it is a pure client-side presentation
      layer over existing state.

## Out of Scope
- Any change to `assemble()`, `AssembledStory`, `FilledBlank`, or `buildRevealParts()`
  - the `playerSessionId` they already produce is the whole data source; adding
  attribution data upstream would be scope creep into `template-model` / `the-reveal`.
- A per-player SCORE, tally, or "who was funniest" ranking - that is the Golden
  Guardian award (`reveal-delight/03`) and the vote it builds; attribution just LABELS
  words, it never scores them (README section 1: a toy, not a competition).
- Attribution on a saved/shared image or the public tale page - keepsake-gallery
  stories decide their own byline treatment; this story is the live Reveal screen only.
- Editing or reassigning a word's contributor (submissions are immutable for the round).
- Showing attribution before the reveal (words are hidden from other players until the
  reveal per `group-play/03` AC-01 - attribution appears only at/after the reveal).

## Technical Notes
- **The data already exists - this is presentation only.** `web/src/engine/assemble.ts`
  puts `playerSessionId` on each `FilledBlank`; `web/src/pages/revealParts.ts` carries
  it onto each `RevealWordPart` (`playerSessionId: string | undefined`). Read it there;
  do not recompute or thread new data through the engine.
- **Roster lookup:** map `playerSessionId -> { nickname, variant }` using the player
  record `session-engine` already broadcasts (feature.md exports "the player record
  ... consumed by group-play distribution/attribution"). If a contributor has since
  left the room, fall back gracefully (show the word with no name rather than crash).
- **Where it renders:** additive `sx` / a small interaction inside `Reveal.tsx`'s
  existing `parts.map` block (`web/src/pages/Reveal.tsx`), the same coral `<Box>`
  elements the other reveal-delight stories touch - coordinate build order with
  `reveal-delight/02` (carving animation) and `/03` (Golden Guardian vote), which touch
  the same elements (see implementation.md Wave Plan). Consider a shared color-per-
  variant helper if a Guardian-keyed legend is chosen, drawn from `web/src/theme.ts`.
- **Animation discipline:** any reveal/pop of the "carved by" chip uses `transform:
  scale` only, never an opacity keyframe (this feature's documented footgun).
- FontAwesome only for any chrome (e.g. a small chisel/tag glyph); all color/spacing
  from the theme - no hardcoded hex.

## Tests
| AC | Test |
|---|---|
| AC-01 | manual (two browser contexts, 2+ players): each coral word can reveal the correct contributor's nickname + Guardian |
| AC-02 | manual: the default reveal stays readable (coral contrast intact); attribution is opt-in per word or a non-intrusive legend |
| AC-03 | unit (`revealParts`/render): a blank with `playerSessionId === undefined` shows no contributor and never "carved by undefined" |
| AC-04 | manual: a solo reveal shows no per-word attribution at all |
| AC-05 | code review: only roster nickname + Guardian shown; no PII; no new text input introduced |
| AC-06 | code review: no new hub invoke/broadcast and no second `HubConnection`; attribution reads only existing client state |

## Dependencies
- the-reveal/01-text-reveal (the `Reveal.tsx` screen + `buildRevealParts()`/`playerSessionId` this reads)
- session-engine/03-player-roster (the `playerSessionId -> nickname + variant` mapping)
- design-system/02-guardian-component (the Guardian avatar shown beside the name)
