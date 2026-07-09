// ----------------------------------------------------------------------------
//  main.bicep - QuibbleStone dev infrastructure ("get it up first, keep it tiny",
//  README section 9).
//
//  Provisions the five resources the charter calls for, plus the App Service
//  Plan that hosting the API on App Service requires:
//
//    1. Static Web App        - hosts the React + Vite web client.
//    2. App Service (+ Plan)  - hosts the single ASP.NET Core app (API + hub).
//    3. Azure SignalR Service - real-time backplane for production scale-out.
//    4. Storage Account       - Table (templates, entitlements) + Blob (images).
//    5. Key Vault             - secrets (Stripe, AI provider keys) later.
//
//  Bar: "deploys cleanly." Intentionally NOT gold-plated - no VNet, no private
//  endpoints, no diagnostics yet. Cost posture: everything is Free/near-zero
//  except the App Service Plan, whose SKU is a parameter (appServicePlanSku)
//  that DEFAULTS to F1 Free ($0) so UAT costs nothing to stand up - scale up to
//  B1+ for Always On + reliable WebSockets when you need a solid demo.
//      az bicep build --file infra/main.bicep                       # validate
//      az deployment group create -g <rg> -f infra/main.bicep \     # deploy
//        -p infra/main.uat.bicepparam
//  Provision push-button from GitHub instead: Actions -> Provision UAT.
// ----------------------------------------------------------------------------

@description('Azure region for all resources. Defaults to the resource group location.')
param location string = resourceGroup().location

@description('Short prefix used to name resources.')
param namePrefix string = 'quibblestone'

@description('Environment moniker (dev, qa, uat, prod). Composes resource names, so each lane in its own resource group gets distinct, isolated resources.')
param environmentName string = 'uat'

@description('App Service Plan SKU. F1 (Free, $0) is the default so UAT costs nothing to stand up; scale up to B1/B2/S1 for Always On + reliable WebSockets (a working real-time game). Change here, in the *.bicepparam file, or pass -p appServicePlanSku=B1 at deploy time.')
@allowed([
  'F1' // Free    - $0, no Always On, 60 CPU-min/day, limited WebSockets (fine for kicking the tyres)
  'B1' // Basic   - Always On + WebSockets; realistic floor for a live multiplayer app
  'B2' // Basic   - more headroom
  'S1' // Standard - deployment slots, autoscale
])
param appServicePlanSku string = 'F1'

@description('Provision the Azure Communication Services (ACS) Email footprint (accounts-identity/04) for magic-link delivery. OFF by default so the core footprint stays tiny (README section 9): a fresh clone or a UAT that has not turned email on gets NONE of these resources and the API runs on the NoOpEmailSender, exactly as before. The deploy pipeline flips this to true from vars.EMAIL_ENABLED (.github/workflows/deploy.yml).')
param enableEmail bool = false

@description('ACS data residency for the email resources (where message-processing metadata lives). Only used when enableEmail is true. Valid ACS values include United States, Europe, Australia, UK, Asia Pacific, etc.')
param emailDataLocation string = 'United States'

// A short, deterministic suffix keeps globally-unique names (storage, vault,
// SignalR, web apps) stable per resource group without hardcoding them.
var suffix = uniqueString(resourceGroup().id)
var baseName = '${namePrefix}-${environmentName}'

// Derive the SKU tier from the name, and enable the "paid tier" niceties only
// when we are actually on a paid tier: Always On and the /health check both
// require Basic+ (they are rejected on Free F1).
var appServicePlanTiers = {
  F1: 'Free'
  B1: 'Basic'
  B2: 'Basic'
  S1: 'Standard'
}
var appServicePlanTier = appServicePlanTiers[appServicePlanSku]
var isPaidPlan = appServicePlanSku != 'F1'

// Tags let the deploy pipeline discover these resources in the group without
// hardcoding the uniqueString-suffixed names (see .github/workflows/deploy.yml).
var commonTags = {
  app: namePrefix
  env: environmentName
}

// Storage / Key Vault names: lowercase, no dashes, length-limited.
var storageAccountName = take(toLower('${namePrefix}${environmentName}${suffix}'), 24)
var keyVaultName = take(toLower('${namePrefix}-${environmentName}-${suffix}'), 24)

// platform-devops/08: the Key Vault secret name the durable magic-link signing key
// lives under (AC-04). Kept in one place so the app-setting KV reference above and
// the create-if-absent deploymentScript below cannot drift.
var tokenSigningKeySecretName = 'AccountsTokenSigningKey'

// --- 2a. App Service Plan (Linux) -------------------------------------------
// Hosting the API on App Service requires a plan.
resource appServicePlan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: '${baseName}-plan'
  location: location
  tags: commonTags
  sku: {
    name: appServicePlanSku
    tier: appServicePlanTier
  }
  kind: 'linux'
  properties: {
    reserved: true // required for Linux plans
  }
}

// --- 2b. App Service - the single ASP.NET Core app (REST API + SignalR hub) --
resource apiApp 'Microsoft.Web/sites@2024-04-01' = {
  name: '${baseName}-api-${suffix}'
  location: location
  tags: commonTags
  identity: {
    type: 'SystemAssigned' // used later to read Key Vault secrets via RBAC
  }
  properties: {
    serverFarmId: appServicePlan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      // SignalR's WebSocket transport is off by default on App Service - turn it
      // on so the real-time hub uses WebSockets rather than falling back to
      // long-polling (README section 4).
      webSocketsEnabled: true
      // Always On and the health check are paid-tier features (rejected on Free
      // F1). They switch on automatically when you scale the plan up.
      alwaysOn: isPaidPlan
      healthCheckPath: isPaidPlan ? '/health' : null
      // story-selection/04 (anonymous serve log): carry the Storage connection so
      // the API's telemetry sink writes anonymous, PII-free "template served"
      // events to the StoryServes table (see api/src/Program.cs, which reads
      // Telemetry:StorageConnectionString). The value is composed at DEPLOY time
      // from the storage account's own key (storage.listKeys()) - it is NEVER a
      // committed literal secret. Double-underscore is ASP.NET Core's config
      // convention for the nested key "Telemetry:StorageConnectionString".
      appSettings: [
        {
          name: 'Telemetry__StorageConnectionString'
          value: 'DefaultEndpointsProtocol=https;AccountName=${storage.name};AccountKey=${storage.listKeys().keys[0].value};EndpointSuffix=${environment().suffixes.storage}'
        }
        // keepsake-gallery/04 (shareable tale link): carry the SAME Storage account's
        // connection so the API's published-tale store writes each host-published,
        // already-filtered public tale to the PublishedTales table (see
        // api/src/Program.cs, which reads PublishedTales:StorageConnectionString and
        // falls back to a DISABLED store - the feature simply OFF - when it is
        // absent). Composed at DEPLOY time from the storage account's own key, NEVER
        // a committed literal secret (the same posture as Telemetry above). Without
        // this setting the app still runs; the public-link feature is just switched off.
        {
          name: 'PublishedTales__StorageConnectionString'
          value: 'DefaultEndpointsProtocol=https;AccountName=${storage.name};AccountKey=${storage.listKeys().keys[0].value};EndpointSuffix=${environment().suffixes.storage}'
        }
        // keepsake-gallery/04 (AC-02/AC-06): the web app base the public tale page's
        // "Play QuibbleStone" / "Start your own tale" CTAs link into (the create/join
        // flow). Composed from the Static Web App's OWN default host name - never a
        // hardcoded literal - so a received link converts straight into a new session.
        {
          name: 'PublishedTales__WebAppBaseUrl'
          value: 'https://${webApp.properties.defaultHostname}'
        }
        // platform-devops/04 (operational observability): surface the App Insights
        // connection string to the API. Sourced from KEY VAULT (a KV reference, not
        // a committed literal and never a VITE_ var - AC-01/AC-05): the value is a
        // pointer to the secret, resolved at runtime by the App Service's
        // SystemAssigned identity (granted "Key Vault Secrets User" below). The
        // ASP.NET Core App Insights SDK reads this exact app-setting name
        // automatically (see api/src/Program.cs AddApplicationInsightsTelemetry).
        // This is the FIRST real consumer of the provisioned Key Vault (CLAUDE.md
        // section 10).
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: '@Microsoft.KeyVault(SecretUri=${appInsightsConnectionSecret.properties.secretUri})'
        }
        // platform-devops/08 (durable Data Protection key ring, AC-01/AC-08): the
        // two NON-SECRET pointers the API chains onto AddDataProtection so the key
        // ring persists to Blob Storage and is wrapped by a Key Vault key (keyless
        // via the API's managed identity - see api/src/Program.cs). Composed at
        // DEPLOY time from THIS lane's own storage account + vault, so each lane's
        // key ring backing is DISTINCT (a qa-minted credential never verifies against
        // beta). Their PRESENCE is what lets the app start in a deployed environment
        // (AC-08 fails closed when they are absent); neither value is a secret.
        {
          name: 'DataProtection__KeyRingBlobUri'
          value: '${storage.properties.primaryEndpoints.blob}${dataProtectionKeysContainer.name}/keyring.xml'
        }
        {
          name: 'DataProtection__KeyVaultKeyUri'
          value: dataProtectionKey.properties.keyUriWithVersion
        }
        // platform-devops/08 (AC-07): the storage connection the durable, SHARED
        // consumed-nonce table (ConsumedMagicLinkNonces) rides, so a single-use magic
        // link consumed on one instance is rejected on every other instance. Composed
        // at DEPLOY time from this lane's own storage key (NEVER a committed literal),
        // the same posture as Telemetry above. Reuses the already-provisioned Storage
        // Account (a new table, not a new resource type).
        {
          name: 'Accounts__StorageConnectionString'
          value: 'DefaultEndpointsProtocol=https;AccountName=${storage.name};AccountKey=${storage.listKeys().keys[0].value};EndpointSuffix=${environment().suffixes.storage}'
        }
        // platform-devops/08 (AC-04): the durable magic-link HMAC signing key, read
        // as a Key Vault reference (the API identity is already "Key Vault Secrets
        // User" on this vault). The secret VALUE is CSPRNG-generated and set
        // create-if-absent by the deploymentScript below - never derived from any
        // publicly-discoverable input (resourceGroup().id / uniqueString), which would
        // be reproducible and let an attacker forge an operator magic link. So a
        // delivered link survives an app recycle / scale-out with no manual Key Vault
        // step. An operator MAY still set a stronger custom value out of band (the
        // create-if-absent script leaves an existing secret untouched).
        {
          name: 'Accounts__TokenSigningKey'
          value: '@Microsoft.KeyVault(VaultName=${keyVault.name};SecretName=${tokenSigningKeySecretName})'
        }
      ]
    }
  }
}

// --- 1. Static Web App - the React + Vite client ----------------------------
resource webApp 'Microsoft.Web/staticSites@2024-04-01' = {
  name: '${baseName}-web-${suffix}'
  location: location
  tags: commonTags
  sku: {
    name: 'Free'
    tier: 'Free'
  }
  properties: {}
}

// --- 3. Azure SignalR Service - real-time backplane (Default service mode) ---
resource signalR 'Microsoft.SignalRService/signalR@2024-03-01' = {
  name: '${baseName}-signalr-${suffix}'
  location: location
  sku: {
    name: 'Free_F1'
    tier: 'Free'
    capacity: 1
  }
  kind: 'SignalR'
  properties: {
    features: [
      {
        flag: 'ServiceMode'
        value: 'Default'
      }
    ]
  }
}

// --- 4. Storage Account - Table (templates, entitlements) + Blob (images) ----
resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageAccountName
  location: location
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
    supportsHttpsTrafficOnly: true
  }
}

// story-selection/04 (anonymous serve log): the Table service + the StoryServes
// table the API's telemetry sink writes to. One tiny, PII-free "template served"
// entity per round start lands here (partitioned by template id for cheap
// per-template frequency reads). This is the "Table" half of the Storage account
// the footprint already provisions (README section 9); the sink creates the table
// on first write too, so this just makes the footprint explicit in IaC.
resource storageTableService 'Microsoft.Storage/storageAccounts/tableServices@2023-05-01' = {
  parent: storage
  name: 'default'
}

resource storyServesTable 'Microsoft.Storage/storageAccounts/tableServices/tables@2023-05-01' = {
  parent: storageTableService
  name: 'StoryServes'
}

// story-selection/05 (per-tale thumbs feedback, issue #95): the StoryFeedback
// table the API's telemetry sink upserts one anonymous, PII-free curation vote
// into per player per round (PartitionKey = template id, RowKey = the opaque
// client-minted vote id, so a changed vote overwrites - see
// TableStorageTelemetrySink.cs). Joinable against StoryServes by template id for
// a like-rate-per-serve report (AC-06). Sits alongside StoryServes on the SAME
// storage account's Table service; the sink also creates it on first write, so
// this just makes the footprint explicit in IaC (unvalidated locally if the
// az/bicep CLI is absent - see this story's build notes).
resource storyFeedbackTable 'Microsoft.Storage/storageAccounts/tableServices/tables@2023-05-01' = {
  parent: storageTableService
  name: 'StoryFeedback'
}

// keepsake-gallery/04 (shareable tale link, issue #66): the PublishedTales table
// the API's PublishedTalesController stores each host-published, already-filtered
// public tale into (PartitionKey = RowKey = an unguessable slug, so the public
// page read is a single-lookup point read - see TableStoragePublishedTaleStore).
// Only the assembled, family-safe story + in-session nicknames + a TTL live here -
// NEVER raw submissions or PII (AC-03/AC-05). It rides the SAME storage account's
// Table service as the two telemetry tables above (README section 9 - no new
// resource); the store also creates it on first write, so this just makes the
// footprint explicit in IaC (unvalidated locally if the az/bicep CLI is absent -
// see this story's build notes). With NO connection string wired to the API the
// feature is simply OFF (the disabled store), so this table is only ever touched
// in a deployed environment.
resource publishedTalesTable 'Microsoft.Storage/storageAccounts/tableServices/tables@2023-05-01' = {
  parent: storageTableService
  name: 'PublishedTales'
}

// platform-devops/08 (AC-07): the ConsumedMagicLinkNonces table the API's durable,
// SHARED single-use nonce store writes to (one row per consumed magic-link nonce -
// the opaque jti + its expiry, NEVER a subject / email / PII, AC-05). It makes a
// single-use link single-use FLEET-wide, not just per-instance, once the signing key
// is durable and shared. Rides the SAME storage account's Table service as the tables
// above (no new resource type); the store also creates it on first write, so this
// just makes the footprint explicit in IaC. Only ever touched in a deployed
// environment (local dev keeps the in-memory nonce set - see api/src/Program.cs).
resource consumedMagicLinkNoncesTable 'Microsoft.Storage/storageAccounts/tableServices/tables@2023-05-01' = {
  parent: storageTableService
  name: 'ConsumedMagicLinkNonces'
}

// --- 5. Key Vault - secrets (Stripe keys, AI provider keys) once they exist --
resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: keyVaultName
  location: location
  properties: {
    sku: {
      family: 'A'
      name: 'standard'
    }
    tenantId: tenant().tenantId
    enableRbacAuthorization: true // grant the API identity access via RBAC later
    enableSoftDelete: true
  }
}

// --- 6. Operational observability (platform-devops/04) -----------------------
// Workspace-based Application Insights: the API's OPERATIONAL telemetry pipeline
// (unhandled exceptions, failed requests, request rate + duration, dependency
// calls - see api/src/Program.cs). This is DISTINCT from the anonymous serve log
// that rides the Storage table above (story-selection/04); they coexist. Kept
// tiny (README section 9): two resources (the workspace App Insights requires +
// the component), the connection string stashed as a Key Vault secret, and one
// role assignment so the API can read it. No action group / alert stack is
// provisioned - the alert seam is DOCUMENTED in infra/README.md (AC-07).

// 6a. Log Analytics workspace - workspace-based App Insights sends its telemetry
//     here (the classic per-component store is retired). PerGB2018 is the standard
//     pay-as-you-go tier; 30-day retention keeps a dev/UAT footprint cheap.
resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: '${baseName}-logs-${suffix}'
  location: location
  tags: commonTags
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
  }
}

// 6b. Application Insights component (workspace-based via WorkspaceResourceId).
resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: '${baseName}-appi-${suffix}'
  location: location
  tags: commonTags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalytics.id
  }
}

// 6c. Store the App Insights connection string as a Key Vault SECRET (AC-01): the
//     API reads it via the KV reference in its appSettings above, so the value is
//     never a committed literal and never a VITE_ var. This is the first real
//     consumer of the provisioned-but-unused Key Vault (CLAUDE.md section 10).
resource appInsightsConnectionSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'AppInsightsConnectionString'
  properties: {
    value: appInsights.properties.ConnectionString
  }
}

// 6d. Grant the API App Service's SystemAssigned identity the built-in "Key Vault
//     Secrets User" role, scoped to the vault, so its KV reference to the App
//     Insights connection string resolves (the vault has enableRbacAuthorization,
//     so access is via RBAC, not access policies). roleDefinitionId
//     4633458b-17de-408a-b874-0445c86b69e6 is the fixed built-in id for that role.
var keyVaultSecretsUserRoleId = '4633458b-17de-408a-b874-0445c86b69e6'
resource apiKeyVaultSecretsUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, apiApp.id, keyVaultSecretsUserRoleId)
  scope: keyVault
  properties: {
    principalId: apiApp.identity.principalId
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultSecretsUserRoleId)
    principalType: 'ServicePrincipal'
  }
}

// --- 7. Magic-link email delivery (accounts-identity/04) ---------------------
// Azure Communication Services (ACS) Email - the transport the purchaser sign-in
// and operator-login magic links ride (see api/src/Accounts/AcsEmailSender.cs).
// GATED behind enableEmail (default false), so the DEFAULT footprint is unchanged
// (README section 9) and this whole block simply does not exist until email is
// turned on. When on it provisions a fully KEYLESS, ZERO-DNS email path:
//   - an Email Communication Service + an AZURE-MANAGED domain (a *.azurecomm.net
//     sender Azure creates and verifies for you - no SPF/DKIM records to add). The
//     managed-domain sender is FIXED at DoNotReply@<generated>.azurecomm.net: Azure
//     does NOT allow custom sender usernames (e.g. no-reply) on a managed domain -
//     a friendly no-reply@quibblestone.com needs the custom-domain upgrade below;
//   - the Communication Services resource the app's EmailClient targets, linked to
//     that domain so it can send from it.
// The app authenticates KEYLESS via its existing SystemAssigned managed identity
// (AcsEmailSender prefers the endpoint path, DefaultAzureCredential), so there is NO
// provider secret to store (AC-05). The identity's "send email" role grant is applied
// by the deploy workflow (name-resolved, so no role GUID is hardcoded here), and the
// endpoint + sender address are OUTPUT below for that workflow to wire onto the API.
//
// A verified CUSTOM sender domain (no-reply@quibblestone.com with real SPF/DKIM, for
// production deliverability - AC-09) is a deliberate operator UPGRADE, not automated
// here: it needs DNS records + a verification wait that cannot live in Bicep (story
// Out of Scope). Set it up in ACS and point vars.EMAIL_FROM_ADDRESS at it; see
// docs/runbooks/enable-magic-link-email.md. The Azure-managed domain is the always-safe
// default so email works end to end the moment enableEmail is true.

// 7a. Email Communication Service - the container for the sending domain. ACS is a
//     GLOBAL resource type (location must be 'global'); dataLocation sets residency.
resource emailService 'Microsoft.Communication/emailServices@2023-04-01' = if (enableEmail) {
  name: '${baseName}-email-${suffix}'
  location: 'global'
  tags: commonTags
  properties: {
    dataLocation: emailDataLocation
  }
}

// 7b. Azure-managed sending domain (a *.azurecomm.net domain Azure provisions AND
//     verifies automatically - no customer DNS). The child name MUST be the literal
//     'AzureManagedDomain' for a managed domain. userEngagementTracking stays
//     Disabled - no open/click tracking, upholding the minimal-data posture (README
//     section 6).
resource emailDomain 'Microsoft.Communication/emailServices/domains@2023-04-01' = if (enableEmail) {
  parent: emailService
  name: 'AzureManagedDomain'
  location: 'global'
  tags: commonTags
  properties: {
    domainManagement: 'AzureManaged'
    userEngagementTracking: 'Disabled'
  }
}

// 7c. The Communication Services resource the app's EmailClient talks to (keyless via
//     the API managed identity). linkedDomains binds the managed domain so this
//     resource is allowed to send from it. hostName (output below) is the endpoint the
//     API reads as Email:Endpoint.
resource communicationService 'Microsoft.Communication/communicationServices@2023-04-01' = if (enableEmail) {
  name: '${baseName}-acs-${suffix}'
  location: 'global'
  tags: commonTags
  properties: {
    dataLocation: emailDataLocation
    linkedDomains: [
      emailDomain.id
    ]
  }
}

// --- 8. Durable Data Protection key ring + signing key (platform-devops/08) ---
// ADR 0003 Layer 0 foundation item ("credentials survive scale-out safely", #199).
// Reuses the ALREADY-provisioned Storage Account + Key Vault (a new Blob container +
// a new Key Vault KEY + a small provisioning script - NOT a new resource TYPE, README
// section 9 / AC-06). Nothing here holds gameplay data - ONLY Data Protection key
// material and the magic-link nonce table (AC-05).

// 8a. The Blob container the Data Protection key ring persists to (AC-01). The API's
//     .PersistKeysToAzureBlobStorage writes the single key-ring blob (keyring.xml)
//     here, so purchaser + operator credentials survive an app restart / scale-out.
//     A dedicated container keeps key material isolated from every other blob.
resource storageBlobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storage
  name: 'default'
}

resource dataProtectionKeysContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: storageBlobService
  name: 'dataprotection-keys'
  properties: {
    publicAccess: 'None' // key material is never publicly reachable
  }
}

// 8b. The Key Vault KEY that ENCRYPTS the key ring at rest (AC-01). This is a KEY
//     (wrap/unwrap), distinct from the SECRETS already in this vault - Data
//     Protection's .ProtectKeysWithAzureKeyVault wraps the ring's master key with it.
resource dataProtectionKey 'Microsoft.KeyVault/vaults/keys@2023-07-01' = {
  parent: keyVault
  name: 'dataprotection'
  properties: {
    kty: 'RSA'
    keySize: 2048
    keyOps: [
      'wrapKey'
      'unwrapKey'
    ]
  }
}

// 8c. Grant the API's SystemAssigned identity the two KEYLESS data-plane roles it
//     needs (mirroring the "Key Vault Secrets User" grant already in this file for
//     App Insights): "Storage Blob Data Contributor" on the key-ring container so it
//     can read/write the ring, and "Key Vault Crypto User" on the vault so it can
//     wrap/unwrap with the key. Both role ids are the documented built-ins
//     (verified against Microsoft Learn - Azure built-in roles), scoped as tightly
//     as the story asks (the container, not the whole account).
var storageBlobDataContributorRoleId = 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'
var keyVaultCryptoUserRoleId = '12338af0-0e69-4776-bea7-57ae8d297424'
var keyVaultSecretsOfficerRoleId = 'b86a8fe4-44ce-4948-aee5-eccb2c155cd7'

resource apiKeyRingBlobContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(dataProtectionKeysContainer.id, apiApp.id, storageBlobDataContributorRoleId)
  scope: dataProtectionKeysContainer
  properties: {
    principalId: apiApp.identity.principalId
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageBlobDataContributorRoleId)
    principalType: 'ServicePrincipal'
  }
}

resource apiKeyVaultCryptoUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, apiApp.id, keyVaultCryptoUserRoleId)
  scope: keyVault
  properties: {
    principalId: apiApp.identity.principalId
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultCryptoUserRoleId)
    principalType: 'ServicePrincipal'
  }
}

// 8d. Auto-provision the durable magic-link signing key (AC-04), CSPRNG-generated,
//     CREATE-IF-ABSENT. This is the ONLY Bicep-native mechanism that is NOT
//     reproducible from public inputs: the earlier "guid(resourceGroup().id, ...)"
//     derivation was REJECTED by the adversarial review because resourceGroup().id is
//     derivable from the subscription id + resource-group name (both discoverable),
//     so anyone who guesses them could forge a valid operator magic link. A
//     deploymentScript instead generates real random bytes (openssl rand) and writes
//     them to the Key Vault secret the API reads. It is a heavier resource (its own
//     managed identity, and the script host provisions a transient storage account +
//     container) - that cost is ACCEPTED because the cheaper derivation is a security
//     defect, not a lighter alternative. NEVER overwrites an existing value (that
//     would invalidate every outstanding magic link at redeploy).
//
//     An operator MAY still set a stronger custom value out of band BEFORE first
//     deploy (docs/runbooks/enable-magic-link-email.md) - the create-if-absent guard
//     leaves it untouched; that path is supported, just no longer REQUIRED (AC-04).
resource tokenSigningKeyScriptIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: '${baseName}-tsk-id-${suffix}'
  location: location
  tags: commonTags
}

// The script identity needs to CREATE the secret (Secrets Officer), which is more
// than the API identity's read-only "Secrets User" - so it is a separate, tightly
// scoped grant that exists only for the provisioning script.
resource tokenSigningKeyScriptSecretsOfficer 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, tokenSigningKeyScriptIdentity.id, keyVaultSecretsOfficerRoleId)
  scope: keyVault
  properties: {
    principalId: tokenSigningKeyScriptIdentity.properties.principalId
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultSecretsOfficerRoleId)
    principalType: 'ServicePrincipal'
  }
}

resource tokenSigningKeyScript 'Microsoft.Resources/deploymentScripts@2023-08-01' = {
  name: '${baseName}-tsk-provision-${suffix}'
  location: location
  tags: commonTags
  kind: 'AzureCLI'
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${tokenSigningKeyScriptIdentity.id}': {}
    }
  }
  // The role assignment must land before the script runs, or the identity cannot
  // write the secret. (The keyVault/name reference is an implicit dependency too.)
  dependsOn: [
    tokenSigningKeyScriptSecretsOfficer
  ]
  properties: {
    azCliVersion: '2.61.0'
    retentionInterval: 'PT1H'
    timeout: 'PT15M'
    cleanupPreference: 'OnSuccess'
    environmentVariables: [
      {
        name: 'VAULT_NAME'
        value: keyVault.name
      }
      {
        name: 'SECRET_NAME'
        value: tokenSigningKeySecretName
      }
    ]
    // CSPRNG value, create-if-absent. openssl rand is a real cryptographic RNG; the
    // value is NEVER derived from any ARM input, so it is not reproducible from the
    // subscription id / resource group name.
    scriptContent: '''
      set -euo pipefail
      existing=$(az keyvault secret list --vault-name "$VAULT_NAME" --query "[?name=='$SECRET_NAME'] | [0].name" -o tsv 2>/dev/null || true)
      if [ -n "$existing" ]; then
        echo "Secret $SECRET_NAME already present - leaving it unchanged (idempotent; never overwrite a live signing key)."
      else
        value=$(openssl rand -base64 32)
        az keyvault secret set --vault-name "$VAULT_NAME" --name "$SECRET_NAME" --value "$value" --output none
        echo "Created $SECRET_NAME from a CSPRNG value (openssl rand)."
      fi
    '''
  }
}

// --- Outputs -----------------------------------------------------------------
output resourceGroupName string = resourceGroup().name
output appServicePlanSku string = appServicePlanSku
output apiAppName string = apiApp.name
output apiDefaultHostName string = apiApp.properties.defaultHostName
output webAppName string = webApp.name
output webDefaultHostName string = webApp.properties.defaultHostname
output signalRServiceName string = signalR.name
output storageAccount string = storage.name
output keyVault string = keyVault.name
output appInsightsName string = appInsights.name
output logAnalyticsWorkspaceName string = logAnalytics.name
// platform-devops/08 (durable Data Protection key ring, #199). Non-secret pointers,
// surfaced for the runbook / a diagnostic - the API already receives them as app
// settings above, so the deploy workflow needs no extra wiring step for the key ring.
output dataProtectionKeyRingBlobUri string = '${storage.properties.primaryEndpoints.blob}${dataProtectionKeysContainer.name}/keyring.xml'
output dataProtectionKeyVaultKeyUri string = dataProtectionKey.properties.keyUriWithVersion
output tokenSigningKeySecretName string = tokenSigningKeySecretName
// Magic-link email (accounts-identity/04). Empty strings when enableEmail is false, so
// the deploy workflow's email-wiring step is a clean no-op unless email is turned on.
// emailAcsEndpoint -> the API's Email:Endpoint (keyless send target). emailSenderAddress
// -> the API's Email:FromAddress: the Azure-managed domain's FIXED DoNotReply sender
// (DoNotReply@<*.azurecomm.net>), which a custom-domain operator override
// (vars.EMAIL_FROM_ADDRESS) supersedes for a friendly no-reply@quibblestone.com.
output emailEnabled bool = enableEmail
output emailAcsEndpoint string = enableEmail ? 'https://${communicationService!.properties.hostName}' : ''
output emailSenderAddress string = enableEmail ? 'DoNotReply@${emailDomain!.properties.fromSenderDomain}' : ''
output communicationServiceName string = enableEmail ? communicationService!.name : ''
