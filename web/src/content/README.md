# Authoring QuibbleStone templates

This folder holds the hand-written **seed library** (`seedLibrary.ts`) - the
tiny, hand-curated set of fill-in-the-blank story templates that ship with
Slice 1 (README sections 7, 8: "a tiny hand-written library"). This doc is for
anyone (engineer or not) who wants to write a new template by hand. No
special tooling is required - you edit a data file in a text editor and
that's it.

There is **no AI generation, no content-authoring UI, and no back-office
content factory** in this story. Those are deliberately parked for later
(README section 12). Today, a template is plain TypeScript data.

## The shape of a Template

A `Template` (defined in `../engine/template.ts` - read it, this doc does not
redefine the schema, just shows you how to use it) has:

- `id` - a stable, unique, kebab-case id, e.g. `"wobbly-wizard"`.
- `title` - the story's title/subject, e.g. `"The Wobbly Wizard & the Golden Sock"`.
- `body` - an ORDERED array mixing two constructors imported from
  `../engine/template`:
  - `text("some literal text")` - a run of story text, shown as-is.
  - `blank({ ... })` - a typed blank a player fills in.
- `tags` - theme and age-appropriateness metadata (see below).
- `wordBank` (optional) - suggested words for word-bank modes (see below).

Read the body top to bottom and you see exactly what the finished story looks
like - that is the point of using `text()` / `blank()` instead of a
templating mini-language: no hidden copy-generation step to trace.

### Writing a blank

Every `blank({...})` needs ALL of these fields:

```ts
blank({
  id: 'b1',                          // unique WITHIN this template, e.g. "b1", "b2"
  category: 'adjective',             // see the BlankCategory union below
  categoryLabel: 'ADJECTIVE',        // the chip label shown on the prompt card
  prompt: 'Give me a silly describing word',   // the friendly prompt sentence
  subHint: 'Something that describes a thing - anything goes!', // helper text under the prompt
  sparkWords: ['squishy', 'gigantic', 'sparkly'], // EXACTLY 3 example words - no more, no fewer
})
```

`category` must be one of the values in `BlankCategory`
(`web/src/engine/template.ts`): `adjective`, `noun`, `plural-noun`, `verb`,
`name`, `place`, `exclamation`, `number`. If you need a kind of word that
isn't on this list, that's a schema change - flag it, don't invent a new
string value here.

### How long should a story be?

Aim for **9-10 blanks spread across 4-6 sentences**. This matters for
multiplayer: group play deals blanks round-robin across the roster
(`web/src/engine/distribute.ts`), so a story only gives every player a turn
when it has at least as many blanks as there are players - and it only gives
everyone SEVERAL turns when it has 2-3x that. A 4-blank story leaves half of
a 6-player room with nothing to do. Longer prose between the blanks also
makes the reveal read like an actual story instead of a caption. Don't go
past ~12 blanks though: a solo player fills every blank alone, and that
starts to feel like homework.

If two blanks sit near each other, give the later one a prompt that
distinguishes it ("Give me ANOTHER describing word") and a `subHint` that
anchors it in the story ("Something that describes the troll") - players
answer blind, but a contextual sub-hint still improves the reveal without
spoiling it.

**`sparkWords` is a strict 3-tuple.** TypeScript will refuse to compile if you
give 2 or 4 words - this is intentional (it keeps the prompt card layout
consistent). Pick 3 fun, family-safe example words that show a stuck player
what kind of answer fits.

## The family-safe bar (read this before writing jokes)

Every template in `seedLibrary.ts` MUST have:

```ts
tags: { familySafe: true, ageRating: 'all-ages', themes: [...] }
```

`themes` is free-form discovery tags (e.g. `['animals', 'space']`) - it does
NOT affect safety gating, only `familySafe` / `ageRating` do.

The bar for what you write here is **the same bar a player's submitted word
has to pass at play time** (the runtime profanity/safety filter is a separate
feature, `child-safety`, that checks player-submitted free text - it does not
review this file, so nothing you write here gets a second chance to be
caught). Concretely:

- **Passes:** silly, absurd, cartoon-silly content. Talking llamas, a sock-eating
  dragon, a snowman with a summer job, a robot that short-circuits confetti.
  The humor comes from absurdity, not edge.
- **Does not pass:** innuendo, slurs, violence beyond cartoon-silly (no real
  injury, no weapons used on people/animals), anything that assumes an adult
  audience, anything that would make a parent wince reading it aloud to a
  7-year-old.

If you are unsure whether a word or scenario clears the bar, it doesn't - pick
something else. The goal is "my family is laughing in the car," not edgy.

## Optional: a wordBank

Some templates can carry an optional `wordBank` - a flat list of suggested
words tagged by category, used by word-bank play modes (where players pick
from a list instead of typing free text):

```ts
wordBank: [
  { category: 'adjective', word: 'squishy' },
  { category: 'noun', word: 'sock' },
],
```

Most templates should OMIT `wordBank` entirely (free-text is Slice 1's only
mode - README section 7) - only add one if you want to exercise word-bank
play ahead of that mode landing. A template without a `wordBank` is just as
valid as one with it (it's `?:` - optional - in the schema).

## Adding a new template (the one documented step, AC-04)

**Adding a template is a pure data change: append one new object literal to
the `seedLibrary` array in `seedLibrary.ts`. No other code changes anywhere
in the app are required.** The array is exported and imported wherever the
app needs the library (currently the engine layer; later this same shape can
move behind the API / Table Storage without changing how callers use it).

Steps:

1. Open `web/src/content/seedLibrary.ts`.
2. Copy the starter template below, paste it as a new entry in the
   `seedLibrary` array (comma-separated, like its neighbors).
3. Give it a unique `id`, write your story in `text()` / `blank()` segments,
   fill in all blank fields (don't forget exactly 3 `sparkWords` per blank),
   and keep `tags.familySafe: true` / `tags.ageRating: 'all-ages'`.
4. Run `npm run test:unit` from `web/` - the validation test in
   `seedLibrary.test.ts` checks your new template assembles cleanly and meets
   the safety/shape bar.

### Starter template (copy, paste, edit)

```ts
{
  id: 'your-template-id',
  title: 'Your Funny Title Here',
  tags: { familySafe: true, ageRating: 'all-ages', themes: ['theme-one', 'theme-two'] },
  body: [
    text('Once there was a '),
    blank({
      id: 'b1',
      category: 'adjective',
      categoryLabel: 'ADJECTIVE',
      prompt: 'Give me a silly describing word',
      subHint: 'Something that describes a thing - anything goes!',
      sparkWords: ['squishy', 'gigantic', 'sparkly'],
    }),
    text(' who loved to '),
    blank({
      id: 'b2',
      category: 'verb',
      categoryLabel: 'VERB',
      prompt: 'Give me an action word',
      subHint: 'Something you DO.',
      sparkWords: ['dance', 'wiggle', 'snore'],
    }),
    text(' every single day.'),
  ],
  // wordBank is optional - omit it for a free-text-only template.
},
```

## Validating your work

`seedLibrary.test.ts` (co-located in this folder) runs under Vitest
(`npm run test:unit` from `web/`) and checks:

- The library has 10 to 15 templates.
- Every template is `familySafe: true` / `ageRating: 'all-ages'`.
- Every template `id` is unique.
- Every blank has exactly 3 `sparkWords`, and non-empty `prompt` / `subHint` /
  `categoryLabel`.
- Every template `assemble()`s without throwing, and the assembled story
  contains the words you fed it (this exercises the real engine assembler -
  `../engine/assemble.ts` - against your data, not a mock).

If your new template fails one of these, the test output tells you which
field is missing or malformed.
