<!--
  TEMPORARY tracking file. Remaining story-file edits to apply the 2026-07-08 adversarial-review
  resolutions. The AUTHORITATIVE spec for every item is the committed
  docs/adr/0003-admin-platform-and-family-accounts.md "Security posture" section + the resolved
  findings #1/#5 + the corrected Cross-feature build order table. Delete this file once the boxes
  below are all checked. Use hyphens/colons/parentheses, never em dashes.
-->

# ADR 0003 review-fixes: remaining story-file edits

Context: a five-lens adversarial review ran on 2026-07-08; the owner approved fixes (kid-gate =
option a, nickname posture = option a, scope = proceed full). The ADR was fully revised and
committed. `control-plane/01-03` fixes are DONE and committed. Four fix agents were terminated by a
session limit mid-write, so the META files below landed but the STORY-FILE ACs did not. Apply the
remaining edits from the ADR's Security posture section.

## Committed already
- [x] ADR 0003 revised (findings #1, #5, wave-plan, full Security posture section)
- [x] ADR 0002 amendment header note
- [x] ROADMAP horizon 2.5
- [x] control-plane/01, 02, 03 + feature.md + implementation.md (all fixes applied)
- [x] platform-devops/07 story (CSPRNG key ring, shared nonce store, fail-closed) - story file done
- [x] Partial meta updates: accounts implementation.md, keepsake feature.md, sysadmin
      feature.md + implementation.md (wave numbering + Decisions entries)

## Remaining STORY-FILE edits (not yet applied)

### accounts-identity
- [ ] 06: per-connection resolution stores ONLY the capability set + adult-unlock bool, never the
      email/identity string; it is a SINGLETON service (fresh hub per invocation - cannot be a hub
      field), registered in Program.cs (correct the footprint that denies Program.cs); add
      OnConnectedAsync (none exists today); note ctor + ~6 hub-test-fixture ripple + FakeHubCallerContext
      null HttpContext; correct the stale "06 touches api/src/Entitlements" hazard (it does not).
- [ ] 09: MAJOR - teen-plus gated behind an affirmative adult signal resolved at CreateRoom and
      captured on the room; family-safe by default for token-less/incognito/cleared sessions;
      host-migration cannot open the gate (property of the captured session, not current host);
      redeemed device defaults to SAFE; supersede the old "force flag / override StartRound" AC-07.
      Token = bearer secret in header/body not path; rolling TTL + re-issue on use + device-binding;
      redeem endpoint per-IP AND global rate limit + per-code burn; real code entropy floor;
      actionable linked-devices list. Add web/src/App.tsx to footprint.
- [ ] 08: add the account-plane carve-out note (preset nickname is consented household data on the
      account plane, distinct from the play-plane invariant; preset join stays indistinguishable).
- [ ] feature.md: Wave column to canonical ADR numbers (05=W1, 06/07=W2, 08/09=W3); Decisions entry.

### keepsake-vault
- [ ] 01: vault id is a bearer secret in header/body NOT the URL path (scrubber keeps path);
      rate-limit the READ endpoint too; server-mint / entropy floor (no Math.random credential);
      per-vault tale cap; FIX TTL contradiction (computed CreatedUtc+TtlDays, expire on LIST path,
      stop citing GetAsync as an exact mirror); createdUtc SERVER-STAMPED not client-supplied.
- [ ] 03: claim code entropy floor + per-code burn + global ceiling (not per-IP only) + single-use
      or TTL+rotation+revocation; code in header/body not path.
- [ ] 04: restoring a moderation takedown carries stronger friction than a user's own delete.
- [ ] implementation.md: PublishedTales W3 hazard with control-plane/03. (feature.md Decisions done.)

### sysadmin-console
- [ ] 05: define + ship the per-entry operator scope config format now (email->scopes), not just
      a flat allowlist.
- [ ] 06: log-before-act (or transactional/outbox), not best-effort after; age-based retention with
      a hard floor an operator setting cannot lower; add settings-change to the logged action set;
      encode operator-influenced fields (no dangerouslySetInnerHTML; validate email).
- [ ] 07: AC-08 STRUCTURAL not asserted - controller must not resolve slug/claim-code to email and
      must not project byline/timestamp/room content (counts + account-plane facts only; do not
      inject byline-bearing stores); resend verb through the same per-account send throttle + cap;
      takedown-restore friction; resync verb rate-limited/debounced; every verb writes a log row.
- [ ] (feature.md + implementation.md meta already updated; verify story files match after edits.)

### billing-entitlements
- [ ] 08: grant store mode-aware OR resync refuses cross-mode writes (Test resync must never touch
      live grants); reconcile by Stripe customer id / AccountId not raw email; rate-limit/debounce
      the resync endpoint; keep accounts/05 dependency.
- [ ] feature.md + implementation.md: Wave column (08=W2, not "wave 7"); note Entitlements folder
      co-occupancy with control-plane/02; Decisions entry.

### platform-devops
- [ ] 08: distinct key-ring backing store per environment; same single-hop trusted edge (XFF
      topology) required or per-IP limiters collapse; serialize with /07 on .github/workflows/deploy.yml
      (they both edit it - not disjoint).
- [ ] feature.md + implementation.md: Wave column (07/08=W1); deploy.yml collision note; Decisions.

### cross-cutting (assign to whichever story/owner fits)
- [ ] PiiScrubbingTelemetryInitializer SensitivePropertyKeys += email, accountId, vaultId,
      claimCode, token/access_token, deviceToken; forbid interpolating identifiers into exception
      messages in new Accounts/Vault/Support code. (Candidate home: platform-devops or a
      child-safety touch - pick one and note it.)
