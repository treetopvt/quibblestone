// QA parameters for main.bicep - the second cloud lane (the auto-deploy proving
// ground that sits IN FRONT of beta). See docs/features/platform-devops/07 and
// docs/runbooks/deploy-qa-and-promote-beta.md.
//
// Resource names are composed in main.bicep from namePrefix + environmentName +
// a deterministic uniqueString(resourceGroup().id) suffix, so QA gets its own
// distinct, isolated names (Storage, Key Vault, App Insights, App Service, SWA)
// in its own resource group. No secrets live here.
//
// NOTE: the Deploy pipeline (deploy-env.yml) passes these params INLINE, driving
// the SKU from the QA Environment's APP_SERVICE_PLAN_SKU var so a scaled plan is
// never downgraded on a routine deploy. This file is for LOCAL / manual deploys
// and to document QA's intended footprint:
//   az deployment group create -g quibblestone-qa-rg -f infra/main.bicep \
//     -p infra/main.qa.bicepparam
//
// Validate (no Azure login):
//   az bicep build --file infra/main.bicep
using './main.bicep'

param namePrefix = 'quibblestone'
param environmentName = 'qa'
// B1 (Basic, ~$13/mo) so QA can exercise realistic multiplayer (Always On +
// reliable WebSockets) while validating the infra overhaul. Drop to 'F1' ($0)
// when QA is idle / only smoke-testing routing + build. In CI this comes from the
// QA Environment's APP_SERVICE_PLAN_SKU var, not this file.
param appServicePlanSku = 'B1'
// location defaults to the resource group's location (resourceGroup().location).
