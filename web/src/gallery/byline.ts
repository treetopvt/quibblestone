// ----------------------------------------------------------------------------
//  byline.ts - pure text formatting for the keepsake-gallery image byline
//  (keepsake-gallery/02 "Share the tale with watermark", PART C: wiring
//  RevealProps.saveImageByline for group play).
//
//  This is the ONE place that turns an ordered list of in-session nicknames
//  into the "carved by [names]" prose rendered onto the saved/shared tablet
//  image (renderTablet.ts's `byline` input, via Reveal.tsx's saveImageByline
//  prop) - App.tsx's GroupReveal wrapper is the current caller, reusing the
//  SAME crew list `buildCrew(reveal.words)` already derives for the Round
//  Complete recap (no second data source, no hub call).
//
//  Format: a natural-language list - "Sam" (one), "Sam & Mia" (two), or
//  "Sam, Mia & Bo" (three or more), matching ordinary comma-and-ampersand
//  English list conventions.
//
//  Child safety / no PII (AC-05): this module performs no filtering itself -
//  callers must pass in-session nicknames that already passed the safety
//  filter at join (child-safety/01), never a real name or device id. This is
//  pure prose formatting only.
// ----------------------------------------------------------------------------

/**
 * Joins names into a natural-language list: "Sam" / "Sam & Mia" /
 * "Sam, Mia & Bo". Returns an empty string for an empty list so a caller can
 * decide whether to omit a byline entirely rather than render a bare phrase
 * with no names in it.
 */
export function joinNamesReadably(names: readonly string[]): string {
  if (names.length === 0) return '';
  if (names.length === 1) return names[0];
  if (names.length === 2) return `${names[0]} & ${names[1]}`;
  return `${names.slice(0, -1).join(', ')} & ${names[names.length - 1]}`;
}

/**
 * Formats the crew byline rendered onto the saved/shared tablet image:
 * "carved by [names]" (see {@link joinNamesReadably} for the list format).
 * Returns `undefined` for an empty crew (e.g. a round where every blank went
 * unfilled) so the caller can omit `saveImageByline` entirely rather than
 * pass a bare "carved by " string with no names.
 */
export function formatCrewByline(names: readonly string[]): string | undefined {
  const joined = joinNamesReadably(names);
  return joined === '' ? undefined : `carved by ${joined}`;
}
