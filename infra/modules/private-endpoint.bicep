// One private endpoint plus its DNS zone group, which writes the A record into the privatelink zone.

param location string
param tags object
param name string
param subnetId string
param targetResourceId string

@description('Sub-resource to expose: "vault" for Key Vault, "sqlServer" for Azure SQL.')
param groupId string

param privateDnsZoneId string

resource endpoint 'Microsoft.Network/privateEndpoints@2024-05-01' = {
  name: name
  location: location
  tags: tags
  properties: {
    subnet: { id: subnetId }
    privateLinkServiceConnections: [
      {
        name: name
        properties: {
          privateLinkServiceId: targetResourceId
          groupIds: [groupId]
        }
      }
    ]
  }

  resource dns 'privateDnsZoneGroups' = {
    name: 'default'
    properties: {
      privateDnsZoneConfigs: [
        {
          name: 'zone'
          properties: { privateDnsZoneId: privateDnsZoneId }
        }
      ]
    }
  }
}

output id string = endpoint.id
# export_csv
# check_health
# export_csv
# check_health
# export_csv
# check_health