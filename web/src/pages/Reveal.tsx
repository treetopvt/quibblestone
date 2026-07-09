// ----------------------------------------------------------------------------
//  Reveal - the payoff screen (the-reveal/01, docs/design/README.md Screens
//  screen 6; issue #34).
//
//  This is the moment "everyone has been waiting to laugh at" (README section
//  10 - "the payoff moment ... deserves the most love"): CSS-only confetti, a
//  "Your tale is carved!" header with twinkling star glyphs, an optional
//  caller-supplied attribution slot, and the assembled story rendered inside a
//  glowing stone-tablet scroll panel with every player-filled word popping in
//  coral. Text only for Slice 1 (AC-05): no TTS audio, no AI illustration.
//
//  REUSE CONTRACT (read this before changing the props): this screen is
//  consumed by BOTH single-player/01 (solo, a personal word-count summary) and
//  later group-play (a "carved by [names] & crew" byline) - see RevealProps.
//  Reveal itself renders NEITHER of those bylines; it only exposes an optional
//  `attribution` slot that the caller fills in (or omits entirely). This is
//  what lets solo reuse the exact same screen without editing it. Likewise
//  `onPlayAgain` / `onHome` are caller-owned: Reveal never decides what
//  "another round" or "go home" means for its host feature.
//
//  Rendering the story: Reveal does NOT re-implement assemble()'s attribution
//  logic. It walks `template.body` via the pure `buildRevealParts()` helper
//  (./revealParts.ts, unit-tested in revealParts.test.ts) to interleave literal
//  text with each filled word IN PLACE, matching filled words to blanks in body
//  order - exactly how assemble() itself paired them.
//
//  Child safety (AC-04): every word in `assembled.filledWords` already passed
//  the safety filter upstream (solo checks at submit via the engine boundary;
//  group checks server-side, per child-safety/01). Reveal renders those vetted
//  words verbatim and introduces no new unfiltered free-text surface.
//
//  Share the tale: Reveal OWNS the Web Share integration internally (mirrors
//  Lobby.tsx's ShareWidget, session-engine/04) - feature-detects
//  `navigator.share`, shares `{ title, text }`, swallows a user-cancelled
//  AbortError, and falls back to `navigator.clipboard` when Web Share is
//  unavailable. The Share button stays visible in the bottom bar either way
//  (AC-06) - unlike ShareWidget's Copy/Share pair, there is no separate Copy
//  button here, so Share must always offer SOME action.
//
//  Share the IMAGE first (keepsake-gallery/02, AC-01/AC-02/AC-06): `handleShare`
//  now PREFERS sharing the same watermarked tablet image `renderTabletImage()`
//  produces (../gallery/renderTablet.ts - the SAME render `handleSaveImage`
//  below already calls, never a second image derivation), wrapped in a `File`.
//  A file payload is offered ONLY when `navigator.canShare({ files: [file] })`
//  reports it is actually supported - this is the ONE place this screen gates
//  on `canShare()`, deliberately: that predicate is unreliable for a plain
//  TEXT/URL share (session-engine/04's note, which is why the fallback below
//  still does NOT gate on it), but it is the correct, narrower feature-detect
//  for a FILE payload specifically. When the browser cannot share files (or
//  the render itself fails - AC guard), Share falls through to the EXISTING
//  text-only path unchanged (title + storyText via `navigator.share`, then
//  `copyTale()`), so the text-share fallback this screen already had keeps
//  working exactly as before this story (AC-06). A user-cancelled AbortError
//  is swallowed at either the image or text step, matching this screen's
//  existing posture; any OTHER rejection falls through rather than leaving
//  Share a silent no-op. The image-share step's actual "share this File,
//  gracefully" logic (feature-detect, AbortError-swallow, never-throw) is
//  EXTRACTED into ../gallery/shareImageFile.ts (keepsake-gallery/03) -
//  Gallery.tsx's re-share action reuses the exact same helper, so there is
//  one canonical file-share code path, not two.
//
//  Save as image (keepsake-gallery/01, AC-01): a LOW-KEY link (not a full-size
//  Button) sits below the outlined "Share the tale" button - secondary weight,
//  deliberately not competing with the gold "Play another round" CTA. On tap
//  it renders the SAME tablet via `renderTabletImage()` (../gallery/renderTablet.ts,
//  which itself reuses `buildRevealParts` - never a second text derivation)
//  and triggers a client-side download; there is no server round-trip (AC-06).
//  A `savingImage` flag disables re-taps and swaps the label to "Saving
//  image..." while it renders (AC-03). The optional `saveImageByline` prop
//  carries the SAME attribution text the caller already shows via
//  `attribution` (when the caller has a plain-text form of it to give) - see
//  its own doc comment below for why Reveal never derives a second byline
//  format and why this prop is presently unwired from any caller.
//
//  Capture into the local gallery (keepsake-gallery/03, AC-01): the SAME blob
//  `handleSaveImage` renders for the download is ALSO handed to `saveTale()`
//  (../gallery/localGallery.ts) - one render, two outcomes (download + a new
//  local-gallery entry), never a second render pass. `saveTale` swallows its
//  own storage failures internally, so a gallery-write problem can never
//  break the existing download (AC-06 posture preserved).
//
//  Narration bar (AC-07): rendered but INACTIVE in Slice 1 - the play button is
//  disabled (a "coming soon" affordance) and the waveform bars are static (no
//  animation). The real estate is reserved so Phase 3 can wire TTS with no
//  layout change; no audio is implemented here.
//
//  Mode-aware slot (game-modes/03): an optional `revealPresentation` node
//  REPLACES the default coral-highlight body (the `parts.map(...)` block
//  below) when supplied - e.g. a paced, word-by-word reveal for a
//  progressively-reveal mode (game-modes/06, `ModeSurfaces.revealPresentation`).
//  It renders inside the SAME stone-tablet scroll panel, in place of the
//  default body only - the title, narration bar, confetti, and bottom CTAs are
//  unaffected either way. Omitted by default, which keeps today's
//  `buildRevealParts(template, assembled)` rendering byte-for-byte (AC-03).
//  `buildRevealParts` itself is not touched by this slot - any presentation
//  that needs the same highlight-correctness logic (05/06) reuses it
//  read-only, exactly as this file already does.
//
//  Fit-to-viewport layout (the UX de-clutter): the page fits ONE phone viewport
//  (~390x844) with NO page scroll in portrait - the STORY is the only element
//  that scrolls, and it scrolls INTERNALLY. The root is a fixed-height flex
//  column (`height:100dvh; display:flex; flexDirection:column; overflow:hidden`):
//  the app bar + trimmed celebratory header are fixed-height at the top, the
//  story card is `flex:1; minHeight:0` and scrolls within (so it absorbs all the
//  slack), and the reactions strip + Golden Guardian status + bottom action bar
//  sit at the bottom, always visible and tappable. The favorite STAR (solo-only,
//  a distinct action from a reaction: it favorites the story TEMPLATE to the
//  player's library) moved into the app bar's RIGHT slot - AppBar.tsx only takes
//  an icon `rightAction`, and FavoriteStarButton is its own icon button, so it is
//  rendered as a positioned element top-right in the header row rather than by
//  changing AppBar. The existing `@media (orientation: landscape)` escape hatch
//  still widens the column and lets the whole page scroll so a rotated phone is
//  never trapped in a sliver.
//
//  Styling: every color comes from theme tokens (theme.palette.coral.main for
//  the highlight color per the coral reconciliation note - the WEIGHT/underline
//  emphasis is content-level sx, but the color itself is never a hardcoded hex).
//  The stone-tablet gradient/glow reuses theme.palette.tablet.gradient (see
//  Home.tsx's hero tablet and Lobby.tsx's ShareWidget for the same pattern).
//  Arched radii and the pulsing glow keyframe use literal px strings/durations
//  per the story's technical notes (a bare sx borderRadius number multiplies by
//  theme.shape.borderRadius = 20, which would corrupt an arched shape). Icons
//  are FontAwesome only, registered in web/src/fontawesome.ts. No em dashes in
//  any prose/comments/strings.
// ----------------------------------------------------------------------------

import type { ReactNode } from 'react';
import { Fragment, useEffect, useState } from 'react';
import type { IconProp } from '@fortawesome/fontawesome-svg-core';
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome';
import { alpha, keyframes, useTheme } from '@mui/material/styles';
import { Box, Button, Link, Stack, Typography } from '@mui/material';
import { AppBar, FavoriteStarButton, Guardian, TaleFeedback } from '../components';
import type { GuardianVariant } from '../components';
import type { AssembledStory } from '../engine/assemble';
import { getBlanks, type Template } from '../engine/template';
import type { RemixableBlank } from '../engine/remixHelpers';
import { renderTabletImage } from '../gallery/renderTablet';
import { saveTale } from '../gallery/localGallery';
import { autoSaveTaleToVault } from '../vault/vaultClient';
import { shareImageFile, slugifyTitle } from '../gallery/shareImageFile';
import { buildRevealParts } from './revealParts';
import { FillBlank } from './FillBlank';

export interface RevealProps {
  /** The assembled story to render (title, storyText, per-blank filled words). */
  assembled: AssembledStory;
  /** The template whose body is walked to interleave literal text with highlighted words. */
  template: Template;
  /**
   * Optional content rendered under the celebratory header. Group play passes
   * a "carved by [names] & crew" byline; single-player passes a personal
   * summary (title + word count). Reveal renders NOTHING here if omitted -
   * it never hardcodes a crew byline, so solo can reuse this screen as-is.
   */
  attribution?: ReactNode;
  /** Gold primary CTA handler. The parent owns what happens next. */
  onPlayAgain: () => void;
  /**
   * Optional label for the gold primary CTA. Defaults to "Play another round"
   * (single-player replays in place). Group play repurposes this same CTA to
   * advance to its Round Complete recap first (group-play/04, AC-01), so it
   * passes a label like "See the round recap" to avoid two identically-labelled
   * "Play another round" buttons back to back.
   */
  playAgainLabel?: string;
  /** Optional home/close action for the app bar. Omit to render a balancing spacer. */
  onHome?: () => void;
  /**
   * Optional low-key "leave this screen" action, rendered as a ghost link below
   * the two primary CTAs so there is a DISCOVERABLE exit (the app-bar icon alone
   * is easy to miss under the confetti). Caller supplies the label so the intent
   * reads right per flow - solo passes "Back to home"; group play routes its exit
   * through Round Complete ("Back to lobby") and can omit this.
   */
  exitAction?: { label: string; onClick: () => void };
  /**
   * Optional mode-supplied presentation that REPLACES the default
   * coral-highlight story body when supplied - e.g. a paced, word-by-word
   * reveal for a progressively-reveal mode (game-modes/06,
   * `ModeSurfaces.revealPresentation`). Rendered inside the same stone-tablet
   * scroll panel, in place of the `buildRevealParts` body only. Omitted by
   * default, which keeps today's coral-highlight rendering byte-for-byte
   * (AC-03).
   */
  revealPresentation?: ReactNode;
  /**
   * Optional per-tale thumbs feedback slot (story-selection/05, AC-01): when
   * supplied, renders the quiet <TaleFeedback> control below the story panel,
   * subordinate to the CTAs. Single-player passes this ({@link templateId} +
   * mode "solo"). Group play's transient reveal (before its own Round Complete
   * recap, group-play/04) OMITS it - the group's vote surface lives on
   * RoundComplete.tsx instead, so a single round is never asked about twice.
   */
  taleFeedback?: { templateId: string; mode: string };
  /**
   * Optional reaction-row slot (reveal-delight/01, AC-01): rendered in the bottom
   * region, ABOVE the pinned <BottomActionBar> and inside the same
   * BottomActionBarSpacer reservation so it is never hidden behind the bar. Like
   * `attribution` / `taleFeedback` this keeps Reveal ROOM-AGNOSTIC - it renders
   * whatever node the caller passes and knows nothing about counts, the hub, or
   * solo-vs-group. Solo passes <ReactionRow> backed by local state (AC-05); group
   * play passes one backed by the hub's ReactionCountsChanged broadcast (AC-04).
   */
  reactionRow?: ReactNode;
  /**
   * Optional Golden Guardian funniest-word vote (reveal-delight/03). Like every
   * other slot here this keeps Reveal ROOM-AGNOSTIC: Reveal knows nothing about
   * the room, the hub, or who won - it only turns each NON-empty coral word into a
   * tap target and paints the caller-supplied winner. OMIT it entirely in solo
   * (AC-06: there is no room to vote in, so the mechanic is absent - not a
   * disabled no-op). When supplied:
   *   - `phase` 'voting': each coral word is tappable once the carve-in has
   *     finished (AC-01); a tap calls `onVote(blankId)` to cast/MOVE my single
   *     vote, and my current pick (`myVote`) is shown selected. A low-key
   *     "N of M voted" status renders (per-word counts are NOT shown mid-vote,
   *     AC-02). The host (only) may pass `onCloseVoting` to reveal the winner
   *     early via a low-pressure affordance (AC-03).
   *   - `phase` 'resolved': the `winningBlankId` coral word gets a gold ring/glow
   *     (theme.palette.gold.main) and a short warm announcement names it - never a
   *     ranked list or a "loser" callout (AC-03).
   *   - `phase` 'off': render nothing vote-related (a general escape hatch; group
   *     play uses 'voting'/'resolved', solo omits the prop outright).
   * The `blankId` value is an OPAQUE token Reveal assigns per coral word (its
   * body-order blank position) and hands back through `onVote` / matches against
   * `winningBlankId` - the caller passes it to the hub verbatim (AC-07: it is just
   * an already-vetted, already-displayed word's position, no new text, no PII).
   */
  goldenGuardian?: GoldenGuardianVote;
  /**
   * Optional per-word "carved by [nickname]" attribution (reveal-delight/04,
   * AC-01/AC-02/AC-06). Like every other slot here this keeps Reveal
   * ROOM-AGNOSTIC: Reveal knows nothing about the roster or the hub, it only
   * asks the caller to resolve a word's `playerSessionId` (the same value
   * `buildRevealParts` already carries on each `RevealWordPart`) to a
   * contributor. OMIT it entirely in solo (AC-04: every word is mine, so
   * naming a contributor is noise - Solo.tsx does not pass this prop).
   *
   * Treatment: a coral word with a resolvable contributor becomes a
   * tap-to-reveal target for a small "carved by [nickname]" chip (with their
   * Guardian) - it never forces a name onto every word inline, which would
   * drown the coral contrast (AC-02). This tap is DELIBERATELY separate from
   * the Golden Guardian vote tap (reveal-delight/03): while voting is
   * interactive (`goldenGuardian.phase === 'voting'` and the carve-in has
   * finished), the SAME coral word's tap is already taken to cast a vote, so
   * attribution reveal is inert there and only becomes tappable once voting
   * is resolved / absent - the two gestures never fight over the same word.
   * A word with no resolvable contributor (an unfilled blank, or a
   * contributor who has since left the room) is never tappable and never
   * renders "carved by undefined" (AC-03).
   */
  wordAttribution?: WordAttribution;
  /**
   * Optional favorite/star slot (story-selection/06, AC-01): when supplied,
   * renders the shared <FavoriteStarButton> below the celebratory header -
   * exactly the same solo-only gating pattern as `taleFeedback` above. Solo
   * passes this (template id + title); group play's transient reveal OMITS
   * it - the group's star lives on RoundComplete.tsx instead, mirroring
   * `taleFeedback`'s "never ask about a round twice" rule.
   */
  favorite?: { templateId: string; title: string };
  /**
   * Optional plain-text byline for the "Save as image" / "Share the tale"
   * actions (keepsake-gallery/01 AC-02, wired by keepsake-gallery/02 PART C):
   * rendered onto the saved/shared PNG as "carved by [names]", sourced from
   * the SAME crew data the caller already shows elsewhere - never a second
   * byline format invented by Reveal. Reveal stays ROOM-AGNOSTIC here too: it
   * does not know about crews or solo, it only forwards this string (or omits
   * the byline entirely, a still-valid image per AC-02) to
   * `renderTabletImage()`.
   *
   * Wired by App.tsx's `GroupReveal` wrapper (`../gallery/byline.ts`'s
   * `formatCrewByline`, built from the same `buildCrew` crew list the Round
   * Complete recap already derives - "carved by Sam, Mia & Bo"). Solo.tsx
   * deliberately OMITS this prop: solo collects no nickname at all (see
   * Solo.tsx's own comment at its `<Reveal>` call), so there is no faithful
   * byline string to give, not an oversight.
   */
  saveImageByline?: string;
  /**
   * Optional HOST-ONLY public-link share (keepsake-gallery/04, AC-01/AC-03/AC-07):
   * when supplied, renders a low-key, OPT-IN "Share a public link" affordance below
   * the "Save as image" link. Reveal stays ROOM-AGNOSTIC - it knows nothing about
   * publishing, the hub, or storage; it only calls `publish` on tap and threads the
   * returned `/t/<slug>` link into its EXISTING share payload (alongside the
   * watermarked image, or as the whole share when file-share is unsupported). Once a
   * link exists, a "Stop sharing this link" affordance calls `revoke` (AC-07).
   * OMITTED for non-hosts and for solo (which has no crew / nickname byline) - the
   * affordance is then simply absent, so publishing is never automatic (AC-03).
   */
  publicShare?: PublicShare;
  /**
   * Optional "Remix a word" slot (replay-remix/02, AC-01/AC-02/AC-03/AC-04/AC-05):
   * a LOW-EMPHASIS secondary action rendered in the bottom cluster, deliberately
   * NOT competing with the gold "Play another round" CTA above it (AC-01). Tapping
   * it shows the blank picker (`blanks` - category label + current word, AC-02),
   * then a SINGLE `<FillBlank>` re-entry step (the SAME stone-tablet prompt card
   * every normal round already uses - AC-03, never a new UI pattern) for the one
   * chosen blank. Submitting calls `onSubmit(blankId, word)`, which the CALLER
   * resolves to a fresh `collectWord` + `assembleStory` pass (solo) or a hub
   * round-trip (group) - Reveal itself holds NO collection state and never calls
   * the engine directly; it only forwards the tap. Once assembly re-runs, the
   * CALLER re-renders this SAME `<Reveal>` with the new `assembled` prop, so the
   * coral highlight body above (AC-05) picks up the remixed word through its
   * existing, unmodified rendering path - this overlay never forks or duplicates
   * that render. Omitted entirely when the caller has nothing to remix into
   * (there is always at least one blank for a real round, so this is really "the
   * caller has not wired remix yet", not a real empty-state).
   */
  remix?: RemixSlot;
}

/**
 * The "Remix a word" slot on the Reveal (replay-remix/02). See RevealProps.remix.
 */
export interface RemixSlot {
  /** The remixable blanks (category label + current word), from engine/remixHelpers.ts's `listRemixableBlanks`. */
  blanks: readonly RemixableBlank[];
  /**
   * Submits a new word for exactly ONE blank (AC-04/AC-06). The caller runs the
   * SAME safety check every other submission uses (solo: the engine-boundary
   * `SafetyCheck` hook via `collectWord`; group: the server-side `_safety.CheckAsync`
   * via the hub's `RemixWord`) and resolves with the same `{ accepted, message }`
   * shape `FillBlank`'s `onSubmitWord` already expects - a rejected word shows the
   * SAME friendly retry message inline and the player can try again without losing
   * their place in the remix step.
   */
  onSubmit: (blankId: string, word: string) => Promise<{ accepted: boolean; message?: string }>;
}

/**
 * The host-only public-link share slot on the Reveal (keepsake-gallery/04). See
 * RevealProps.publicShare. Both callbacks are network actions the caller owns;
 * Reveal only invokes them from an explicit host tap and never on its own.
 */
export interface PublicShare {
  /**
   * Publishes the current tale and resolves its public `/t/<slug>` URL, or `null`
   * when publishing is unavailable / failed (Reveal then falls back to the plain
   * image / text share, AC-01). Host-initiated and opt-in - only ever called from
   * the affordance's tap.
   */
  publish: () => Promise<string | null>;
  /** Revokes the last-published link so it stops resolving (AC-07). */
  revoke: (url: string) => Promise<void>;
}

/**
 * The word-attribution slot on the Reveal (reveal-delight/04). See
 * RevealProps.wordAttribution.
 */
export interface WordAttribution {
  /**
   * Resolve a filled word's `playerSessionId` (already carried on
   * `RevealWordPart`, unchanged by this feature) to the in-session
   * contributor's nickname + Guardian variant, sourced from the roster /
   * reveal payload the room already broadcasts (AC-01, AC-06). Return
   * `undefined` for an unattributed blank or a contributor who has since
   * left the room (AC-03) - Reveal then renders that word with no
   * attribution affordance at all, rather than a broken tile.
   */
  contributorFor: (playerSessionId: string) => { nickname: string; variant: GuardianVariant } | undefined;
}

/** The Golden Guardian vote slot on the Reveal (reveal-delight/03). See RevealProps.goldenGuardian. */
export interface GoldenGuardianVote {
  /** 'voting' while the room picks, 'resolved' once a winner is known, 'off' to render nothing. */
  phase: 'voting' | 'resolved' | 'off';
  /** Cast (or MOVE) my single vote to the tapped coral word's opaque blank token. */
  onVote: (blankId: string) => void;
  /** The blank token I currently voted for (shown selected), or undefined if I have not voted. */
  myVote?: string;
  /** How many present players have voted so far (the "N of M voted" status, AC-02). */
  votedCount: number;
  /** The total present players who can vote (the "M" in "N of M voted"). */
  totalVoters: number;
  /** When resolved: the winning coral word's blank token (gets the gold ring + announcement). */
  winningBlankId?: string;
  /** Host-only (AC-03): a low-pressure "Reveal the winner" affordance to close voting early. Omit for non-hosts. */
  onCloseVoting?: () => void;
}

// The stone tablet's pulsing glow (docs/design/Reveal.dc.html qsTabletGlow):
// alternates between a purple-tinted and gold-tinted shadow over ~4s.
const tabletGlow = keyframes`
  0%, 100% { box-shadow: 0 26px 55px -22px var(--qs-glow-purple), 0 0 0 6px var(--qs-glow-rim), inset 0 3px 0 var(--qs-glow-inner), inset 0 -5px 14px var(--qs-glow-edge); }
  50% { box-shadow: 0 30px 60px -20px var(--qs-glow-gold), 0 0 0 6px var(--qs-glow-rim), inset 0 3px 0 var(--qs-glow-inner), inset 0 -5px 14px var(--qs-glow-edge); }
`;

// A twinkling star glyph (docs/design/Reveal.dc.html qsTwinkle): fades and
// scales in place, never affecting layout of neighboring content.
const twinkle = keyframes`
  0%, 100% { opacity: .3; transform: scale(.8); }
  50% { opacity: 1; transform: scale(1.2); }
`;

// CSS-only confetti fall+spin (AC / out-of-scope: no canvas, no library).
// Each piece translates down and rotates; durations/delays vary per piece.
const confettiFall = keyframes`
  0% { transform: translateY(-10px) rotate(0deg); }
  100% { transform: translateY(14px) rotate(220deg); }
`;

// Word-by-word "carving" entrance (reveal-delight/02, AC-01/AC-02): each
// coral filled word pops from a smaller scale up to its natural size. This is
// TRANSFORM ONLY, deliberately - never an `opacity` keyframe step, per this
// feature's documented footgun (an opacity keyframe with fill-mode:both can
// leave a re-rendered list item stuck invisible, which here would make the
// WHOLE story look half-missing). The literal template text and empty-word
// gaps are untouched by this keyframe (AC-01).
const carveIn = keyframes`
  from { transform: scale(.4); }
  to { transform: scale(1); }
`;

// Stagger between each filled word's carve-in entrance, in body order
// (AC-01). Computed per filled-word index, not the raw `parts` index (which
// also counts literal text gaps).
const CARVE_STAGGER_MS = 140;

// The carve-in keyframe's own duration (matches the `0.4s` on the word span's
// animation shorthand below). reveal-delight/03 (AC-01) reads it to know when the
// LAST word has finished carving, so the vote step only becomes interactive after
// the story is fully shown.
const CARVE_DURATION_MS = 400;

// reveal-delight/03 (AC-03): the winning coral word's gentle one-shot pop when the
// vote resolves. TRANSFORM ONLY (scale) - never an opacity keyframe on this
// re-rendered word span (this feature's documented footgun) - so a re-render can
// never strand the winning word invisible. The gold ring/glow itself is a static
// box-shadow applied via sx, not animated here.
const winnerPop = keyframes`
  0% { transform: scale(1); }
  45% { transform: scale(1.14); }
  100% { transform: scale(1); }
`;

// reveal-delight/04 (AC-02): the "carved by [nickname]" chip's tap-to-reveal
// pop, keyed by a single word. TRANSFORM ONLY (scale) - never an opacity
// keyframe on this reused span - so a re-render can never strand the chip
// invisible (this feature's shared footgun, see implementation.md).
const attributionPop = keyframes`
  0% { transform: scale(.6); }
  100% { transform: scale(1); }
`;

/** One CSS-only confetti piece: color, shape, position, and animation timing. */
interface ConfettiPiece {
  top: number;
  left?: number;
  right?: number;
  size: number;
  round: boolean;
  color: 'coral' | 'teal' | 'primary' | 'gold';
  rotate?: number;
  duration: number;
  delay: number;
}

// 8 pieces, palette colors only, scattered across the celebratory header band
// (docs/design/Reveal.dc.html confetti layout, AC / out-of-scope note).
const CONFETTI_PIECES: readonly ConfettiPiece[] = [
  { top: 8, left: 42, size: 9, round: false, color: 'coral', rotate: 20, duration: 2.6, delay: 0 },
  { top: 34, left: 88, size: 8, round: true, color: 'teal', duration: 3.1, delay: 0.3 },
  { top: 0, left: 150, size: 10, round: false, color: 'primary', rotate: 40, duration: 2.9, delay: 0.5 },
  { top: 50, right: 120, size: 8, round: false, color: 'gold', rotate: -15, duration: 3.3, delay: 0.2 },
  { top: 14, right: 64, size: 9, round: true, color: 'coral', duration: 2.7, delay: 0.6 },
  { top: 40, right: 34, size: 9, round: false, color: 'primary', rotate: 25, duration: 3.0, delay: 0.15 },
  { top: -10, left: 108, size: 7, round: false, color: 'teal', rotate: 30, duration: 3.4, delay: 0.45 },
  { top: 60, left: 60, size: 8, round: false, color: 'gold', duration: 2.8, delay: 0.35 },
];

/** CSS-only confetti band: 8 pieces, palette colors, fall+spin (AC, out-of-scope). */
function Confetti() {
  const theme = useTheme();
  const colorFor = (color: ConfettiPiece['color']) => theme.palette[color].main;

  return (
    <Box
      aria-hidden
      sx={{
        position: 'absolute',
        inset: '0 0 auto 0',
        height: 220,
        overflow: 'hidden',
        pointerEvents: 'none',
      }}
    >
      {CONFETTI_PIECES.map((piece, index) => (
        <Box
          key={index}
          sx={{
            position: 'absolute',
            top: piece.top,
            left: piece.left,
            right: piece.right,
            width: piece.size,
            height: piece.round ? piece.size : piece.size * 1.5,
            bgcolor: colorFor(piece.color),
            borderRadius: piece.round ? '50%' : '2px',
            transform: piece.rotate ? `rotate(${piece.rotate}deg)` : undefined,
            animation: `${confettiFall} ${piece.duration}s ease-in-out ${piece.delay}s infinite alternate`,
          }}
        />
      ))}
    </Box>
  );
}

/**
 * The trimmed celebratory header (the UX de-clutter): ONE compact line with a
 * twinkling star on each side of "Your tale is carved!". Deliberately a single
 * fixed-height band at the top of the fit-to-viewport column so the story card
 * gets the slack. The word-count / crew line is NOT hardcoded here - it belongs
 * to the caller-owned `attribution` slot (solo passes "You filled N words";
 * group passes its crew byline), so this header never duplicates it or asserts
 * "together" in a solo game.
 */
function CelebrationHeader() {
  return (
    <Stack alignItems="center" sx={{ position: 'relative', textAlign: 'center', px: 5.5, pt: 0.5, pb: 0.5 }}>
      <Stack direction="row" alignItems="center" justifyContent="center" spacing={1.25}>
        <Box sx={{ color: 'gold.main', fontSize: 18, display: 'flex', animation: `${twinkle} 2.4s ease-in-out infinite` }}>
          <FontAwesomeIcon icon="star" />
        </Box>
        <Typography
          component="h2"
          sx={{ fontFamily: '"Fredoka", sans-serif', fontWeight: 700, fontSize: 23, color: 'text.primary' }}
        >
          Your tale is carved!
        </Typography>
        <Box
          sx={{
            color: 'gold.main',
            fontSize: 18,
            display: 'flex',
            animation: `${twinkle} 2.4s ease-in-out .8s infinite`,
          }}
        >
          <FontAwesomeIcon icon="star" />
        </Box>
      </Stack>
    </Stack>
  );
}

/**
 * replay-remix/02 (AC-02): the "which blank could I remix?" picker - one
 * tappable row per remixable blank, showing its category label + the word
 * currently sitting there, so the player picks exactly ONE to re-fill. A
 * "Cancel remix" app-bar exit mirrors FillBlank's own leave affordance
 * (`onExit`) rather than inventing a new escape gesture. Rendered full-screen
 * as an overlay OVER the (still-mounted, unmodified) Reveal body below it -
 * this never forks Reveal's own rendering, it just sits on top of it.
 */
function RemixPicker({
  blanks,
  onPick,
  onCancel,
}: {
  blanks: readonly RemixableBlank[];
  onPick: (blankId: string) => void;
  onCancel: () => void;
}) {
  const theme = useTheme();

  return (
    <Box
      sx={{
        position: 'relative',
        height: '100dvh',
        display: 'flex',
        flexDirection: 'column',
        // Tablet / desktop: a wider column than the 430 phone width (viewport pass).
        maxWidth: { xs: 430, sm: 560 },
        mx: 'auto',
        overflow: 'hidden',
      }}
    >
      <AppBar title="Remix a word" leftAction={{ icon: 'xmark', label: 'Cancel remix', onClick: onCancel }} />
      <Stack sx={{ flex: 1, minHeight: 0, overflowY: 'auto', px: 5, pt: 2, pb: 3 }} spacing={1.5}>
        <Typography
          sx={{
            fontFamily: '"Nunito", sans-serif',
            fontWeight: 700,
            fontSize: 14,
            color: 'text.secondary',
            mb: 0.5,
          }}
        >
          Pick ONE word to swap - the rest of the tale stays just as it was.
        </Typography>
        {blanks.map((entry) => (
          <Box
            key={entry.blankId}
            component="button"
            type="button"
            onClick={() => onPick(entry.blankId)}
            sx={{
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'space-between',
              gap: 2,
              minHeight: 56,
              px: 3,
              py: 1.5,
              border: 'none',
              borderRadius: '18px',
              textAlign: 'left',
              cursor: 'pointer',
              bgcolor: alpha(theme.palette.primary.main, 0.08),
              '&:hover': { bgcolor: alpha(theme.palette.primary.main, 0.14) },
              '&:active': { transform: 'scale(0.98)' },
            }}
          >
            <Box sx={{ minWidth: 0 }}>
              <Typography
                sx={{
                  fontFamily: '"Nunito", sans-serif',
                  fontWeight: 800,
                  fontSize: 11.5,
                  letterSpacing: '0.06em',
                  textTransform: 'uppercase',
                  color: 'primary.main',
                }}
              >
                {entry.categoryLabel}
              </Typography>
              <Typography
                noWrap
                sx={{
                  fontFamily: '"Fredoka", sans-serif',
                  fontWeight: 600,
                  fontSize: 18,
                  color: 'text.primary',
                }}
              >
                {entry.word !== '' ? entry.word : '(skipped)'}
              </Typography>
            </Box>
            <Box sx={{ color: 'primary.main', flexShrink: 0, display: 'flex' }}>
              <FontAwesomeIcon icon="shuffle" style={{ width: 16, height: 16 }} />
            </Box>
          </Box>
        ))}
      </Stack>
    </Box>
  );
}

/** The narration bar: play/pause, waveform, label - RENDERED but INACTIVE (AC-07). */
function NarrationBar({ title }: { title: string }) {
  const theme = useTheme();
  // Exactly 12 static waveform bars (docs/design/Reveal.dc.html qsWave layout).
  // No animation in Slice 1 - the waveform does not move (AC-07).
  const barColors = [
    'primary.main', 'primary.main', 'teal.main', 'primary.main', 'gold.main',
    'primary.main', 'primary.main', 'teal.main', 'coral.main', 'primary.main',
    'primary.main', 'teal.main',
  ] as const;

  return (
    <Stack
      direction="row"
      alignItems="center"
      spacing={1.5}
      sx={{
        px: 4,
        py: 3.5,
        bgcolor: alpha(theme.palette.primary.main, 0.1),
        borderBottom: `1.5px solid ${alpha(theme.palette.stoneEdge.main, 0.22)}`,
      }}
    >
      {/* Disabled play affordance: "coming soon" in Slice 1 (AC-07). Real
          narration wiring (Phase 3) swaps this to a live toggle with no
          layout change. */}
      <Box
        component="button"
        type="button"
        disabled
        aria-label="Narration coming soon"
        sx={{
          flexShrink: 0,
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          width: 48,
          height: 48,
          border: 'none',
          borderRadius: '50%',
          bgcolor: 'primary.main',
          color: theme.palette.common.white,
          opacity: 0.5,
          cursor: 'not-allowed',
        }}
      >
        <FontAwesomeIcon icon="play" style={{ width: 18, height: 18 }} />
      </Box>
      <Box sx={{ minWidth: 0, flex: 1 }}>
        <Typography sx={{ fontFamily: '"Fredoka", sans-serif', fontWeight: 600, fontSize: 15 }}>
          {title}
        </Typography>
        <Typography
          sx={{ fontFamily: '"Nunito", sans-serif', fontWeight: 700, fontSize: 11.5, color: 'text.secondary', mt: 0.25 }}
        >
          Narration coming soon
        </Typography>
      </Box>
      {/* Static waveform (does not animate in Slice 1, AC-07). */}
      <Stack direction="row" alignItems="flex-end" spacing={0.5} sx={{ height: 18, flexShrink: 0 }}>
        {barColors.map((color, index) => (
          <Box
            key={index}
            sx={{ width: 3, height: index % 3 === 0 ? 16 : 10, bgcolor: color, borderRadius: '2px' }}
          />
        ))}
      </Stack>
    </Stack>
  );
}

export function Reveal({
  assembled,
  template,
  attribution,
  onPlayAgain,
  playAgainLabel = 'Play another round',
  onHome,
  exitAction,
  revealPresentation,
  taleFeedback,
  reactionRow,
  goldenGuardian,
  wordAttribution,
  favorite,
  saveImageByline,
  publicShare,
  remix,
}: RevealProps) {
  const theme = useTheme();
  const parts = buildRevealParts(template, assembled);

  // reveal-delight/04 (AC-02): which coral word's "carved by" chip is
  // currently revealed, keyed by its opaque blank token (the same body-order
  // position token 03 uses for votes). Client-local, tap-to-toggle - never
  // shared over the hub (AC-06). Reset is implicit: GroupReveal remounts per
  // reveal, so this starts fresh each round.
  const [revealedAttribution, setRevealedAttribution] = useState<string | null>(null);

  // reveal-delight/03 (AC-01): the vote step must only become interactive once the
  // story is FULLY shown - i.e. once the last coral word has finished carving in
  // (reveal-delight/02), or IMMEDIATELY when reduced-motion is on / there is no
  // carve to wait for. Count the filled (non-empty) coral words - the carve stagger
  // follows their body order - and flip `carveComplete` after the last one lands.
  const filledWordCount = parts.reduce(
    (count, part) => (part.kind === 'word' && part.word !== '' ? count + 1 : count),
    0,
  );
  const [carveComplete, setCarveComplete] = useState(false);
  useEffect(() => {
    // Respect reduced-motion (the carve is skipped there, so voting is ready at
    // once) and the no-filled-words edge (nothing to carve). Otherwise wait for the
    // last word: (n-1) staggers + one carve duration, plus a little slack.
    const prefersReducedMotion =
      typeof window !== 'undefined' &&
      typeof window.matchMedia === 'function' &&
      window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    if (prefersReducedMotion || filledWordCount === 0) {
      setCarveComplete(true);
      return;
    }
    setCarveComplete(false);
    const totalMs = (filledWordCount - 1) * CARVE_STAGGER_MS + CARVE_DURATION_MS + 80;
    const timer = setTimeout(() => setCarveComplete(true), totalMs);
    return () => clearTimeout(timer);
  }, [filledWordCount]);

  // Voting is live only while phase is 'voting' AND the carve-in has completed
  // (AC-01). Resolution paints the winner regardless of carve timing.
  const voteInteractive = goldenGuardian?.phase === 'voting' && carveComplete;
  const voteResolved = goldenGuardian?.phase === 'resolved';

  // Resolve the winning coral word's TEXT from its opaque blank token (body-order
  // blank position) for the warm announcement (AC-03) - Reveal owns the token, so
  // it maps it back here rather than the caller shipping the word.
  const winningWord = (() => {
    if (!voteResolved || goldenGuardian?.winningBlankId === undefined) return '';
    let position = 0;
    for (const part of parts) {
      if (part.kind !== 'word') continue;
      const token = String(position);
      position += 1;
      if (token === goldenGuardian.winningBlankId && part.word !== '') return part.word;
    }
    return '';
  })();

  // Feature-detect Web Share once - it does not change over the component's
  // lifetime (mirrors Lobby.tsx's ShareWidget, session-engine/04).
  const [canShare] = useState(
    () => typeof navigator !== 'undefined' && typeof navigator.share === 'function',
  );

  // Copy the tale as the always-available fallback for Share (AC-06 - the button
  // must always DO something). Guards navigator/clipboard and swallows a
  // denied-permission rejection. keepsake-gallery/04 (AC-01): when a public tale
  // `link` exists, copy THAT (the whole point of the back-link is that the
  // recipient can open it) rather than the raw story text.
  const copyTale = async (link?: string) => {
    if (typeof navigator === 'undefined' || !navigator.clipboard) return;
    try {
      await navigator.clipboard.writeText(link !== undefined && link.length > 0 ? link : assembled.storyText);
    } catch {
      // Clipboard permission denied or unavailable - fail silently, no error surfaced.
    }
  };


  // The EXISTING text-only share path (session-engine/04 pattern): feature-detect
  // `navigator.share`, swallow a user-cancelled AbortError, and fall back to
  // clipboard when Web Share is unavailable or rejects for any other reason
  // (AC-06's "no JS error ever thrown"). keepsake-gallery/04 (AC-01): an optional
  // public tale `link` rides the SAME payload (Web Share `url` slot) when the host
  // has published one - and when Web Share is unavailable, the link is copied
  // instead of the story text so the recipient still gets the real back-link. With
  // no link this is byte-for-byte the pre-04 text share.
  const shareText = async (link?: string): Promise<void> => {
    if (canShare) {
      try {
        const payload: ShareData = { title: assembled.title, text: assembled.storyText };
        if (link !== undefined && link.length > 0) payload.url = link;
        await navigator.share(payload);
        return;
      } catch (error) {
        if (error instanceof Error && error.name === 'AbortError') return;
        await copyTale(link);
        return;
      }
    }
    await copyTale(link);
  };

  // keepsake-gallery/02 (AC-01/AC-06): try sharing the SAME watermarked
  // tablet image handleSaveImage renders (never a second image derivation),
  // wrapped in a File. Returns true once the share attempt is "handled" - a
  // successful share OR a user cancellation (AbortError, swallowed exactly
  // like the text-share path above) - so handleShare knows NOT to fall
  // through to the text share in either of those cases. Returns false to
  // signal "fall back to text share" when file sharing is unsupported, the
  // render itself fails (AC guard: rendering must never break the button), or
  // navigator.share rejects for any reason other than a cancellation.
  //
  // `navigator.canShare({ files: [file] })` is used HERE deliberately - the
  // one narrow, correct use of that predicate on this screen (a FILE
  // payload), unlike the plain text/URL share above and in session-engine/04,
  // which must NOT gate on it (see this file's header comment).
  const shareImage = async (link?: string): Promise<boolean> => {
    // Quick feature-detect BEFORE rendering: no point paying the render cost
    // when this browser cannot share files at all. The actual share-with-
    // fallback logic (the real canShare-with-a-File check, the share call,
    // the AbortError swallow) lives in the shared ../gallery/shareImageFile.ts
    // helper (keepsake-gallery/03) - Gallery.tsx's re-share reuses the exact
    // same function, so there is one canonical file-share code path.
    // keepsake-gallery/04 (AC-01): an optional public tale `link` rides ALONGSIDE
    // the image in the SAME payload when the host has published one.
    if (typeof navigator === 'undefined' || typeof navigator.canShare !== 'function') return false;
    try {
      const blob = await renderTabletImage({ assembled, template, theme, byline: saveImageByline });
      const file = new File([blob], `${slugifyTitle(assembled.title)}.png`, { type: 'image/png' });
      return await shareImageFile({ file, title: assembled.title, text: assembled.storyText, url: link });
    } catch {
      return false;
    }
  };

  // keepsake-gallery/02 (AC-01/AC-03/AC-06 style): a client-local "share is in
  // flight" flag - disables re-taps while the image renders and the share
  // sheet resolves, mirroring `savingImage` below. Never shared with the hub;
  // purely a UI affordance.
  const [sharingImage, setSharingImage] = useState(false);

  const handleShare = async () => {
    if (sharingImage) return;
    setSharingImage(true);
    try {
      const shared = await shareImage();
      if (shared) return;
      // File sharing unsupported, or the image path failed/was rejected for a
      // non-cancellation reason: fall back to the existing text share,
      // exactly as before this story (AC-06 - it is never removed or forked).
      await shareText();
    } finally {
      setSharingImage(false);
    }
  };

  // keepsake-gallery/04 (AC-01/AC-03/AC-07): host-only public-link state. The
  // last-published link (so the share payload and the revoke affordance can both
  // reference it) and an "in flight" flag that disables re-taps while publishing +
  // sharing resolve. Purely client-local UI - the publish/revoke network calls
  // live in the caller-owned `publicShare` callbacks (Reveal stays room-agnostic).
  const [publicLink, setPublicLink] = useState<string | null>(null);
  const [publishing, setPublishing] = useState(false);

  // keepsake-gallery/04 (AC-01/AC-03): host taps the OPT-IN "Share a public link"
  // affordance. Publish the tale (server re-vets + mints the slug), then share the
  // returned link in the SAME payload as the watermarked image - or, when file
  // sharing is unsupported, the link ALONE is the share (text/url). If publishing
  // is unavailable / fails, fall back to the plain image/text share so the tap is
  // never a dead no-op. Reuses the EXISTING share plumbing (shareImage/shareText),
  // never a second forked share flow.
  const handleShareLink = async () => {
    if (!publicShare || publishing || sharingImage) return;
    setPublishing(true);
    try {
      const link = await publicShare.publish();
      if (link !== null) setPublicLink(link);
      const shared = await shareImage(link ?? undefined);
      if (shared) return;
      await shareText(link ?? undefined);
    } finally {
      setPublishing(false);
    }
  };

  // keepsake-gallery/04 (AC-07): stop sharing - revoke the published link so it
  // stops resolving. Low-ceremony; clears the local link either way.
  const handleRevokeLink = async () => {
    if (!publicShare || publicLink === null || publishing) return;
    setPublishing(true);
    try {
      await publicShare.revoke(publicLink);
      setPublicLink(null);
    } finally {
      setPublishing(false);
    }
  };

  // keepsake-gallery/01 (AC-01/AC-03): a client-local "is the image rendering
  // right now" flag - disables re-taps and swaps the label while renderTablet
  // does its work. Never shared with the hub; purely a UI affordance.
  const [savingImage, setSavingImage] = useState(false);

  const handleSaveImage = async () => {
    if (savingImage) return;
    setSavingImage(true);
    try {
      const blob = await renderTabletImage({ assembled, template, theme, byline: saveImageByline });
      const url = URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = url;
      link.download = `${slugifyTitle(assembled.title)}.png`;
      document.body.appendChild(link);
      link.click();
      document.body.removeChild(link);
      // Revoke on the next tick, not synchronously: some browsers (notably
      // Safari variants) abort the download if the object URL is revoked in the
      // same turn as the click. The delay still frees the URL, just after the
      // download has latched onto it (Copilot review, PR #130).
      setTimeout(() => URL.revokeObjectURL(url), 0);
      // keepsake-gallery/03 (AC-01): persist the SAME rendered blob to the
      // local gallery so it shows up in "Tales we've carved" - fire-and-forget
      // (saveTale swallows its own storage failures), so a gallery-write
      // problem can never undo or block the download above, which has
      // already completed by this point.
      //
      // keepsake-gallery/05: ALSO persist the flattened display parts (the SAME
      // already-vetted `parts` this screen renders, mapped to {isWord, text}),
      // so a signed-in purchaser can later upload this tale to the cloud gallery
      // as text from the Account area. This adds LOCAL data only - it does not
      // make the reveal flow credential/sign-in aware (the auth boundary: the
      // child-facing reveal never touches a purchaser credential).
      void saveTale({
        title: assembled.title,
        image: blob,
        bylineNames: saveImageByline,
        parts: parts.map((part) => ({
          isWord: part.kind === 'word',
          text: part.kind === 'word' ? part.word : part.text,
        })),
      });
      // keepsake-vault/01 (AC-02): ALSO auto-save this completed reveal to the
      // anonymous server-side keepsake vault - a SIBLING fire-and-forget call to
      // saveTale above, same posture, same place. It resolves the durable device
      // vault id (minting on first use, see vault/vaultId.ts) and POSTs the SAME
      // already-vetted flattened parts + byline, with the vault id in the
      // X-Vault-Id header (never a URL path). It swallows every failure, so a
      // vault/network problem can never block, delay, or degrade the download that
      // has already completed above. This adds NO credential/sign-in awareness to
      // the child-facing reveal (a vault id is an anonymous random handle, not an
      // account).
      void autoSaveTaleToVault({
        title: assembled.title,
        bylineNames: saveImageByline,
        parts: parts.map((part) => ({
          isWord: part.kind === 'word',
          text: part.kind === 'word' ? part.word : part.text,
        })),
      });
    } catch {
      // Rendering/download can fail (an unsupported canvas API, a blocked
      // download) - fail quietly rather than surface an error UI in Slice 1,
      // mirroring the Share button's own silent-failure posture above.
    } finally {
      setSavingImage(false);
    }
  };

  // replay-remix/02 (AC-01/AC-02/AC-03): the client-local remix overlay step -
  // 'closed' (default), 'picker' (choosing which blank), or 'prompt' (re-entering
  // the chosen blank's word via the SAME reused <FillBlank> chrome). Reveal holds
  // NO collection state of its own here - `remix.onSubmit` is the one path back
  // to the caller's engine/hub call (AC-04); a successful submit just closes the
  // overlay, and the caller re-rendering THIS component with a fresh `assembled`
  // is what actually shows the remixed word (AC-05).
  const [remixStep, setRemixStep] = useState<'closed' | 'picker' | 'prompt'>('closed');
  const [remixBlankId, setRemixBlankId] = useState<string | null>(null);

  const closeRemix = () => {
    setRemixStep('closed');
    setRemixBlankId(null);
  };

  const remixBlank =
    remixBlankId !== null ? getBlanks(template).find((b) => b.id === remixBlankId) : undefined;

  return (
    <Box
      sx={{
        // Fit-to-viewport (the UX de-clutter): a FIXED-height flex column that
        // cannot page-scroll in portrait - the story card (flex:1) absorbs the
        // slack and scrolls internally, so the header + reactions + bottom bar
        // all stay put and visible. See the file header.
        position: 'relative',
        height: '100dvh',
        display: 'flex',
        flexDirection: 'column',
        // Tablet / desktop: a wider column than the 430 phone width (viewport pass).
        maxWidth: { xs: 430, sm: 560 },
        mx: 'auto',
        overflow: 'hidden',
        // Landscape (design-system/03): a handed-off phone that auto-rotates
        // must not trap the tale in an unreadable sliver. Widen the column and
        // let the whole PAGE scroll (height auto + overflow visible) so the story
        // renders full-length instead of inside a short flex box. Portrait is
        // untouched - every override is scoped to `orientation: landscape`.
        '@media (orientation: landscape)': {
          maxWidth: 720,
          height: 'auto',
          minHeight: '100dvh',
          display: 'block',
          overflow: 'visible',
        },
      }}
    >
      <Confetti />

      {/* replay-remix/02 (AC-01/AC-02/AC-03): the remix overlay sits ON TOP of
          this same screen (`position: fixed`, above everything else) rather than
          replacing any of the rendering below - the story body, header, and
          bottom cluster stay mounted and unmodified underneath. Closing the
          overlay (cancel, skip, or a successful submit) just returns to this
          same Reveal, already showing whatever `assembled` the caller passed. */}
      {remix && remixStep !== 'closed' && (
        <Box
          sx={{
            position: 'fixed',
            inset: 0,
            zIndex: 30,
            bgcolor: 'background.default',
          }}
        >
          {remixStep === 'picker' ? (
            <RemixPicker
              blanks={remix.blanks}
              onPick={(blankId) => {
                setRemixBlankId(blankId);
                setRemixStep('prompt');
              }}
              onCancel={closeRemix}
            />
          ) : (
            remixBlank && (
              <FillBlank
                key={remixBlank.id}
                subject={assembled.title}
                blank={remixBlank}
                wordNumber={1}
                totalWords={1}
                onExit={closeRemix}
                exitLabel="Cancel remix"
                onSkip={closeRemix}
                onSubmitWord={async (word) => {
                  const result = await remix.onSubmit(remixBlank.id, word);
                  if (result.accepted) closeRemix();
                  return result;
                }}
              />
            )
          )}
        </Box>
      )}

      {/* App bar + trimmed celebratory header: the fixed-height top band of the
          flex column (flexShrink:0 so it never gives up height to the story).
          Kept above the confetti (which is absolutely positioned) so it never
          paints over the home icon or the favorite star. */}
      <Box sx={{ position: 'relative', zIndex: 1, flexShrink: 0 }}>
        <Box sx={{ position: 'relative' }}>
          <AppBar
            title="The Reveal"
            leftAction={onHome ? { icon: 'house', label: 'Go home', onClick: onHome } : undefined}
          />
          {/* Favorite/star toggle (story-selection/06, AC-01): the star favorites
              this story TEMPLATE to the player's library - a DISTINCT action, NOT a
              reaction. Solo-only, mirroring `taleFeedback`'s gating (omitted for
              group play's transient reveal). AppBar.tsx takes only an ICON
              rightAction and must not change, and FavoriteStarButton is its own icon
              button, so it is positioned over the app bar's right slot here. */}
          {favorite && (
            <Box
              sx={{
                position: 'absolute',
                top: '50%',
                right: 'max(8px, env(safe-area-inset-right))',
                transform: 'translateY(-50%)',
              }}
            >
              <FavoriteStarButton templateId={favorite.templateId} title={favorite.title} />
            </Box>
          )}
        </Box>

        <CelebrationHeader />

        {attribution && (
          <Box sx={{ px: 5.5, pb: 1, textAlign: 'center' }}>{attribution}</Box>
        )}
      </Box>

      {/* Story region: the HERO. `flex:1; minHeight:0` so it fills all the slack
          between the header and the bottom cluster, and scrolls INTERNALLY (the
          tablet body below) so the page itself never scrolls in portrait. The
          Golden Guardian status + taleFeedback ride along under the tablet inside
          this same flex region. In landscape the whole column lays out in normal
          flow, so this is a plain block that grows with its content. */}
      <Stack
        sx={{
          flex: 1,
          minHeight: 0,
          px: 5,
          pb: 0,
          '@media (orientation: landscape)': { flex: 'none', minHeight: 'auto' },
        }}
      >
        {/* STONE-TABLET scroll panel (AC-01): arched radius, glowing carved rim,
            pulsing purple/gold shadow. `flex:1; minHeight:0` so it fills the story
            region and its body (below) scrolls within. Literal px strings for the
            arch and glow - a bare sx borderRadius number multiplies by
            theme.shape.borderRadius (20), which would corrupt this shape. */}
        <Box
          sx={{
            position: 'relative',
            display: 'flex',
            flexDirection: 'column',
            flex: 1,
            minHeight: 0,
            borderRadius: '40px 40px 28px 28px',
            background: theme.palette.tablet.gradient,
            overflow: 'hidden',
            '--qs-glow-purple': alpha(theme.palette.primary.main, 0.55),
            '--qs-glow-gold': alpha(theme.palette.gold.main, 0.6),
            '--qs-glow-rim': alpha(theme.palette.common.white, 0.3),
            '--qs-glow-inner': alpha(theme.palette.common.white, 0.55),
            '--qs-glow-edge': alpha(theme.palette.stoneEdge.main, 0.4),
            animation: `${tabletGlow} 4s ease-in-out infinite`,
            // In landscape the tablet is a normal block that grows with its
            // content (the whole page scrolls), matching the body override below.
            '@media (orientation: landscape)': { flex: 'none', minHeight: 'auto' },
          }}
        >
          <NarrationBar title="Hear it in the Guardian's voice" />

          {/* Story scroll: the ONE element that scrolls in portrait. `flex:1;
              minHeight:0` so it fills the tablet's remaining height (below the
              narration bar) and scrolls INTERNALLY - the page itself never scrolls
              (the UX de-clutter). A soft bottom fade (mask-image, no layout impact)
              cues that there is more story below the fold. In landscape the inner
              scroll is lifted (design-system/03): a short landscape viewport would
              turn this into an unreadable sliver, so the panel renders full-length
              and the whole page scrolls instead - and the fade mask is dropped there
              so the last line is never clipped. */}
          <Box
            sx={{
              flex: 1,
              minHeight: 0,
              overflowY: 'auto',
              px: 5,
              py: 4,
              // Soft bottom fade cue: the last ~24px fades toward the tablet, hinting
              // the story continues past the fold. A mask never affects layout.
              maskImage: 'linear-gradient(to bottom, black calc(100% - 24px), transparent 100%)',
              WebkitMaskImage: 'linear-gradient(to bottom, black calc(100% - 24px), transparent 100%)',
              '@media (orientation: landscape)': {
                flex: 'none',
                minHeight: 'auto',
                overflowY: 'visible',
                maskImage: 'none',
                WebkitMaskImage: 'none',
              },
            }}
          >
            <Typography
              component="h3"
              sx={{
                mb: 3,
                fontFamily: '"Fredoka", sans-serif',
                fontWeight: 700,
                fontSize: 23,
                lineHeight: 1.18,
                color: 'primary.main',
              }}
            >
              {assembled.title}
            </Typography>
            {revealPresentation === undefined ? (
              <Typography
                component="p"
                sx={{
                  fontFamily: '"Nunito", sans-serif',
                  fontWeight: 600,
                  fontSize: 17.5,
                  lineHeight: 1.72,
                  color: 'text.primary',
                }}
              >
                {(() => {
                  // Running counter over FILLED (non-empty) words only, in body
                  // order, so the carve-in stagger (reveal-delight/02, AC-01)
                  // follows the story's reading order rather than the raw
                  // `parts` index, which also counts literal text gaps.
                  let filledWordIndex = 0;
                  // reveal-delight/03: a SEPARATE counter over EVERY blank (empty or
                  // filled), in body order, so each coral word's opaque vote token
                  // (its blank position) stays aligned with the server's blank
                  // indices - a vote token round-trips to the right word/contributor.
                  let blankPos = 0;
                  return parts.map((part, index) => {
                    if (part.kind === 'text') {
                      return (
                        <Box key={`p-${index}`} component="span">
                          {part.text}
                        </Box>
                      );
                    }
                    // This blank occupies one body-order position whether or not it
                    // was filled - advance the token counter for empty blanks too so
                    // it never drifts out of step with the server's blank indices.
                    const token = String(blankPos);
                    blankPos += 1;
                    // A skipped blank arrives as an empty-word part. Render it as
                    // plain nothing (no coral treatment) so it reads as a natural
                    // gap rather than a stray zero-width coral underline artifact
                    // (Gate-2 CR-W-001). Only NON-empty, player-supplied words get
                    // the coral pop (and, when voting, the tap target).
                    if (part.word === '') {
                      return <Box key={`p-${index}`} component="span" />;
                    }
                    const delayMs = filledWordIndex * CARVE_STAGGER_MS;
                    filledWordIndex += 1;
                    // reveal-delight/03: additive vote state on THIS coral word.
                    const isMyVote = voteInteractive && goldenGuardian?.myVote === token;
                    const isWinner = voteResolved && goldenGuardian?.winningBlankId === token;
                    // reveal-delight/04 (AC-01/AC-03): resolve this word's
                    // contributor from its playerSessionId (unchanged data
                    // from buildRevealParts). Undefined for an unfilled blank
                    // or a contributor who has since left the room - never
                    // rendered, never tappable, never "carved by undefined".
                    const contributor =
                      wordAttribution && part.playerSessionId !== undefined
                        ? wordAttribution.contributorFor(part.playerSessionId)
                        : undefined;
                    // reveal-delight/04: the attribution tap only goes live once
                    // the story is FULLY shown (carveComplete, AC-01), matching the
                    // vote gate - so it never fires mid-carve. It is also
                    // DELIBERATELY inert while the Golden Guardian vote tap owns
                    // this same word (voteInteractive) - the two gestures never
                    // fight over one coral word. So: once carving is done and a
                    // contributor is resolvable, attribution is live except while
                    // voting is interactive (then the vote owns the tap).
                    const attributionInteractive =
                      contributor !== undefined && carveComplete && !voteInteractive;
                    const attributionRevealed =
                      attributionInteractive && revealedAttribution === token;
                    const toggleAttribution = () =>
                      setRevealedAttribution((current) => (current === token ? null : token));
                    const wordBox = (
                      <Box
                        component="span"
                        // reveal-delight/03 (AC-01): a tap casts/moves my single
                        // vote once voting is interactive. reveal-delight/04
                        // (AC-02): once voting is not interactive, the same tap
                        // instead toggles this word's "carved by" chip, but only
                        // when a contributor is resolvable. A no-op otherwise.
                        // Kept an inline span (not a <button>) so the story's
                        // text flow is preserved; role/tabIndex/keydown make it
                        // operable either way.
                        onClick={
                          voteInteractive
                            ? () => goldenGuardian?.onVote(token)
                            : attributionInteractive
                              ? toggleAttribution
                              : undefined
                        }
                        role={voteInteractive || attributionInteractive ? 'button' : undefined}
                        tabIndex={voteInteractive || attributionInteractive ? 0 : undefined}
                        aria-pressed={
                          voteInteractive
                            ? isMyVote
                            : attributionInteractive
                              ? attributionRevealed
                              : undefined
                        }
                        aria-label={
                          voteInteractive
                            ? `Vote for "${part.word}" as the funniest word`
                            : attributionInteractive && contributor
                              ? `Show who carved "${part.word}"`
                              : undefined
                        }
                        onKeyDown={
                          voteInteractive
                            ? (event) => {
                                if (event.key === 'Enter' || event.key === ' ') {
                                  event.preventDefault();
                                  goldenGuardian?.onVote(token);
                                }
                              }
                            : attributionInteractive
                              ? (event) => {
                                  if (event.key === 'Enter' || event.key === ' ') {
                                    event.preventDefault();
                                    toggleAttribution();
                                  }
                                }
                              : undefined
                        }
                        sx={{
                          // AC-02: coral COLOR comes from the theme token (never a
                          // hardcoded hex); the weight/underline emphasis is
                          // content-level styling applied via sx, per the coral
                          // reconciliation note.
                          color: theme.palette.coral.main,
                          fontWeight: 800,
                          borderBottom: `2px solid ${alpha(theme.palette.coral.main, 0.4)}`,
                          // Carving entrance (reveal-delight/02, AC-01/AC-02):
                          // a pure CSS `transform: scale` keyframe, staggered by
                          // body order. Never blocks interactivity elsewhere on
                          // the screen (AC-04) - it only animates this word span.
                          // `transform` does NOT apply to non-replaced INLINE
                          // boxes (CSS Transforms Level 1), so the word span must
                          // be inline-block for the scale to take effect - without
                          // this the carve is a silent no-op (Gate-1 CR-001). The
                          // coral color/weight/underline are unchanged and a word
                          // is a single token, so wrapping and the final rendered
                          // frame stay identical (AC-06).
                          display: 'inline-block',
                          animation: `${carveIn} 0.4s ease-out ${delayMs}ms both`,
                          '@media (prefers-reduced-motion: reduce)': {
                            animation: 'none',
                          },
                          // reveal-delight/03 (AC-01): tappable-word affordance while
                          // voting - ADDITIVE only (coral treatment above unchanged).
                          // `outline` marks my pick without shifting the inline text.
                          ...(voteInteractive && {
                            cursor: 'pointer',
                            borderRadius: '8px',
                            px: 0.5,
                            bgcolor: isMyVote ? alpha(theme.palette.gold.main, 0.22) : 'transparent',
                            outline: isMyVote ? `2px solid ${theme.palette.gold.main}` : 'none',
                            outlineOffset: '1px',
                          }),
                          // reveal-delight/03 (AC-03): the single winner gets a GOLD
                          // ring/glow + a gentle transform-only pop. Never a loser
                          // callout - only this one word is ever styled.
                          ...(isWinner && {
                            borderRadius: '8px',
                            px: 0.5,
                            bgcolor: alpha(theme.palette.gold.main, 0.2),
                            boxShadow: `0 0 0 3px ${theme.palette.gold.main}, 0 0 14px ${alpha(theme.palette.gold.main, 0.7)}`,
                            animation: `${winnerPop} 0.5s ease-out both`,
                            // The winner's animation override re-adds motion on top of the
                            // carve-in's reduced-motion guard above, so re-assert the guard
                            // here (emitted last, it wins): a reduced-motion player gets the
                            // static gold ring with no pop (Copilot review on #112).
                            '@media (prefers-reduced-motion: reduce)': {
                              animation: 'none',
                            },
                          }),
                          // reveal-delight/04 (AC-02): a subtle tappable cue for
                          // attribution, ADDITIVE only - never overrides the coral
                          // color/weight/underline above, and never fires while
                          // the vote tap (styled separately above) owns this word.
                          ...(attributionInteractive &&
                            !isWinner && {
                              cursor: 'pointer',
                            }),
                        }}
                      >
                        {part.word}
                      </Box>
                    );

                    return (
                      <Fragment key={`p-${index}`}>
                        {wordBox}
                        {/* reveal-delight/04 (AC-01/AC-02): the "carved by
                            [nickname]" chip, tap-revealed per word - never
                            forced inline by default (that would drown the
                            coral contrast this reveal depends on). Sits right
                            after the word so it reads inline with the story
                            text; a scale-only pop, never opacity keyframes
                            (this feature's shared footgun). */}
                        {attributionRevealed && contributor && (
                          <Box
                            component="span"
                            sx={{
                              display: 'inline-flex',
                              alignItems: 'center',
                              gap: 0.5,
                              ml: 0.5,
                              px: 1,
                              py: 0.25,
                              borderRadius: 999,
                              bgcolor: alpha(theme.palette.primary.main, 0.12),
                              color: theme.palette.primary.dark,
                              fontFamily: '"Nunito", sans-serif',
                              fontWeight: 800,
                              fontSize: 11.5,
                              verticalAlign: 'middle',
                              transformOrigin: 'left center',
                              animation: `${attributionPop} 0.2s ease-out both`,
                              '@media (prefers-reduced-motion: reduce)': {
                                animation: 'none',
                              },
                            }}
                          >
                            <Guardian variant={contributor.variant} size={16} />
                            carved by {contributor.nickname}
                          </Box>
                        )}
                      </Fragment>
                    );
                  });
                })()}
              </Typography>
            ) : (
              revealPresentation
            )}
          </Box>
        </Box>

        {/* Golden Guardian funniest-word vote status (reveal-delight/03). Sits
            below the story panel: a low-key "tap the funniest word" prompt +
            "N of M voted" status while voting (AC-02), and a warm, singular winner
            announcement once resolved (AC-03) - NEVER a ranked list or a loser
            callout. Rendered only when the caller opts in (absent in solo, AC-06).
            The gold ring on the winning WORD itself lives in the story body above. */}
        {goldenGuardian && goldenGuardian.phase !== 'off' && (
          <Box sx={{ mt: 3, textAlign: 'center' }}>
            {voteResolved ? (
              <Stack alignItems="center" spacing={1}>
                <Box
                  component="span"
                  sx={{
                    display: 'inline-flex',
                    alignItems: 'center',
                    gap: 1,
                    px: 2,
                    py: 1,
                    borderRadius: 999,
                    bgcolor: alpha(theme.palette.gold.main, 0.18),
                    color: theme.palette.gold.dark,
                    fontFamily: '"Nunito", sans-serif',
                    fontWeight: 800,
                    fontSize: 13.5,
                  }}
                >
                  <FontAwesomeIcon icon="crown" style={{ width: 16, height: 16 }} />
                  {winningWord
                    ? 'Crowned the funniest word this round'
                    : 'No favorite picked this round'}
                </Box>
                {winningWord && (
                  <Typography
                    sx={{
                      fontFamily: '"Fredoka", sans-serif',
                      fontWeight: 700,
                      fontSize: 18,
                      color: 'primary.main',
                    }}
                  >
                    The funniest word this round: "{winningWord}"
                  </Typography>
                )}
              </Stack>
            ) : (
              <Stack alignItems="center" spacing={1.25}>
                <Stack direction="row" alignItems="center" spacing={1}>
                  <Box sx={{ color: 'gold.main', display: 'flex' }}>
                    <FontAwesomeIcon icon="hand-pointer" style={{ width: 16, height: 16 }} />
                  </Box>
                  <Typography
                    sx={{
                      fontFamily: '"Fredoka", sans-serif',
                      fontWeight: 600,
                      fontSize: 16,
                      color: 'text.primary',
                    }}
                  >
                    {carveComplete
                      ? 'Tap the funniest word'
                      : 'The tale is still carving in...'}
                  </Typography>
                </Stack>
                <Stack direction="row" alignItems="center" spacing={0.75}>
                  <Box sx={{ color: 'teal.main', display: 'flex' }}>
                    <FontAwesomeIcon icon="circle-check" style={{ width: 14, height: 14 }} />
                  </Box>
                  <Typography
                    sx={{
                      fontFamily: '"Nunito", sans-serif',
                      fontWeight: 700,
                      fontSize: 12.5,
                      color: 'text.secondary',
                    }}
                  >
                    {goldenGuardian.votedCount} of {goldenGuardian.totalVoters} voted
                  </Typography>
                </Stack>
                {/* Host-only low-pressure "Reveal the winner" affordance (AC-03) -
                    mirrors the Waiting screen's "no rush, but the host can move
                    things along" posture. Only rendered when the caller (host)
                    supplies onCloseVoting; never shown to non-hosts. */}
                {goldenGuardian.onCloseVoting && (
                  <Link
                    component="button"
                    type="button"
                    onClick={goldenGuardian.onCloseVoting}
                    underline="none"
                    sx={{
                      fontFamily: '"Nunito", sans-serif',
                      fontWeight: 800,
                      fontSize: 13,
                      color: 'gold.dark',
                    }}
                  >
                    Reveal the winner
                  </Link>
                )}
              </Stack>
            )}
          </Box>
        )}

        {/* Quiet per-tale curation vote (story-selection/05, AC-01): sits below
            the story panel, visually subordinate to the CTAs in the pinned bar
            below. Omitted entirely when the caller does not opt in (group
            play's transient reveal - see the taleFeedback prop doc). */}
        {taleFeedback && (
          <TaleFeedback templateId={taleFeedback.templateId} mode={taleFeedback.mode} />
        )}
      </Stack>

      {/* Bottom cluster (the UX de-clutter): an IN-FLOW footer, NOT the absolute
          BottomActionBar. In a fixed-height flex column an absolutely-positioned
          bar sits OVER the flex:1 story region (which grows to the full height),
          so the reactions painted on top of the story and a single-button spacer
          could never reserve this tall cluster's real height. As a normal
          flexShrink:0 child the story region shrinks to fit above it and nothing
          overlaps. Order top -> bottom: the reactions strip, the gold "Play
          another round" CTA, then a compact 3-pill row (Share / Save image /
          Home) matching the design mock - not a huge full-width Share button. */}
      <Box
        sx={{
          flexShrink: 0,
          display: 'flex',
          flexDirection: 'column',
          gap: 1.25,
          px: 5,
          pt: 1.5,
          pb: 'calc(14px + env(safe-area-inset-bottom, 0px))',
          borderTop: `1px solid ${alpha(theme.palette.stoneEdge.main, 0.16)}`,
          // In landscape the whole page scrolls (see the root override), so this
          // footer just flows after the story rather than pinning.
        }}
      >
        {/* Reactions strip (reveal-delight/01; reactions v2): a small uppercase
            "WHAT DID YOU THINK?" label above the caller-supplied reaction row, in
            normal flow BELOW the story card so it never overlaps the tale (the bug
            the de-clutter fixes). Reveal stays room-agnostic - it renders whatever
            node the caller passed. */}
        {reactionRow && (
          <Box>
            <Typography
              sx={{
                textAlign: 'center',
                textTransform: 'uppercase',
                letterSpacing: '0.08em',
                fontFamily: '"Nunito", sans-serif',
                fontWeight: 800,
                fontSize: 11,
                color: 'text.secondary',
                mb: 0.5,
              }}
            >
              What did you think?
            </Typography>
            {reactionRow}
          </Box>
        )}
        <Button
          variant="contained"
          fullWidth
          onClick={onPlayAgain}
          startIcon={<FontAwesomeIcon icon="arrow-rotate-right" style={{ width: 20, height: 20 }} />}
        >
          {playAgainLabel}
        </Button>
        {/* Compact secondary-action row (design mock): three equal purple pills -
            Share the tale, Save as image, and Home - instead of a full-width
            outlined Share button + stacked links. Each is a big-enough tap target
            (44px) but visually subordinate to the gold CTA above. The Home pill
            uses onHome (falling back to exitAction) and is omitted when neither is
            supplied. Share/Save keep their in-flight disabled + label-swap
            behavior (keepsake-gallery/01-02). */}
        <Stack direction="row" spacing={1.25}>
          {(() => {
            const homeHandler = onHome ?? exitAction?.onClick;
            const pills: {
              key: string;
              icon: IconProp;
              label: string;
              onClick: () => void;
              disabled: boolean;
              busy: boolean;
            }[] = [
              {
                key: 'share',
                icon: 'share-nodes',
                label: sharingImage ? 'Sharing...' : 'Share',
                onClick: handleShare,
                disabled: sharingImage,
                busy: sharingImage,
              },
              {
                key: 'save',
                icon: 'image',
                label: savingImage ? 'Saving...' : 'Save image',
                onClick: handleSaveImage,
                disabled: savingImage,
                busy: savingImage,
              },
              ...(homeHandler
                ? [
                    {
                      key: 'home',
                      icon: 'house' as IconProp,
                      label: 'Home',
                      onClick: homeHandler,
                      disabled: false,
                      busy: false,
                    },
                  ]
                : []),
            ];
            return pills.map((pill) => (
              <Box
                key={pill.key}
                component="button"
                type="button"
                onClick={pill.onClick}
                disabled={pill.disabled}
                aria-busy={pill.busy}
                sx={{
                  flex: 1,
                  minWidth: 0,
                  minHeight: 44,
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'center',
                  gap: 1,
                  px: 1.5,
                  border: 'none',
                  borderRadius: '14px',
                  cursor: pill.disabled ? 'default' : 'pointer',
                  bgcolor: alpha(theme.palette.primary.main, 0.09),
                  color: theme.palette.primary.main,
                  fontFamily: '"Nunito", sans-serif',
                  fontWeight: 800,
                  fontSize: 13.5,
                  opacity: pill.disabled ? 0.6 : 1,
                  whiteSpace: 'nowrap',
                  transition: 'background-color 120ms ease-out, transform 120ms ease-out',
                  '&:hover': { bgcolor: alpha(theme.palette.primary.main, pill.disabled ? 0.09 : 0.16) },
                  '&:active': { transform: pill.disabled ? 'none' : 'scale(0.96)' },
                  '&:focus-visible': { outline: `2px solid ${theme.palette.primary.main}`, outlineOffset: 2 },
                }}
              >
                <FontAwesomeIcon icon={pill.icon} style={{ width: 15, height: 15 }} />
                {pill.label}
              </Box>
            ));
          })()}
        </Stack>
        {/* "Remix a word" (replay-remix/02, AC-01): a LOW-EMPHASIS secondary action -
            plain secondary-text weight, deliberately quieter than even the purple
            pills above it, so it never competes with the gold "Play another round"
            CTA. Opens the blank picker overlay on tap; omitted entirely when the
            caller has not wired `remix`. */}
        {remix && (
          <Box sx={{ textAlign: 'center' }}>
            <Link
              component="button"
              type="button"
              onClick={() => setRemixStep('picker')}
              underline="none"
              sx={{
                display: 'inline-flex',
                alignItems: 'center',
                gap: 1,
                fontFamily: '"Nunito", sans-serif',
                fontWeight: 700,
                fontSize: 13,
                color: 'text.secondary',
              }}
            >
              <FontAwesomeIcon icon="shuffle" style={{ width: 13, height: 13 }} />
              Remix a word
            </Link>
          </Box>
        )}
        {/* Share a public link (keepsake-gallery/04, AC-01/AC-03/AC-07): a low-key,
            HOST-ONLY, OPT-IN affordance - only rendered when the caller supplies
            `publicShare` (host in group play), so publishing is never automatic.
            On tap it publishes the tale and shares the returned `/t/<slug>` link.
            Once a link exists, a quiet "Stop sharing this link" revokes it (AC-07). */}
        {publicShare && (
          <Box sx={{ textAlign: 'center' }}>
            <Link
              component="button"
              type="button"
              onClick={handleShareLink}
              disabled={publishing || sharingImage}
              aria-busy={publishing}
              underline="none"
              sx={{
                display: 'inline-flex',
                alignItems: 'center',
                gap: 1,
                fontFamily: '"Nunito", sans-serif',
                fontWeight: 700,
                fontSize: 13.5,
                color: 'text.secondary',
                opacity: publishing ? 0.6 : 1,
                cursor: publishing ? 'default' : 'pointer',
              }}
            >
              <FontAwesomeIcon icon="link" style={{ width: 14, height: 14 }} />
              {publishing ? 'Preparing link...' : publicLink ? 'Share the public link again' : 'Share a public link'}
            </Link>
            {publicLink && !publishing && (
              <Box sx={{ mt: 0.5 }}>
                <Link
                  component="button"
                  type="button"
                  onClick={handleRevokeLink}
                  underline="none"
                  sx={{
                    fontFamily: '"Nunito", sans-serif',
                    fontWeight: 700,
                    fontSize: 12.5,
                    color: 'coral.main',
                  }}
                >
                  Stop sharing this link
                </Link>
              </Box>
            )}
          </Box>
        )}
      </Box>
    </Box>
  );
}
