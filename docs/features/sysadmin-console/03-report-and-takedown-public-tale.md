# Story: Report -> auto-hide-after-N -> operator review of a public tale

**Feature:** Sys-Admin Console  ·  **Status:** Not Started  ·  **Issue:** TBD

## Context
Public keepsake tales (`keepsake-gallery/04`, already shipped) have no report or takedown path
today - the content-safety filter runs once at publish time, and nothing happens after that. ADR
0002 Decision E closes this child-safety gap: a "report this tale" control on the public tale page;
reports accumulate; a tale auto-hides once a small threshold N is reached, pending human review; an
operator (story 01's back office) confirms the hide or restores the tale. The threshold stops a
single bad actor unilaterally suppressing a tale; the auto-hide means the app does not wait on
always-on human moderation to react to a real problem. This is actionable *now*, because public
tales already exist (feature.md's Candidate stories table - "actionable now"). It reuses the
authoritative child-safety filter posture and the keepsake-gallery publish endpoint's per-IP
rate-limit posture; it does not reimplement either, and it never touches the anonymous author - it
operates on published *content* only. See [feature.md](./feature.md).

## Acceptance Criteria
- [ ] AC-01: Given a visitor viewing a public tale page (`GET /t/{slug}`, keepsake-gallery/04), when
      they use a "report this tale" control, then a report is recorded against that tale's slug -
      no sign-in, account, or PII is required or collected from the reporter (consistent with the
      public tale page's existing no-account posture).
- [ ] AC-02: Given a tale accumulates reports, when the count reaches a small configured threshold N
      (a config constant, per feature.md's Open decisions - pick a small starting value, e.g. 3, and
      make it easy to tune), then the tale auto-hides: `GET /t/{slug}` stops serving the tale content
      (a neutral "under review" page, not the 404 "expired/unshared" page, so a legitimate host is
      not confused with a revoked-by-them tale) pending operator review.
- [ ] AC-03: Given an auto-hidden tale, when a signed-in operator (story 01's boundary) opens the
      back office's review queue, then they see the tale's content, its report count, and two
      actions - confirm-hidden (the tale stays hidden / is deleted) or restore (the tale resumes
      serving normally at its slug, and its report count resets so it is not immediately re-hidden
      by the same reports).
- [ ] AC-04 (reuse, not reimplement, content safety): Given the report/takedown path, then it never
      re-implements or duplicates the `IContentSafetyFilter` logic - a report is a signal from a
      human viewer, evaluated by an operator, not a second automated content check. The existing
      publish-time re-vet (keepsake-gallery/04 AC-03) is untouched and remains the authoritative
      pre-display gate; this story is what happens *after* publish, on a report.
- [ ] AC-05 (anti-abuse, reuse the publish-endpoint posture): Given the report endpoint is public and
      anonymous (no account, by design), then it is rate-limited per client IP (the same
      `X-Forwarded-For`-aware posture `PublishTalesRateLimit` already establishes for `POST
      /api/tales`) so a single actor cannot flood reports to force-hide a tale beyond what the
      threshold N and the per-IP cap together allow, and cannot spam the endpoint to exhaust
      storage.
- [ ] AC-06 (anonymity invariant): Given a report or a review action, then neither ever surfaces or
      requires the anonymous author's identity - a report is filed against a slug, and an operator
      reviews/restores content, never a person; there is no path from a report back to a player
      nickname, room, or session (the same firewall ADR 0002 defines for `CreateRoom` applies here
      to moderation).
- [ ] AC-07: Given a tale has never been reported, then nothing about its behavior changes - the
      report/takedown machinery is additive to keepsake-gallery/04's existing publish/serve/revoke
      flow, verified by re-running its existing coverage with zero regressions.

## Out of Scope
- Any change to the pre-publish content-safety re-vet itself (keepsake-gallery/04's existing
  server-side re-vet stays exactly as-is) - this story is post-publish, report-driven review only.
- A public gallery, discovery, or browse surface for reported/hidden tales - there is still no
  directory (keepsake-gallery/04's Out of Scope stands).
- Reporter feedback beyond a simple "thanks, we'll take a look" acknowledgment - no reporter
  account, no status tracking, no notification when a review completes.
- Escalation workflows, severity levels, or reason-for-report categorization - a report is a flat
  signal; the operator's judgment on review is the sole arbiter (feature.md's "operator, not audit
  ceremony" design note).
- Multi-operator review assignment/ownership - Parked in feature.md; alpha has one operator working
  a single shared queue.
- Deleting the tale's underlying Table Storage row on confirm-hidden vs. marking it hidden - either
  is acceptable; this story does not mandate soft-delete-vs-hard-delete, only that a confirmed-hidden
  tale never serves again at its slug.

## Technical Notes
- New additions to `api/src/PublishedTales/` (this feature reuses that folder rather than
  duplicating it, since reports are a property of a published tale, not a new domain): a `Report`
  concept on `PublishedTale` (a report count, or a small companion "reports" collection keyed by
  slug) and a `Hidden`/`UnderReview` state distinct from the existing `IsExpired` lazy-expiry state
  used by `GET /t/{slug}` (mirrors `PublishedTalesController`'s existing `IPublishedTaleStore`
  pattern - extend it, do not fork a second store).
- New endpoint on `PublishedTalesController` (or a small sibling controller in the same folder):
  `POST /api/tales/{slug}/report` - public, anonymous, rate-limited exactly like `POST /api/tales`
  (reuse `PublishTalesRateLimit`'s per-IP fixed-window pattern and `ForwardedHeaders` posture from
  `Program.cs`, either the same policy or a sibling policy with its own tunables - keep the pattern,
  not necessarily the exact numbers). On reaching threshold N, flips the tale to
  hidden/under-review; `GET /t/{slug}` for a hidden tale renders a distinct, neutral "this tale is
  under review" page (not the 404 used for missing/expired/revoked, so the two states read
  differently to a visitor).
- Threshold N: a small `public const int` (e.g. `AutoHideThreshold`) alongside `TaleTtl` in
  `PublishedTalesController` or a small companion constants file - the one open story-level detail
  feature.md names; pick a small starting value (3-5 is a reasonable alpha default) and leave a
  comment inviting tuning from real signal.
- New admin-only endpoint(s) in `api/src/Admin/` (story 01's folder): `GET
  /admin/reported-tales` (the review queue) and `POST /admin/reported-tales/{slug}/confirm` /
  `POST /admin/reported-tales/{slug}/restore` - behind story 01's `[Authorize(Policy =
  "Operator")]`, calling into the same `IPublishedTaleStore` extension this story adds, never a
  parallel read path.
- Web (back office, story 01's separate bundle): a small review-queue page listing hidden tales with
  their content + report count and confirm/restore buttons - styled from `web/src/theme.ts`, no
  bespoke design system, consistent with story 02's minimal internal-page pattern.
- No change needed to `web/src/gallery/publishTale.ts` beyond, optionally, a small `reportTale()`
  helper for the public tale page's report control - the report control itself lives on the
  server-rendered `GET /t/{slug}` HTML page (`PublishedTalesController.RenderTalePage`, plain
  markup + a small fetch, consistent with that page's existing "no SPA import" approach), not the
  React app.

## Tests
| AC | Test |
|---|---|
| AC-01 | `api/tests/PublishedTales/ReportTaleTests.cs (to be created): reporting a slug with no account/session data succeeds and records a report.` |
| AC-02 | `api/tests/PublishedTales/ReportTaleTests.cs: N reports against one slug flips it to hidden; GET /t/{slug} then serves the under-review page, not the tale.` |
| AC-03 | `api/tests/Admin/ReportedTalesControllerTests.cs (to be created): the review queue lists a hidden tale with its report count; confirm keeps it hidden, restore resumes normal serving and resets the report count.` |
| AC-04 | `manual: code review - confirm no new IContentSafetyFilter logic is added; the existing publish-time re-vet path is untouched.` |
| AC-05 | `api/tests/PublishedTales/ReportRateLimitTests.cs (to be created, mirrors PublishTalesRateLimitTests): per-IP partitioning on the report endpoint; fails closed to a shared bucket when no IP is available.` |
| AC-06 | `manual: code review - confirm no report/review code path ever queries or displays a player nickname, room, or session id.` |
| AC-07 | `existing keepsake-gallery/04 test suite (PublishedTalesControllerTests, publishTale.test.ts) re-run as regression: zero behavior change for a never-reported tale.` |

## Dependencies
- `sysadmin-console/01` (this feature) - the operator login + admin boundary the review queue sits
  behind.
- `keepsake-gallery/04` (#66) - the public tale page, publish/store, and per-IP rate-limit posture
  this story extends.
- child-safety - the authoritative `IContentSafetyFilter` posture this story reuses and does not
  reimplement.
