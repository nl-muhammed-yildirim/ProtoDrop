// Azure SQL, Entra-only auth. The app identity is the server's Entra admin, so api / workers / migrate
// connect with "Authentication=Active Directory Managed Identity" (built into SqlClient, no password, TA-9.3).
// PITR 14 d (production plan §7). No VNet yet: the firewall allows Azure services only (prod VNet is a follow-up).
param serverName string
param location string
param skuName string
param adminIdentityName string
param adminIdentityClientId string
param adminIdentityPrincipalId string
param zoneRedundant bool

resource server 'Microsoft.Sql/servers@2021-11-01' = {
  name: serverName
  location: location
  properties: {
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
    administrators: {
      administratorType: 'ActiveDirectory'
      azureADOnlyAuthentication: true
      login: adminIdentityName
      sid: adminIdentityPrincipalId
      principalType: 'Application'
      tenantId: subscription().tenantId
    }
  }
}

resource allowAzure 'Microsoft.Sql/servers/firewallRules@2021-11-01' = {
  parent: server
  name: 'AllowAzureServices'
  properties: { startIpAddress: '0.0.0.0', endIpAddress: '0.0.0.0' }
}

resource db 'Microsoft.Sql/servers/databases@2021-11-01' = {
  parent: server
  name: 'wa'
  location: location
  sku: { name: skuName }
  properties: {
    zoneRedundant: zoneRedundant
    autoPauseDelay: zoneRedundant ? -1 : 60
    minCapacity: json('0.5')
    requestedBackupStorageRedundancy: zoneRedundant ? 'Geo' : 'Local'
  }
}

resource pitr 'Microsoft.Sql/servers/databases/backupShortTermRetentionPolicies@2021-11-01' = {
  parent: db
  name: 'default'
  properties: { retentionDays: 14 }
}

output fqdn string = server.properties.fullyQualifiedDomainName
output connectionString string = 'Server=tcp:${server.properties.fullyQualifiedDomainName},1433;Database=wa;Authentication=Active Directory Managed Identity;User Id=${adminIdentityClientId};Encrypt=True;TrustServerCertificate=False;Connection Timeout=30'
