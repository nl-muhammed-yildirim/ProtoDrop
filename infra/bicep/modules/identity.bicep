// One user-assigned identity for api, web, workers and the migrate job (KV read, ACR pull, SQL admin, blob data).
param name string
param location string

resource id 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: name
  location: location
}

output id string = id.id
output name string = id.name
output clientId string = id.properties.clientId
output principalId string = id.properties.principalId
