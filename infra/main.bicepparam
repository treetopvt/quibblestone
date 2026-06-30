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
