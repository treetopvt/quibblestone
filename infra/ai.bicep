// =============================================================================
// infra/ai.bicep - the AI cost gate's Azure provider footprint (ai-cost-gate/06)
// =============================================================================
//
// WHY THIS IS A SEPARATE FILE (and not part of main.bicep):
// The QuibbleStone app footprint (App Service, SignalR, Storage, Key Vault, App
// Insights - see main.bicep) lives in `quibblestone-uat-rg` on the "Azure for
// Students" subscription. That subscription CANNOT host Azure OpenAI (student
// offer + spending limit -> Cognitive Services OpenAI accounts are blocked and
// gpt-4o-mini quota is 0). So the AI provider is deployed to its OWN resource
// group in a Pay-As-You-Go subscription in the SAME tenant (BullIT), and the
// app's managed identity reaches it cross-subscription (keyless, AC-02).
//
//   app RG   : quibblestone-uat-rg   (sub af7f9e54..., Azure for Students)
//   AI RG    : quibblestone-ai-rg    (sub 52bec743..., Pay-As-You-Go)  <-- THIS FILE
//
// Cross-sub managed-identity RBAC works because both subs are in one tenant: the
// role assignment below scopes the built-in "Cognitive Services OpenAI User" role
// to the Foundry account (here, PAYG sub) but names the API app's identity
// principalId (there, student sub) as a PARAMETER (apiPrincipalId) - a GUID that
// resolves across subs within the tenant. The API then calls the deployment with
// an AAD token; no key is ever stored, referenced, or committed.
//
// WIRING THE APP: this file cannot set the Ai__Endpoint / Ai__Deployment app
// settings on the API app (that app is in a different sub/RG and is owned by
// main.bicep). Instead it OUTPUTS them; a post-deploy step sets them on the API:
//   az webapp config appsettings set -g quibblestone-uat-rg \
//     -n <apiAppName> --settings Ai__Endpoint=<aiEndpoint> Ai__Deployment=<aiDeploymentName>
// (see docs / the deploy workflow). The app no-ops on absent Ai:* config
// (ai-cost-gate story 01 AC-04), so ordering is not fragile.
//
// Deploy (PAUSE for spend approval first - the account is ~$0 idle but the model
// is billed per token, and the $20 budget is real money):
//   az deployment group create -g quibblestone-ai-rg -f infra/ai.bicep \
//     -p infra/ai.uat.bicepparam -p alertEmail='owner@example.com'
//
// Validate (no Azure login):
//   az bicep build --file infra/ai.bicep
//
// See docs/features/ai-cost-gate/06-iac-provisioning-seam.md and the memory note
// ai-cost-gate-azure-openai-plan for the full decision trail.
// =============================================================================

// --- Parameters --------------------------------------------------------------
@description('Region for the AI resources. Defaults to the resource group location (eastus2), which has gpt-5-mini GlobalStandard quota on the target PAYG sub.')
param location string = resourceGroup().location

@description('Name prefix, matched to the app footprint so resource names read consistently across resource groups.')
param namePrefix string = 'quibblestone'

@description('Environment name (uat). Part of the composed resource names.')
param environmentName string = 'uat'

@description('Object (principal) ID of the API App Service SystemAssigned managed identity that will call the model - LIVES IN THE APP SUBSCRIPTION. This is a cross-subscription grant, so it is passed as a value (a GUID), not derived from a resource in this deployment. Not a secret.')
param apiPrincipalId string

@description('The Azure OpenAI model name to deploy. ADR 0001 chose gpt-4o-mini, but by mid-2026 the whole gpt-4o/gpt-4.1 mini family is in "Deprecating" state (blocked for new deployments) and gpt-4.1-nano has 0 real-time quota in eastus2. gpt-5-mini is the current GenerallyAvailable small chat model with quota here. Swappable - it is config, not code.')
param aiModelName string = 'gpt-5-mini'

@description('The Azure OpenAI model deployment name the API reads as Ai:Deployment. Named after the model for clarity; the app setting is wired from this at deploy time.')
param aiDeploymentName string = 'gpt-5-mini'

@description('The model version to deploy. Pinned so a re-deploy is deterministic; bump when a version is retired (check status: az cognitiveservices model list -l <region> --query "[?model.name==\'gpt-5-mini\'].model.lifecycleStatus").')
param aiModelVersion string = '2025-08-07'

@description('Model deployment SKU. gpt-5-mini is GlobalStandard-capable (quota 500 in eastus2); it does NOT offer the regional Standard SKU the older mini models did. GlobalStandard is also the cheapest per-token tier. Switch to DataZoneStandard for US-data-residency (e.g. with gpt-5.4-mini).')
@allowed([
  'GlobalStandard'
  'DataZoneStandard'
  'Standard'
])
param aiDeploymentSku string = 'GlobalStandard'

@description('Model deployment capacity in thousands of tokens/min (TPM). A concurrency backstop distinct from the per-session quota + spend breaker (the real business controls). Must fit under the region quota (GlobalStandard gpt-5-mini = 500 on the target sub).')
@minValue(1)
@maxValue(500)
param aiDeploymentCapacity int = 10

@description('Deploy the OPTIONAL Azure AI Content Safety account (ai-cost-gate story 05, second moderation layer). OFF by default to keep the slice footprint minimal; the app blocklist + family-safe gate stays the enforced hard gate regardless.')
param deployContentSafety bool = false

@description('Monthly Azure Cost Management budget ceiling in USD - the authoritative-but-slow backstop to the app spend breaker (story 04). Defaults to the $20/month constraint (ADR 0001).')
param monthlyBudgetUsd int = 20

@description('Email for the budget action group. A DEPLOY INPUT, never committed (AC-05/AC-07): leave empty and the budget + action group are simply not provisioned (the app still runs; the story-04 app breaker is the real-time enforcer). Supply at deploy time to arm the billing backstop.')
param alertEmail string = ''

@description('First-of-month start date for the Cost Management budget (Monthly grain requires the first of a month). Set to the first of the month you provision in. Not a secret.')
param budgetStartDate string = '2026-07-01'

// --- Variables ---------------------------------------------------------------
// A short, deterministic suffix keeps the globally-unique account name stable per
// resource group without hardcoding it (mirrors main.bicep).
var suffix = uniqueString(resourceGroup().id)
var baseName = '${namePrefix}-${environmentName}'
var commonTags = {
  app: namePrefix
  env: environmentName
  role: 'ai-cost-gate'
}

// --- 1. Azure AI Foundry (Azure OpenAI) account ------------------------------
// One AIServices account exposing the Azure OpenAI data plane. customSubDomainName
// is REQUIRED for keyless (AAD / managed-identity) token auth against the data
// plane and doubles as the globally-unique endpoint host.
resource aiFoundry 'Microsoft.CognitiveServices/accounts@2024-10-01' = {
  name: '${baseName}-aoai-${suffix}'
  location: location
  tags: commonTags
  kind: 'AIServices'
  sku: {
    name: 'S0' // standard pay-as-you-go; there is no free tier for OpenAI-capable accounts
  }
  properties: {
    customSubDomainName: '${baseName}-aoai-${suffix}'
    publicNetworkAccess: 'Enabled'
    // Local auth (API keys) left enabled as a break-glass fallback, but the API
    // uses the keyless managed-identity role granted below - no key is stored.
    disableLocalAuth: false
  }
}

// --- 1a. The model deployment (defaults to gpt-5-mini) -----------------------
// The deployment NAME is what the API reads as Ai:Deployment (output below).
//
// SKU = GlobalStandard (param): gpt-5-mini is GA with GlobalStandard quota 500 in
// eastus2 on the target PAYG sub. The current-gen mini models dropped the regional
// 'Standard' SKU the old gpt-4o-mini offered, so GlobalStandard is the deployable
// real-time tier (and the cheapest per token). Capacity 10 (K TPM) is far more
// than a party word game needs.
resource aiFoundryDeployment 'Microsoft.CognitiveServices/accounts/deployments@2024-10-01' = {
  parent: aiFoundry
  name: aiDeploymentName
  sku: {
    name: aiDeploymentSku
    capacity: aiDeploymentCapacity
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: aiModelName
      version: aiModelVersion
    }
  }
}

// --- 1b. Keyless RBAC: API identity -> "Cognitive Services OpenAI User" -------
// Scoped to the Foundry account (data-plane role, lets the identity call the
// deployment without any API key). CROSS-SUBSCRIPTION: principalId names the API
// app's identity which lives in the app subscription; the assignment is created
// here (AI sub) at the Foundry scope. Same-tenant, so the GUID resolves. This
// mirrors main.bicep's apiKeyVaultSecretsUser pattern (guid name,
// subscriptionResourceId roleDefinitionId, ServicePrincipal principalType).
var cognitiveServicesOpenAiUserRoleId = '5e0bd9bd-7b93-4f28-af87-19fc36ad61bd'
resource apiFoundryOpenAiUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(aiFoundry.id, apiPrincipalId, cognitiveServicesOpenAiUserRoleId)
  scope: aiFoundry
  properties: {
    principalId: apiPrincipalId
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', cognitiveServicesOpenAiUserRoleId)
    principalType: 'ServicePrincipal'
  }
}

// --- 2. OPTIONAL Azure AI Content Safety -------------------------------------
// The config-gated second moderation layer (story 05), OFF by default. When on,
// its endpoint is output for the app; the key (if key auth is used) is wired to
// the app's existing Key Vault by a post-deploy step (this AI RG has no vault by
// design - the slice keeps its footprint to the OpenAI account only).
resource contentSafety 'Microsoft.CognitiveServices/accounts@2024-10-01' = if (deployContentSafety) {
  name: '${baseName}-safety-${suffix}'
  location: location
  tags: commonTags
  kind: 'ContentSafety'
  sku: {
    name: 'S0'
  }
  properties: {
    customSubDomainName: '${baseName}-safety-${suffix}'
    publicNetworkAccess: 'Enabled'
  }
}

// --- 3. Cost Management budget + action group (AC-04/05) ----------------------
// The authoritative-but-slow backstop to the app spend breaker (story 04). Budget
// evaluation is billing-driven and lags hours, so it is NOT the real-time
// enforcer - the app breaker is; the budget catches EVERYTHING in this RG (AI
// spend). Provisioned ONLY when alertEmail is supplied (the email is a deploy
// input, never hardcoded - AC-05/AC-07); a no-email deploy still stands the
// account up cleanly.

// 3a. Email action group the budget notifications target (global resource type).
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

// 3b. The $20/month (parameterised) resource-group-scoped budget. Fires at
// 25/50/75/100% Actual plus Forecasted 100%. Each notification carries the owner
// email AND the action group id so the alert reaches the recipient two ways.
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
        contactEmails: [ alertEmail ]
        contactGroups: [ aiBudgetActionGroup.id ]
      }
      actual_50: {
        enabled: true
        operator: 'GreaterThanOrEqualTo'
        threshold: 50
        thresholdType: 'Actual'
        contactEmails: [ alertEmail ]
        contactGroups: [ aiBudgetActionGroup.id ]
      }
      actual_75: {
        enabled: true
        operator: 'GreaterThanOrEqualTo'
        threshold: 75
        thresholdType: 'Actual'
        contactEmails: [ alertEmail ]
        contactGroups: [ aiBudgetActionGroup.id ]
      }
      actual_100: {
        enabled: true
        operator: 'GreaterThanOrEqualTo'
        threshold: 100
        thresholdType: 'Actual'
        contactEmails: [ alertEmail ]
        contactGroups: [ aiBudgetActionGroup.id ]
      }
      forecasted_100: {
        enabled: true
        operator: 'GreaterThanOrEqualTo'
        threshold: 100
        thresholdType: 'Forecasted'
        contactEmails: [ alertEmail ]
        contactGroups: [ aiBudgetActionGroup.id ]
      }
    }
  }
}

// --- Outputs -----------------------------------------------------------------
// Consumed by the post-deploy step that sets the app's Ai:* config (see header).
output aiAccountName string = aiFoundry.name
output aiEndpoint string = aiFoundry.properties.endpoint
output aiDeploymentNameOut string = aiFoundryDeployment.name
output contentSafetyEndpoint string = deployContentSafety ? contentSafety!.properties.endpoint : ''
output resourceGroupName string = resourceGroup().name
