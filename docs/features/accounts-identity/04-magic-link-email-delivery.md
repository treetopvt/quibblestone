# Story: Magic-link email delivery

**Feature:** Accounts & Identity  ·  **Status:** Not Started  <!-- Not Started | In Progress | Complete | Blocked | Dropped -->  ·  **Issue:** #167

## Context
The magic-link flow is built, but nothing delivers the link. accounts-identity/02
mints a one-time token (`IMagicLinkTokenService`), and accounts-identity/03's
`POST /api/accounts/signin/request` - plus, reusing the SAME plumbing,
`sysadmin-console/01`'s `POST /api/admin/login/request` - call it. But in any
non-Development environment the endpoint returns only the neutral "check your
inbox" acknowledgement and the token goes nowhere: the code itself says "the
token is delivered by email (a later story)", and only echoes the token in
`Development` so the flow is walkable locally. This is that later story.

It is now on the critical path. Verified on UAT (2026-07-04, after
`sysadmin-console` #158 shipped): the deployed API runs as Production (no
dev-token echo), no email provider is wired, and `Accounts:TokenSigningKey` is
unset (an ephemeral per-process signing key). Net effect: neither a purchaser
(sign-in / restore) nor an operator (the just-shipped back office) can complete
sign-in in a deployed environment - there is no path for the one-time link to
reach them, so the console the team just built is unusable on UAT. ADR 0002
Decision A chose magic-link email and named its cost explicitly ("you own token
issuance + email delivery"); issuance shipped, delivery did not. See
[feature.md](./feature.md) and [ADR 0002](../../adr/0002-accounts-subscriptions-and-admin.md).

## Acceptance Criteria
- [ ] AC-01: Given an email-delivery provider is configured for the environment,
      when a purchaser requests a sign-in link (`POST /api/accounts/signin/request`)
      OR an operator requests one (`POST /api/admin/login/request`), then the
      one-time magic link is emailed to the address entered and following it
      completes sign-in in a deployed (Production) environment - closing the gap
      that today leaves both flows unusable outside `Development`.
- [ ] AC-02: Given both request endpoints, then they deliver through ONE shared
      sender seam (e.g. `IEmailSender`), consumed the same way both already reuse
      the ONE `IMagicLinkTokenService` - there is no second delivery
      implementation, and the purchaser and operator flows differ only in the
      copy / link they hand the sender, never in the transport.
- [ ] AC-03 (config-presence, zero-setup default): Given NO provider is
      configured (local dev, CI, a fresh clone), then the app builds and runs
      EXACTLY as today - a no-op / dev sender is registered, the `Development`
      token echo still works for local walkthroughs, and nothing errors -
      mirroring the config-presence idiom `ITelemetrySink` / `IAiCompletionClient`
      / `IPublishedTaleStore` already use in `Program.cs`.
- [ ] AC-04 (no enumeration, preserved): Given a link is requested, then the
      endpoint's response shape and timing are the SAME whether or not an account
      / operator exists AND whether or not delivery succeeds - sending happens
      without leaking existence (the purchaser request still never reads the
      account store; the operator allowlist is still checked only at verify), and
      a delivery failure never becomes an existence oracle.
- [ ] AC-05 (secret in Key Vault, never `VITE_*`): Given the provider needs a key
      / connection string, then it is supplied per-environment from Azure Key
      Vault via an App Service app setting (the same pattern as the Stripe keys
      and the magic-link signing key) - never committed, never a `VITE_*` var,
      never logged.
- [ ] AC-06 (minimal content / minimal PII): Given a delivered email, then it
      carries only the one-time sign-in link and minimal transactional copy - no
      player nickname, room code, session id, or anything beyond the recipient's
      own email - and is sent only to the address entered, upholding README
      section 6's minimal-data posture and the anonymity firewall (purchaser /
      operator plane only, never a player).
- [ ] AC-07 (durable signing key, so delivered links actually work): Given a
      deployed environment, then `Accounts:TokenSigningKey` is a durable Key
      Vault-backed secret (not the ephemeral per-process fallback), so a delivered
      link stays valid across app restarts and scale-out. Without it a magic link
      can die the moment the app recycles, so it ships together with delivery.
- [ ] AC-08 (fail-safe on provider error): Given the provider errors or is
      unreachable, then the request endpoint still returns the neutral
      acknowledgement (never a 500, never a different response that reveals the
      failure), logs the failure WITHOUT the token, link, email body, or any
      secret, and does not retry in a way that becomes an email-bomb vector - the
      existing per-IP guards (`SignInRateLimit`, `OperatorLoginRateLimit`) remain
      the abuse boundary.
- [ ] AC-09 (verified sender identity): Given real mail must reach inboxes, then a
      verified sender identity / domain (a `no-reply@`-style from-address with SPF
      / DKIM) is configured per-environment and documented in a runbook, so
      delivery is not silently spam-filtered.

## Out of Scope
- A general transactional-email / notification system, a templating engine, or
  any email beyond the sign-in magic link (receipts, renewal notices, marketing)
  - one email, one purpose.
- i18n / localized email copy (plain strings, consistent with the app).
- Bounce / complaint handling, open / click analytics, or a delivery dashboard.
- Changing the token issue / verify logic, the neutral-response contract, or the
  operator allowlist check (accounts-identity/02, sysadmin-console/01 own those)
  - this story only adds the transport that carries the already-minted link.
- Procuring / verifying the sender domain itself beyond wiring config - that
  one-time ops task is noted in the runbook, not automated here.

## Technical Notes
- **The seam:** add a single `IEmailSender` (one method, e.g.
  `SendMagicLinkAsync(toEmail, link, purpose)`), registered in `Program.cs` via
  the SAME config-presence idiom as `ITelemetrySink` / `IAiCompletionClient` /
  `IPublishedTaleStore`: read the provider config once at startup; absent =>
  register a no-op / dev sender (preserves today's `Development` echo + a neutral
  no-op elsewhere); present => register the real provider-backed sender. Both
  `AccountsController.RequestLink` and `OperatorLoginController.RequestLink` inject
  it and call it after `IMagicLinkTokenService.Issue(...)`.
- **Provider (RECOMMENDED: Azure Communication Services Email; OPEN - see
  feature.md):** the recommendation is Azure Communication Services Email
  (Azure-native, fits the footprint; keyless via the App Service managed identity
  the app already uses for Key Vault / Stripe, so no new secret custody) - vs
  SendGrid (no Azure resource, external SaaS + API key). ACS Email adds an Email Communication Service resource + a verified
  domain to `infra/main.bicep` - a deliberate addition to the five-resource
  footprint (README section 9), justified because magic-link is now load-bearing;
  SendGrid keeps the footprint but adds a third-party dependency. The seam is
  provider-agnostic, so the choice is one registration.
- **Reuses (do not reimplement):** `IMagicLinkTokenService` (issuer, unchanged),
  the per-IP rate-limit policies (`SignInRateLimit`, `OperatorLoginRateLimit`,
  unchanged), the Key Vault + App Service app-setting pattern (Stripe / Accounts
  keys), and the config-presence registration pattern. The `IsDevelopment()`-gated
  dev-token echo stays for local walkthroughs.
- **Durable signing key (AC-07):** set `Accounts:TokenSigningKey` as a KV secret +
  an app-setting reference (mirror how `Admin__ModeToggleSecret` / the operator
  allowlist are wired in `.github/workflows/deploy.yml`). Today it is unset on UAT
  (ephemeral), so even a delivered link would break on the next recycle.
- **Deploy wiring:** the provider key + the sender from-address are app settings
  the deploy step re-applies after the always-provision replaces the array (mirror
  the `Stripe__*` / `Admin__ModeToggleSecret` wiring in `deploy.yml`).
- **Files:** new `api/src/Accounts/IEmailSender.cs` + a real sender (e.g.
  `AcsEmailSender.cs`) + a `NoOpEmailSender.cs` (dev / log sender); `Program.cs`
  (one config-presence block); `AccountsController` + `OperatorLoginController`
  (inject + call the sender); `appsettings.json` (a commented, empty `Email`
  section like the existing `Stripe` / `Accounts` sections); `infra/main.bicep`
  (provider resource + KV secret, if ACS); `.github/workflows/deploy.yml`
  (app-setting wiring). No `api/src/Rooms/` or hub changes.

## Tests
| AC | Test |
|---|---|
| AC-01 | `tests/QuibbleStone.Api.Tests/Accounts/EmailSenderTests.cs (to be created): a configured sender is invoked with the requester's email + issued link for BOTH the purchaser and operator request paths; plus manual: end-to-end on a configured env - request -> receive email -> follow link -> signed-in.` |
| AC-02 | `manual: code read - both RequestLink actions depend on the ONE IEmailSender; no second transport exists.` |
| AC-03 | `tests/QuibbleStone.Api.Tests/Accounts/EmailSenderTests.cs: with no provider configured the NoOp/dev sender is registered, RequestLink still returns the neutral result, and the Development echo path is unchanged; app builds/runs with zero email config.` |
| AC-04 | `tests/QuibbleStone.Api.Tests/... : the request response is identical for a known vs unknown email and for a sender success vs a thrown failure (no enumeration, no failure oracle).` |
| AC-05 | `manual: config/secret audit - the provider key is a Key Vault-backed app setting, never committed, never VITE_*, never logged.` |
| AC-06 | `manual: inspect a sent email - only the link + minimal copy, addressed only to the entered email; no player/room/session data.` |
| AC-07 | `manual: set a durable Accounts:TokenSigningKey, restart the app, confirm a previously issued link still verifies (and, with the ephemeral fallback, confirm it does not).` |
| AC-08 | `tests/QuibbleStone.Api.Tests/... : a sender that throws still yields the neutral 200; log assertion that no token/link/secret is written.` |
| AC-09 | `manual/runbook: a verified sender domain (SPF/DKIM) is configured and a test send lands in an inbox, not spam.` |

## Dependencies
- accounts-identity/02 (#68) - the `IMagicLinkTokenService` issuer + the request
  endpoints this story adds delivery to.
- accounts-identity/03 (#69) - the purchaser sign-in / verify flow that becomes
  usable in a deployed environment once links are delivered.
- sysadmin-console/01 (#135) - the operator login flow (same plumbing), currently
  unusable on UAT without delivery; this story unblocks it.
- infra - Azure Key Vault (the provider key + the durable signing key) and, if ACS
  Email is chosen, an Email Communication Service + verified domain in
  `infra/main.bicep`.
- child-safety - the minimal-data / anonymity posture the email content upholds
  (no player data; purchaser / operator plane only).
