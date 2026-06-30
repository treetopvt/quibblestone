// ----------------------------------------------------------------------------
//  main.bicep - Quibbler dev infrastructure ("get it up first, keep it tiny",
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
//  Bar: "deploys cleanly to dev." Intentionally NOT gold-plated - no VNet, no
//  private endpoints, no diagnostics yet; free/cheap SKUs. Validate locally:
//      az bicep build --file infra/main.bicep
//  Deploy to a resource group:
//      az deployment group create -g <rg> -f infra/main.bicep -p infra/main.bicepparam
// ----------------------------------------------------------------------------

@description('Azure region for all resources. Defaults to the resource group location.')
param location string = resourceGroup().location

@description('Short prefix used to name resources.')
param namePrefix string = 'quibbler'

@description('Environment moniker (dev, test, prod).')
param environmentName string = 'dev'

// A short, deterministic suffix keeps globally-unique names (storage, vault,
// SignalR, web apps) stable per resource group without hardcoding them.
var suffix = uniqueString(resourceGroup().id)
var baseName = '${namePrefix}-${environmentName}'

// Storage / Key Vault names: lowercase, no dashes, length-limited.
var storageAccountName = take(toLower('${namePrefix}${environmentName}${suffix}'), 24)
var keyVaultName = take(toLower('${namePrefix}-${environmentName}-${suffix}'), 24)

// --- 2a. App Service Plan (Linux) -------------------------------------------
// Hosting the API on App Service requires a plan.
resource appServicePlan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: '${baseName}-plan'
  location: location
  sku: {
    name: 'B1'
    tier: 'Basic'
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
    }
  }
}

// --- 1. Static Web App - the React + Vite client ----------------------------
resource webApp 'Microsoft.Web/staticSites@2024-04-01' = {
  name: '${baseName}-web-${suffix}'
  location: location
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
output apiAppName string = apiApp.name
output apiDefaultHostName string = apiApp.properties.defaultHostName
output webAppName string = webApp.name
output webDefaultHostName string = webApp.properties.defaultHostname
output signalRServiceName string = signalR.name
output storageAccount string = storage.name
output keyVault string = keyVault.name
