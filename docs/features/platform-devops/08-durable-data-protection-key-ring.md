# Story: Durable Data Protection key ring + token signing key posture

**Feature:** Platform & DevOps  ·  **Status:** Complete  ·  **Issue:** #199

> **Implementation note (branch `claude/platform-devops-08-durable-key-ring`).** Built:
> `api/src/Program.cs` `AddDataProtection` durable wiring + fail-closed guard (AC-01/02/08);
> the `IConsumedNonceStore` seam (`InMemory*` / `TableStorage*`) wired into
> `MagicLinkTokenService` with async `TryVerifyAsync` (AC-07); `infra/main.bicep` Blob
> container + Key Vault key + role assignments + a CSPRNG `deploymentScripts` signing-key
> provisioner and the `ConsumedMagicLinkNonces` table (AC-04/05/06); the Wave 0 XFF
> single-hop-trusted-edge item made explicit (`ForwardLimit = 1`, both lanes verified
> single-hop App Service). Automated coverage: host-boot tests prove AC-02 (Development
> boots on the in-process fallback) and AC-08 (a deployed env refuses to start), and
> nonce-store tests prove AC-07's cross-instance single use. AC-01/03/04's deploy-observable
> halves (inspect the Blob ring entry, restart-and-reverify, fresh-env signing key) remain
> the manual/first-deploy checks in the Tests table. `az bicep build` could not run in the
> build sandbox (network policy blocks the Bicep binary download; Bicep is not in CI) - run
> it locally before merge.

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
`MagicLinkTokenService.ConfigKeyName`) IS durably set today on UAT/beta, but only because an
operator manually created the Key Vault secret and pointed the deploy workflow's "Wire magic-link
email delivery" step at it (`docs/runbooks/enable-magic-link-email.md`). A fresh environment - such
as the isolated `quibblestone-qa-rg` lane that shipped in `platform-devops/07` (QA lane + tag-based
promotion to beta) - would ship with NO durable signing key until someone remembers that manual
step - exactly the piecemeal-mechanism pattern ADR 0003's audit calls out. Because
`platform-devops/07` now stands up more than one environment (qa alongside beta), this story's
auto-provisioning is what keeps their key rings and signing keys consistent AND isolated (each
environment's own Storage account + Key Vault backs its own key ring - qa credentials never verify
against beta and vice versa).

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
- [x] AC-01 (durable key ring in deployed environments): Given a deployed environment (storage
      connection string + Key Vault key configured), when the API mints or reads a Data
      Protection-protected purchaser or operator credential, then the underlying key ring persists
      to Azure Blob Storage and is encrypted at rest by a Key Vault key
      (`.PersistKeysToAzureBlobStorage(...)` + `.ProtectKeysWithAzureKeyVault(...)`, or the
      simplest durable Azure-native equivalent) - not the framework's per-instance default.
- [x] AC-02 (local dev unchanged, Development environment ONLY): Given `ASPNETCORE_ENVIRONMENT` is
      `Development` (local development or CI) and no storage/Key Vault configuration is present,
      then Data Protection falls back to the framework's default in-process key ring exactly as it
      does today - no new required local setup, no behavior change for a `dotnet run` on a laptop.
      This fallback applies ONLY in `Development` - see AC-08 for every other environment.
- [x] AC-03 (survives restart and scale-out): Given the durable key ring is wired, when the API
      process restarts or a second instance is added (scale-out), then a purchaser sign-in
      credential or an operator admin-session credential minted before the restart/scale-out event
      still verifies successfully after it - a manual (or scripted) restart-and-reverify check.
- [x] AC-04 (durable token signing key, CSPRNG-generated, provisioned not hand-set): Given a fresh
      environment provisioned from `infra/main.bicep` (a first-time deploy, including the
      `quibblestone-qa-rg` lane `platform-devops/07` stood up), then `Accounts:TokenSigningKey` is durably set
      automatically from a value generated by a **cryptographically secure random number
      generator** - a `Microsoft.Resources/deploymentScripts` resource that generates random bytes,
      or an out-of-band Key Vault secret an operator sets once before first deploy - stored as a Key
      Vault secret and wired as an app setting by the provisioning step, with NO manual "create
      this Key Vault secret by hand" step required as the DEFAULT path (unlike today's
      `docs/runbooks/enable-magic-link-email.md` posture). The value MUST NOT be derived
      deterministically from `guid()`/`uniqueString()` seeded on `resourceGroup().id` (or any other
      publicly-discoverable input) plus a literal string - that derivation is reproducible by
      anyone who knows or guesses the subscription id and resource group name, which would let an
      attacker forge a valid magic-link token for an allowlisted operator email and take over the
      operator console (revised 2026-07-08 after the adversarial review; see Technical Notes for
      why the earlier "option (a)" recommendation is removed, not merely deprioritized).
- [x] AC-05 (no scope creep into player/room data): Given the new Blob container and/or Key Vault
      key this story provisions, then it holds ONLY Data Protection key material - never a
      player nickname, room code, session id, or any gameplay/content data.
- [x] AC-06 (tiny footprint, documented): Given the existing 5-resource-plus Bicep footprint
      (README section 9), then this story's addition reuses the ALREADY-provisioned Storage
      Account and Key Vault (a new Blob container + a new Key Vault key, not a new resource type),
      and the addition is documented in `infra/README.md` - no VNet, no private endpoints, no
      gold-plating.
- [x] AC-07 (single-use nonce set moves to the same durable shared store as the key ring): Given
      `MagicLinkTokenService`'s single-use nonce bookkeeping (today a per-process
      `ConcurrentDictionary`, `_consumedNonces`), when the signing key becomes durable and shared
      across instances (AC-04), then the consumed-nonce set moves to the SAME durable, SHARED
      backing (e.g. the same Blob container/Table this story already provisions, or Azure Table
      Storage alongside it) so a token consumed on one instance is recognized as consumed by every
      other instance behind the load balancer - closing the replay-once-per-instance gap a shared
      signing key would otherwise open for both purchaser sign-in and operator login. Local
      development (AC-02) keeps the in-process set (no durable backing configured there).
- [x] AC-08 (fail closed in a deployed environment): Given `ASPNETCORE_ENVIRONMENT` is anything
      OTHER than `Development` (a deployed environment - beta, the platform instance, or any
      future environment) and the durable storage/Key Vault configuration for the Data Protection
      key ring is NOT present, then the API REFUSES TO START (a clear startup failure/log entry
      naming the missing configuration) rather than silently falling back to the framework's
      ephemeral per-instance key ring. A silent fallback there would invalidate every outstanding
      purchaser and operator credential on the next restart or scale-out event with no error to
      point at - a self-inflicted lockout. This does not change AC-02's local-dev behavior.

## Out of Scope
- Building the environment lanes themselves - `platform-devops/07` (QA lane + tag-based promotion
  to beta) already shipped that (the isolated `quibblestone-qa-rg` alongside the beta site). This
  story's job is that EVERY lane (beta and qa, and any future one) provisions the durable key ring +
  signing key the same way, automatically and isolated per environment; standing up the lanes is
  `platform-devops/07`, done.
- Rotating or expiring Data Protection keys on a schedule, or any key-management ceremony beyond
  "durable and Key-Vault-protected" - ASP.NET Core's own key-ring rotation defaults are sufficient
  for a toy (CLAUDE.md preamble: not a system of record).
- Migrating the purchaser/operator credential SHAPE, purpose strings, or lifetimes
  (`PurchaserCredentialService.Purpose`, the operator scheme from `sysadmin-console/01`) - this
  story only changes WHERE the keys that protect them live, not what they protect.
- A Key Vault Managed HSM or any premium Key Vault tier - the standard tier already provisioned is
  sufficient.
- Any change to `MagicLinkTokenService`'s signing algorithm or token format - this story only
  ensures the signing key VALUE is durable and auto-provisioned, not a crypto redesign (AC-07's
  nonce-store move changes WHERE the consumed-nonce set lives, not the token shape either).

## Technical Notes
- **Where this lands:**
  - `api/src/Program.cs` (~line 705-719, the `AddDataProtection()` call and its surrounding
    comment block) - extend the config-presence idiom already used throughout this file
    (Telemetry, Stripe, Email): when a storage connection string AND a Key Vault key identifier
    are both configured, chain `.PersistKeysToAzureBlobStorage(new Uri(blobUri), tokenCredential)`
    and `.ProtectKeysWithAzureKeyVault(new Uri(keyVaultKeyUri), tokenCredential)` onto
    `AddDataProtection()`; when they are ABSENT, branch on environment (AC-08): in `Development`,
    leave the bare default call exactly as it is today (AC-02); in any other environment, throw a
    clear startup exception naming the missing configuration rather than silently falling through
    to the bare default - a small `if (!builder.Environment.IsDevelopment()) throw ...` guard around
    the "config absent" branch is enough, no new abstraction needed. Use the API's existing
    `DefaultAzureCredential` (the same one `AcsEmailSender` already uses for keyless ACS auth) - no
    new credential type, no stored key material in config beyond the blob URI / Key Vault key URI
    (neither is a secret).
  - **The consumed-nonce set (AC-07):** `MagicLinkTokenService`'s `_consumedNonces`
    (`ConcurrentDictionary<string, DateTimeOffset>`) moves to the SAME durable backing this story
    already provisions - the simplest shape is a small Azure Table (e.g. a `ConsumedMagicLinkNonces`
    table alongside the Blob container, reusing the already-provisioned Storage Account, no new
    resource type) keyed by the nonce, with the expiry as a stored property so pruning past-expiry
    rows works the same way the in-memory prune does today. Add an `IConsumedNonceStore`-shaped
    seam (mirroring the `TableStorage*Store` / `InMemory*Store` config-presence pairing every other
    durable-vs-local store in this codebase already uses) so `MagicLinkTokenService` calls
    `TryConsumeAsync(nonce, expiry)` instead of `_consumedNonces.TryAdd(...)` directly - local dev
    (no storage configured) keeps today's in-memory `ConcurrentDictionary` implementation
    unchanged. This is a small, additive change to `MagicLinkTokenService`'s single-use bookkeeping,
    not a rewrite of its signing/verification logic (Out of Scope).
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
  - **`Accounts:TokenSigningKey` auto-provisioning (AC-04, revised 2026-07-08 after the
    adversarial review):** generate a durable CSPRNG-quality random value AT PROVISION TIME and
    store it as a Key Vault secret the API already reads via the existing
    `@Microsoft.KeyVault(...)` app-setting reference pattern
    (`api/src/Accounts/MagicLinkTokenService.cs`'s `ConfigKeyName`). Exactly ONE mechanism is
    acceptable - the earlier "two options, recommend (a)" framing is REMOVED because option (a) is
    a security defect, not a lighter-weight alternative:
    - A `Microsoft.Resources/deploymentScripts` resource (Azure CLI or PowerShell flavor) that
      generates cryptographically random bytes (e.g. `openssl rand -base64 32` or
      `[System.Security.Cryptography.RandomNumberGenerator]`) and writes the result into the Key
      Vault secret. This is the ONLY Bicep-native mechanism that is not reproducible from public
      inputs. Yes, it is a heavier resource (its own managed identity + a storage account for
      script output) - that cost is accepted because the alternative (a `guid()`/`uniqueString()`
      derivation seeded on `resourceGroup().id`) is reproducible by anyone who knows or guesses the
      subscription id and resource group name, which forges a valid operator magic link.
    - Equally acceptable: an OUT-OF-BAND Key Vault secret an operator sets once, by hand, using a
      real CSPRNG (e.g. `openssl rand -base64 32`) BEFORE the first deploy of a given environment -
      this is today's `docs/runbooks/enable-magic-link-email.md` posture, kept as a documented,
      supported path rather than eliminated, but it no longer satisfies AC-04's "no manual step
      required" clause by itself; pair it with a Bicep check that FAILS the deployment (or at least
      logs a clear warning) if the secret is absent, rather than silently proceeding with the
      framework's ephemeral fallback (AC-08's same fail-closed posture, applied at provision time).
    Whichever is chosen, the secret must be created/set ONLY IF ABSENT (do not overwrite an
    existing value on a re-deploy - that would invalidate every outstanding magic link at redeploy
    time, a regression against `docs/runbooks/enable-magic-link-email.md`'s existing durability
    promise); a `deploymentScripts` resource with `IsAbsent` semantics or an idempotent secret-set
    script call are both workable, but MUST NOT be a value that is trivially reproducible from
    `resourceGroup().id`, `subscription().subscriptionId`, or any other publicly-discoverable Azure
    Resource Manager input.
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
