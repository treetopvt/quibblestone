// Dev parameters for main.bicep - the tiny dev footprint (README section 9).
//
// These are the defaults for the single "dev" environment. Resource names are
// composed in main.bicep from namePrefix + environmentName + a deterministic
// uniqueString(resourceGroup().id) suffix, so the only knobs here are the prefix,
// the environment moniker, and (optionally) the region. No secrets live here.
//
// Validate (no Azure login):
//   az bicep build --file infra/main.bicep
// Deploy to a dev resource group:
//   az deployment group create -g <rg> -f infra/main.bicep -p infra/main.bicepparam
//
// Full walkthrough: docs/runbooks/deploy-to-dev.md
using './main.bicep'

param namePrefix = 'quibblestone'
param environmentName = 'dev'
// location defaults to the resource group's location (resourceGroup().location).
// Override only if the resources must live in a different region than the group:
//   param location = 'eastus'

// --- AI cost gate deploy inputs (ai-cost-gate/06) ----------------------------
// All optional with safe defaults (see main.bicep). Non-secret knobs can live here;
// the alert EMAIL must NOT (AC-07) - pass it at deploy time instead:
//   az deployment group create ... -p alertEmail='you@example.com'
// Leave alertEmail unset and the budget + action group are simply not provisioned.
//   param monthlyBudgetUsd = 20          // the $20/month backstop ceiling (default)
//   param deployContentSafety = false    // flip true to add the optional 2nd moderation layer
//   param budgetStartDate = '2026-07-01' // first of the month you provision in
