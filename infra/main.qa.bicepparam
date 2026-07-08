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
//   az deployment group create -g quibblestone-qa-rg --subscription <playground> \
//     -f infra/main.bicep -p infra/main.qa.bicepparam
//
// Validate (no Azure login):
//   az bicep build --file infra/main.bicep
using './main.bicep'

param namePrefix = 'quibblestone'
param environmentName = 'qa'
// F1 (Free, $0). QA runs entirely on the Playground PAYG sub (the student sub that
// hosts beta caps App Service at one plan, held by beta). PAYG allows F1; bump to B1
// (~$13/mo) for a real 6-player load test. A MANUAL local deploy must target
// Playground (the --subscription flag in the header command above). In CI the SKU
// comes from the QA Environment's APP_SERVICE_PLAN_SKU var, not this file.
param appServicePlanSku = 'F1'
// location defaults to the resource group's location (resourceGroup().location).
