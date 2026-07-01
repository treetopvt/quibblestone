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
