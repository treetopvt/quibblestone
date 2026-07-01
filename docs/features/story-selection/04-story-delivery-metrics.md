# Story: Story delivery metrics (the anonymous serve log)

**Feature:** Story Selection & Freshness  ·  **Status:** Not Started  ·  **Issue:** #94

## Context
Over time the questions that matter for the content library are: which stories
get served, how often, in which mode, and (with story 05) which ones people
actually like. Today nothing is recorded anywhere - a served template is
gone the moment the round ends. This story creates the smallest honest record:
an anonymous, fire-and-forget "template served" event written to the
already-provisioned Storage account (README section 9). It is the data
foundation story 05's feedback counts join, and the input the future
ai-content-factory vet loop and any curation decisions will read. It is NOT
an analytics platform (root README section 12 parks that as demand-driven).
See [feature.md](./feature.md).

## Acceptance Criteria
- [ ] AC-01: Given a group round starts, then the server writes one serve
      event: template id, UTC timestamp, mode, length class, player count,
      family-safe flag, and an opaque room instance id (a GUID minted per
      room, NOT the join code) - and nothing else.
- [ ] AC-02: Given a solo round starts, then the client fire-and-forgets one
      serve event to a small REST endpoint carrying template id, mode
      (`solo`), length class, family-safe flag, and an opaque per-device
      session GUID - and nothing else. The endpoint validates the template id
      against the catalog and drops junk silently.
- [ ] AC-03: Given the telemetry sink is down, slow, or misconfigured, then
      gameplay is completely unaffected: round start never awaits the write,
      failures are logged server-side and swallowed, and the client call has
      no retry loop that can wedge the solo flow.
- [ ] AC-04: Given any serve event, then it contains no PII and nothing
      traceable to a person: no nickname, no join code, no IP capture in our
      table, no player session ids from the hub (README section 6). An
      engineer reading the table can learn WHAT was served WHEN and how
      often - never to whom by name.
- [ ] AC-05: Given local development with no Azure connection, then the sink
      degrades to a no-op (or console) implementation behind the same
      interface, and the app runs exactly as today with zero setup.
- [ ] AC-06: Given the stored events, then frequency questions ("how many
      times was `space-llama` served this month, split by mode?") are
      answerable with a straightforward table query - partition/row key design
      makes per-template time-range reads cheap.

## Out of Scope
- Any dashboard, chart, export, or creator-facing view (parked; dev-only
  queries are the reader for now).
- Like/dislike capture (story 05, which reuses this story's sink).
- Per-player play history or personalization (no accounts; see feature.md).
- Sampling, batching, or a queue - volumes are toy-scale; write one row.
- Recording reveal outcomes, submitted words, or anything content-bearing
  (words are the players'; the log is about templates).

## Technical Notes
- API: a small `ITelemetrySink` (or similar) service with two
  implementations - Azure Table Storage and no-op - chosen by configuration,
  singleton, injected into the hub and one new minimal REST controller
  (`api/src/Controllers/`). Connection string comes from configuration /
  Key Vault, never committed (repo convention).
- Table design suggestion: PartitionKey = template id, RowKey = inverted-ticks
  + GUID (cheap per-template recent-first scans, AC-06). One table, e.g.
  `StoryServes`.
- Hub write point: end of `StartRound` after the broadcast - the round is
  already running; the write is an epilogue, never a gate (AC-03). Use the
  SDK's async API fire-and-forget with exception capture.
- Solo endpoint: one POST, anonymous, no auth (there are no accounts). Keep
  the DTO tiny and validate template id against `TemplateCatalog` so the
  table cannot fill with invented ids. Basic rate limiting is a nice-to-have,
  not an AC - this is a toy.
- The opaque solo session GUID is minted client-side per device (localStorage)
  purely so frequency-per-device is estimable; it must not be derivable from
  anything personal.
- Infra: Bicep already provisions the Storage account; this story adds the
  table (or lets the SDK create-if-not-exists) and the App Service app
  setting. `az bicep build` stays green.

## Tests
| AC | Test |
|---|---|
| AC-01 | API unit test with a fake sink: StartRound records exactly the specified fields |
| AC-02 | API unit test on the controller: valid event accepted, unknown template id dropped; manual: solo round writes one row locally against Azurite or the no-op logs |
| AC-03 | API unit test: throwing sink does not fault StartRound or the controller response |
| AC-04 | code review checklist on the event shape + unit test asserting the DTO has no name/code fields |
| AC-05 | manual: `dotnet run` with no storage config boots and plays clean |
| AC-06 | manual: table query for one template id over a time range returns in one partition scan |

## Dependencies
- story-selection/01 (length class on the event; otherwise this story is
  independent of 02/03 and can run in parallel with them).
- group-play / single-player round starts (the hook points) - Complete.
- infra/platform-devops (Storage account exists) - Complete.
