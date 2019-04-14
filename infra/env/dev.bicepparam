// Dev: cheapest settings that still run the whole stack. Public endpoints, no slot,
// Azure SQL free offer, Key Vault without purge protection so the environment can be thrown away.
//
// Values that differ per tenant or are secret come from environment variables. The pipeline
// sets them from its variable group; locally, export them before `az deployment group create`.

using '../main.bicep'

param appName = 'iacdemo'
param environment = 'dev'

param appServicePlan = {
  skuName: 'B1'
  instanceCount: 1
  zoneRedundant: false
}

param sqlDatabase = {
  skuName: 'GP_S_Gen5_2'
  tier: 'GeneralPurpose'
  family: 'Gen5'
  vCores: 2
  autoPauseDelayMinutes: 60
  minCapacity: '0.5'
  // One free database per subscription. Set false if another database already uses it.
  useFreeLimit: true
  zoneRedundant: false
  backupStorageRedundancy: 'Local'
  pointInTimeRestoreDays: 7
}

param sqlAdmin = {
  login: readEnvironmentVariable('SQL_ADMIN_LOGIN', 'sp-iacdemo-dev')
  objectId: readEnvironmentVariable('SQL_ADMIN_OBJECT_ID')
  principalType: 'Application'
}

// Your public IP, so DbSetup can run from your machine against dev. Optional.
var devClientIp = readEnvironmentVariable('DEV_CLIENT_IP', '')
param sqlFirewallAllowedIps = empty(devClientIp) ? [] : [devClientIp]

param keyVaultPurgeProtection = false
param keyVaultSoftDeleteDays = 7

param keyVaultAppSettings = [
  { setting: 'Demo__ApiKey', secretName: 'demo-api-key' }
]

param secretValues = {
  'demo-api-key': readEnvironmentVariable('DEMO_API_KEY', '')
}

param enablePrivateNetworking = false
param enableStagingSlot = false
param enableSqlDefender = false
param logRetentionDays = 30
param alertEmail = readEnvironmentVariable('ALERT_EMAIL', '')
# render_template
# cleanup_resources
# render_template
# cleanup_resources