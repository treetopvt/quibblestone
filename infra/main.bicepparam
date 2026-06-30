// Dev parameters for main.bicep.
// Deploy:
//   az deployment group create -g <rg> -f infra/main.bicep -p infra/main.bicepparam
using './main.bicep'

param namePrefix = 'quibblestone'
param environmentName = 'dev'
// location defaults to the resource group's location.
