# Story: Durable Data Protection key ring + token signing key posture

**Feature:** Platform & DevOps  ·  **Status:** Not Started  ·  **Issue:** #TBD

## Context
[ADR 0003](../../adr/0003-admin-platform-and-family-accounts.md) Layer 0 names this as a
foundation item: "durable Data Protection key ring (Key Vault / Blob backed) so purchaser and
operator credentials survive restarts and scale-out." Today they do not.
`api/src/Program.cs` registers `builder.Services.AddDataProtection()` bare (around line 719), which
uses the framework's DEFAULT key ring - per-instance, in-memory-backed on the local file system,
and explicitly called out as a known gap in two places already in the tree:
`api/src/Accounts/PurchaserCredentialService.cs` (lines ~18-22) and
`api/src/Accounts/MagicLinkTokenService.cs` (~line 79, the ephemeral signing-key fallback for
`Accounts:TokenSigningKey`). On a single always-on instance this rarely bites; the moment the API
scales out to a second instance, or an app restart recycles the key ring, every purchaser
sign-in credential and every operator admin-session credential minted before that moment stops
verifying - a real, silent support incident (a signed-in operator gets logged out mid-shift; a
purchaser's magic link, already emailed and sitting in an inbox, dies before it is clicked).

Separately, `Accounts:TokenSigningKey` (the magic-link HMAC signing key,
`MagicLinkTokenService.ConfigKeyName`) IS durably set today on UAT, but only because an operator
manually created the Key Vault secret and pointed the deploy workflow's "Wire magic-link email
delivery" step at it (`docs/runbooks/enable-magic-link-email.md`). A fresh environment (this
ADR's Decision 4 second environment, `platform-devops/08`) would ship with NO durable signing key
until someone remembers that manual step - exactly the piecemeal-mechanism pattern ADR 0003's
audit calls out.

This story fixes both: a durable, Azure-native Data Protection key ring for purchaser + operator
credentials, and automatic provisioning of a durable `Accounts:TokenSigningKey` so a fresh
environment needs no manual Key Vault step. See [feature.md](./feature.md).

**Revised 2026-07-08 after the adversarial review ("Credentials survive scale-out safely," ADR
0003 Security posture).** Three findings changed this story's design, all binding:

1. **The signing-key generation mechanism must not be reproducible from public inputs.** The
   review rejected this story's earlier "Recommendation: (a)" - a Bicep-native value derived
   deterministically from `guid()`/`uniqueString()` seeded on `resourceGroup().id` plus a literal
   string. `resourceGroup().id` is not a secret (it is derivable from the subscription id and
   resource group name, both discoverable), so a deterministic derivation is reproducible by
   anyone who can guess or learn those inputs - which would let an attacker FORGE a valid
   magic-link token for an allowlisted operator email and take over the operator console. AC-04 is
   rewritten below to require a CSPRNG-generated value (a `deploymentScripts` random value, or an
   out-of-band Key Vault secret an operator sets once) - never a `guid()`/`uniqueString()`
   derivation. The former "option (a)" recommendation is removed; only the CSPRNG path remains.
2. **The single-use nonce set must move to the same durable, SHARED store as the key ring.**
   `MagicLinkTokenService` consumes a token's nonce into a per-process `ConcurrentDictionary`
   (`api/src/Accounts/MagicLinkTokenService.cs`, `_consumedNonces`). Once this story makes the
   SIGNING KEY durable and shared across instances, a token that verifies on instance A but whose
   nonce-consumption lives only in instance A's memory can be replayed once per OTHER instance
   behind the load balancer - a single-use token is no longer actually single-use under
   scale-out. This affects purchaser sign-in AND operator login (both ride
   `MagicLinkTokenService`). New AC-07 below closes this.
3. **The key ring must fail CLOSED, not silently degrade, in a deployed environment.** The
   original AC-02 described a graceful fallback to the ephemeral per-instance key ring whenever
   storage/Key Vault configuration is absent. Read literally, that fallback would ALSO trigger on
   a deployed (non-Development) environment that is merely missing its config by mistake -
   silently reverting to per-instance keys there would invalidate every outstanding credential on
   the next restart or scale-out event, a self-inflicted lockout with no error to point at. AC-02
   is narrowed to local development only; a new AC-08 requires the app to refuse to start in any
   non-Development environment when the durable backing is not configured.

## Acceptance Criteria
- [ ] AC-01 (durable key ring in deployed environments): Given a deployed environment (storage
      connection string + Key Vault key configured), when the API mints or reads a Data
      Protection-protected purchaser or operator credential, then the underlying key ring persists
      to Azure Blob Storage and is encrypted at rest by a Key Vault key
      (`.PersistKeysToAzureBlobStorage(...)` + `.ProtectKeysWithAzureKeyVault(...)`, or the
      simplest durable Azure-native equivalent) - not the framework's per-instance default.
- [ ] AC-02 (local dev unchanged): Given local development or CI (no storage/Key Vault
      configuration present), then Data Protection falls back to the framework's default
      in-process key ring exactly as it does today - no new required local setup, no behavior
      change for a `dotnet run` on a laptop.
- [ ] AC-03 (survives restart and scale-out): Given the durable key ring is wired, when the API
      process restarts or a second instance is added (scale-out), then a purchaser sign-in
      credential or an operator admin-session credential minted before the restart/scale-out event
      still verifies successfully after it - a manual (or scripted) restart-and-reverify check.
- [ ] AC-04 (durable token signing key, provisioned not hand-set): Given a fresh environment
      provisioned from `infra/main.bicep` (a first-time deploy, including `platform-devops/08`'s
      second environment), then `Accounts:TokenSigningKey` is durably set automatically - a
      generated value stored as a Key Vault secret and wired as an app setting by the provisioning
      step - with NO manual "create this Key Vault secret by hand" step required, unlike today's
      `docs/runbooks/enable-magic-link-email.md` posture.
- [ ] AC-05 (no scope creep into player/room data): Given the new Blob container and/or Key Vault
      key this story provisions, then it holds ONLY Data Protection key material - never a
      player nickname, room code, session id, or any gameplay/content data.
- [ ] AC-06 (tiny footprint, documented): Given the existing 5-resource-plus Bicep footprint
      (README section 9), then this story's addition reuses the ALREADY-provisioned Storage
      Account and Key Vault (a new Blob container + a new Key Vault key, not a new resource type),
      and the addition is documented in `infra/README.md` - no VNet, no private endpoints, no
      gold-plating.

## Out of Scope
- Building the second environment itself (`platform-devops/08`) - this story's job is that BOTH
  the existing (beta/UAT) and any future environment provision the durable key ring + signing key
  the same way, automatically; standing up the second environment is the other story.
- Rotating or expiring Data Protection keys on a schedule, or any key-management ceremony beyond
  "durable and Key-Vault-protected" - ASP.NET Core's own key-ring rotation defaults are sufficient
  for a toy (CLAUDE.md preamble: not a system of record).
- Migrating the purchaser/operator credential SHAPE, purpose strings, or lifetimes
  (`PurchaserCredentialService.Purpose`, the operator scheme from `sysadmin-console/01`) - this
  story only changes WHERE the keys that protect them live, not what they protect.
- A Key Vault Managed HSM or any premium Key Vault tier - the standard tier already provisioned is
  sufficient.
- Any change to `MagicLinkTokenService`'s signing algorithm or token format - this story only
  ensures the signing key VALUE is durable and auto-provisioned, not a crypto redesign.

## Technical Notes
- **Where this lands:**
  - `api/src/Program.cs` (~line 705-719, the `AddDataProtection()` call and its surrounding
    comment block) - extend the config-presence idiom already used throughout this file
    (Telemetry, Stripe, Email): when a storage connection string AND a Key Vault key identifier
    are both configured, chain `.PersistKeysToAzureBlobStorage(new Uri(blobUri), tokenCredential)`
    and `.ProtectKeysWithAzureKeyVault(new Uri(keyVaultKeyUri), tokenCredential)` onto
    `AddDataProtection()`; otherwise leave the bare default call exactly as it is today (AC-02).
    Use the API's existing `DefaultAzureCredential` (the same one `AcsEmailSender` already uses
    for keyless ACS auth) - no new credential type, no stored key material in config beyond the
    blob URI / Key Vault key URI (neither is a secret).
  - NuGet: `Azure.Extensions.AspNetCore.DataProtection.Blobs` +
    `Azure.Extensions.AspNetCore.DataProtection.Keys` - both Microsoft-published, both small, both
    the standard pairing for this exact scenario (Blob-persisted keys, Key-Vault-wrapped).
  - `infra/main.bicep` - add a Blob container (e.g. `dataprotection-keys`) on the ALREADY
    provisioned `storage` account (a `Microsoft.Storage/storageAccounts/blobServices/containers`
    resource, same posture as the existing `StoryServes`/`PublishedTales` tables - no new storage
    account), and a Key Vault KEY (`Microsoft.KeyVault/vaults/keys`, distinct from the existing
    SECRETS already stored there - Data Protection's `ProtectKeysWithAzureKeyVault` wants a key,
    not a secret) on the already-provisioned `keyVault`. Grant the API's SystemAssigned identity
    "Storage Blob Data Contributor" on the container and "Key Vault Crypto User" on the key
    (mirroring the existing "Key Vault Secrets User" role-assignment pattern already in the file
    for App Insights). Both role assignment IDs are documented, name-resolved built-ins (do not
    hardcode a new GUID without checking Microsoft Learn for the correct built-in role id).
  - **`Accounts:TokenSigningKey` auto-provisioning (AC-04):** generate a durable random value AT
    PROVISION TIME and store it as a Key Vault secret the API already reads via the existing
    `@Microsoft.KeyVault(...)` app-setting reference pattern
    (`api/src/Accounts/MagicLinkTokenService.cs`'s `ConfigKeyName`). Two workable mechanisms, pick
    the simplest that keeps `main.bicep` declarative:
    (a) a Bicep `Microsoft.KeyVault/vaults/secrets` resource whose `value` is derived
    deterministically from `guid()`/`uniqueString()` seeded on `resourceGroup().id` plus a fixed
    string (concatenate two or three `guid()` calls for adequate length/entropy) - simplest, no
    extra resource type, matches this story's "reuse what is already provisioned" bar; or
    (b) an `Microsoft.Resources/deploymentScripts` resource that generates cryptographically
    random bytes and writes them into the Key Vault secret - more entropy, but a heavier resource
    (its own managed identity + storage for script output) that arguably over-invests for a toy.
    **Recommendation: (a)** unless review flags the entropy as insufficient - a magic-link HMAC
    key does not need CSPRNG-grade Bicep-generated entropy to be a large improvement over "unset
    until an operator remembers a runbook step." Whichever is chosen, the secret must be created
    ONLY IF ABSENT (do not overwrite an existing value on a re-deploy - that would invalidate every
    outstanding magic link at redeploy time, a regression against
    `docs/runbooks/enable-magic-link-email.md`'s existing durability promise); Bicep secret
    resources are idempotent on value but a `guid()`-derived value is stable across identical
    inputs anyway, so a redeploy naturally reproduces the same value rather than rotating it.
  - `.github/workflows/deploy.yml`'s "Wire magic-link email delivery" step already references
    `AccountsTokenSigningKey` as a Key Vault secret set out of band - once this story's Bicep
    change provisions it automatically, that step's comment should note the manual-creation
    instruction in `docs/runbooks/enable-magic-link-email.md` is now a fallback/override path
    (an operator can still set a stronger custom value), not the only path.
- **Serial-merge hazard (ADR 0003 cross-feature Wave 1):** this story's `Program.cs` edit sits in
  the SAME systemic hotspot several other ADR 0003 wave-1 stories touch (`accounts-identity/05`,
  `keepsake-vault/01`, `control-plane/01`, `sysadmin-console/04` all add service registrations to
  `Program.cs`). Land this story's `Program.cs` edit as its own small, promptly-rebased PR rather
  than batching it with unrelated registrations - see this feature's implementation.md Wave Plan
  and ADR 0003's own cross-feature table.
- **Reuse, do not reinvent:** the existing config-presence idiom (Telemetry, Stripe, Email
  wiring in `Program.cs`); the API's existing `SystemAssigned` managed identity + `DefaultAzureCredential`
  (already used keylessly for ACS email); the existing single Storage Account + Key Vault (no new
  resource types); the existing Key Vault secret app-setting reference pattern.

## Tests
| AC | Test |
|---|---|
| AC-01 | `manual: deploy with the storage/Key Vault config present; mint a purchaser credential, inspect the Blob container for a persisted key ring entry (not just the local file system).` |
| AC-02 | `tests/QuibbleStone.Api.Tests/ (existing Program/host boot tests, extended): running with no storage/Key Vault config configured builds and starts the host without error, using the default key ring.` |
| AC-03 | `manual: mint a credential, restart the app (or add a second instance in a scale-out test), confirm the SAME credential still verifies.` |
| AC-04 | `az bicep build`/validate + manual: a fresh resource group deploy has Accounts:TokenSigningKey set to a Key Vault-backed value with no manual step; a magic-link token minted and verified end to end on that fresh environment.` |
| AC-05 | `manual: inspect the Blob container / Key Vault key contents - confirm no player/room/session field is ever written there.` |
| AC-06 | `az bicep build`/validate + code review: no new resource TYPE is introduced beyond a Blob container + a Key Vault key on the already-provisioned accounts; infra/README.md documents the addition.` |

## Dependencies
- none (infra + `Program.cs` only; independent of every other ADR 0003 feature per the ADR's Wave
  1 table, though it shares `Program.cs` as a merge hazard with several of them - see Technical
  Notes).
