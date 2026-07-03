<!--
  Feature EXPLORATION (not a fully-specified, ready-to-build feature): the sys-admin surface for
  QuibbleStone. Companion to docs/adr/0002-accounts-subscriptions-and-admin.md. This is deliberately
  a feature.md only - no implementation.md, no full story files - because the finding is that the
  admin "site" should NOT be built as a monolith for alpha; its pieces are minted by the features
  that create their need. Candidate stories below are Issue: TBD and Status: Not Started by design.
  Use hyphens/colons/parentheses, never em dashes.
-->

# Feature: Sys-Admin Console (exploration)

## Summary
The operator-facing back office for QuibbleStone: the surface a solo operator (later, a human
moderator) uses to keep the paid product healthy - purchaser/subscription support, moderation
review of public content, and a window on cost/abuse. The headline finding of the exploration
(ADR 0002): **this is not one thing to build.** Most of what "sys-admin site" evokes is already
served by another feature or by an Azure surface; only two responsibilities are genuinely new and
admin-only, and each is minted by the feature that creates its need - not built up front as a
console.

> **This is an exploration, not a ready-to-build feature.** There is intentionally no
> `implementation.md` and no full story files yet - the point of the exploration is to decide the
> shape and the first sliver (ADR 0002 Open Decisions A-F) before anything is decomposed. The
> Candidate stories table below is a map, not a backlog: every row is Issue TBD / Not Started.

## README reference
README section 3 (Monetization - the tiered identity model and "only the purchaser gets a
lightweight account"), section 6 (Child Safety & Moderation - "a strong moderation pipeline",
minimal data on minors), and section 7 (Epic Map - Phase 2 "AI Content Factory - back office" and
Phase 2 monetization). CLAUDE.md section 5 (child safety non-negotiable) and section 6 (the
monetization seam). Full rationale + the load-bearing invariant:
[ADR 0002](../../adr/0002-accounts-subscriptions-and-admin.md).

## Candidate stories (a map, not a backlog - every row is Issue TBD / Not Started)
<!-- Status: Not Started | In Progress | Complete | Blocked | Dropped -->
| # | Title | Trigger feature (mints this story) | Genuinely admin-only? | Status |
|---|---|---|---|---|
| 01 | Admin auth boundary (a separate, access-controlled surface) | first bespoke admin need below | yes (foundation) | Not Started |
| 02 | Operator grant / revoke an entitlement by purchaser email | real charging goes live (`billing-entitlements/03-04`) | yes | Not Started |
| 03 | Report / hide / takedown a public keepsake tale | public tales exist (`keepsake-gallery`, already shipped) | yes | Not Started |
| - | AI content vetting queue | already owned by `ai-content-factory/02` (#79) | no - not this feature | n/a |
| - | Library / pack management | already owned by `ai-content-factory/03` + `story-packs` | no - not this feature | n/a |
| - | Cost / abuse oversight dashboard | already served by `platform-devops/04` App Insights + Cost Management + the `ai-cost-gate` breaker | no - do not rebuild | n/a |

The bottom three rows are recorded on purpose: the exploration's job is as much to say **what is NOT
this feature** as what is. Rebuilding cost dashboards or a second vetting queue here would be the
smell.

## Dependencies
- `billing-entitlements/01` (#70) - the `IEntitlementService` seam + capability catalog that story
  02 (grant/revoke) reads and writes grants against. (Currently unbuilt - see ADR 0002 "State of
  the tree".)
- `accounts-identity/02` (#68) - the purchaser account that story 02 looks a customer up by.
- `keepsake-gallery` - the public tale link (already shipped) that story 03 moderates.
- `platform-devops/04` (#106, App Insights) + ADR 0001 (Cost Management budget) - the cost/abuse
  oversight this feature deliberately does NOT rebuild.
- child-safety - story 03's takedown honors the same authoritative filter posture; it does not
  reimplement moderation logic.

## Design notes
- **The admin surface is a deferred umbrella, not a monolith (ADR 0002 recommendation).** For a
  solo, ~50-sessions/month alpha, build none of it as a standalone site. Cost/abuse = App Insights
  + budget emails; content vetting = the content-factory queue when that feature lands; refunds =
  Stripe's own dashboard; entitlement grants = Table Storage tooling (`az` / Storage Explorer). A
  bespoke admin surface is minted only when a specific need cannot be met that way.
- **Where it lives: a SEPARATE, auth-gated back office (option A), never the kid PWA (option B).**
  When the first bespoke need arrives, it goes in a separate bundle/route tree with its own auth -
  it handles PII-adjacent purchaser data and moderation actions and must never share a surface with
  the anonymous, kid-facing app (blast radius; kid-safety-by-construction, CLAUDE.md section 5).
  Option B (an in-app admin area) is explicitly not recommended. See ADR 0002 Open Decision B.
- **The first bespoke sliver is story 02 (grant/revoke by email), and only when real charging is
  live.** The concrete need: unstick a paying customer whose entitlement did not apply, without
  hand-editing Table Storage. Build it as the thinnest option A - one or two protected endpoints +
  a minimal internal page reusing the MUI theme - not a full admin app.
- **Story 03 (public-tale takedown) is a live safety question, not a clean deferral.** Keepsake
  tales are already public, so a report/hide path may already be worth a thin slice. Because it has
  a child-safety dimension, whether to build it now is an explicit owner call (ADR 0002 Open
  Decision E), raised rather than parked silently.
- **The anonymity invariant applies here too.** No admin surface may join a purchaser identity to a
  player nickname, room, or session. Purchaser support operates on the purchaser plane (email ->
  grant); moderation operates on published *content*, not on the anonymous author. Reviewers guard
  the same firewall ADR 0002 defines for `CreateRoom`.
- **Operator, not audit ceremony.** This is a toy, not a system of record (CLAUDE.md preamble): the
  admin surface is minimal operator convenience, not a compliance/audit console. Resist growing it
  into role hierarchies, audit trails, or dashboards that Azure already provides.

## Parked - later
- Multi-operator / role-based access (a human moderator distinct from the owner) - alpha has one
  operator; RBAC is a Phase 3+ concern if the team grows.
- A bespoke cost/spend dashboard - deferred indefinitely; App Insights + Cost Management cover it
  until they demonstrably do not.
- Purchaser self-service beyond `billing-entitlements/05` (restore/manage) - the operator surface is
  for the cases self-service cannot handle, not a replacement for it.
- Any audit-trail / immutability ceremony - explicitly out (CLAUDE.md: toy, not a system of record).

## Open decisions
Tracked centrally in [ADR 0002](../../adr/0002-accounts-subscriptions-and-admin.md) Open Decisions
A-F. The two that gate THIS feature specifically:
- **B - where the admin surface lives** (separate back office recommended; resolve before story 01).
- **E - is a public-content takedown path needed in alpha** (child-safety call; gates story 03).

## Decisions
- 2026-07-03: Created as an exploration (feature.md only, no implementation.md / no full stories)
  rather than a fully-specified feature, because the finding is that the admin "site" should not be
  built as a monolith - its pieces are minted by their trigger features, and the first bespoke
  sliver (operator grant/revoke) is justified only when real charging goes live. Recorded alongside
  ADR 0002. Decompose into real stories only after ADR 0002 Open Decisions B + E are resolved.
