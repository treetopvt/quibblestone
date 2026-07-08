# Story: Kid seat presets

**Feature:** Accounts & Identity  ·  **Status:** Not Started  ·  **Issue:** #TBD

## Context
[ADR 0003](../../adr/0003-admin-platform-and-family-accounts.md) Decision 1
ships kid profiles in the SAME change as the free family account (story 07),
shaped as a strict, firm-edged convenience rather than any kind of kid
identity: a "seat preset" - a named (nickname + Guardian variant) shortcut a
parent sets up once, so a kid does not have to re-type their name and re-pick
their Guardian avatar every car ride. This story builds exactly that, and only
that. See [feature.md](./feature.md) and ADR 0003's "the kid-profile boundary"
section, quoted in full in the Acceptance Criteria below - it is the
non-negotiable edge every reviewer checks this story against.

**Account-plane carve-out (ADR 0003, added 2026-07-08 after the adversarial
review, finding #5).** A preset is `accountId -> {nickname, variant}`: it
necessarily stores a chosen nickname alongside a family `AccountId`. That is
NOT a violation of the play-plane invariant - it is the ADR's explicit
account-plane carve-out. The invariant governs the PLAY plane (`Room`/
`Player`, broadcasts, telemetry): a preset join remains byte-for-byte
indistinguishable from a manual join there, exactly as AC-03 below already
requires. The preset record ITSELF lives on the ACCOUNT plane - adult-owned,
adult-consented household data, created only by a signed-in adult managing
their own family's presets, never harvested from play and never surfaced to
co-players. The two planes are firewalled from each other (see ADR 0003's
"The architecture: four layers" intro and the invariant section) - this story
stays entirely on the account-plane side of that firewall; it does not change
the play-plane boundary this story already held before the review.

## Acceptance Criteria
- [ ] AC-01: Given a signed-in family account (accounts-identity/07), when the
      account holder opens a "Manage kid presets" area on the Account page,
      then they can create, edit, and delete named presets, each holding ONLY
      a nickname (free text, same max length as any display name) and a
      Guardian variant - nothing else.
- [ ] AC-02: Given a device that holds a valid family credential
      (accounts-identity/03's `PurchaserSession`) or a family device-link token
      (accounts-identity/09, once it ships), when that device's Join or
      HostSetup screen renders `PlayerIdentityFields`, then a one-tap preset
      picker appears alongside the manual name field, listing that family's
      saved presets.
- [ ] AC-03 (the hard boundary - quoted from ADR 0003): "A kid profile is a
      seat preset, never an identity... Selecting a preset is EXACTLY
      equivalent to typing that nickname and picking that Guardian by hand.
      Nothing preset-related lands on `Room` or `Player`; the server cannot
      tell a preset join from a manual join." Given a preset is tapped, then it
      only fills the SAME `displayName`/`variant` controlled fields the manual
      path already uses and submits through the SAME `CreateRoom`/`JoinRoom`
      hub invokes - a code-level check confirms no new field reaches `Room.cs`,
      `PlayerDto`, or any broadcast.
- [ ] AC-04: Given a nickname supplied via a preset, then it passes through the
      EXACT SAME server-side safety filter (child-safety) as any manually typed
      name before it is stored or shown - a preset name is never trusted or
      pre-approved client-side, and the server independently vets it every
      time, exactly like a manual join.
- [ ] AC-05 (no per-kid anything - quoted from ADR 0003): "No per-profile
      history, no per-profile gallery (the vault is family-level), no
      per-profile entitlements, no kid login, no kid PII (a preset name is a
      nickname and passes the same safety filter as any nickname). If a future
      feature wants per-kid anything, that is a new ADR, not a story-level
      slide." A preset is purely a `{ id, label, nickname, variant }` tuple
      stored under the family account - selecting it changes nothing about
      that session's entitlements (accounts-identity/06 resolves those from the
      family credential/device link, independent of which preset, if any, was
      tapped).
- [ ] AC-06 (degraded-but-shippable path, explicit): Given the family device
      link (accounts-identity/09) has not yet shipped, when this story ships
      alone, then the preset picker appears ONLY on the signed-in parent's own
      device (the one holding the `PurchaserSession` credential) - a kid's own
      device shows no picker until story 09 lands. This is an accepted,
      documented interim state, not a blocking dependency on 09.
- [ ] AC-07 (no PII / safety): Given a preset's nickname, then it is subject to
      the exact same length cap and content-safety filter as any nickname
      (README section 6) - no kid PII (birthdate, real name, photo) is ever a
      field on a preset.

## Out of Scope
- Per-kid history, gallery, or entitlements of any kind (AC-05) - the vault
  (`keepsake-vault`) stays family-level, never per-preset.
- Kid login, kid identity, or anything that would let the server distinguish a
  preset join from a manual one (AC-03) - a future feature that wants this is a
  new ADR, not a slide in this story.
- The family device link itself (accounts-identity/09) - this story only
  reads whether one exists (once built); it does not build the link mechanism.
- Editing presets from a kid's device - presets are managed only from the
  Account page by the signed-in family-account holder.
- QR-code or any non-manual preset-management UX beyond a simple list/add/
  edit/delete.

## Technical Notes
- **api:** a small new store for presets - e.g. `api/src/Accounts/SeatPreset.cs`
  (`{ Id, Nickname, Variant }`) plus a store keyed by the family's `AccountId`
  (accounts-identity/05's spine): Table Storage `PartitionKey = accountId`,
  `RowKey = presetId`, properties `Nickname` + `Variant`. A small REST surface
  on `AccountsController` (list/create/update/delete), authorized by resolving
  the SAME `PurchaserCredentialService`-based credential this story's device
  check also uses - no new auth mechanism.
- **web:** extend `Account.tsx`'s family-account area with a presets manager
  (list + add/edit/delete, reusing the existing Guardian avatar picker
  component). `Join.tsx` and `HostSetup.tsx` (via the shared
  `PlayerIdentityFields` component, `web/src/components/PlayerIdentityFields.tsx`)
  gain a thin wrapper that checks whether `usePurchaserSession()`
  (accounts-identity/03) currently holds a credential and, if so, fetches that
  family's presets and renders a one-tap row of preset chips ABOVE the manual
  fields; tapping one calls the SAME `setValue`/`onChange` on the EXISTING
  react-hook-form fields `PlayerIdentityFields` already exposes - no new submit
  path, no new hub invoke.
- **Reuse map:** `PlayerIdentityFields` (do not fork), the `Guardian` avatar
  component, `usePurchaserSession` (accounts-identity/03), the existing
  server-side safety filter (unchanged), theme tokens (`web/src/theme.ts`).
- **Gotcha:** the picker must be presentation-only over the EXISTING
  controlled fields - resist building a parallel "preset join" invoke; there
  must be exactly ONE path into `CreateRoom`/`JoinRoom` (AC-03's guard).
- **Forward-compatible with story 09:** once the family device link ships, the
  SAME "does this device hold a family-resolving credential" check just gains
  a second credential type. Build that check as one small shared helper (e.g.
  `useFamilyDeviceCredential()` or similar) so story 09 only has to extend
  THAT helper, not touch the picker component itself.
- **Files:** new `api/src/Accounts/SeatPreset.cs` + `ISeatPresetStore.cs` +
  Table/in-memory implementations, `api/src/Controllers/AccountsController.cs`
  (preset endpoints), `web/src/pages/Account.tsx` (presets manager),
  `web/src/components/PlayerIdentityFields.tsx` (or a thin wrapper around it),
  `web/src/pages/Join.tsx`, `web/src/pages/HostSetup.tsx`.

## Tests
| AC | Test |
|---|---|
| AC-01 | `tests/QuibbleStone.Api.Tests/Accounts/SeatPresetTests.cs (new): create/edit/delete a preset under a family account; a preset never carries any field beyond nickname + variant.` |
| AC-02 | `manual: sign in as a family account on a device, open Join/HostSetup, confirm the preset picker renders with the saved presets.` |
| AC-03 | `manual + code check: tap a preset, confirm the SAME CreateRoom/JoinRoom invoke fires with the preset's nickname/variant as plain arguments; grep the diff for any new Room/Player/PlayerDto field.` |
| AC-04 | `web/src/safety/checkWord.test.ts (existing, re-run as regression) + manual: submit a preset whose nickname the filter would reject on a manual join - confirm the SAME rejection happens via the preset path.` |
| AC-05 | `manual: code read of SeatPreset.cs and the picker component - confirm no per-preset history/gallery/entitlement reference exists anywhere.` |
| AC-06 | `manual: on a device with NO PurchaserSession and NO family device-link token (accounts-identity/09 not yet shipped), confirm Join/HostSetup shows no preset picker.` |
| AC-07 | `manual: attempt to create a preset with an over-length or filtered nickname - confirm the same cap/filter as story 02's display-name validation.` |

## Dependencies
- accounts-identity/07 (the free family account presets are stored under).
- accounts-identity/05 (`AccountId` keying for the preset store).
- accounts-identity/03 (`PurchaserSession` credential - the device check this
  story ships with, before 09 lands).
- accounts-identity/09 (extends the device check to a kid's own linked device
  - NOT required for this story to ship; see AC-06's degraded-but-shippable
  path).
