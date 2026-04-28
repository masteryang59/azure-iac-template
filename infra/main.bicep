// Entry point: one resource group holding a Linux web app, an Azure SQL database and a Key Vault,
// wired together with a user-assigned managed identity so nothing stores a password.
//
//   az deployment group create -g <rg> -p infra/env/dev.bicepparam
//
// main.json next to this file is the compiled ARM template (bicep build). It backs the
// "Deploy to Azure" button and is checked for drift in CI.

metadata info = {
  title: 'Web app + Azure SQL + Key Vault'
  description: 'App Service (Linux) with a user-assigned identity, Entra-only Azure SQL, RBAC Key Vault with Key Vault references, Log Analytics, optional private endpoints.'
}

import {
  appServicePlanConfig
  environmentName
  networkConfig
  keyVaultAppSetting
  sqlDatabaseConfig
  sqlEntraAdmin
} from 'types.bicep'

// ---------- parameters ----------

@description('Short workload name used in every resource name. Lowercase letters and digits.')
@minLength(3)
@maxLength(10)
param appName string

param environment environmentName

param location string = resourceGroup().location

@description('Extra tags merged over the standard set.')
param tags object = {}

param appServicePlan appServicePlanConfig

@description('Runtime stack for the Linux web app.')
param linuxFxVersion string = 'DOTNETCORE|10.0'

@description('Creates a "staging" slot so the pipeline can deploy, smoke test, then swap.')
param enableStagingSlot bool = false

param sqlDatabase sqlDatabaseConfig

param sqlAdmin sqlEntraAdmin

@description('Public client IPs allowed through the SQL firewall (e.g. a developer running DbSetup). Ignored with private networking.')
param sqlFirewallAllowedIps string[] = []

@description('Turns on Microsoft Defender for SQL on the server. Billed per server.')
param enableSqlDefender bool = false

@description('Purge protection cannot be turned off again once on. Keep it off in throwaway environments.')
param keyVaultPurgeProtection bool

@minValue(7)
@maxValue(90)
param keyVaultSoftDeleteDays int = 90

@description('App settings resolved from Key Vault at runtime through Key Vault references.')
param keyVaultAppSettings keyVaultAppSetting[] = []

@description('Secret values to write into the vault, keyed by secret name. Empty values are skipped, so a run without them leaves existing secrets alone.')
@secure()
param secretValues object = {}

@description('Puts SQL and Key Vault behind private endpoints and routes the web app through a VNet.')
param enablePrivateNetworking bool = false

param network networkConfig = {
  vnetAddressPrefix: '10.40.0.0/22'
  appSubnetPrefix: '10.40.0.0/26'
  privateEndpointSubnetPrefix: '10.40.1.0/27'
}

@minValue(30)
@maxValue(730)
param logRetentionDays int = 30

@description('Email for metric alerts. Leave empty to skip alerting resources.')
param alertEmail string = ''

// ---------- naming ----------

var suffix = take(uniqueString(subscription().id, resourceGroup().id, appName, environment), 5)
var baseName = '${appName}-${environment}'

var names = {
  identity: 'id-${baseName}'
  logAnalytics: 'log-${baseName}-${suffix}'
  appInsights: 'appi-${baseName}-${suffix}'
  plan: 'asp-${baseName}-${suffix}'
  site: 'app-${baseName}-${suffix}'
  sqlServer: 'sql-${baseName}-${suffix}'
  sqlDatabase: 'sqldb-${appName}'
  keyVault: 'kv-${baseName}-${suffix}'
  vnet: 'vnet-${baseName}'
  actionGroup: 'ag-${baseName}'
}

var allTags = union({
  application: appName
  environment: environment
  managedBy: 'bicep'
  template: 'azure-iac-template'
}, tags)

// ---------- identity ----------

resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: names.identity
  location: location
  tags: allTags
}

// ---------- monitoring ----------

module monitoring 'modules/monitoring.bicep' = {
  name: 'monitoring'
  params: {
    location: location
    tags: allTags
    logAnalyticsName: names.logAnalytics
    appInsightsName: names.appInsights
    retentionDays: logRetentionDays
    ingestionPrincipalId: identity.properties.principalId
  }
}

// ---------- network (optional) ----------

module vnet 'modules/network.bicep' = if (enablePrivateNetworking) {
  name: 'network'
  params: {
    location: location
    tags: allTags
    vnetName: names.vnet
    config: network
  }
}

// ---------- data ----------

module keyVault 'modules/keyvault.bicep' = {
  name: 'keyvault'
  params: {
    location: location
    tags: allTags
    name: names.keyVault
    purgeProtection: keyVaultPurgeProtection
    softDeleteDays: keyVaultSoftDeleteDays
    publicNetworkAccess: !enablePrivateNetworking
    appIdentityPrincipalId: identity.properties.principalId
    secretValues: secretValues
    workspaceId: monitoring.outputs.workspaceId
  }
}

module sql 'modules/sql.bicep' = {
  name: 'sql'
  params: {
    location: location
    tags: allTags
    serverName: names.sqlServer
    databaseName: names.sqlDatabase
    admin: sqlAdmin
    database: sqlDatabase
    publicNetworkAccess: !enablePrivateNetworking
    allowedClientIps: sqlFirewallAllowedIps
    enableDefender: enableSqlDefender
    workspaceId: monitoring.outputs.workspaceId
  }
}

module keyVaultEndpoint 'modules/private-endpoint.bicep' = if (enablePrivateNetworking) {
  name: 'pe-keyvault'
  params: {
    location: location
    tags: allTags
    name: 'pe-${names.keyVault}'
    subnetId: vnet!.outputs.privateEndpointSubnetId
    targetResourceId: keyVault.outputs.id
    groupId: 'vault'
    privateDnsZoneId: vnet!.outputs.keyVaultDnsZoneId
  }
}

module sqlEndpoint 'modules/private-endpoint.bicep' = if (enablePrivateNetworking) {
  name: 'pe-sql'
  params: {
    location: location
    tags: allTags
    name: 'pe-${names.sqlServer}'
    subnetId: vnet!.outputs.privateEndpointSubnetId
    targetResourceId: sql.outputs.serverId
    groupId: 'sqlServer'
    privateDnsZoneId: vnet!.outputs.sqlDnsZoneId
  }
}

// ---------- web app ----------

// Not secret: the identity's client ID tells SqlClient which identity to request a token for.
var sqlConnectionString = 'Server=tcp:${sql.outputs.serverFqdn},1433;Database=${names.sqlDatabase};Authentication=Active Directory Managed Identity;User Id=${identity.properties.clientId};Encrypt=True;TrustServerCertificate=False;Connection Timeout=30'

var baseAppSettings = [
  { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
  { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: monitoring.outputs.appInsightsConnectionString }
  { name: 'AZURE_CLIENT_ID', value: identity.properties.clientId }
  { name: 'SCM_DO_BUILD_DURING_DEPLOYMENT', value: 'false' }
  { name: 'App__EnvironmentName', value: environment }
  { name: 'Sql__ConnectionString', value: sqlConnectionString }
]

var keyVaultReferenceSettings = [
  for s in keyVaultAppSettings: {
    name: s.setting
    value: '@Microsoft.KeyVault(VaultName=${names.keyVault};SecretName=${s.secretName})'
  }
]

module web 'modules/appservice.bicep' = {
  name: 'appservice'
  params: {
    location: location
    tags: allTags
    planName: names.plan
    siteName: names.site
    plan: appServicePlan
    linuxFxVersion: linuxFxVersion
    identityId: identity.id
    appSettings: concat(baseAppSettings, keyVaultReferenceSettings)
    enableStagingSlot: enableStagingSlot
    vnetSubnetId: enablePrivateNetworking ? vnet!.outputs.appSubnetId : ''
    workspaceId: monitoring.outputs.workspaceId
  }
  // Key Vault references resolve on first start; the role assignment and private endpoints must exist by then.
  dependsOn: [
    keyVault
    keyVaultEndpoint
    sqlEndpoint
  ]
}

module alerts 'modules/alerts.bicep' = if (!empty(alertEmail)) {
  name: 'alerts'
  params: {
    tags: allTags
    actionGroupName: names.actionGroup
    alertEmail: alertEmail
    siteId: web.outputs.siteId
    siteName: names.site
    sqlDatabaseId: sql.outputs.databaseId
    sqlDatabaseName: names.sqlDatabase
  }
}

// ---------- outputs (read by the pipeline) ----------

output webAppName string = web.outputs.siteName
output webAppHostName string = web.outputs.defaultHostName
output stagingSlotName string = enableStagingSlot ? 'staging' : ''
output stagingHostName string = web.outputs.stagingHostName
output keyVaultName string = keyVault.outputs.name
output sqlServerName string = sql.outputs.serverName
output sqlServerFqdn string = sql.outputs.serverFqdn
output sqlDatabaseName string = sql.outputs.databaseName
output identityName string = identity.name
output identityClientId string = identity.properties.clientId
output appInsightsName string = monitoring.outputs.appInsightsName
# schedule_task
# send_notification
# schedule_task
# send_notification
# schedule_task
# send_notification