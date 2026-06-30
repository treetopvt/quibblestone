// ----------------------------------------------------------------------------
//  template.ts - the mode-agnostic story template schema.
//
//  This is the PURE-TS heart of "one engine, many thin modes" (README section
//  4 / CLAUDE.md section 2): a Template describes a title/subject and an
//  ordered body of typed Blanks. It carries NO opinions about how a player
//  sees it, how they answer, or when the reveal happens - those are the three
//  axes that a *mode* layers on top, never something the template itself
//  encodes (AC-05). Free text / word-bank decisions, progressive vs. blind
//  reveal, single vs. group play - none of that lives here.
//
//  Why pure: this module has zero UI, zero SignalR, zero fetch - just types
//  and (in assemble.ts) one pure function. That keeps it trivially unit
//  testable (see assemble.test.ts) and safe to import from anywhere: game
//  modes, the-reveal, single-player, group-play, and eventually the API's
//  template authoring tooling, all share this one shape.
//
//  Authoring format: every field on a Blank (category, prompt, subHint,
//  sparkWords) is a literal property of the blank definition, NOT something
//  derived at runtime from the category. That keeps templates hand-authorable
//  as plain data (e.g. JSON or TS object literals) without a copy-generation
//  step. AI-generated templates and dynamic spark chips are explicitly out of
//  scope for this story (see docs/features/template-model/01-template-schema.md).
//
//  Who imports this: game-modes (decides see/answer/reveal), single-player,
//  group-play (host template list, reads `tags`), the-reveal (reads body +
//  assembled output), child-safety (reads `tags.ageRating` / `tags.familySafe`
//  to gate which templates the family-safe toggle allows).
// ----------------------------------------------------------------------------

/**
 * The kind of word a blank expects. Deliberately a small, EXTENSIBLE union -
 * add new categories here as new prompt copy is authored. Keep values
 * lowercase-kebab so they can double as stable identifiers (e.g. CSS classes,
 * analytics event props) without remapping.
 */
export type BlankCategory =
  | 'adjective'
  | 'noun'
  | 'plural-noun'
  | 'verb'
  | 'name'
  | 'place'
  | 'exclamation'
  | 'number';

/**
 * Exactly 3 hardcoded "spark" example words shown as tappable chips on the
 * FillBlank prompt card (docs/design/README.md, Screens, screen 4) to help a
 * stuck player. Modeled as a 3-tuple (not a generic array) so the schema
 * itself enforces "short list of exactly 3" (AC-02) - any author who omits or
 * adds a 4th example word fails to type-check.
 */
export type SparkWords = readonly [string, string, string];

/**
 * One ordered, typed blank inside a template's body. Every field here is
 * authored data (AC-02) - none of it is computed from `category` at runtime,
 * so a new engineer can read a template literal and see exactly what will
 * render on the prompt card, with no hidden copy-generation logic to trace.
 */
export interface Blank {
  /** Stable id within the template, e.g. "blank-1". Used for attribution. */
  id: string;
  /** What kind of word this blank wants (drives nothing by itself - see prompt/category below for the actual copy). */
  category: BlankCategory;
  /**
   * The purple category chip's label as it renders on the FillBlank screen,
   * e.g. "ADJECTIVE". Authored separately from `category` so copy can differ
   * from the raw union value (capitalization, synonyms) without touching the
   * type system.
   */
  categoryLabel: string;
  /** Human-facing prompt sentence, e.g. "Give me a silly describing word". */
  prompt: string;
  /** Sub-hint shown under the prompt, e.g. "Something that describes a thing - anything goes!". */
  subHint: string;
  /** Exactly 3 example words shown as tappable spark chips, e.g. ["squishy", "gigantic", "sparkly"]. */
  sparkWords: SparkWords;
}

/**
 * Theme and age-appropriateness tags a template carries (AC-04). Kept as a
 * small flat shape (not a free-form string array) so the family-safe gate
 * (child-safety/02) and content discovery (group-play/01) can query it
 * cleanly without parsing prose:
 *   - `familySafe`: true means safe for the family-safe toggle to surface it.
 *   - `ageRating`: a coarse, queryable signal a future gate can filter on.
 *   - `themes`: free-form discovery tags (e.g. "animals", "road-trip");
 *     NOT used for safety gating - only `familySafe` / `ageRating` are.
 */
export interface TemplateTags {
  /** Whether this template is appropriate for the family-safe toggle (child-safety/02 reads this). */
  familySafe: boolean;
  /** Coarse age-appropriateness signal; "all-ages" is the default, safest choice for hand-authored Slice 1 content. */
  ageRating: 'all-ages' | 'kids' | 'teen-plus';
  /** Free-form theme/discovery tags, e.g. ["animals", "space", "road-trip"]. Not used for safety gating. */
  themes: readonly string[];
}

/**
 * A suggested word for word-bank modes. A template's `wordBank` is OPTIONAL
 * (AC-03) - templates without one remain fully valid for free-text modes
 * (e.g. Classic blind, Slice 1's only mode). Word-bank entries are tagged
 * with the blank `category` they suit, since a single bank may serve several
 * blanks of the same category in a body.
 */
export interface WordBankEntry {
  /** The blank category this word suits, e.g. "adjective". */
  category: BlankCategory;
  /** The suggested word itself. */
  word: string;
}

/**
 * A story template: title/subject plus an ORDERED body of typed blanks
 * (AC-01). Mode-agnostic by construction (AC-05) - nothing here says how a
 * player sees the body, how they answer, or when blanks are revealed. A mode
 * (game-modes feature) interprets this same shape three different ways
 * without ever needing to clone or mutate it.
 *
 * `body` is the ordered list of segments that make up the story: literal text
 * interleaved with blanks, in the exact order they appear in the finished
 * story. This is what assemble() (assemble.ts) walks to deterministically
 * produce the final text (AC-06).
 */
export interface Template {
  /** Stable id for this template, e.g. "wobbly-wizard". */
  id: string;
  /** The story's title/subject, e.g. "The Wobbly Wizard & the Golden Sock". */
  title: string;
  /** Ordered body segments: literal text and typed blanks, interleaved in story order. */
  body: readonly TemplateSegment[];
  /** Theme + age-appropriateness tags (AC-04). */
  tags: TemplateTags;
  /** Optional suggested word list for word-bank modes (AC-03). Absent for free-text-only templates. */
  wordBank?: readonly WordBankEntry[];
}

/** A literal run of story text, rendered as-is (no blank to fill). */
export interface TextSegment {
  type: 'text';
  text: string;
}

/** A typed blank to be filled by a player's word, in its place in the body order. */
export interface BlankSegment {
  type: 'blank';
  blank: Blank;
}

/** One ordered element of a template's body: either literal text or a blank. */
export type TemplateSegment = TextSegment | BlankSegment;

/** Convenience constructor for a literal text segment, used when hand-authoring templates. */
export function text(value: string): TextSegment {
  return { type: 'text', text: value };
}

/** Convenience constructor for a blank segment, used when hand-authoring templates. */
export function blank(definition: Blank): BlankSegment {
  return { type: 'blank', blank: definition };
}

/** Returns the ordered list of Blanks in a template's body (skips text segments). */
export function getBlanks(template: Template): Blank[] {
  return template.body
    .filter((segment): segment is BlankSegment => segment.type === 'blank')
    .map((segment) => segment.blank);
}
