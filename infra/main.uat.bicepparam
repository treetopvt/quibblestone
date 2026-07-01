// UAT parameters for main.bicep - the first (and, for now, only) cloud
// environment (see docs/features/platform-devops/03-cicd-uat.md).
//
// Resource names are composed in main.bicep from namePrefix + environmentName +
// a deterministic uniqueString(resourceGroup().id) suffix, so they are stable
// per resource group. No secrets live here.
//
// Cost posture: appServicePlanSku defaults to F1 (Free, $0), so standing UAT up
// costs nothing. Scale up when you want Always On + reliable WebSockets - either
// flip it here to 'B1' and re-provision, or pass it at deploy time:
//   az deployment group create -g quibblestone-uat-rg -f infra/main.bicep \
//     -p infra/main.uat.bicepparam -p appServicePlanSku=B1
// (or use the "sku" input on the Provision UAT GitHub workflow).
//
// Validate (no Azure login):
//   az bicep build --file infra/main.bicep
//
// Full walkthrough: docs/runbooks/deploy-to-uat.md
using './main.bicep'

param namePrefix = 'quibblestone'
param environmentName = 'uat'
param appServicePlanSku = 'F1' // Free ($0). Bump to 'B1' for Always On + WebSockets.
// location defaults to the resource group's location (resourceGroup().location).
// Override only if the resources must live in a different region than the group:
//   param location = 'eastus'
