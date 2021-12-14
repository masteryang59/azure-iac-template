// Linux App Service plan and web app running as the user-assigned identity, with an optional
// "staging" slot for deploy → smoke test → swap. FTP and basic-auth publishing are off; the
// pipeline deploys with its Entra token through `az webapp deploy`.

import { appServicePlanConfig } from '../types.bicep'

param location string
param tags object
param planName string
param siteName string
param plan appServicePlanConfig
param linuxFxVersion string
param identityId string
param appSettings array
param enableStagingSlot bool

@description('Subnet for outbound VNet integration. Empty string = no VNet.')
param vnetSubnetId string

param workspaceId string

var useVnet = !empty(vnetSubnetId)

// Free and Shared plans can't run Always On.
var alwaysOn = !startsWith(plan.skuName, 'F') && !startsWith(plan.skuName, 'D')

var siteProperties = {
  serverFarmId: appPlan.id
  httpsOnly: true
  clientAffinityEnabled: false
  keyVaultReferenceIdentity: identityId
  virtualNetworkSubnetId: useVnet ? vnetSubnetId : null
  vnetRouteAllEnabled: useVnet
  siteConfig: {
    linuxFxVersion: linuxFxVersion
    alwaysOn: alwaysOn
    ftpsState: 'Disabled'
    minTlsVersion: '1.2'
    scmMinTlsVersion: '1.2'
    http20Enabled: true
    remoteDebuggingEnabled: false
    healthCheckPath: '/healthz'
    appCommandLine: 'dotnet StatusApp.dll'
  }
}

resource appPlan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: planName
  location: location
  tags: tags
  kind: 'linux'
  sku: {
    name: plan.skuName
    capacity: plan.instanceCount
  }
  properties: {
    reserved: true
    zoneRedundant: plan.zoneRedundant
  }
}

resource site 'Microsoft.Web/sites@2024-04-01' = {
  name: siteName
  location: location
  tags: tags
  kind: 'app,linux'
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${identityId}': {}
    }
  }
  properties: siteProperties
}

// appSettings are written through the config resource so the slot can override one value
// (Deployment__Slot) and mark it sticky.
resource siteSettings 'Microsoft.Web/sites/config@2024-04-01' = {
  parent: site
  name: 'appsettings'
  properties: toObject(
    concat(appSettings, [{ name: 'Deployment__Slot', value: 'production' }]),
    s => s.name,
    s => s.value
  )
}

resource stickySettings 'Microsoft.Web/sites/config@2024-04-01' = if (enableStagingSlot) {
  parent: site
  name: 'slotConfigNames'
  properties: {
    appSettingNames: ['Deployment__Slot']
  }
}

resource ftpPolicy 'Microsoft.Web/sites/basicPublishingCredentialsPolicies@2024-04-01' = {
  parent: site
  name: 'ftp'
  properties: { allow: false }
}

resource scmPolicy 'Microsoft.Web/sites/basicPublishingCredentialsPolicies@2024-04-01' = {
  parent: site
  name: 'scm'
  properties: { allow: false }
}

resource slot 'Microsoft.Web/sites/slots@2024-04-01' = if (enableStagingSlot) {
  parent: site
  name: 'staging'
  location: location
  tags: tags
  kind: 'app,linux'
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${identityId}': {}
    }
  }
  properties: siteProperties
}

resource slotSettings 'Microsoft.Web/sites/slots/config@2024-04-01' = if (enableStagingSlot) {
  parent: slot
  name: 'appsettings'
  properties: toObject(
    concat(appSettings, [{ name: 'Deployment__Slot', value: 'staging' }]),
    s => s.name,
    s => s.value
  )
}

resource slotFtpPolicy 'Microsoft.Web/sites/slots/basicPublishingCredentialsPolicies@2024-04-01' = if (enableStagingSlot) {
  parent: slot
  name: 'ftp'
  properties: { allow: false }
}

resource slotScmPolicy 'Microsoft.Web/sites/slots/basicPublishingCredentialsPolicies@2024-04-01' = if (enableStagingSlot) {
  parent: slot
  name: 'scm'
  properties: { allow: false }
}

// 2021-05-01-preview is the newest version and the first with categoryGroup; the linter's only GA alternative is 2016-09-01.
#disable-next-line use-recent-api-versions
resource diagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  scope: site
  name: 'to-log-analytics'
  properties: {
    workspaceId: workspaceId
    logs: [
      { category: 'AppServiceHTTPLogs', enabled: true }
      { category: 'AppServiceConsoleLogs', enabled: true }
      { category: 'AppServiceAppLogs', enabled: true }
      { category: 'AppServicePlatformLogs', enabled: true }
      { category: 'AppServiceAuditLogs', enabled: true }
    ]
    metrics: [
      { category: 'AllMetrics', enabled: true }
    ]
  }
}

output siteId string = site.id
output siteName string = site.name
output defaultHostName string = site.properties.defaultHostName
output stagingHostName string = enableStagingSlot ? slot!.properties.defaultHostName : ''
# archive_record
# verify_token
# archive_record
# verify_token
# archive_record