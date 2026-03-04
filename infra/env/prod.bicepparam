// Prod: two Premium v3 instances, staging slot for swap deployments, provisioned SQL with
// geo-redundant backups, Defender for SQL, purge-protected Key Vault, and SQL + Key Vault
// reachable only through private endpoints.
//
// With private networking on, the DbSetup step has to run from inside the VNet. See
// "Private networking" in the README.

using '../main.bicep'

param appName = 'iacdemo'
param environment = 'prod'

param appServicePlan = {
  skuName: 'P0v3'
  instanceCount: 2
  // Spreads the two instances across availability zones. Needs a region with zones;
  // set false for regions without them.
  zoneRedundant: true
}

param sqlDatabase = {
  skuName: 'GP_Gen5_2'
  tier: 'GeneralPurpose'
  family: 'Gen5'
  vCores: 2
  useFreeLimit: false
  zoneRedundant: false
  backupStorageRedundancy: 'Geo'
  pointInTimeRestoreDays: 14
}

param sqlAdmin = {
  login: readEnvironmentVariable('SQL_ADMIN_LOGIN', 'sp-iacdemo-prod')
  objectId: readEnvironmentVariable('SQL_ADMIN_OBJECT_ID')
  principalType: 'Application'
}

param keyVaultPurgeProtection = true
param keyVaultSoftDeleteDays = 90

param keyVaultAppSettings = [
  { setting: 'Demo__ApiKey', secretName: 'demo-api-key' }
]

param secretValues = {
  'demo-api-key': readEnvironmentVariable('DEMO_API_KEY', '')
}

param enablePrivateNetworking = true
param network = {
  vnetAddressPrefix: '10.40.0.0/22'
  appSubnetPrefix: '10.40.0.0/26'
  privateEndpointSubnetPrefix: '10.40.1.0/27'
}

param enableStagingSlot = true
param enableSqlDefender = true
param logRetentionDays = 90
param alertEmail = readEnvironmentVariable('ALERT_EMAIL', '')
# generate_report
# log_event
# generate_report
# log_event
# generate_report
# log_event