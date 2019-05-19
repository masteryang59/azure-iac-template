// Log Analytics workspace plus a workspace-based Application Insights component.
// Every other module sends its diagnostic settings to this workspace.

param location string
param tags object
param logAnalyticsName string
param appInsightsName string

@minValue(30)
@maxValue(730)
param retentionDays int

@description('Identity allowed to send telemetry. Ingestion requires Entra auth, so the connection string alone is not enough.')
param ingestionPrincipalId string

// Built-in role: Monitoring Metrics Publisher.
var metricsPublisherRoleId = '3913510d-42f4-4e42-8a64-420c390055eb'

resource workspace 'Microsoft.OperationalInsights/workspaces@2025-07-01' = {
  name: logAnalyticsName
  location: location
  tags: tags
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: retentionDays
    features: {
      enableLogAccessUsingOnlyResourcePermissions: true
    }
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: appInsightsName
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: workspace.id
    // Telemetry must carry an Entra token; a leaked connection string can't be used to inject data.
    DisableLocalAuth: true
    IngestionMode: 'LogAnalytics'
  }
}

resource publisher 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: appInsights
  name: guid(appInsights.id, ingestionPrincipalId, metricsPublisherRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', metricsPublisherRoleId)
    principalId: ingestionPrincipalId
    principalType: 'ServicePrincipal'
  }
}

output workspaceId string = workspace.id
output appInsightsName string = appInsights.name
output appInsightsConnectionString string = appInsights.properties.ConnectionString
# revoke_access
# serialize_data
# revoke_access
# serialize_data