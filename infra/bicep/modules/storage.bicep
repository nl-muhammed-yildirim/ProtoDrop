// One storage account, four app containers (TA-3.5 + `exports` for F-XCT-004) + `deploy` (Flex package).
// Browser uploads/downloads go straight to blob via SAS (ADR-002), so public network access stays on and
// shared-key signing stays on (SAS is signed with the account key from Key Vault, TA-9.3).
// Lifecycle: staging 24 h, transfers 30 d safety net, exports 7 d (D-22). Soft delete 14 d (TA-11.2).
param name string
param location string
param geoRedundant bool
param corsOrigins array
param keyVaultName string
param dataPrincipalId string

resource sa 'Microsoft.Storage/storageAccounts@2023-01-01' = {
  name: name
  location: location
  kind: 'StorageV2'
  sku: { name: geoRedundant ? 'Standard_GRS' : 'Standard_LRS' }
  properties: {
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    allowBlobPublicAccess: false
    allowSharedKeyAccess: true
    publicNetworkAccess: 'Enabled'
  }
}

resource blob 'Microsoft.Storage/storageAccounts/blobServices@2023-01-01' = {
  parent: sa
  name: 'default'
  properties: {
    deleteRetentionPolicy: { enabled: true, days: 14 }
    containerDeleteRetentionPolicy: { enabled: true, days: 14 }
    isVersioningEnabled: false
    cors: {
      corsRules: [
        {
          allowedOrigins: corsOrigins
          allowedMethods: ['GET', 'HEAD', 'PUT', 'OPTIONS']
          allowedHeaders: ['*']
          exposedHeaders: ['*']
          maxAgeInSeconds: 3600
        }
      ]
    }
  }
}

resource containers 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-01-01' = [for c in ['staging', 'transfers', 'exports', 'avatars', 'deploy']: {
  parent: blob
  name: c
  properties: { publicAccess: 'None' }
}]

resource lifecycle 'Microsoft.Storage/storageAccounts/managementPolicies@2023-01-01' = {
  parent: sa
  name: 'default'
  properties: {
    policy: {
      rules: [
        {
          name: 'staging-24h'
          enabled: true
          type: 'Lifecycle'
          definition: {
            filters: { blobTypes: ['blockBlob'], prefixMatch: ['staging/'] }
            actions: { baseBlob: { delete: { daysAfterModificationGreaterThan: 1 } } }
          }
        }
        {
          name: 'transfers-30d-safety-net'
          enabled: true
          type: 'Lifecycle'
          definition: {
            filters: { blobTypes: ['blockBlob'], prefixMatch: ['transfers/'] }
            actions: { baseBlob: { delete: { daysAfterModificationGreaterThan: 30 } } }
          }
        }
        {
          name: 'exports-7d'
          enabled: true
          type: 'Lifecycle'
          definition: {
            filters: { blobTypes: ['blockBlob'], prefixMatch: ['exports/'] }
            actions: { baseBlob: { delete: { daysAfterModificationGreaterThan: 7 } } }
          }
        }
      ]
    }
  }
}

var roles = [
  'b7e6dc6d-f1e8-4753-8033-0f276bb0955b' // Storage Blob Data Owner (Functions host + Flex deploy container)
  '974c5e8b-45b9-4653-ba55-5f855dd0fb88' // Storage Queue Data Contributor (Functions host)
  '0a9a7e1f-b9d0-4cc4-a60d-0319b160aaa3' // Storage Table Data Contributor (Functions host)
]

resource dataRoles 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for r in roles: {
  name: guid(sa.id, dataPrincipalId, r)
  scope: sa
  properties: {
    principalId: dataPrincipalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', r)
  }
}]

resource kv 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
}

resource keySecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: kv
  name: 'wa-blob-key'
  properties: { value: sa.listKeys().keys[0].value }
}

output name string = sa.name
// The code expects no trailing slash (AccountUrl + "/container/...").
output blobEndpoint string = 'https://${sa.name}.blob.${environment().suffixes.storage}'
