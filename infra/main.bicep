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

@description('Environment moniker (dev, uat, prod).')
param environmentName string = 'uat'

@description('App Service Plan SKU. F1 (Free, $0) is the default so UAT costs nothing to stand up; scale up to B1/B2/S1 for Always On + reliable WebSockets (a working real-time game). Change here, in the *.bicepparam file, or pass -p appServicePlanSku=B1 at deploy time.')
@allowed([
  'F1' // Free    - $0, no Always On, 60 CPU-min/day, limited WebSockets (fine for kicking the tyres)
  'B1' // Basic   - Always On + WebSockets; realistic floor for a live multiplayer app
  'B2' // Basic   - more headroom
  'S1' // Standard - deployment slots, autoscale
])
param appServicePlanSku string = 'F1'

// --- AI cost gate parameters (ai-cost-gate/06, issue #125) ------------------
// These drive the AI Foundry deployment + the Cost Management backstop. They all
// have sane, non-secret defaults so `az bicep build` and a no-AI deploy still
// work; the owner supplies the email (and optionally flips Content Safety on) at
// deploy time. NEVER commit a real email or key here (CLAUDE.md section 4).

@description('The Azure AI Foundry (Azure OpenAI) model deployment name the API reads as Ai:Deployment. gpt-4o-mini is the model the owner chose in ADR 0001 (swappable to gpt-4.1-nano later - it is config, not code).')
param aiDeploymentName string = 'gpt-4o-mini'

@description('The gpt-4o-mini model version to deploy. Pinned so a re-deploy is deterministic; bump when Foundry retires a version.')
param aiModelVersion string = '2024-07-18'

@description('Provision the OPTIONAL Azure AI Content Safety resource (ADR 0001 decision B). Defaults false so the slice footprint stays minimal - the existing blocklist + family-safe gate is the enforced hard gate; Content Safety is the later second layer for larger free-text payloads. When false, nothing is provisioned and story 05 no-ops on absent config.')
param deployContentSafety bool = false

@description('Monthly Azure Cost Management budget ceiling in USD - the authoritative-but-slow backstop to the app spend breaker (story 04). Defaults to the $20/month hard business constraint (ADR 0001).')
param monthlyBudgetUsd int = 20

@description('Email address for the budget action group (the alert recipient). A DEPLOY INPUT, never hardcoded (AC-05 / AC-07): leave empty and the budget + action group are simply not provisioned (the app still runs; the app breaker in story 04 is the real-time enforcer). Supply the owner address at deploy time to arm the billing backstop.')
param alertEmail string = ''

@description('First-of-month start date for the Cost Management budget (Monthly grain requires the first of a month). Defaults to the current month at authoring time; set it to the first of the month you provision in. Not a secret.')
param budgetStartDate string = '2026-07-01'

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
      // ai-cost-gate/06: the base telemetry + observability settings, then the AI
      // Foundry config (endpoint + deployment - CONFIG, not secrets), then the
      // OPTIONAL Content Safety settings appended only when deployContentSafety is
      // true. concat() keeps the array flat; the ternary contributes an empty array
      // when Content Safety is off so nothing dangling references a resource that
      // was not provisioned. The AI credential itself is KEYLESS (managed-identity
      // RBAC below), so there is no Ai key app-setting at all.
      appSettings: concat([
        {
          name: 'Telemetry__StorageConnectionString'
          value: 'DefaultEndpointsProtocol=https;AccountName=${storage.name};AccountKey=${storage.listKeys().keys[0].value};EndpointSuffix=${environment().suffixes.storage}'
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
        // ai-cost-gate/06 AC-01: the Foundry endpoint + model deployment name the AI
        // proxy (story 01) reads as Ai:Endpoint / Ai:Deployment. Double-underscore is
        // ASP.NET Core's nested-key convention (Ai:Endpoint), matching
        // Telemetry__StorageConnectionString above. Both are CONFIG, not secrets: the
        // endpoint is a public URL and auth is keyless (managed identity). Story 01
        // no-ops if these are absent, so the app still runs with no Foundry deployed.
        {
          name: 'Ai__Endpoint'
          value: aiFoundry.properties.endpoint
        }
        {
          name: 'Ai__Deployment'
          value: aiFoundryDeployment.name
        }
      ], deployContentSafety ? [
        // ai-cost-gate/06 AC-03: the OPTIONAL Content Safety second-layer config
        // (story 05). Endpoint is config; the key is surfaced the SAME KV-backed way
        // as the App Insights connection string (a KV reference resolved by the
        // App Service identity), never a committed literal. Present only when
        // deployContentSafety is true; story 05 no-ops when this config is absent.
        {
          name: 'ContentSafety__Endpoint'
          value: contentSafety!.properties.endpoint
        }
        {
          name: 'ContentSafety__Key'
          value: '@Microsoft.KeyVault(SecretUri=${contentSafetyKeySecret!.properties.secretUri})'
        }
      ] : [])
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

// --- 7. AI Foundry (Azure OpenAI) - the AI cost gate's provider (ai-cost-gate/06)
// The Azure AI Foundry / Azure OpenAI account + a gpt-4o-mini model deployment the
// server-side AI proxy (story 01) calls. Chosen in ADR 0001: stay in the Azure
// ecosystem the footprint is already built for (Key Vault + Storage + managed
// identity), so the provider key never leaves Azure and never reaches the browser.
// Kept tiny (README section 9): one account + one deployment. Per-call cost is
// negligible (~$0.0001/jumble); the $20 budget below is a bug/abuse backstop, not
// an organic-usage cap. This is CONFIG the app no-ops without, so local dev and an
// AI-free deploy still run (story 01 AC-04).
//
// customSubDomainName is REQUIRED for keyless (Azure AD / managed-identity) token
// auth against the data plane - without it only key auth works, and we want keyless
// (AC-02). It doubles as the account's globally-unique endpoint host.
resource aiFoundry 'Microsoft.CognitiveServices/accounts@2024-10-01' = {
  name: '${baseName}-aoai-${suffix}'
  location: location
  tags: commonTags
  kind: 'AIServices' // Foundry multi-service account; exposes the Azure OpenAI data plane for the deployment below
  sku: {
    name: 'S0' // standard pay-as-you-go; there is no free tier for OpenAI-capable accounts
  }
  properties: {
    customSubDomainName: '${baseName}-aoai-${suffix}'
    publicNetworkAccess: 'Enabled'
    // Local auth (API keys) stays enabled as a fallback path, but the API uses the
    // keyless managed-identity role granted below - no key is stored or committed.
    disableLocalAuth: false
  }
}

// 7a. The gpt-4o-mini model deployment (ADR 0001 decision A). The deployment NAME is
//     what the API reads as Ai:Deployment (surfaced as the Ai__Deployment app-setting
//     above). GlobalStandard is the cheapest, most widely available SKU; the small
//     capacity (10 = 10K tokens/min) is a sane concurrency backstop, distinct from
//     our per-session quota + spend breaker which are the real business controls
//     (ADR 0001 point 4).
resource aiFoundryDeployment 'Microsoft.CognitiveServices/accounts/deployments@2024-10-01' = {
  parent: aiFoundry
  name: aiDeploymentName
  sku: {
    name: 'GlobalStandard'
    capacity: 10
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: 'gpt-4o-mini'
      version: aiModelVersion
    }
  }
}

// 7b. Grant the API App Service's SystemAssigned identity the built-in "Cognitive
//     Services OpenAI User" role, scoped to the Foundry account (AC-02: keyless,
//     preferred over a stored key). This mirrors the apiKeyVaultSecretsUser pattern
//     exactly (guid name, subscriptionResourceId roleDefinitionId, ServicePrincipal).
//     roleDefinitionId 5e0bd9bd-7b93-4f28-af87-19fc36ad61bd is the fixed built-in id
//     for that data-plane role; it lets the identity call the deployment without any
//     API key, so nothing secret is ever provisioned or committed.
var cognitiveServicesOpenAiUserRoleId = '5e0bd9bd-7b93-4f28-af87-19fc36ad61bd'
resource apiFoundryOpenAiUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(aiFoundry.id, apiApp.id, cognitiveServicesOpenAiUserRoleId)
  scope: aiFoundry
  properties: {
    principalId: apiApp.identity.principalId
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', cognitiveServicesOpenAiUserRoleId)
    principalType: 'ServicePrincipal'
  }
}

// --- 8. OPTIONAL Azure AI Content Safety (ai-cost-gate/06 AC-03) --------------
// The config-gated second moderation layer (story 05), OFF by default so the slice
// footprint stays minimal (ADR 0001 decision B): the existing blocklist + family-safe
// gate remains the enforced hard gate on every AI word; Content Safety earns its place
// only on the larger free-text payloads (whole generated templates) later. When
// deployContentSafety is false NOTHING here is provisioned and story 05 no-ops on the
// absent ContentSafety:* config. When on, the key is stashed in Key Vault and surfaced
// as a KV-reference app-setting (above), the same secret-safe path as App Insights.
resource contentSafety 'Microsoft.CognitiveServices/accounts@2024-10-01' = if (deployContentSafety) {
  name: '${baseName}-safety-${suffix}'
  location: location
  tags: commonTags
  kind: 'ContentSafety'
  sku: {
    name: 'S0' // standard; a free F0 tier (5,000 records/mo) exists if one-per-subscription suits you
  }
  properties: {
    customSubDomainName: '${baseName}-safety-${suffix}'
    publicNetworkAccess: 'Enabled'
  }
}

// 8a. Content Safety key -> Key Vault secret (only when the resource exists). Surfaced
//     to the API as the ContentSafety__Key KV-reference app-setting above; never a
//     committed literal. Guarded by the same deployContentSafety condition so the
//     reference to contentSafety.listKeys() is only evaluated when the account is real.
resource contentSafetyKeySecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = if (deployContentSafety) {
  parent: keyVault
  name: 'ContentSafetyKey'
  properties: {
    value: contentSafety!.listKeys().key1
  }
}

// --- 9. Cost Management budget + action group (ai-cost-gate/06 AC-04/05) -------
// The AUTHORITATIVE-BUT-SLOW backstop to the app spend breaker (story 04). Budget
// evaluation is billing-driven and lags hours (typically 8-24h), so it is NOT the
// real-time enforcer - the app breaker is; the budget catches EVERYTHING (AI + infra)
// and is reconciled against the breaker periodically (ADR 0001 point 6). Both the
// action group and the budget are provisioned ONLY when alertEmail is supplied, so a
// no-email deploy still stands the app up cleanly (the email is a deploy input, never
// hardcoded - AC-05/AC-07). Set alertEmail at deploy time to arm the backstop.

// 9a. Email action group the budget notifications target. Global (action groups are a
//     global resource type). groupShortName is capped at 12 chars.
resource aiBudgetActionGroup 'Microsoft.Insights/actionGroups@2023-01-01' = if (!empty(alertEmail)) {
  name: '${baseName}-ai-budget-ag'
  location: 'Global'
  tags: commonTags
  properties: {
    groupShortName: 'aibudget'
    enabled: true
    emailReceivers: [
      {
        name: 'owner'
        emailAddress: alertEmail
        useCommonAlertSchema: true
      }
    ]
  }
}

// 9b. The $20/month (parameterised) resource-group-scoped budget. Notifications fire
//     at 25 / 50 / 75 / 100% of Actual spend, plus a Forecasted 100% for earlier
//     warning. Each notification carries the owner email (from the alertEmail param)
//     AND the action group id, so the alert reaches the same recipient two ways. No
//     email or amount is a secret; amount defaults to the $20 hard constraint.
resource aiBudget 'Microsoft.Consumption/budgets@2023-11-01' = if (!empty(alertEmail)) {
  name: '${baseName}-ai-budget'
  properties: {
    category: 'Cost'
    amount: monthlyBudgetUsd
    timeGrain: 'Monthly'
    timePeriod: {
      startDate: budgetStartDate
    }
    notifications: {
      actual_25: {
        enabled: true
        operator: 'GreaterThanOrEqualTo'
        threshold: 25
        thresholdType: 'Actual'
        contactEmails: [
          alertEmail
        ]
        contactGroups: [
          aiBudgetActionGroup.id
        ]
      }
      actual_50: {
        enabled: true
        operator: 'GreaterThanOrEqualTo'
        threshold: 50
        thresholdType: 'Actual'
        contactEmails: [
          alertEmail
        ]
        contactGroups: [
          aiBudgetActionGroup.id
        ]
      }
      actual_75: {
        enabled: true
        operator: 'GreaterThanOrEqualTo'
        threshold: 75
        thresholdType: 'Actual'
        contactEmails: [
          alertEmail
        ]
        contactGroups: [
          aiBudgetActionGroup.id
        ]
      }
      actual_100: {
        enabled: true
        operator: 'GreaterThanOrEqualTo'
        threshold: 100
        thresholdType: 'Actual'
        contactEmails: [
          alertEmail
        ]
        contactGroups: [
          aiBudgetActionGroup.id
        ]
      }
      // Forecasted 100% - a heads-up before Actual spend actually crosses the ceiling.
      forecasted_100: {
        enabled: true
        operator: 'GreaterThanOrEqualTo'
        threshold: 100
        thresholdType: 'Forecasted'
        contactEmails: [
          alertEmail
        ]
        contactGroups: [
          aiBudgetActionGroup.id
        ]
      }
    }
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
// ai-cost-gate/06: the Foundry account + endpoint + deployment the API consumes as
// Ai:Endpoint / Ai:Deployment, and (when enabled) the Content Safety account name.
output aiFoundryName string = aiFoundry.name
output aiFoundryEndpoint string = aiFoundry.properties.endpoint
output aiDeploymentName string = aiFoundryDeployment.name
output contentSafetyName string = deployContentSafety ? contentSafety.name : ''
