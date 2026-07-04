<!--
  Website copy describing QuibbleStone for the company site. Mined from the README
  charter, docs/ROADMAP.md, and the docs/features/ backlog (shipped + upcoming).
  House prose style: hyphens, colons, or parentheses - never em dashes.
  Note for whoever publishes this: "QuibbleStone" is still a working title pending
  the brand-clearance gate (see docs/launch-readiness/brand-clearance-checklist.md).
-->

# QuibbleStone

**Fill in the blanks together and watch a wild story get carved into stone - perfect for car rides and kitchen tables.**

QuibbleStone is a multiplayer, multi-device, fill-in-the-blank word game built for one thing: getting a family or a group of friends laughing together in about two minutes. It is the classic "give me a plural noun" surprise, rebuilt for a generation that plays on phones instead of paper - and, crucially, for people who are not always in the same room.

Everyone joins a shared room from their own phone. Each player is asked for words (sometimes blind, sometimes from a word bank), and then the finished tale is revealed on a glowing stone tablet for the whole group to laugh at together. Same car or different houses, the game is the same.

---

## Why it is different

Fill-in-the-blank word games are a crowded, beloved category - but they share two weaknesses QuibbleStone was designed to beat:

- **Real, live, cross-device multiplayer - including remote.** Most word-fill apps are single-device or same-room only. QuibbleStone runs on a real-time cloud backbone, so a group split across different houses plays the exact same session as a group sharing one car. Live multiplayer that shrugs off a highway dead zone is the thing this category has essentially left unclaimed.
- **It never runs out of stories.** The number-one complaint about the games in this space is "we finished the content and hit a paywall." QuibbleStone is built around a bottomless, always-fresh content engine: a "no repeats until you have seen them all" rotation today, and a carefully governed AI content pipeline to keep the well full.

## How a round works

1. **Gather round the stone.** A host taps *Create a game* and gets a short, car-friendly room code - easy to read aloud, with no confusable letters. Friends tap *Join*, pick a nickname and a **Guardian** (a friendly stone-mascot avatar), and appear on everyone's roster in real time.
2. **Everyone carves.** The blanks are dealt out across the players. Each person fills their words - an *adjective*, a *silly noise*, a *name* - without seeing the story. Stuck? Tap a spark suggestion, or skip.
3. **The reveal.** The finished tale carves itself onto a glowing tablet, word by word, with every player-supplied word glowing in coral. Tap a word to see *who* wrote it (the "wait, YOU wrote that?!" moment), react with a tap, and crown the single funniest word with a **Golden Guardian**.
4. **Again!** Replay the same tale with fresh words, remix a single word, pass the host crown to someone else, or save the tale as a keepsake to share.

## The feature tour

**Four ways to play, one engine.** Classic Blind (fill blind, laugh at the end), Word Bank (tap words from a curated list), Progressive Story (watch the tale build as you write), and Progressive Reveal (fill blind, then the story unveils one word at a time). Every mode is a small variation on a single shared engine, which is why new ways to play stay cheap to add.

**Solo or group, both first-class.** Play alone in a checkout line, or wire up a whole room. Solo is instant: no code, no account, straight into a game - and the friction-free on-ramp into playing with everyone else.

**The reveal is the point.** A word-by-word carving animation, one-tap reactions (Love / Wow / Didn't-like), and a light "funniest word" award that dresses the winning contributor's Guardian in a crown for the next round only - a costume, never a scoreboard, because QuibbleStone is a toy, not a competition. Per-word attribution then shows exactly whose brain produced *that*.

**Keep the laughs coming.** "Carve it again" replays a beloved tale with brand-new words. "Remix a word" lets anyone swap the single word that made *them* laugh. "Pass the chisel" hands the host role around the car. And the freshness engine makes sure you never see the same story twice until you have seen them all.

**Keepsakes that spread the word.** Save any finished tale as a shareable stone-tablet image (watermarked "carved with QuibbleStone"), keep a private "Tales we've carved" gallery on your device, or publish a tale to a short public link with a *Play QuibbleStone* button - the game's built-in, family-safe word-of-mouth loop.

**A real hand-written library.** Nearly twenty original, all-ages tales ship today - *The Wobbly Wizard & the Golden Sock*, *The Space Llama Who Forgot His Helmet*, *Captain Puddlebeard and the Bathtub Treasure*, *The Dragon Who Only Eats Mismatched Socks*, and more - in quick (five-minute) and full-length flavors.

## Safe and private by design

Child safety is not a setting bolted on later. It is built into the foundation.

- **Players are anonymous, forever.** You join with a nickname and a Guardian. No email, no account, no personal data - that is the entire player identity, by design (and the right posture for a game you hand to a kid).
- **A family-safe toggle, on by default,** keeps content age-appropriate.
- **Every typed word is checked** before anyone else sees it, with a friendly "try another one" rather than a scolding.
- **No ads, ever.** The single most-resented feature in this category is one QuibbleStone refuses to ship.

## Smart about AI - and about its costs

QuibbleStone's content engine uses AI, but never carelessly. Before a single AI feature shipped, we built a **cost gate** that every AI call must pass through: server-side only (no keys in the browser), moderated before any child sees the output, metered per session rather than per person (so players stay anonymous), and protected by a real-time spend circuit-breaker that falls back gracefully to free, deterministic content the instant a budget ceiling is reached. The first AI feature - **Fresh Runes**, which conjures a fresh set of on-theme words on demand - already runs behind that gate for a fraction of a cent per use.

That same gate is the runway for what comes next: **character voices** (your phone reads the finished tale aloud in a pirate, robot, or wizard voice - the car-ride killer feature), **AI illustrations** of your story, and **bespoke tales on demand** ("a story about our dog Biscuit in space").

## Built with care

QuibbleStone is a Progressive Web App: no app-store friction, installable to a phone's home screen, instantly cross-platform, shareable by a link. Under the hood it is a single real-time service (REST plus live SignalR sync in one app), a theme-driven Material UI front end, and infrastructure defined as code on Azure - already live and playable end to end. It stays generous where it counts, with a free tier that covers solo and group play and a "Family Plan" designed for the households that fall in love with it.

*No account needed. Just pick a name and play.*

---

### Pull-quotes and taglines (pick-and-choose for the page)

- "Carve a silly tale together."
- "The word game for the same car or different houses."
- "Everyone laughing within two minutes."
- "Fill in the blanks. Reveal the chaos."
- "A fill-in-the-blank party game that never runs out of stories."
- "No account. No ads. Just pick a name and play."
