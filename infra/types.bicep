// Shared parameter shapes. Exported so main.bicep and the modules agree on one definition,
// and so a typo in a .bicepparam file fails at compile time instead of during deployment.

@export()
@description('Environments this template knows how to size. Add one here before adding a param file.')
type environmentName = 'dev' | 'test' | 'prod'

@export()
@description('App Service plan size. Slots need Standard or higher; zone redundancy needs Premium v3 and 2+ instances.')
type appServicePlanConfig = {
  @description('Plan SKU name, e.g. B1, S1, P0v3, P1v3.')
  skuName: string

  @minValue(1)
  @maxValue(30)
  instanceCount: int

  zoneRedundant: bool
}

@export()
@description('Azure SQL database compute. Serverless SKUs (GP_S_*) honour autoPauseDelayMinutes and minCapacity.')
type sqlDatabaseConfig = {
  @description('e.g. GP_S_Gen5_1 (serverless) or GP_Gen5_2 (provisioned).')
  skuName: string

  tier: 'GeneralPurpose' | 'BusinessCritical' | 'Hyperscale'

  family: 'Gen5'

  @minValue(1)
  vCores: int

  @description('Serverless only. -1 disables auto-pause.')
  autoPauseDelayMinutes: int?

  @description('Serverless only. Minimum vCores while running.')
  minCapacity: string?

  @description('Use the subscription\'s free Azure SQL offer (one database per subscription, serverless GP only).')
  useFreeLimit: bool

  zoneRedundant: bool

  backupStorageRedundancy: 'Local' | 'Zone' | 'Geo' | 'GeoZone'

  @minValue(1)
  @maxValue(35)
  pointInTimeRestoreDays: int
}

@export()
@description('Microsoft Entra principal that administers the SQL server. Normally the pipeline\'s service principal or a DBA group.')
type sqlEntraAdmin = {
  @description('Display name of the user, group, or app registration.')
  login: string

  @description('Object ID (users/groups) or service principal object ID (apps).')
  objectId: string

  principalType: 'User' | 'Group' | 'Application'
}

@export()
@description('An app setting whose value comes from a Key Vault secret through a Key Vault reference.')
type keyVaultAppSetting = {
  @description('App setting name. Use __ for nested .NET config keys, e.g. Demo__ApiKey.')
  setting: string

  @description('Secret name in the vault (letters, digits, dashes).')
  secretName: string
}

@export()
@description('Address space for the optional private-networking layout.')
type networkConfig = {
  vnetAddressPrefix: string

  @description('Delegated to Microsoft.Web/serverFarms for App Service VNet integration. /27 or larger.')
  appSubnetPrefix: string

  @description('Holds the SQL and Key Vault private endpoints.')
  privateEndpointSubnetPrefix: string
}
# sync_records
# sync_records