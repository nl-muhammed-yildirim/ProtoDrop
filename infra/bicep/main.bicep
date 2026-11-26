// ProtoDrop infrastructure — one resource group per environment (TA-11.2 / TA-11.4).
// Deploy:  az deployment group create -g wa-<env>-rg -f infra/bicep/main.bicep -p infra/bicep/main/<env>.bicepparam
// Two phases (cd-staging.yml): deployApps=false creates the platform (ACR, KV, SQL, ...); images are pushed;
// then deployApps=true creates/updates the Container Apps, the migrate job and the Function App.
targetScope = 'resourceGroup'

@allowed(['dev', 'staging', 'prod'])
param env string
param location string = resourceGroup().location
@description('Short prefix for every resource name (TA-11.2 uses "wa").')
param prefix string = 'wa'
@description('false = platform only (first run, before images exist).')
param deployApps bool = true
@description('Image tag pushed to ACR by CI (git sha).')
param imageTag string = 'latest'
@description('Public base URL of the SPA, e.g. https://wa-staging-xxxx.z01.azurefd.net (Front Door default host) or https://www.protodrop.com.')
param publicUrl string = ''
@description('Comma-separated admin emails (D-18). Never committed for prod — pass from a pipeline variable.')
param adminEmails string = ''
@description('Email From address (D-04). Empty = the Azure-managed domain sender (dev/staging).')
param emailFromAddress string = ''
@description('Create Front Door Standard + WAF (TA-11.3). Off for dev to save cost.')
param enableFrontDoor bool = true
@description('Create Communication Services + Azure-managed email domain (D-13: dev/staging via IaC, prod manual).')
param createCommunication bool = env != 'prod'
@description('Existing Communication Services connection string secret already in Key Vault (prod, manual P-06).')
param communicationSecretName string = 'wa-ch-connection'
@description('Monthly budget in USD (D-09: ~$1,800 prod target; alerts at 50/80/100 %).')
param budgetAmount int = env == 'prod' ? 1800 : 150
@description('Who receives budget / alert mail.')
param alertEmails array = []
@description('Budget start (first day of the current month).')
param budgetStartDate string = '${utcNow('yyyy-MM')}-01'
@description('SQL database SKU (serverless for dev/staging).')
param sqlSkuName string = env == 'prod' ? 'GP_S_Gen5_4' : 'GP_S_Gen5_1'
@description('Email sender mode for the workers: "log" (dev only, prints mails) or "ch" (Communication Services).')
@allowed(['log', 'ch'])
param emailSender string = env == 'dev' ? 'log' : 'ch'
@description('Object id of the CI service principal (OIDC); gets Secrets Officer on Key Vault.')
param deployerPrincipalId string = ''

var base = '${prefix}-${env}'
// Globally unique, lowercase, no dashes (storage / ACR / KV limits).
var uniq = toLower(take(uniqueString(resourceGroup().id), 6))
var compact = toLower('${prefix}${env}${uniq}')

module identity 'modules/identity.bicep' = {
  name: 'identity'
  params: { name: '${base}-id', location: location }
}

module monitoring 'modules/monitoring.bicep' = {
  name: 'monitoring'
  params: {
    base: base
    location: location
    alertEmails: alertEmails
    budgetAmount: budgetAmount
    budgetStartDate: budgetStartDate
  }
}

module kv 'modules/keyvault.bicep' = {
  name: 'keyvault'
  params: {
    name: take('${compact}kv', 24)
    location: location
    readerPrincipalId: identity.outputs.principalId
    softDeleteDays: env == 'prod' ? 90 : 7
    deployerPrincipalId: deployerPrincipalId
  }
}

module acr 'modules/acr.bicep' = {
  name: 'acr'
  params: {
    name: take('${compact}acr', 50)
    location: location
    pullPrincipalId: identity.outputs.principalId
  }
}

module storage 'modules/storage.bicep' = {
  name: 'storage'
  params: {
    name: take('${compact}blob', 24)
    location: location
    geoRedundant: env == 'prod'
    corsOrigins: empty(publicUrl) ? ['*'] : [publicUrl]
    keyVaultName: kv.outputs.name
    dataPrincipalId: identity.outputs.principalId
  }
}

module sql 'modules/sql.bicep' = {
  name: 'sql'
  params: {
    serverName: '${base}-sql-${uniq}'
    location: location
    skuName: sqlSkuName
    adminIdentityName: identity.outputs.name
    adminIdentityClientId: identity.outputs.clientId
    adminIdentityPrincipalId: identity.outputs.principalId
    zoneRedundant: env == 'prod'
  }
}

module bus 'modules/servicebus.bicep' = {
  name: 'servicebus'
  params: {
    name: '${base}-sb-${uniq}'
    location: location
    keyVaultName: kv.outputs.name
  }
}

module comm 'modules/communication.bicep' = if (createCommunication) {
  name: 'communication'
  params: {
    base: base
    keyVaultName: kv.outputs.name
    secretName: communicationSecretName
  }
}

module apps 'modules/apps.bicep' = if (deployApps) {
  name: 'apps'
  params: {
    base: base
    compact: compact
    location: location
    imageTag: imageTag
    acrLoginServer: acr.outputs.loginServer
    identityId: identity.outputs.id
    identityClientId: identity.outputs.clientId
    logAnalyticsId: monitoring.outputs.logAnalyticsId
    appInsightsConnectionString: monitoring.outputs.appInsightsConnectionString
    keyVaultUri: kv.outputs.uri
    sqlConnectionString: sql.outputs.connectionString
    blobAccountUrl: storage.outputs.blobEndpoint
    storageAccountName: storage.outputs.name
    publicUrl: publicUrl
    adminEmails: adminEmails
    emailSender: emailSender
    emailFromAddress: empty(emailFromAddress) && createCommunication ? comm.outputs.senderAddress : emailFromAddress
    communicationSecretName: communicationSecretName
    aspnetEnvironment: env == 'prod' ? 'Production' : 'Staging'
  }
}

module frontDoor 'modules/frontdoor.bicep' = if (deployApps && enableFrontDoor) {
  name: 'frontdoor'
  params: {
    base: base
    webHost: deployApps ? apps.outputs.webFqdn : ''
    apiHost: deployApps ? apps.outputs.apiFqdn : ''
  }
}

module alerts 'modules/alerts.bicep' = {
  name: 'alerts'
  params: {
    base: base
    actionGroupId: monitoring.outputs.actionGroupId
    serviceBusId: bus.outputs.id
    frontDoorProfileId: deployApps && enableFrontDoor ? frontDoor.outputs.profileId : ''
  }
}

output acrLoginServer string = acr.outputs.loginServer
output acrName string = acr.outputs.name
output keyVaultName string = kv.outputs.name
output sqlServerFqdn string = sql.outputs.fqdn
output apiFqdn string = deployApps ? apps.outputs.apiFqdn : ''
output webFqdn string = deployApps ? apps.outputs.webFqdn : ''
output functionAppName string = deployApps ? apps.outputs.functionAppName : ''
output migrateJobName string = deployApps ? apps.outputs.migrateJobName : ''
output frontDoorHost string = deployApps && enableFrontDoor ? frontDoor.outputs.endpointHost : ''
