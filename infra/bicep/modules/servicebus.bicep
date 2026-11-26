// Service Bus Standard (TA-5.1): topic "core" with subscription "email" (max delivery 5, DLQ on expiry)
// and topic "billing" with "billing-mirror" (max delivery 10). 30 d retention (production plan §7).
// The connection string goes to Key Vault (the code reads Wa:ServiceBus:ConnectionString).
param name string
param location string
param keyVaultName string

resource ns 'Microsoft.ServiceBus/namespaces@2021-11-01' = {
  name: name
  location: location
  sku: { name: 'Standard', tier: 'Standard' }
  properties: { minimumTlsVersion: '1.2' }
}

resource core 'Microsoft.ServiceBus/namespaces/topics@2021-11-01' = {
  parent: ns
  name: 'core'
  properties: {
    defaultMessageTimeToLive: 'P30D'
    requiresDuplicateDetection: true
    duplicateDetectionHistoryTimeWindow: 'PT10M'
  }
}

resource email 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2021-11-01' = {
  parent: core
  name: 'email'
  properties: {
    maxDeliveryCount: 5
    deadLetteringOnMessageExpiration: true
    lockDuration: 'PT1M'
  }
}

resource billing 'Microsoft.ServiceBus/namespaces/topics@2021-11-01' = {
  parent: ns
  name: 'billing'
  properties: {
    defaultMessageTimeToLive: 'P30D'
    requiresDuplicateDetection: true
  }
}

resource billingMirror 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2021-11-01' = {
  parent: billing
  name: 'billing-mirror'
  properties: {
    maxDeliveryCount: 10
    deadLetteringOnMessageExpiration: true
  }
}

resource rootRule 'Microsoft.ServiceBus/namespaces/authorizationRules@2021-11-01' existing = {
  parent: ns
  name: 'RootManageSharedAccessKey'
}

resource kv 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
}

resource conn 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: kv
  name: 'wa-sb-connection'
  properties: { value: rootRule.listKeys().primaryConnectionString }
}

output id string = ns.id
