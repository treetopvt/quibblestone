# Story: Round complete and replay loop

**Feature:** Group Play Experience  ·  **Status:** Complete

## Context
After the reveal, the group can play again without returning to the home screen.
The Round Complete screen celebrates the round, shows per-player word counts so
everyone sees who contributed what, and offers two actions: play another round
(new template, same group) or return to the Lobby. This closes the replay loop
that makes group play self-sustaining - the group doesn't have to start from
scratch after each laugh. See [feature.md](./feature.md) and
`docs/design/README.md` Screens - screen 7 (Round Complete) and
`docs/design/screens/07-roundcomplete.png`.

## Acceptance Criteria
- [x] AC-01: Given the reveal is complete, when the host (or all players)
      triggers "Play another round", then the Round Complete screen is shown
      before starting the next round. The screen shows a teal "ROUND N CARVED"
      badge, confetti, and the header "Round complete!". See
      `docs/design/README.md` Screens screen 7.
- [x] AC-02: Given the Round Complete screen, then a stone-tablet keepsake panel
      shows the story title, a word-count pill ("N words"), and a carvers-count
      pill ("N carvers"). See `docs/design/README.md` Screens screen 7.
- [x] AC-03: Given the Round Complete screen, then a "Carved by your crew"
      section shows a row of Guardian avatars (56px), each with display name and
      a per-player teal word-count caption ("2 words", "1 word"). The counts sum
      to the total blanks in the template. See `docs/design/README.md` Screens
      screen 7 and `docs/design/screens/07-roundcomplete.png`.
- [x] AC-04: Given I tap the gold "Play another round" CTA, then a new round
      begins for the same group (same room, same players) - the group does not
      need to re-join or re-enter a code.
- [x] AC-05: Given I tap the secondary outlined-purple "Back to lobby" button,
      then all players return to the Lobby screen where the host can start a fresh
      round (same room code, still live).
- [x] AC-06: Given any player name or word count shown on the Round Complete
      screen, then the names displayed have passed the safety filter and no PII
      is shown.

## Out of Scope
- Saving or exporting the finished story as an image (Phase 3, parked).
- A "Share the tale" share sheet from the Round Complete screen (Phase 3,
  parked - the Reveal's share is the natural place anyway).
- Scoring, leaderboards, or win conditions.
- Kicking or removing players between rounds.

## Technical Notes
- Per-player word count comes from the server's assignment record: the hub
  tracks which player submitted which blanks during distribute-blanks and
  collect-words; the Round Complete payload includes this attribution.
- Round number increments server-side; the client displays the current round
  number in the badge.
- "Play another round": the hub starts a new round in the existing room (new
  template selection, resets phase to `prompting`). The flow is the same as
  group-play/01-start-round but does not require re-gathering in the lobby.
- "Back to lobby": the hub sets the room phase back to `lobby`; all players
  transition to the Lobby screen, preserving the room code and roster.
- Guardian avatars in the crew row: `<Guardian variant size={56} />` per
  player tile. Use each player's `variant` from their join record.
- Confetti: 8 pieces, palette colors, gentle fall+spin, 2.6-3.4s alternate
  animation. Keep it CSS-only (no canvas library). See
  `docs/design/README.md` Animation reference.
- See `docs/design/screens/07-roundcomplete.png` for layout.

## Dependencies
- group-play/03-collect-words
- the-reveal/01-text-reveal
- group-play/02-distribute-blanks (word count attribution)
- design-system/02-guardian-component
