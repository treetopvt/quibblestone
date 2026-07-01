# Story: Tip jar ("Buy the Guardians a coffee")

**Feature:** Billing & Entitlements  ·  **Status:** Not Started  ·  **Issue:** TBD

## Context
Before anything is ever locked, QuibbleStone wants a friendly, ungated way for
a happy family to say thanks with a small one-time donation - "Buy the
Guardians a coffee." It grants no entitlement (it is pure goodwill, optionally
with a cosmetic thank-you), and it lives well outside the kid play-flow. This
is the donate-first rung the product wants on day one, ahead of any paywall.
See [feature.md](./feature.md) and README section 3 ("Avoid ads").

## Acceptance Criteria
- [ ] AC-01: Given the tip jar affordance, when it is placed in the app, then
      it lives on Home or in a settings-style area only (e.g. an AppBar
      affordance or a Home footer link) - it never appears inside the join
      code, lobby, word entry, or reveal screens a child is actively using
      during play.
- [ ] AC-02: Given a person completes a tip jar donation, when the payment
      succeeds, then no entitlement/capability key from billing-entitlements/01's
      catalog is granted as a result - the tip jar is entitlement-neutral by
      design, verified by confirming `EvaluateForSession` is unaffected before
      and after a tip.
- [ ] AC-03: Given a person opens the tip jar, then no sign-in and no
      purchaser account (accounts-identity/02) is required to complete a
      one-time tip - a purchaser account may be created afterward only if the
      chosen payment flow naturally produces one (e.g. to email a receipt),
      never as a precondition to donating.
- [ ] AC-04: Given a successful tip, when the confirmation is shown, then it
      is a warm, kid-safe thank-you (e.g. a gold Guardian sparkle moment and a
      short "thank you" message) - no dark patterns (no "are you sure you
      don't want to help the Guardians?" guilt prompts, no forced upsell into
      a bigger amount, no countdown timers).
- [ ] AC-05: Given the tip jar surface displays any free text (a custom
      "message to the Guardians" field, if offered), then that text passes the
      existing safety filter before it is stored or shown anywhere, and no PII
      beyond what the payment processor itself requires is collected by
      QuibbleStone's own database.
- [ ] AC-06: Given the tip jar is never used by a family, then nothing about
      its presence nags, blocks, or interrupts normal free play - it is fully
      passive until tapped.

## Out of Scope
- Any entitlement grant, discount, or "unlock" tied to tipping - explicitly
  disallowed by AC-02; if product direction ever wants a tip to unlock
  something, that is a new, deliberate story, not a change to this one.
- Recurring/subscription tipping - this story is one-time only; a
  subscription is the gated-purchase family plan (story 04), a separate
  concept.
- The general Stripe plumbing (checkout session creation, webhook
  confirmation, entitlement persistence) - story 03 builds the shared
  plumbing this story's payment call uses; this story owns the tip jar's UI
  and the "grant nothing" business rule on top of it.
- Ads of any kind as an alternative/adjacent monetization path - README
  section 3 rules this out entirely, not just for the tip jar.

## Technical Notes
- Web: a small `TipJar` component/dialog (new file under `web/src/components/`
  or `web/src/pages/`, naming TBD at build time) reachable from Home's
  settings area, styled entirely from `web/src/theme.ts` tokens - gold
  (`#FFB22E`) for the confirm CTA per the design vocabulary, FontAwesome
  icons only (`web/src/fontawesome.ts`). The optional cosmetic thank-you (a
  gold Guardian sparkle) is a lightweight animation reusing the existing
  `Guardian` component (`web/src/components/Guardian.tsx`) rather than a new
  illustration.
- The payment call itself goes through the shared Stripe plumbing from story
  03 (a one-time Checkout Session, no subscription mode) - this story should
  not embed its own Stripe SDK call if story 03 is landing in the same wave;
  if built ahead of story 03 for sequencing reasons, keep the payment call
  behind a small interface so story 03 can slot in without a rewrite.
- No entitlement write of any kind happens in this story's code path - the
  simplest correctness check is that this story's success handler never
  imports `IEntitlementService`'s grant method (only, if anything, a receipt/
  thank-you confirmation).
- If a "message to the Guardians" free-text field is offered, route it
  through the existing `IContentSafetyFilter` (`api/src/Safety/`) exactly
  like a nickname or blank answer - do not add a second filter.
- Keep the placement decision (Home footer vs. AppBar vs. a dedicated
  "Support" area) consistent with the Home screen's existing "No account
  needed - just pick a name & play" reassurance-row pattern (see
  `web/src/pages/Home.tsx`) - the tip jar should read as an invitation, not a
  billboard.

## Tests
| AC | Test |
|---|---|
| AC-01 | `manual: UI audit - confirm the tip jar entry point is absent from Join, Lobby, FillBlank, and Reveal.` |
| AC-02 | `api/tests/Entitlements/EntitlementServiceTests.cs (or a tip-jar-specific test): EvaluateForSession is identical before/after a successful tip for the same purchaser identity.` |
| AC-03 | `manual: complete a tip jar flow start-to-finish with no sign-in step presented.` |
| AC-04 | `manual: UX review of the confirmation state against the no-dark-patterns checklist in feature.md.` |
| AC-05 | `web/src/safety/checkWord.test.ts` (existing coverage, re-run as regression) plus `manual: submit a flagged message and confirm it is blocked.` |
| AC-06 | `tests/*.spec.ts (Playwright smoke): a full free-play round with the tip jar visible but untouched, zero interruption.` |

## Dependencies
- billing-entitlements/01 (the entitlement seam this story deliberately does
  NOT call for grants, but confirms is unaffected).
- billing-entitlements/03 (the shared Stripe plumbing this story's payment
  call uses - see the Wave Plan in implementation.md for build-order nuance).
- child-safety/01 (only if a free-text message field is offered).
- design-system (theme tokens, Guardian component, Button/AppBar contracts).
