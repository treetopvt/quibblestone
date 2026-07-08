<!--
  Implementation runbook for the ADR 0003 admin platform. The build-order source of truth is
  docs/adr/0003-admin-platform-and-family-accounts.md (its "Cross-feature build order" table + the
  "Security posture" section, which is BINDING on every story). This runbook adds the operating
  model, the serial-merge discipline, and a per-wave definition of done. Work top to bottom; check
  the boxes as PRs merge. Use hyphens/colons/parentheses, never em dashes.
-->

# Implementation runbook: the admin platform (ADR 0003)

## How to read this
- **Source of truth:** [ADR 0003](./adr/0003-admin-platform-and-family-accounts.md). Its
  "Cross-feature build order" table is the canonical wave numbering; its "Security posture" section
  is a set of BINDING requirements every listed story must satisfy. Each feature's own
  `implementation.md` has the DAG-ready per-story footprints the `orchestrate-feature` skill reads.
- **This file** is the operating manual over that: the run model, the serial-merge points, and the
  per-wave definition of done.

## The operating model (read before starting)
Do NOT attempt one autonomous run that builds everything. Three constraints forbid it:
1. **`Program.cs` is a serial chokepoint** - nearly every Wave 1/2/3 story adds a DI registration
   there. Parallel branches editing it conflict; they merge one PR at a time, each rebased on the
   last.
2. **CI must stay green per PR** - every story is a real PR gated by `dotnet test` + Vitest + both
   builds. A fire-and-forget swarm cannot keep a buildable tree.
3. **These are doc-derived stories** - keep code-review in the loop, especially on the money and
   child-safety seams.

**The model:** one feature/wave at a time via `orchestrate-feature`, with a HUMAN (or a driving
session) integrating between waves - build a wave, get each story green, merge serially through
`Program.cs`, rebase, then start the next wave. Semi-automated per wave, human at the integration
seam.

## The per-story loop
On a branch per story (`claude/<feature>-<story>-...`):
1. Build: `orchestrate-feature` for the feature, or for a single story: an implementing agent
   (`frontend-agent` for web, a general agent for API) -> `testing-agent` for specs ->
   `code-review` agent on the diff.
2. Gate: run the `ci-check` skill (API build + web build + Bicep validate) + `dotnet test` +
   `npm run test:unit`; `/verify` anything with runtime behavior. Playwright is not in CI - run
   `npm run test:e2e` locally where a flow changed (API must be up on :5180).
3. PR + merge. If the story touched `Program.cs` or a flagged shared file, merge SERIALLY (one at a
   time) and rebase the next story on the new `main` before merging it.

## Definition of done (per wave)
A wave is done when: every story's PR is merged to `main`; `main` is green (both builds + both test
suites); the new UAT/beta and the platform environment both deploy clean; and any story that
deferred a seam (dependency-tolerant panels, no-op log seam) is noted so a later wave lights it up.

---

## Wave 0 - the environment (ALREADY DELIVERED by main's `platform-devops/07`)
- [x] The second environment landed on `main` before this plan started: `platform-devops/07` (QA
  lane + tag-based promotion to beta, #192/#193) rebadges the existing UAT site as "beta" and stands
  up an isolated `quibblestone-qa-rg` lane that auto-deploys on merge to `main`. This IS ADR 0003
  Decision 4's second environment, so the planning branch's standalone second-environment story was
  dropped as superseded. Nothing to build here - just orient on the two lanes (qa = auto on merge;
  beta = tag-promoted). One verification item to carry into Wave 1: confirm both lanes sit behind
  the same single-hop trusted edge (XFF topology) before relying on per-IP limiters, and that qa and
  beta each have their own Storage + Key Vault (they do - it is what makes their key rings isolated).

## Wave 1 - the foundation (all touch `Program.cs`: merge serially, land 05 early)
- [ ] `accounts-identity/05` - stable `AccountId` + re-key `PurchaserAccounts`/`EntitlementGrants`/
  `CloudGalleryTales`. Re-keys `StoredValueEntitlementService.cs` - **must merge before
  control-plane/02 and billing-entitlements/08.**
- [ ] `keepsake-vault/01` - vault store + auto-save (bearer id in `X-Vault-Id` header, server-
  stamped `CreatedUtc`, computed TTL on list, read+write limits, per-vault cap, entropy floor).
- [ ] `control-plane/01` - runtime settings service (per-key bounds on PUT; mandatory settings
  action-log write via a dependency-tolerant seam that lights up when `sysadmin-console/06` lands).
- [ ] `sysadmin-console/04` - one console, one auth (relocate Stripe toggle behind the Operator
  policy; delete the interim `IOperatorGate` + the kid-bundle `/admin/billing-mode`). Touches
  `App.tsx` (route delete).
- [ ] `platform-devops/08` - durable CSPRNG key ring (never a deterministic Bicep-derived key) +
  the magic-link nonce set moved to the shared durable store + fail-closed in deployed envs +
  each lane (qa/beta) provisions its own isolated key ring. (Renumbered from 07: main's shipped 07
  is the QA lane.)

## Wave 2 - capabilities + billing metadata
- [ ] `accounts-identity/06` - ADR 0002 Decision F wired: purchaser proof at `CreateRoom` via the
  hub access token, resolved to a capability set in a NEW singleton (not a hub field), identity
  discarded at the boundary. Depends on 05. Touches `Program.cs`.
- [ ] `accounts-identity/07` - free family account (decoupled from purchase). Depends on 05.
- [ ] `keepsake-vault/02` - the device gallery becomes a view over the vault. Depends on kv/01.
- [ ] `control-plane/02` - system-scope capability flags (post-compose filter; kill switches force
  OFF only, never enable unconfigured infra). Depends on **05** (same file `StoredValueEntitlement
  Service.cs`); serialize with billing/08 on `api/src/Entitlements/`.
- [ ] `sysadmin-console/05` - jobs shell (Support/Content/Operations) + per-entry operator scope
  config format. Depends on sc/04.
- [ ] `billing-entitlements/08` - grant metadata (`GrantId`/`PlanId`/`StripeSubscriptionId`/`Mode`)
  + mode-safe, metadata-verified per-account Stripe resync (rate-limited). Co-occupies
  `Entitlements/` with cp/02 - land the `EntitlementGrant.cs` shape change non-concurrently with
  cp/02's `StoredValueEntitlementService.cs` edit.

## Wave 3 - the leaf features (`Program.cs` hot again: 4 stories, merge serially)
- [ ] `accounts-identity/08` - kid seat presets (account-plane household data; a preset join is
  indistinguishable from a manual join). **Merge before 09** (shared `Account.tsx`/
  `AccountsController.cs`).
- [ ] `accounts-identity/09` - family device link + the teen-plus adult-signal gate
  (`Room.AdultUnlocked`, family-safe by default, host-migration-proof) + kid-device token as a
  bearer secret (rolling TTL, per-code + global rate limits). Depends on 06 + 07. Touches
  `Program.cs` + `App.tsx`. NOTE: AC-07's guarantee is GROUP-play-scoped; the solo gate is a
  tracked follow-up (see accounts-identity/feature.md), not this story.
- [ ] `keepsake-vault/03` - claim + recovery (claim code as a bearer secret: entropy floor,
  per-code burn + global ceiling, single-use/rotation). Depends on kv/01 + 07; serialize with
  kv/04 on the vault store files.
- [ ] `keepsake-vault/04` - soft-delete + restore (takedown restore carries stronger friction).
  Touches `PublishedTales/` - order vs cp/03.
- [ ] `control-plane/03` - knob migration (clamp rate-limit permits at the read site). **Run alone
  in its slot** (touches many files). Touches `Program.cs`.
- [ ] `sysadmin-console/06` - operator action log (log-before-act; age-based retention with a hard
  floor an operator setting cannot lower). Touches `Program.cs`. This is the seam cp/01 and sc/07
  write to.

## Wave 4 - the support surface
- [ ] `sysadmin-console/07` - support lookup + verbs. Structural cross-plane firewall (count-only
  contracts, no slug/claim-code -> email, resend on the shared throttle + per-account cap, resync
  debounced). Consumes 05, kv/03-04, billing/08, sc/06. Dependency-tolerant: start once sc/06
  lands, light panels up as seams arrive.

## Deferred (not in these waves)
- [ ] Solo-play teen-plus gate (follow-up; a real design choice - server-side solo selection vs.
  gated library download vs. lightweight solo session). Sized after the alpha shows whether solo
  teen-plus exposure is a real risk.
- [ ] Stripe live - waits on Waves 0-2 being solid (ADR 0003 Decision 4). The grant metadata (08),
  Decision F (06), and the key ring (07) are its prerequisites.

## Cross-cutting (fold into whichever story touches the file first)
- [ ] Add `email`, `accountId`, `vaultId`, `claimCode`, `token`/`access_token`, `deviceToken` to
  `PiiScrubbingTelemetryInitializer`'s `SensitivePropertyKeys`, and forbid interpolating those into
  exception messages in the new Accounts/Vault/Support code. (Candidate home flagged in
  keepsake-vault's reuse map.)

## Sequencing reminder
Nothing here blocks the friends-and-family alpha on the rebadged beta - run that in parallel and
let its telemetry inform which of these features earns its keep. Layers 0-1 (Waves 0-2) gate Stripe
live.
