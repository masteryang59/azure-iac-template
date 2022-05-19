// Optional private-networking layout:
//   snet-app  delegated to App Service for outbound VNet integration
//   snet-pe   private endpoints for SQL and Key Vault
// plus the two privatelink DNS zones linked to the VNet so the app resolves the private IPs.

import { networkConfig } from '../types.bicep'

param location string
param tags object
param vnetName string
param config networkConfig

// Derived from the cloud's suffixes so the same file works outside public Azure.
var sqlZoneName = 'privatelink${az.environment().suffixes.sqlServerHostname}'
var keyVaultZoneName = 'privatelink.vaultcore.${replace(az.environment().suffixes.keyvaultDns, '.vault.', '')}'

var lateralTraversalRules = [
  {
    name: 'deny-lateral-ssh-rdp'
    properties: {
      description: 'Nothing in these subnets needs SSH or RDP to other hosts in the VNet.'
      priority: 4000
      direction: 'Outbound'
      access: 'Deny'
      protocol: 'Tcp'
      sourceAddressPrefix: 'VirtualNetwork'
      sourcePortRange: '*'
      destinationAddressPrefix: 'VirtualNetwork'
      destinationPortRanges: ['22', '3389']
    }
  }
]

resource appNsg 'Microsoft.Network/networkSecurityGroups@2024-05-01' = {
  name: 'nsg-${vnetName}-app'
  location: location
  tags: tags
  properties: {
    securityRules: lateralTraversalRules
  }
}

resource peNsg 'Microsoft.Network/networkSecurityGroups@2024-05-01' = {
  name: 'nsg-${vnetName}-pe'
  location: location
  tags: tags
  properties: {
    securityRules: lateralTraversalRules
  }
}

resource vnet 'Microsoft.Network/virtualNetworks@2024-05-01' = {
  name: vnetName
  location: location
  tags: tags
  properties: {
    addressSpace: {
      addressPrefixes: [config.vnetAddressPrefix]
    }
    subnets: [
      {
        name: 'snet-app'
        properties: {
          addressPrefix: config.appSubnetPrefix
          // App Service egress goes through the integration, not the subnet's implicit outbound IP.
          defaultOutboundAccess: false
          networkSecurityGroup: { id: appNsg.id }
          delegations: [
            {
              name: 'appservice'
              properties: { serviceName: 'Microsoft.Web/serverFarms' }
            }
          ]
        }
      }
      {
        name: 'snet-pe'
        properties: {
          addressPrefix: config.privateEndpointSubnetPrefix
          defaultOutboundAccess: false
          networkSecurityGroup: { id: peNsg.id }
          privateEndpointNetworkPolicies: 'Enabled'
        }
      }
    ]
  }

  resource appSubnet 'subnets' existing = {
    name: 'snet-app'
  }

  resource peSubnet 'subnets' existing = {
    name: 'snet-pe'
  }
}

resource sqlZone 'Microsoft.Network/privateDnsZones@2024-06-01' = {
  name: sqlZoneName
  location: 'global'
  tags: tags
}

resource keyVaultZone 'Microsoft.Network/privateDnsZones@2024-06-01' = {
  name: keyVaultZoneName
  location: 'global'
  tags: tags
}

resource sqlZoneLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2024-06-01' = {
  parent: sqlZone
  name: vnetName
  location: 'global'
  tags: tags
  properties: {
    registrationEnabled: false
    virtualNetwork: { id: vnet.id }
  }
}

resource keyVaultZoneLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2024-06-01' = {
  parent: keyVaultZone
  name: vnetName
  location: 'global'
  tags: tags
  properties: {
    registrationEnabled: false
    virtualNetwork: { id: vnet.id }
  }
}

output appSubnetId string = vnet::appSubnet.id
output privateEndpointSubnetId string = vnet::peSubnet.id
output sqlDnsZoneId string = sqlZone.id
output keyVaultDnsZoneId string = keyVaultZone.id
# promote_user
# compress_payload
# promote_user
# compress_payload
# promote_user