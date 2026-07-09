# Story: The jobs shell + scoped authz

**Feature:** Sys-Admin Console  ·  **Status:** Not Started  ·  **Issue:** #214

## Context
ADR 0003 Layer 3's second finding: the console should be organized around **jobs an operator does**,
not around the features that happened to mint each tab. Today `web/src/admin/main.tsx` is a flat
two-tab shell (`Reported tales` / `Entitlements`) that grew one tab per shipped story. ADR 0003
reframes this as three jobs: **Support** (find a person, fix their problem), **Content** (moderation
- the existing review queue, later joined by content-factory vetting and pack publishing in this
same shell), and **Operations** (settings/flags, Stripe mode from story 04, an AI spend snapshot -
still linking OUT to App Insights rather than rebuilding a dashboard). Alongside the reorganization,
ADR 0003 asks for admin endpoints to carry a scope tag (`support`/`content`/`ops`) that the
`"Operator"` authorization policy checks - not because today's single operator needs restricting
(the existing allowlist keeps granting every scope to every operator, so behavior is unchanged), but
so that a FUTURE moderator is an allowlist entry with a scope list, not a rework of every controller.
This story does the shell reorganization and wires the scope metadata; it does not build any role
management UI (that stays parked, per feature.md). **Revised 2026-07-08 (adversarial review):** the
review found the earlier draft's "RBAC later, no controller rework" promise half-true - production's
`Operator:AllowedEmails` is a flat email list with no way to express a per-email scope subset, so a
future de-scoped moderator would in fact need a config-FORMAT change this story never defined. AC-06
now defines and ships that format (`Operator:Scopes`, index-aligned with `Operator:AllowedEmails`)
today, so scopes are a real, usable boundary the moment a restricted entry is added, not theater. See
[feature.md](./feature.md) and [ADR 0003](../../adr/0003-admin-platform-and-family-accounts.md) Layer
3 and its "Security posture" section.

## Acceptance Criteria
- [ ] AC-01: Given a signed-in operator, when the console shell renders, then it shows three tabs -
      Support, Content, Operations - replacing the prior two-tab (Reported tales / Entitlements)
      shell in `web/src/admin/main.tsx`.
- [ ] AC-02: Given the Support tab, when opened, then it shows the existing purchaser lookup +
      grant/revoke screen (`PurchaserEntitlements.tsx`, story 02, #136) relocated as-is - the
      component is not rewritten, only re-homed under the Support tab (the "find a person, fix
      their problem" job).
- [ ] AC-03: Given the Content tab, when opened, then it shows the existing reported-tales review
      queue (`ReviewQueue.tsx`, story 03, #137) relocated as-is, unchanged.
- [ ] AC-04: Given the Operations tab, when opened, then it shows the Stripe-mode panel (story 04)
      relocated into it, plus a control-plane settings panel that is dependency-tolerant: it
      renders its content only when its backing endpoint (`control-plane/01`, not yet built)
      responds successfully, and otherwise renders a plain "settings controls are not available
      yet" state - never an error, a blank screen, or a crash of the surrounding shell.
- [ ] AC-05 (scope metadata, no behavior change today): Given every existing admin endpoint
      (`AdminEntitlementsController`, `ReportedTalesController`, `StripeModeController`), when this
      story ships, then each is decorated with a scope tag (`support`, `content`, or `ops`
      respectively) implemented as authorization policy/attribute metadata that the `"Operator"`
      authorization pipeline checks - and because the current `IOperatorAllowlist` grants every
      allowlisted operator every scope, the existing (single-operator) test suite for all three
      controllers passes UNMODIFIED, proving zero behavior change.
- [ ] AC-06 (the config shape is real, not theater - REVISED 2026-07-08 adversarial review):
      Given the new `Operator:Scopes` configuration key (defined and shipped BY THIS STORY,
      index-aligned with `Operator:AllowedEmails` and read via the identical dual-shape pattern
      `ConfigurationOperatorAllowlist` already uses for emails - an indexed array locally/in tests,
      a single delimited scalar for one Key Vault secret), when an allowlisted email has NO
      corresponding `Operator:Scopes` entry (true for every operator configured today), then
      `IOperatorAllowlist` resolves that email to ALL THREE scopes (a backward-compatible default -
      today's single operator needs zero config change); and when an entry DOES restrict an email to
      a scope subset (e.g. `"support"` only), then the resolved scope set is exactly that subset and
      is honored by the policy checks AC-05 wires - proving a future de-scoped moderator is a
      config-only change TODAY, with the format already shipped, not a promise deferred to a future
      story that would need a controller rework. (The 2026-07-08 review found the prior draft of
      this AC "proves the mechanism by structure" without ever defining a config format capable of
      expressing a restricted email - production `Operator:AllowedEmails` is a flat list with no
      per-email scope field, so the promise was half-true. This AC closes that gap.)
- [ ] AC-07 (out-of-scope guard): Given this story, then no role-management UI, no operator list
      editor, and no per-operator scope-editing screen is added anywhere in the console - Parked in
      feature.md; this story is authorization plumbing only.

## Out of Scope
- Any role-management UI (adding/removing operators, editing scopes from the console) - Parked in
  feature.md; alpha still has one operator, config-managed via Key Vault (story 01).
- The operator action log and its Operations-tab view - that is story 06, which lands after this
  one specifically so its view has an Operations tab to live in.
- The Support job's full lookup-by-email/claim-code/slug + verb set (resend link, extend TTL,
  restore, comp, resync) - that is story 07; this story only relocates the EXISTING grant/revoke
  screen into the Support tab, it does not extend its capability.
- Building `control-plane/01`'s settings endpoints themselves - this story only makes the Operations
  tab's settings panel dependency-tolerant of that endpoint existing; the endpoint is that other
  feature's job.
- Any change to the underlying behavior of `PurchaserEntitlements.tsx`, `ReviewQueue.tsx`, or the
  Stripe-mode panel beyond their tab placement.

## Technical Notes
- **Shell reorganization:** rework `web/src/admin/main.tsx`'s `AdminTab` union from
  `'review' | 'entitlements'` (plus story 04's added Stripe-mode tab) to `'support' | 'content' |
  'ops'`, and its three-way render to place `PurchaserEntitlements` under Support, `ReviewQueue`
  under Content, and a new `OperationsPanel` (composing the Stripe-mode panel from story 04 plus the
  dependency-tolerant settings panel) under Operations. Keep the existing session-check /
  credential-holding logic in the shell untouched - only the tab set and routing change.
- **Dependency-tolerant panel pattern (AC-04):** the settings panel should attempt its fetch on
  mount and hold three states - loading, available (render real controls), unavailable (a plain
  message, e.g. "Runtime settings are not wired up yet.") - collapsing any non-2xx response or
  network failure into "unavailable," never letting it propagate as an unhandled rejection or an
  error boundary trip. This is the same defensive posture `web/src/admin/main.tsx`'s existing
  session-check already uses ("falls back to `<AdminLogin/>`... the fail-safe default").
- **Scope metadata (AC-05/AC-06):** the cleanest fit for ASP.NET Core authorization is a small
  custom requirement + attribute pair in `api/src/Admin/` (e.g. `OperatorScope.cs` defining the
  three scope constants `Support`/`Content`/`Ops`, an `IAuthorizationRequirement` carrying the
  required scope, and a `RequireOperatorScopeAttribute` or additional named policies -
  `"Operator:Support"`, `"Operator:Content"`, `"Operator:Ops"` - registered in `Program.cs`
  alongside the existing `"Operator"` policy). Apply the appropriate scope policy to each of
  `AdminEntitlementsController` (Support), `ReportedTalesController` (Content), and
  `StripeModeController` (Ops) IN PLACE of their current bare `[Authorize(Policy =
  OperatorSession.PolicyName)]` - or alongside it, whichever the chosen implementation shape
  prefers, so long as a de-scoped future operator is rejected at the policy layer, not by
  convention.
- **The per-entry scope config format (AC-06, REVISED 2026-07-08 - defined and shipped now, not
  deferred):** `IOperatorAllowlist` grows one member, e.g. `ScopesFor(string? email) ->
  IReadOnlySet<OperatorScope>`, resolved from a NEW `Operator:Scopes` key living alongside
  `Operator:AllowedEmails` in `ConfigurationOperatorAllowlist`, read via the SAME dual-shape
  strategy that key already uses so one code path still serves local dev, tests, and a Key
  Vault-backed deployment:
  - **Array shape (local/appsettings/tests):** `Operator:Scopes:0`, `Operator:Scopes:1`, ...
    index-aligned with `Operator:AllowedEmails:0`, `:1`, ... - each entry a comma-delimited scope
    list (`"support,content,ops"`) or the shorthand `"all"`.
  - **Single delimited scalar (one Key Vault secret, mirroring `AllowedEmails`'s own KV fallback -
    a KV secret NAME cannot hold the `@`/`.`/`:` an email or an indexer needs, so the value must
    carry the structure instead):** `Operator:Scopes` = a semicolon-separated list positionally
    aligned with the semicolon-separated `Operator:AllowedEmails` scalar, each position's scopes
    plus-joined internally (e.g. `"support+content+ops;support"` pairs position 0 -> all three
    scopes, position 1 -> `support` only).
  - **Missing key, missing index, or an unparseable entry -> default to ALL THREE scopes** for that
    operator (fail-open on WIDTH only - `IOperatorAllowlist.IsOperator` stays fail-closed on
    MEMBERSHIP; a non-operator email never gets a scope set at all). This default is what keeps
    today's single-operator deployment a zero-config-change no-op while still making the format
    real: adding a restricted future entry is one `Operator:Scopes` value, not a schema migration.
  - `ScopesFor` returns an empty set for any email `IsOperator` rejects - the scope check never runs
    ahead of the membership check.
- **`Program.cs` hazard:** registering the new named policies (or the requirement handler) is
  another `Program.cs` edit - per ADR 0003's rule, land it as its own small, rebased PR, not
  batched with story 04's `IOperatorGate` deletion or any other unrelated `Program.cs` change even
  though both stories touch admin authorization.
- **Files this story owns:** `web/src/admin/main.tsx` (rework), a new
  `web/src/admin/OperationsPanel.tsx` (composes story 04's Stripe-mode panel + the new settings
  panel), a new `web/src/admin/SettingsPanel.tsx` (or similar, dependency-tolerant), `api/src/Admin/
  OperatorScope.cs` (new), edits to `OperatorAuthenticationHandler.cs` / `ConfigurationOperatorAllowlist.cs`
  (scope resolution AND the new `Operator:Scopes` reading logic, AC-06), and one-line attribute
  additions to the three existing admin controllers.

## Tests
| AC | Test |
|---|---|
| AC-01 | `manual + a small web/src/admin/main.test.tsx: three tabs render (Support/Content/Operations) after a signed-in session.` |
| AC-02 | `manual: Support tab renders PurchaserEntitlements's existing lookup + grant/revoke controls.` |
| AC-03 | `manual: Content tab renders ReviewQueue's existing reported-tales list.` |
| AC-04 | `web/src/admin/SettingsPanel.test.tsx: a 404/network failure from the settings endpoint renders the "not available yet" state, not an error; a successful response renders controls.` |
| AC-05 | `tests/QuibbleStone.Api.Tests/Admin/AdminEntitlementsControllerTests.cs, ReportedTalesControllerTests.cs, StripeModeControllerTests.cs re-run UNMODIFIED: all still pass, proving the scope check is a no-op for the current allowlist.` |
| AC-06 | `tests/QuibbleStone.Api.Tests/Admin/OperatorScopeTests.cs (new): an operator with no Operator:Scopes entry resolves to all three scopes (default); an operator with a configured "support" entry is rejected by a policy requiring Content or Ops and accepted by one requiring Support - covering both the array config shape and the delimited-scalar (Key Vault) shape.` |
| AC-07 | `manual: code/UI audit - no operator-list or scope-editor control exists anywhere in the admin bundle.` |

## Dependencies
- `sysadmin-console/04` (#TBD) - the Stripe-mode panel this story relocates into the Operations tab.
- `sysadmin-console/01` (#135, Complete) - the `"Operator"` policy this story's scope requirements
  extend, and the admin bundle shell this story reorganizes.
- `control-plane/01` (not yet decomposed, ADR 0003 Layer 1) - the settings endpoint the Operations
  tab's settings panel is dependency-tolerant of; this story does not block on it landing first.
