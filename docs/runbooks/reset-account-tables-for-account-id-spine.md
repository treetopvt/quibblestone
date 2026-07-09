<!--
  Runbook: the one-time UAT table reset for accounts-identity/05 (the stable
  AccountId spine, issue #195, ADR 0003 Layer 0). Owner-run handoff: the code is
  committed; the one-time reset of three Azure Table Storage tables on the UAT
  Storage account is yours to run, once, after this change deploys.

  Prose uses hyphens, colons, parentheses - never em dashes.
-->

# Runbook: one-time account-table reset for the AccountId spine (accounts-identity/05)

`accounts-identity/05` re-keys three already-shipped stores off a hash of the
purchaser email and onto a stable, durable `AccountId` (a GUID). The on-disk
schema of all three tables changes, so **pre-existing rows written under the old
email-hash scheme are not readable by the new code and must be cleared once**.

This is a **deliberate, documented choice, not silent data loss of anything that
matters** (story AC-05): UAT holds only a near-zero number of pre-ADR-0003 rows
(QuibbleStone is a toy, not a system of record - README section 4), so a one-time
reset is an accepted migration path. There is intentionally **no** dual-write /
dual-read compatibility shim or general migration tool (story Out of Scope).

## What changes on disk

All three tables live on the **existing** UAT Storage account (README section 9) -
no new resource. The re-key:

| Table | Old key (pre-05) | New key (05) |
|---|---|---|
| `PurchaserAccounts` | `PartitionKey = RowKey = SHA-256(email)`, one row per account | a PRIMARY row (`PartitionKey = "account"`, `RowKey = <AccountId GUID>`, holds `Email` + `CreatedUtc`) plus a slim INDEX row (`PartitionKey = "emailIndex"`, `RowKey = SHA-256(email)`, holds `AccountId`) |
| `EntitlementGrants` | `PartitionKey = SHA-256(email)` | `PartitionKey = <AccountId GUID>` |
| `CloudGalleryTales` | `PartitionKey = SHA-256(email)` (the tale OwnerKey) | `PartitionKey = <AccountId GUID>` |

An old row keeps its old key, so after deploy the new code simply does not find
it (an account read misses the new index; a grant / gallery read hits an empty
`AccountId` partition). Nothing is corrupted; the old rows are just inert. The
reset removes them so no stale, unreachable rows linger.

## The reset (run once, after this change deploys to qa)

Do this once, against the **qa lane's** Storage account only (resource group
`quibblestone-qa-rg` - the isolated platform-work lane, NOT beta). Beta runs on the
rebadged `quibblestone-uat-rg` footprint, so never point this at that RG. Two
equivalent options - pick whichever you prefer:

- **Drop and recreate the three tables.** The app recreates each table lazily on
  its first write (`CreateIfNotExists`), so simply deleting the three tables is
  enough - no manual recreate step. With Azure CLI:

  ```bash
  # The qa lane RG (platform-work lane), NEVER quibblestone-uat-rg (that is beta).
  RG=quibblestone-qa-rg
  # Discover the qa Storage account name (Bicep derives it with a uniqueString suffix):
  ACCOUNT=$(az storage account list -g "$RG" --query "[0].name" -o tsv)
  echo "About to reset account tables on: $ACCOUNT (RG $RG) - confirm this is qa, not beta."
  KEY=$(az storage account keys list -g "$RG" -n "$ACCOUNT" --query "[0].value" -o tsv)
  for t in PurchaserAccounts EntitlementGrants CloudGalleryTales; do
    az storage table delete --account-name "$ACCOUNT" --account-key "$KEY" --name "$t"
  done
  ```

  Or the SAME steps in **PowerShell** (Windows `az` uses PowerShell variable +
  `foreach` syntax, not bash `RG=...` / `for ... do ... done`):

  ```powershell
  az account show --query "{subscription:name}" -o table   # sanity-check the subscription
  $RG      = "quibblestone-qa-rg"                           # qa lane ONLY (never quibblestone-uat-rg = beta)
  $ACCOUNT = az storage account list -g $RG --query "[0].name" -o tsv
  $KEY     = az storage account keys list -g $RG -n $ACCOUNT --query "[0].value" -o tsv
  Write-Host "Target: storage account '$ACCOUNT' in RG '$RG' - confirm this is qa, not beta."
  if ((Read-Host "Type yes to proceed") -eq "yes") {
    foreach ($t in "PurchaserAccounts","EntitlementGrants","CloudGalleryTales") {
      az storage table delete --account-name $ACCOUNT --account-key $KEY --name $t
    }
  } else { Write-Host "Aborted - nothing deleted." }
  ```

  (Azure may hold a deleted table name for a short interval before it can be
  recreated; the app's first write retries `CreateIfNotExists`, so a brief delay
  is harmless. If `$ACCOUNT` / `ACCOUNT` comes back empty, the qa lane has not
  provisioned its Storage account yet - wait for Wave 1's qa deploy to finish,
  then re-run.)

- **Short manual re-seed.** If any of the handful of UAT rows is worth keeping,
  note the email(s), delete the tables as above, then re-create each account via
  the normal flow (a magic-link sign-in / a fresh test purchase) so it is minted
  with a new stable `AccountId`, and re-grant / re-sync as needed. Given the
  near-zero real data, dropping is expected to be sufficient.

## What NOT to do

- **Do not run this against the beta / friends-and-family environment** to erase
  real player-facing data - the beta runs on the rebadged UAT-as-beta instance
  per ADR 0003 Decision 4, but this platform work proceeds on the **qa** lane
  (`platform-devops/07`), which is where this reset applies. Confirm you are
  pointed at the platform-work Storage account, not the beta one, before deleting.
- **Do not** try to migrate old rows into the new shape - there is deliberately
  no re-key tool (Out of Scope); the accepted path is the reset above.

## Verification

After the reset and a fresh sign-in / test purchase:

- A new account read (`GET /api/account/entitlements` while signed in) returns
  cleanly (empty grants for a brand-new account, or the grants you re-seeded).
- The cloud gallery (`GET /api/account/gallery`) lists cleanly (empty or the
  re-synced tales).
- No 5xx from a stale-schema row (the symptom the reset removes).
