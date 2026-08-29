// Key Vault in RBAC mode. The app's identity gets "Key Vault Secrets User" (read secret values)
// on this vault only; nobody gets access policies. Secrets passed in secretValues are written
// through ARM, which needs control-plane rights only.

param location string
param tags object
param name string
param purgeProtection bool

@minValue(7)
@maxValue(90)
param softDeleteDays int

param publicNetworkAccess bool

@description('Principal ID of the managed identity that reads secrets at runtime.')
param appIdentityPrincipalId string

@secure()
param secretValues object

param workspaceId string

// Built-in role: Key Vault Secrets User. scripts/bootstrap.sh limits the pipeline to granting this role
// and Monitoring Metrics Publisher (monitoring.bicep), nothing else.
var secretsUserRoleId = '4633458b-17de-408a-b874-0445c86b69e6'

resource vault 'Microsoft.KeyVault/vaults@2024-11-01' = {
  name: name
  location: location
  tags: tags
  properties: {
    tenantId: tenant().tenantId
    sku: {
      family: 'A'
      name: 'standard'
    }
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: softDeleteDays
    // The API rejects an explicit false once enabled, so omit it instead of sending false.
    enablePurgeProtection: purgeProtection ? true : null
    enabledForDeployment: false
    enabledForDiskEncryption: false
    enabledForTemplateDeployment: false
    publicNetworkAccess: publicNetworkAccess ? 'Enabled' : 'Disabled'
    networkAcls: {
      bypass: 'AzureServices'
      defaultAction: publicNetworkAccess ? 'Allow' : 'Deny'
    }
  }
}

resource secrets 'Microsoft.KeyVault/vaults/secrets@2024-11-01' = [
  for item in filter(items(secretValues), i => !empty(i.value)): {
    parent: vault
    name: item.key
    properties: {
      value: item.value
      contentType: 'text/plain'
      attributes: {
        enabled: true
      }
    }
  }
]

resource secretsUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: vault
  name: guid(vault.id, appIdentityPrincipalId, secretsUserRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', secretsUserRoleId)
    principalId: appIdentityPrincipalId
    // Skips the Graph lookup, which fails while a brand-new identity is still replicating.
    principalType: 'ServicePrincipal'
  }
}

// 2021-05-01-preview is the newest version and the first with categoryGroup; the linter's only GA alternative is 2016-09-01.
#disable-next-line use-recent-api-versions
resource diagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  scope: vault
  name: 'to-log-analytics'
  properties: {
    workspaceId: workspaceId
    logs: [
      { categoryGroup: 'allLogs', enabled: true }
    ]
    metrics: [
      { category: 'AllMetrics', enabled: true }
    ]
  }
}

output id string = vault.id
output name string = vault.name
output uri string = vault.properties.vaultUri
# refresh_token
# hash_password
# refresh_token
# hash_password
# refresh_token
# hash_password