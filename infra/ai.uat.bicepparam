// UAT parameters for ai.bicep - the AI cost gate provider footprint deployed to
// quibblestone-ai-rg on the Pay-As-You-Go subscription (52bec743...), separate
// from the app footprint on the student sub (see ai.bicep header for why).
//
// No secrets here. The alert EMAIL is intentionally NOT in this file (AC-07) -
// supply it at deploy time to arm the Cost Management budget:
//   az deployment group create -g quibblestone-ai-rg -f infra/ai.bicep \
//     -p infra/ai.uat.bicepparam -p alertEmail='owner@example.com'
// Without alertEmail the account + model + keyless grant still deploy; only the
// budget + action group are skipped.
//
// Validate (no Azure login):
//   az bicep build --file infra/ai.bicep
using './ai.bicep'

param namePrefix = 'quibblestone'
param environmentName = 'uat'

// Object ID of the API App Service's SystemAssigned managed identity (in the app
// subscription). Not a secret - it is an identity GUID. Stable unless the API app
// is deleted and recreated (then re-read it:
//   az webapp identity show -g quibblestone-uat-rg \
//     -n quibblestone-uat-api-7achtfuwtltwo --query principalId -o tsv
// and update this value).
param apiPrincipalId = '1f4bd8dd-58d4-4a77-b9e6-119652154b5e'

// Model deployment: gpt-5-mini @ 2025-08-07 on GlobalStandard (quota 500 in
// eastus2). ADR 0001 named gpt-4o-mini, but by mid-2026 the gpt-4o/4.1 mini family
// is all "Deprecating" (blocked for new deployments) and gpt-4.1-nano has 0
// real-time quota here. gpt-5-mini is the current GenerallyAvailable small chat
// model with quota. 10K TPM is a generous concurrency backstop for a word game.
param aiModelName = 'gpt-5-mini'
param aiDeploymentName = 'gpt-5-mini'
param aiModelVersion = '2025-08-07'
param aiDeploymentSku = 'GlobalStandard'
param aiDeploymentCapacity = 10

// Optional second moderation layer - off for the slice (story 05 turns it on later).
param deployContentSafety = false

// Cost backstop. $20/month hard constraint; set budgetStartDate to the first of the
// month you provision in.
param monthlyBudgetUsd = 20
param budgetStartDate = '2026-07-01'
