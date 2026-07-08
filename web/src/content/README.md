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

Most stories should aim for **9-10 blanks spread across 4-6 sentences** (a
"full" story - see quick vs full below). This matters for multiplayer: group
play deals blanks round-robin across the roster
(`web/src/engine/distribute.ts`), so a story only gives every player a turn
when it has at least as many blanks as there are players - and it only gives
everyone SEVERAL turns when it has 2-3x that. Longer prose between the blanks
also makes the reveal read like an actual story instead of a caption. Don't go
past ~12 blanks though: a solo player fills every blank alone, and that
starts to feel like homework.

### Quick vs full (story length classes)

The selection pipeline (`web/src/content/length.ts`) sorts every template into
one of two **length classes**, and the class is **derived from the blank count,
never authored** - there is no length tag on a template:

- **quick** - **6 blanks or fewer** (`QUICK_MAX_BLANKS`). A short story a solo
  player or a tiny group finishes fast. Aim for **4-6 blanks**.
- **full** - **7 or more blanks**. The longer stories above; aim for **8+**.

So when you write a NEW template, land it clearly in one class: a quick story
at 4-6 blanks, or a full story at 8+. Avoid the 7-blank borderline unless you
have a reason. The seed library ships at least four quick stories (see the
`QUICK stories` block at the bottom of `seedLibrary.ts`) alongside the full
ones; both clear the exact same family-safe / all-ages / exactly-3-spark-words
bar. If you add or resize a quick story, update its `BlankCount` in the server
mirror `api/src/Content/TemplateCatalog.cs` too.

If two blanks sit near each other, give the later one a prompt that
distinguishes it ("Give me ANOTHER describing word") and a `subHint` that
anchors it in the story ("Something that describes the troll") - players
answer blind, but a contextual sub-hint still improves the reveal without
spoiling it.

**`sparkWords` is a strict 3-tuple.** TypeScript will refuse to compile if you
give 2 or 4 words - this is intentional (it keeps the prompt card layout
consistent). Pick 3 fun, family-safe example words that show a stuck player
what kind of answer fits.

## Content tiers and the safety bar (read this before writing jokes)

The library ships in **two tiers**, and the ONE thing that separates them is
`tags.familySafe`. The family-safe toggle (`../content/familySafe.ts`, and the
server's `FamilySafeContentSelector`) reads only that flag: toggle **on** offers
just the family-safe set; toggle **off** offers everything. Keep `ageRating`
consistent with the flag so the metadata never lies (`themes` is free-form
discovery tags and does not affect gating).

### Family-safe tier (the default)

```ts
tags: { familySafe: true, ageRating: 'all-ages', themes: [...] }
```

The bar here is **the same bar a player's submitted word has to pass at play
time** (the runtime profanity/safety filter is a separate feature,
`child-safety`, that checks player-submitted free text - it does not review this
file, so nothing you write here gets a second chance to be caught). Concretely:

- **Passes:** silly, absurd, cartoon-silly content. Talking llamas, a sock-eating
  dragon, a snowman with a summer job, a robot that short-circuits confetti.
  The humor comes from absurdity, not edge.
- **Does not pass:** innuendo, slurs, violence beyond cartoon-silly (no real
  injury, no weapons used on people/animals), anything that assumes an adult
  audience, anything that would make a parent wince reading it aloud to a
  7-year-old.

If you are unsure whether a word or scenario clears the bar, it doesn't - pick
something else. The goal is "my family is laughing in the car," not edgy.

### Non-family-safe tier ("toggle off" / adult)

```ts
tags: { familySafe: false, ageRating: 'teen-plus', themes: [...] }
```

These live at the END of the `seedLibrary` array and appear ONLY when a host
turns the family-safe toggle off. Register is **cheeky / grown-up** - dating,
nightlife, office life, parties, hangovers, adulting - built on innuendo and
adult THEMES, not shock. The comedy is in the setup and in the spicy answers a
player can now supply; keep the authored prose clever. Still-firm limits, even
here:

- **Passes:** suggestive humor, mild language (`hell`, `damn`, `crap`), drunken
  chaos, awkward romance, workplace misery, morning-after regret.
- **Does not pass:** explicit or graphic sexual content, slurs, hate or
  demeaning content about any group, anything sexual involving minors, graphic
  violence, real-person targeting, or how-to for illegal/dangerous activity.

This tier is NOT a licence to be gross - it is a party-game register for adults.
When in doubt, dial it back.

> **Keep the server mirror in sync.** Every template - either tier - must be
> mirrored into `api/src/Content/TemplateCatalog.cs` by `{ Id, FamilySafe,
> BlankCount }` (non-family-safe entries with `FamilySafe: false`), or group play
> will pick a template the client cannot resolve, or the gate will mis-filter it.

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
   fill in all blank fields (don't forget exactly 3 `sparkWords` per blank), and
   pick a tier: `tags.familySafe: true` / `ageRating: 'all-ages'` for the default
   set, or `tags.familySafe: false` / `ageRating: 'teen-plus'` for the "toggle
   off" adult set (drop non-family-safe entries in the adult block at the end of
   the array).
4. Mirror the new entry into `api/src/Content/TemplateCatalog.cs`
   (`{ Id, FamilySafe, BlankCount }`) - the server catalog is hand-synced.
5. Run `npm run test:unit` from `web/` - the validation test in
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

- The library size is within the expected range, and carries both a family-safe
  set and a non-family-safe (toggle-off) set.
- Every template's `familySafe` flag is consistent with its `ageRating`
  (family-safe => `all-ages`, non-family-safe => `teen-plus`).
- The family-safe gate over the real library never surfaces a non-family-safe
  template when the toggle is on (see `familySafe.test.ts`).
- Every template `id` is unique.
- Every blank has exactly 3 `sparkWords`, and non-empty `prompt` / `subHint` /
  `categoryLabel`.
- Every template `assemble()`s without throwing, and the assembled story
  contains the words you fed it (this exercises the real engine assembler -
  `../engine/assemble.ts` - against your data, not a mock).

If your new template fails one of these, the test output tells you which
field is missing or malformed.
