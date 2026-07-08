// QA parameters for ai.bicep - the QA lane's OWN AI cost-gate provider footprint,
// deployed to quibblestone-ai-qa-rg on the Pay-As-You-Go subscription (52bec743...),
// separate from beta's quibblestone-ai-rg. Isolating QA's AI resources + budget
// means QA / load testing never spends against or trips beta's $20 breaker.
// See docs/features/platform-devops/07 and the ai.bicep header for the cross-sub
// keyless design.
//
// apiPrincipalId is deliberately NOT set here: the QA API App Service has its own
// SystemAssigned identity (different from beta's), and the Deploy pipeline
// (deploy-env.yml) DISCOVERS it at run time (az webapp identity show) and passes it
// inline - so no per-lane principal GUID is ever committed. For a MANUAL local
// deploy, read it and pass it yourself:
//   az deployment group create -g quibblestone-ai-qa-rg -f infra/ai.bicep \
//     -p infra/ai.qa.bicepparam \
//     -p apiPrincipalId=$(az webapp identity show -g quibblestone-qa-rg \
//          -n <qa-api-name> --query principalId -o tsv) \
//     -p alertEmail='owner@example.com'
//
// Validate (no Azure login):
//   az bicep build --file infra/ai.bicep
using './ai.bicep'

param namePrefix = 'quibblestone'
param environmentName = 'qa'

// Same model choice as beta (config, not code - swap when availability shifts).
param aiModelName = 'gpt-5-mini'
param aiDeploymentName = 'gpt-5-mini'
param aiModelVersion = '2025-08-07'
param aiDeploymentSku = 'GlobalStandard'
param aiDeploymentCapacity = 10

// Optional second moderation layer - off (matches beta).
param deployContentSafety = false

// QA's OWN, smaller monthly budget (isolated from beta's $20). In CI this can be
// overridden by the QA Environment's AI_MONTHLY_BUDGET_USD var.
param monthlyBudgetUsd = 10
param budgetStartDate = '2026-07-01'
