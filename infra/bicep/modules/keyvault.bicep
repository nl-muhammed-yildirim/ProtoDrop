// Key Vault, RBAC mode (TA-9.3). The app identity reads secrets; CI (OIDC principal) writes them via seed-secrets.sh.
param name string
param location string
param readerPrincipalId string
param softDeleteDays int
@description('Object id of the CI/OIDC service principal; gets Key Vault Secrets Officer to seed secrets. Empty = skip.')
param deployerPrincipalId string = ''

resource kv 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: name
  location: location
  properties: {
    tenantId: subscription().tenantId
    sku: { family: 'A', name: 'standard' }
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: softDeleteDays
    publicNetworkAccess: 'Enabled'
  }
}

// Key Vault Secrets User
resource read 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(kv.id, readerPrincipalId, '4633458b-17de-408a-b874-0445c86b69e6')
  scope: kv
  properties: {
    principalId: readerPrincipalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6')
  }
}

// Key Vault Secrets Officer for the pipeline principal (seed-secrets.sh writes jwt / zip / stripe secrets).
resource officer 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(deployerPrincipalId)) {
  name: guid(kv.id, deployerPrincipalId, 'b86a8fe4-44ce-4948-aee5-eccb2c155cd7')
  scope: kv
  properties: {
    principalId: deployerPrincipalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'b86a8fe4-44ce-4948-aee5-eccb2c155cd7')
  }
}

output name string = kv.name
output uri string = kv.properties.vaultUri
