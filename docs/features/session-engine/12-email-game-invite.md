# Story: Email a game invite to a friend

**Feature:** Session & Room Engine  ·  **Status:** Not Started  <!-- Not Started | In Progress | Complete | Blocked | Dropped -->  ·  **Issue:** #180

## Context
Today the Lobby's invite mechanism offers two channels: copy the room's join
link to the clipboard, or hand it to the OS's `navigator.share` sheet
(session-engine/04, upgraded by 06, and wired to the roster's "+ invite" slot
by 11 - all three live behind the one `useRoomInvite(code)` hook,
`web/src/pages/useRoomInvite.ts`). Both assume the sender already has a
channel open to the recipient (a text thread, a messaging app). There is no
way to just type a friend's email address into the app and have IT deliver
the invite - the sender has to leave QuibbleStone, open their own mail app,
and paste the copied link in themselves. This story adds that third channel
directly to the Lobby.

The app already has a working, config-gated email transport: `IEmailSender`
(`api/src/Accounts/IEmailSender.cs`), built for accounts-identity/04's
magic-link sign-in and reused as-is by the operator back office
(`OperatorLoginController`). This is the FIRST time anything on the anonymous
play plane reaches for that seam. It should be reused, not re-implemented -
but not blindly: `IEmailSender.SendMagicLinkAsync` and its `MagicLinkPurpose`
enum are shaped specifically for a one-time sign-in token (see that file's own
header comment: the purpose "carries NO authorization meaning... purely a
copy selector" between the purchaser and operator sign-in wording). A game
invite has no token, no account, and no sign-in behind it - it is a plain
notification carrying the same room code and join link Copy/Share already
carry. See [feature.md](./feature.md),
[04-copy-share-room-code.md](./04-copy-share-room-code.md),
[06-share-room-link.md](./06-share-room-link.md), and
[11-invite-slot-action.md](./11-invite-slot-action.md) (the invite mechanism
this extends), plus
[../accounts-identity/04-magic-link-email-delivery.md](../accounts-identity/04-magic-link-email-delivery.md)
(the email seam this reuses without reusing its sign-in-specific machinery).

## Acceptance Criteria
- [ ] AC-01: Given I am on the Lobby, when I open the email-invite option
      (an input alongside the existing Copy/Share buttons) and enter one
      friend's email address, then tapping "Send" delivers a QuibbleStone
      email to that address carrying the SAME payload Copy/Share already
      produce for this room: the tappable `/join/:code` deep link
      (`buildJoinLink`) and the human-readable "Room code: XXXX" text
      (matching `useRoomInvite`'s existing share copy) - not a second,
      drifting copy of that content.
- [ ] AC-02: Given the send action, then it is a stateless REST call (a new
      `POST` endpoint) rather than a `GameHub` method - it mutates no room
      state, requires no SignalR round-trip, and never touches
      `Room.cs`/`RoomRegistry.cs`, mirroring how Copy/Share already act on the
      room code today without any server call at all.
- [ ] AC-03: Given the delivered email, then it travels through the EXISTING
      `IEmailSender` seam (the same interface, the same `AcsEmailSender` /
      `NoOpEmailSender` pair, the same `EmailOptions` config-presence gate
      accounts-identity/04 built) - there is no second email transport
      anywhere in the codebase. It is sent via its OWN method and template
      (fixed copy, no magic link, no token): sending a game invite never
      calls `SendMagicLinkAsync` and never touches `IMagicLinkTokenService`,
      since this is not a sign-in flow.
- [ ] AC-04 (child safety / privacy, README section 6): Given the invite
      email, then its body is FIXED, templated copy (the room code + the
      join link) with no free-text field for the sender to fill in - there is
      nothing here for the profanity filter to check. The only data collected
      is the recipient's own email address, entered by the sender, used only
      for this one send and never stored - it is not PII collected about a
      player (the sender chooses to hand it over; no minor is asked for it).
      A short personal-note field is deliberately NOT part of this story (see
      Out of Scope): if one is added later, it MUST run through the same
      `IContentSafetyFilter` every submitted word already passes through
      before it can reach an email body - that is a new AC on that future
      story, not a silent addition.
- [ ] AC-05 (abuse / rate-limit): Given the send endpoint is public and
      anonymous, then it is protected by a per-IP fixed-window rate limit
      registered the same way as `PublishTalesRateLimit` and `SignInRateLimit`
      (a named policy, `[EnableRateLimiting]` on the action,
      `RateLimitPartition.GetFixedWindowLimiter` keyed on
      `Connection.RemoteIpAddress`, added to `Program.cs`'s existing
      `AddRateLimiter` block) - generous enough for a family inviting a
      handful of relatives in one sitting, tight enough that this cannot
      become a scripted email-bombing relay.
- [ ] AC-06 (degrades cleanly, no email config): Given no email provider is
      configured (`EmailOptions.IsConfigured == false`, today's default
      posture), when the Lobby's invite surface renders, then the
      email-invite option is hidden or shown clearly disabled with a brief
      note (e.g. "Email invites aren't available right now - use Copy or
      Share instead") - the player learns this BEFORE typing anything, never
      after a submit that silently does nothing and never as a raw error.
      Copy, Share, and the "+ invite" slot are completely unaffected.
- [ ] AC-07 (not gated): Given any player in the room, not only the host,
      when they use the email-invite option, then it behaves identically for
      them - mirroring session-engine/11 AC-04's reasoning (the room code is
      already visible to every player on this screen). There is no
      account/entitlement check anywhere on this path - free-tier,
      anonymous-compatible, like the rest of the invite mechanism.

## Out of Scope
- Sending to more than one recipient in a single send (a comma/newline-
  separated list). Deliberately cut to keep this story small and the
  endpoint's shape simple; a natural fast-follow if one address at a time
  proves too limiting.
- A free-text personal note / custom message field. See AC-04: explicitly
  deferred, and explicitly flagged as needing its own profanity-filter AC if
  it is ever added - not something to sneak in as a "small" addition to this
  story.
- Any change to the Copy/Share payload, wording, or the `useRoomInvite`
  behavior those already have. This story adds a THIRD channel; it does not
  touch the other two beyond extending the same hook with a new capability.
- Checking that the room code actually corresponds to a still-live room
  before sending (mirrors session-engine/06 AC-03's posture: a dead room's
  link already fails gracefully at JOIN time, not at share time). The send
  endpoint only shape-validates the code against the alphabet/length
  `RoomRegistry` mints from - it never queries `RoomRegistry`.
- Host-only gating. AC-07 deliberately keeps this open to any player,
  matching session-engine/11's reasoning for the roster invite slot.
- A generic "email this URL to someone" utility. The endpoint sends exactly
  ONE fixed template (room code + join link); it is not a free-form email
  relay - see Technical Notes for why that distinction matters.
- Delivery-status tracking, a "resend" action, retry logic, or a count of how
  many invites were sent (mirrors session-engine/11's own scope cut on not
  counting invites).

## Technical Notes
- **Shape (AC-02): a new stateless REST endpoint, not a hub method.** Every
  existing email send in this codebase (`AccountsController`,
  `OperatorLoginController`) is REST, never the hub - `GameHub.cs` has no
  email dependency today and should not gain one here. Recommend a small new
  controller (e.g. `api/src/Controllers/EmailInviteController.cs`) with one
  action, e.g. `POST /api/invite/email`, body `{ roomCode, toEmail }` ONLY.
  Exact route naming is the builder's call; this doc assumes `/api/invite/email`
  for concreteness, matching the flat, concern-named-segment convention already
  used by `/api/tales`, `/api/billing/*`, `/api/accounts/*`.
- **The server builds the join link itself; never accept a client-supplied
  URL to embed (a security note, not a style preference).** A templated,
  "from QuibbleStone" email whose link comes from client input is an
  open-relay / phishing smell - an attacker could get an official-looking
  email to point anywhere. Instead: shape-validate `roomCode` against the
  SAME alphabet and length `RoomRegistry` mints codes from (`CodeAlphabet`,
  `CodeLength = 4`, `api/src/Rooms/RoomRegistry.cs` - today `private const`,
  so either mirror them here the way `Join.tsx` already mirrors them
  client-side, or promote them to a small public helper as a light optional
  refactor so there is one source of truth instead of a third copy) and
  reject anything else with a friendly 400 - no `RoomRegistry` lookup needed
  (see Out of Scope). Build the link server-side as `{base}/join/{code}`,
  where `{base}` reuses an EXISTING "public web app origin" config value
  rather than adding a third one: `EmailOptions.LinkBaseUrl`
  (`api/src/Accounts/EmailOptions.cs`, already the magic link's base) is the
  natural fit since this story is already extending that seam;
  `PublishedTales:WebAppBaseUrl` (`PublishedTalesController`) is the other
  existing precedent if a builder prefers to share that one instead.
- **Reuse `IEmailSender`; grow it with a second method - do not reuse
  `SendMagicLinkAsync` / `MagicLinkPurpose` (AC-03).** Add e.g.
  `SendGameInviteAsync(toEmail, joinLink, roomCode, cancellationToken)` to the
  interface, implemented by the SAME two DI-resolved classes: `AcsEmailSender`
  gains its own fixed subject/body template (mirror `useRoomInvite.share()`'s
  existing text/url split, `web/src/pages/useRoomInvite.ts` - the same "Join
  my QuibbleStone game! Room code: XXXX" line, plus the link as its own
  tappable element, so the email reads like the same product as Copy/Share,
  not a different copy deck); `NoOpEmailSender` logs the same neutral,
  no-recipient/no-link debug breadcrumb it already logs for magic links. This
  keeps exactly ONE interface, ONE config-presence gate in `Program.cs`, and
  ONE pair of implementations - just a second, independent send method.
- **The availability signal for AC-06** should mirror `GET /api/billing/products`'s
  `{ enabled, ... }` posture (`BillingController.Products`)
  rather than a new pattern: surface `EmailOptions.IsConfigured` somewhere the
  Lobby reads BEFORE rendering the control (a field on a lightweight GET, or
  a small dedicated status endpoint if nothing existing fits), and have the
  web side read it once and fail toward "hidden/disabled", mirroring
  `stripeModeClient.ts`'s narrow-and-fail-gracefully posture - never a bare
  failed POST as the only signal.
- **Rate limit (AC-05):** a new `EmailInviteRateLimit` static class mirroring
  `PublishTalesRateLimit` / `SignInRateLimit` byte-for-byte in shape
  (`PolicyName`, `PermitLimit`, `Window`, a `PartitionKey(HttpContext)` keyed
  on `RemoteIpAddress`, falling back to a shared "unknown" bucket), registered
  in `Program.cs`'s existing `AddRateLimiter` block alongside its siblings.
  Per-IP only, deliberately: every limiter in this codebase today is per-IP,
  none are per-room, and a second room-scoped counter would add real state
  (a room-keyed table, cleanup on room expiry) for a risk the per-IP guard
  already bounds at this game's scale. Revisit only if real signal says
  otherwise.
- **Web:** extend `useRoomInvite(code)` (`web/src/pages/useRoomInvite.ts`,
  session-engine/11) with the new capability rather than a parallel hook, so
  there is still exactly one invite-action hook - e.g. an `emailAvailable`
  flag and a `sendEmail(toEmail)` action that resolves a friendly, narrowed
  outcome rather than throwing (mirror `publishTale.ts` / `stripeModeClient.ts`'s
  "never throw, resolve a typed result" posture). A new thin REST-client
  module (e.g. `web/src/pages/emailInvite.ts`, mirroring `publishTale.ts`'s
  shape) is a reasonable split point if the hook would otherwise grow a raw
  `fetch` call. `Lobby.tsx`'s `ShareWidget` (the existing Copy/Share row) is
  the natural place for the new input + "Send" control - add it as a third
  element, not a replacement for Copy/Share.
- **No hub, no `Room.cs` / `RoomRegistry.cs` / `GameHub.cs` edits (AC-02).**
  This keeps the story fully decoupled from the serial `GameHub.cs` chain the
  rest of this feature (and `group-play`) grows, so it can be built without
  competing for that file.
- No em dashes; hyphens/colons/parentheses only, matching house style.

## Tests
| AC | Test |
|---|---|
| AC-01 | `tests/QuibbleStone.Api.Tests/EmailInviteControllerTests.cs`: a valid `{roomCode, toEmail}` POST invokes the configured `IEmailSender` with the recipient, the server-built join link, and the room code; plus manual: send to a real inbox on a configured environment and confirm the email arrives with a tappable `/join/<code>` link and the code. |
| AC-02 | code review: the new controller has no dependency on `IHubContext<GameHub>`, `RoomRegistry`, or `Room` - no room-state touch anywhere on this path. |
| AC-03 | code review + a sender test (e.g. `tests/QuibbleStone.Api.Tests/Accounts/EmailInviteSenderTests.cs`): `AcsEmailSender` / `NoOpEmailSender` gain the new method with its own template; no call site passes a `MagicLinkPurpose` value for a game invite. |
| AC-04 | code review: the request DTO carries only `roomCode` + `toEmail` - no free-text field; the email template is fixed copy with two substitutions. |
| AC-05 | `tests/QuibbleStone.Api.Tests/EmailInviteRateLimitTests.cs` (mirrors `PublishTalesRateLimitTests.cs`): `PartitionKey` buckets by IP; manual: rapid repeated sends from one IP trip a 429. |
| AC-06 | manual: with no `Email:*` config, load the Lobby and confirm the email-invite control is hidden or disabled with the explanatory note before any input is possible; a Vitest test for whichever pure "is email available" narrowing function backs the web check (mirroring `useRoomInvite.test.ts`'s coverage of `resolveOrigin`). |
| AC-07 | manual: as a non-host player, use the email-invite option and confirm it behaves identically to the host's. |

## Dependencies
- session-engine/04-copy-share-room-code (the share widget this extends; Complete)
- session-engine/06-share-room-link (the `/join/:code` deep-link shape this reuses; Complete)
- session-engine/11-invite-slot-action (the shared `useRoomInvite` hook this grows; Complete)
- accounts-identity/04-magic-link-email-delivery (the `IEmailSender` seam,
  `EmailOptions`, and the two DI-resolved senders this reuses; Complete) - all
  four dependencies have already shipped, so this story is immediately
  buildable with nothing blocking it.
