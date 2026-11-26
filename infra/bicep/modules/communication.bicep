// Communication Services + Email with an Azure-managed domain (D-13: dev/staging via IaC; prod is created
// manually with the verified protodrop.com domain + SPF/DKIM/DMARC, Preflight P-06/P-07).
// The connection string goes to Key Vault (the workers read Wa:CommunicationHub:Connection).
param base string
param keyVaultName string
param secretName string

resource email 'Microsoft.Communication/emailServices@2023-04-01' = {
  name: '${base}-email'
  location: 'global'
  properties: { dataLocation: 'Europe' }
}

resource domain 'Microsoft.Communication/emailServices/domains@2023-04-01' = {
  parent: email
  name: 'AzureManagedDomain'
  location: 'global'
  properties: {
    domainManagement: 'AzureManaged'
    userEngagementTracking: 'Disabled' // D-20: no tracking pixel
  }
}

resource acs 'Microsoft.Communication/communicationServices@2023-04-01' = {
  name: '${base}-acs'
  location: 'global'
  properties: {
    dataLocation: 'Europe'
    linkedDomains: [domain.id]
  }
}

resource kv 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
}

resource conn 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: kv
  name: secretName
  properties: { value: acs.listKeys().primaryConnectionString }
}

output senderAddress string = 'DoNotReply@${domain.properties.mailFromSenderDomain}'
