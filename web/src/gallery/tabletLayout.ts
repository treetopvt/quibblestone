// ----------------------------------------------------------------------------
//  tabletLayout.ts - PURE word-wrap layout for the keepsake-gallery stone-
//  tablet image (keepsake-gallery/01, AC-02).
//
//  Why this exists as its own module, separate from renderTablet.ts: canvas
//  text measurement/drawing cannot run under Vitest's `node` environment (no
//  DOM, no CanvasRenderingContext2D), so any layout math worth unit-testing
//  has to be extracted into a function that takes measurement as an injected
//  dependency rather than reaching for a real canvas context itself. This
//  module does exactly that: it turns `buildRevealParts()` output (the SAME
//  pure text/coral-word interleaving the live Reveal screen renders from, see
//  ../pages/revealParts.ts) into wrapped lines of positioned segments, given
//  any `measure` function that reports a token's rendered width. renderTablet.ts
//  supplies a REAL measurer backed by `CanvasRenderingContext2D.measureText`;
//  tabletLayout.test.ts supplies a FAKE one (e.g. character count) so the
//  wrap-at-max-width behavior is testable without a browser.
//
//  This module is plain-text layout only - it does not know about colors,
//  fonts-as-CSS, canvas, or the theme. renderTablet.ts is the only place that
//  turns a `coral` flag into an actual paint color (theme.palette.coral.main).
//
//  Child safety: this module never sees free text before it has been vetted -
//  it only re-shapes `RevealPart[]`, which `buildRevealParts()` already built
//  from an already-assembled, already-filtered `AssembledStory` (AC-04). It
//  introduces no new text and performs no filtering itself.
// ----------------------------------------------------------------------------

import type { RevealPart } from '../pages/revealParts';

/** One wrapped-line token: a single word (no embedded spaces) plus whether it renders coral. */
export interface TabletLineSegment {
  text: string;
  /** True for a player-filled word (coral highlight); false for literal template text. */
  coral: boolean;
}

/** One wrapped line of the tablet image body: an ordered run of same-line tokens. */
export interface TabletLine {
  segments: TabletLineSegment[];
}

/**
 * Reports the rendered width of a single token, given whether it is a coral
 * (player-filled) word - the caller (renderTablet.ts) uses this to pick the
 * matching font weight before measuring, since the live screen renders coral
 * words bolder than literal text.
 */
export type MeasureTextWidth = (text: string, coral: boolean) => number;

/**
 * Tokenizes `parts` into words (splitting literal text segments on
 * whitespace; a filled word is normally a single token but is defensively
 * split too, in case a free-text answer itself contains spaces, e.g. a
 * "place" blank answered "New York"). Empty-word parts (a skipped blank)
 * contribute nothing, matching the live screen's "renders as a natural gap"
 * treatment (see Reveal.tsx).
 */
function tokenize(parts: readonly RevealPart[]): TabletLineSegment[] {
  const tokens: TabletLineSegment[] = [];
  for (const part of parts) {
    if (part.kind === 'text') {
      for (const word of part.text.split(/\s+/)) {
        if (word.length > 0) tokens.push({ text: word, coral: false });
      }
      continue;
    }
    if (part.word === '') continue;
    for (const word of part.word.split(/\s+/)) {
      if (word.length > 0) tokens.push({ text: word, coral: true });
    }
  }
  return tokens;
}

/**
 * Greedy word-wrap: packs tokens onto a line while `measure` reports the
 * running line width fits within `maxWidth` (a single space is assumed
 * between adjacent tokens on the same line), starting a new line otherwise.
 * A token wider than `maxWidth` on its own still gets its own line rather
 * than being split mid-word (matches ordinary text-wrapping behavior - a very
 * long single word may overflow visually, which is expected and not a bug).
 *
 * Pure and deterministic: same `parts` + same `measure` + same `maxWidth`
 * always produces the same lines.
 */
export function wrapRevealPartsIntoLines(
  parts: readonly RevealPart[],
  measure: MeasureTextWidth,
  maxWidth: number,
): TabletLine[] {
  const tokens = tokenize(parts);
  return packTokensIntoLines(tokens, measure, maxWidth);
}

/**
 * Same greedy wrap as {@link wrapRevealPartsIntoLines}, but for a single
 * plain-text string (the story title, or the "carved by ... & crew" byline) -
 * neither of which carries any coral words. Reuses the same token-packing
 * core so the title/byline and the story body share one tested wrap
 * algorithm rather than two.
 */
export function wrapPlainTextIntoLines(
  plainText: string,
  measure: MeasureTextWidth,
  maxWidth: number,
): TabletLine[] {
  const tokens: TabletLineSegment[] = plainText
    .split(/\s+/)
    .filter((word) => word.length > 0)
    .map((word) => ({ text: word, coral: false }));
  return packTokensIntoLines(tokens, measure, maxWidth);
}

/** Shared greedy line-packing core used by both wrap functions above. */
function packTokensIntoLines(
  tokens: readonly TabletLineSegment[],
  measure: MeasureTextWidth,
  maxWidth: number,
): TabletLine[] {
  const spaceWidth = measure(' ', false);
  const lines: TabletLine[] = [];
  let current: TabletLineSegment[] = [];
  let currentWidth = 0;

  for (const token of tokens) {
    const tokenWidth = measure(token.text, token.coral);
    if (current.length === 0) {
      current = [token];
      currentWidth = tokenWidth;
      continue;
    }
    const widthWithToken = currentWidth + spaceWidth + tokenWidth;
    if (widthWithToken <= maxWidth) {
      current.push(token);
      currentWidth = widthWithToken;
    } else {
      lines.push({ segments: current });
      current = [token];
      currentWidth = tokenWidth;
    }
  }
  if (current.length > 0) lines.push({ segments: current });
  return lines;
}

/** Joins a wrapped line's tokens back into a single string (space-separated) for single-color drawing (title/byline). */
export function lineToPlainText(line: TabletLine): string {
  return line.segments.map((segment) => segment.text).join(' ');
}
