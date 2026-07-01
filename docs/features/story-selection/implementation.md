# Implementation Plan: Story Selection & Freshness

> The bridge between planning and orchestration. The **Wave Plan** below is DAG-ready: the `orchestrate-feature`
> skill's Phase 1 validates and adjusts it rather than deriving it. See
> [`docs/FEATURE_ORCHESTRATION_PLAYBOOK.md`](../../FEATURE_ORCHESTRATION_PLAYBOOK.md).

## Reuse map

| Concern | Reuse | Where |
|---|---|---|
| Selection stages (pattern) | the pure-gate shape: data in, data out, mirrored by hand web/server | `web/src/content/familySafe.ts`, `api/src/Safety/FamilySafeContentSelector.cs` |
| Length signal | blank count, already on both sides - derive, never author | `web/src/engine/template.ts` (`getBlanks`), `api/src/Content/TemplateCatalog.cs` (`BlankCount`) |
| Template data + authoring bar | seed library + authoring guide (quick templates follow it) | `web/src/content/seedLibrary.ts`, `web/src/content/README.md` |
| Solo pick site | the existing composed pick in Solo | `web/src/pages/Solo.tsx` (`selectTemplates` -> random) |
| Group pick site | the existing StartRound pipeline | `api/src/Hubs/GameHub.cs` (`StartRound`) |
| Real-time | the one SignalR connection hook (start-round param travels here) | `web/src/signalr/useGameHub.ts` |
| Room state | the in-memory Room record (length pref, played ids) | `api/src/Rooms/Room.cs` |
| Styling / components | MUI theme + existing toggle/button patterns; FontAwesome icons | `web/src/theme.ts`, `web/src/components/`, `web/src/fontawesome.ts` |
| Device-local storage posture | keepsake-gallery/03's documented localStorage stance | `docs/features/keepsake-gallery/03-tales-weve-carved-history.md` |
| Config / secrets | API config + Key Vault for the storage connection; `VITE_*` for the API base URL | `api/src/appsettings*.json`, `web/.env.development` |
| Infra | provisioned Storage account | `infra/main.bicep` |

## Wave Plan (DAG)

Sizing rule: a builder owns files that are **disjoint** from its concurrent siblings. 02 and 03 both edit
`Solo.tsx`, `useGameHub.ts`, `GameHub.cs`, and `Room.cs`, so they serialize. 04 is disjoint from both (new
telemetry service + controller + infra) and can run beside 02. 05 needs 04's sink and 02/03's settled screens.

| Story | Issue | Files it owns (footprint) | Depends-on | Can-run-with | Wave | Effort |
|---|---|---|---|---|---|---|
| 01 (foundation) | #91 | `web/src/content/length.ts` + test, `web/src/content/seedLibrary.ts` (quick templates), `seedLibrary.test.ts`, `web/src/content/README.md`, `api/src/Content/TemplateCatalog.cs`, `api/src/Safety/LengthContentSelector.cs` + test, `GameHub.cs` (pipeline refactor), `Program.cs` (DI) | - | - | 1 | high |
| 02 | #92 | `web/src/pages/Solo.tsx`, group lobby screen, `web/src/signalr/useGameHub.ts`, `api/src/Hubs/GameHub.cs` (param), `api/src/Rooms/Room.cs` (length pref) | 01 | 04 | 2 | medium |
| 04 | #94 | `api/src/Telemetry/*` (sink + implementations), new controller, `Program.cs` (DI), `infra/main.bicep` (app setting/table), API tests | 01 | 02 | 2 | medium |
| 03 | #93 | `web/src/content/fresh.ts` + history module + tests, `web/src/pages/Solo.tsx`, `api/src/Hubs/GameHub.cs`, `api/src/Rooms/Room.cs` (played ids) | 02 (file overlap, not logic) | - | 3 | medium |
| 05 | #95 | `web/src/components/TaleFeedback.tsx`, `Reveal.tsx` / `RoundComplete.tsx` wiring, feedback endpoint + table, API tests | 04, 03 (screen/file overlap) | - | 4 | medium |

**Concurrency per wave:** Wave 1 = 01 alone. Wave 2 = {02, 04} in parallel. Wave 3 = 03. Wave 4 = 05.

## Per-story tech notes

### 01 - Length classes + the one selection pipeline
Foundation. Exports the threshold constant and `selectByLength` (web) /
`LengthContentSelector` (server); refactors both pick sites into the explicit
staged pipeline so 02/03 slot in without re-touching the pick's shape. Also
authors the quick seed templates (the data the filter needs) and raises the
seed-size test bound. Gotcha: the family-safe gate must remain stage one on
both sides; the empty-pool fallback (AC-06) belongs in the pipeline, not in
callers.

### 02 - Quick story option
Pure configuration UI + one new param on the existing start-round wire
contract. Mirrors the family-safe flag's journey exactly (client toggle ->
invoke param -> server-enforced filter -> Room state). Do not add a second
hub method.

### 03 - Freshness rotation
Two small pure exclusion stages (web reference spec + C# mirror) plus two tiny
state holders: localStorage id list (solo) and `Room.PlayedTemplateIds`
(group). Gotcha: replay paths must neither filter nor append (feature.md
Decision); coordinate the seam with replay-remix/01 if it has landed.

### 04 - Story delivery metrics
New, self-contained telemetry vertical: `ITelemetrySink` with Table Storage +
no-op implementations, a write in StartRound's epilogue, one anonymous REST
POST for solo serves. Gotcha: never await the sink on the round-start path;
validate template ids against the catalog on the public endpoint.

### 05 - Like / dislike a tale
One shared component on two screens + one upserting POST reusing 04's sink.
Gotcha: this is NOT reveal-delight/01's Reaction row - no live room tallies,
no SignalR; keep it a quiet REST write and keep the control visually
subordinate to the replay CTAs.

## Cross-cutting concerns

- The family-safe gate is always the first selection stage; no story may
  reorder or bypass it (child-safety AC in every selection story).
- No PII anywhere in stored telemetry: template ids, opaque GUIDs, timestamps,
  mode flags only - never nicknames, join codes, or player session ids
  (README section 6).
- Telemetry never gates gameplay: every write is fire-and-forget with a no-op
  local fallback.
- One engine, many thin modes: everything here is selection-layer and
  UI-layer; `assemble`, `collectWord`, distribution, and the Template schema
  are untouchable in this feature.
- Web purity discipline: every new selection stage is a pure, Vitest-covered
  module in `web/src/content/`, mirrored by hand in C# with the header-comment
  cross-reference both directions.
- MUI theme + FontAwesome only; big tap targets on the two new controls
  (length choice, thumbs).
