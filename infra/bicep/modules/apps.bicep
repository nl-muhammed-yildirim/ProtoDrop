// Compute (TA-11.2): Container Apps env + wa-api + wa-web (nginx SPA image; SWA not adopted, Open-Decisions
// 2026-11-26) + wa-migrate job (Dockerfile.migrate, run by CD before API cutover, TA-12.2) + Function App on
// Flex Consumption (TA-6.2, zip-deployed by CD). Every secret is a Key Vault reference read with the app
// identity (ADR approved 2026-11-26); SQL uses the same identity (no password).
param base string
param compact string
param location string
param imageTag string
param acrLoginServer string
param identityId string
param identityClientId string
param logAnalyticsId string
param appInsightsConnectionString string
param keyVaultUri string
param sqlConnectionString string
param blobAccountUrl string
param storageAccountName string
param publicUrl string
param adminEmails string
param emailSender string
param emailFromAddress string
param communicationSecretName string
param aspnetEnvironment string

var kvSecret = '${keyVaultUri}secrets'
// Secrets the CD seeds (infra/scripts/seed-secrets.sh) plus the ones Bicep writes (blob key, SB, CH).
var secretNames = {
  jwt: 'wa-jwt-secret'
  zip: 'wa-zip-sign-key'
  blobKey: 'wa-blob-key'
  sb: 'wa-sb-connection'
  ch: communicationSecretName
  stripeKey: 'wa-stripe-secret-key'
  stripeHook: 'wa-stripe-webhook-secret'
}

resource law 'Microsoft.OperationalInsights/workspaces@2022-10-01' existing = {
  name: last(split(logAnalyticsId, '/'))
}

resource cae 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: '${base}-cae'
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: law.properties.customerId
        sharedKey: law.listKeys().primarySharedKey
      }
    }
  }
}

var apiName = '${base}-api'
var webName = '${base}-web'
var faName = '${base}-fa-${take(uniqueString(resourceGroup().id), 6)}'
var apiFqdn = '${apiName}.${cae.properties.defaultDomain}'
var webFqdn = '${webName}.${cae.properties.defaultDomain}'
// Front Door routes /api/* and /* on one host, so with a public URL the SPA and API share an origin.
var effectivePublicUrl = empty(publicUrl) ? 'https://${webFqdn}' : publicUrl
var effectiveApiUrl = empty(publicUrl) ? 'https://${apiFqdn}' : publicUrl

var registries = [
  {
    server: acrLoginServer
    identity: identityId
  }
]

var apiSecrets = [for s in items(secretNames): {
  name: replace(s.value, 'wa-', '')
  keyVaultUrl: '${kvSecret}/${s.value}'
  identity: identityId
}]

var commonEnv = [
  { name: 'AZURE_CLIENT_ID', value: identityClientId }
  { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsightsConnectionString }
]

resource api 'Microsoft.App/containerApps@2024-03-01' = {
  name: apiName
  location: location
  identity: { type: 'UserAssigned', userAssignedIdentities: { '${identityId}': {} } }
  properties: {
    managedEnvironmentId: cae.id
    configuration: {
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
        allowInsecure: false
      }
      registries: registries
      secrets: apiSecrets
    }
    template: {
      containers: [
        {
          name: 'wa-api'
          image: '${acrLoginServer}/wa-api:${imageTag}'
          resources: { cpu: json('0.5'), memory: '1Gi' }
          env: concat(commonEnv, [
            { name: 'ASPNETCORE_ENVIRONMENT', value: aspnetEnvironment }
            { name: 'ASPNETCORE_URLS', value: 'http://+:8080' }
            { name: 'ConnectionStrings__WaDb', value: sqlConnectionString }
            { name: 'Wa__Blob__AccountUrl', value: blobAccountUrl }
            { name: 'Wa__Blob__AccountKey', secretRef: 'blob-key' }
            { name: 'Wa__ServiceBus__ConnectionString', secretRef: 'sb-connection' }
            { name: 'Wa__Jwt__Secret', secretRef: 'jwt-secret' }
            { name: 'Wa__Zip__SignKey', secretRef: 'zip-sign-key' }
            { name: 'Wa__Stripe__SecretKey', secretRef: 'stripe-secret-key' }
            { name: 'Wa__Stripe__WebhookSecret', secretRef: 'stripe-webhook-secret' }
            { name: 'Wa__Url__Public', value: effectivePublicUrl }
            { name: 'Wa__Url__Api', value: effectiveApiUrl }
            { name: 'Wa__Cors__AllowedOrigins', value: effectivePublicUrl }
            { name: 'Wa__AdminEmails', value: adminEmails }
            { name: 'Wa__ZipFunction__BaseUrl', value: 'https://${fa.properties.defaultHostName}' }
          ])
          probes: [
            {
              type: 'Liveness'
              httpGet: { path: '/health', port: 8080 }
              initialDelaySeconds: 10
              periodSeconds: 30
            }
          ]
        }
      ]
      // TA-11.2: min 1 / max 10, scale on load.
      scale: {
        minReplicas: 1
        maxReplicas: 10
        rules: [
          {
            name: 'http'
            http: { metadata: { concurrentRequests: '50' } }
          }
        ]
      }
    }
  }
}

resource web 'Microsoft.App/containerApps@2024-03-01' = {
  name: webName
  location: location
  identity: { type: 'UserAssigned', userAssignedIdentities: { '${identityId}': {} } }
  properties: {
    managedEnvironmentId: cae.id
    configuration: {
      ingress: {
        external: true
        targetPort: 5173
        transport: 'auto'
        allowInsecure: false
      }
      registries: registries
    }
    template: {
      containers: [
        {
          name: 'wa-web'
          image: '${acrLoginServer}/wa-web:${imageTag}'
          resources: { cpu: json('0.25'), memory: '0.5Gi' }
          // CHORE-deploy-hardening: nginx CSP template vars. Same-origin API behind Front Door ('self'),
          // else the api Container App host; blob origin for direct SAS upload/download (ADR-002).
          env: [
            { name: 'WA_API_ORIGIN', value: empty(publicUrl) ? 'https://${apiFqdn}' : '\'self\'' }
            { name: 'WA_BLOB_ORIGIN', value: blobAccountUrl }
          ]
        }
      ]
      scale: { minReplicas: 1, maxReplicas: 3 }
    }
  }
}

resource migrate 'Microsoft.App/jobs@2024-03-01' = {
  name: '${base}-migrate'
  location: location
  identity: { type: 'UserAssigned', userAssignedIdentities: { '${identityId}': {} } }
  properties: {
    environmentId: cae.id
    configuration: {
      triggerType: 'Manual'
      replicaTimeout: 1800
      replicaRetryLimit: 0
      manualTriggerConfig: { parallelism: 1, replicaCompletionCount: 1 }
      registries: registries
    }
    template: {
      containers: [
        {
          name: 'wa-migrate'
          image: '${acrLoginServer}/wa-migrate:${imageTag}'
          resources: { cpu: json('1.0'), memory: '2Gi' }
          env: concat(commonEnv, [
            { name: 'ConnectionStrings__WaDb', value: sqlConnectionString }
          ])
        }
      ]
    }
  }
}

// ---- Functions: Flex Consumption (FC1), identity-based host storage, zip package in the `deploy` container.
resource plan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: '${base}-fa-plan'
  location: location
  kind: 'functionapp'
  sku: { name: 'FC1', tier: 'FlexConsumption' }
  properties: { reserved: true }
}

var kvRef = '@Microsoft.KeyVault(SecretUri=${kvSecret}'

resource fa 'Microsoft.Web/sites@2024-04-01' = {
  name: faName
  location: location
  kind: 'functionapp,linux'
  identity: { type: 'UserAssigned', userAssignedIdentities: { '${identityId}': {} } }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    keyVaultReferenceIdentity: identityId
    functionAppConfig: {
      deployment: {
        storage: {
          type: 'blobContainer'
          value: '${blobAccountUrl}/deploy'
          authentication: {
            type: 'UserAssignedIdentity'
            userAssignedIdentityResourceId: identityId
          }
        }
      }
      scaleAndConcurrency: {
        maximumInstanceCount: 40
        instanceMemoryMB: 2048
      }
      runtime: { name: 'dotnet-isolated', version: '10.0' }
    }
    siteConfig: {
      minTlsVersion: '1.2'
      appSettings: [
        { name: 'AzureWebJobsStorage__accountName', value: storageAccountName }
        { name: 'AzureWebJobsStorage__credential', value: 'managedidentity' }
        { name: 'AzureWebJobsStorage__clientId', value: identityClientId }
        { name: 'AZURE_CLIENT_ID', value: identityClientId }
        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsightsConnectionString }
        { name: 'WaDb', value: sqlConnectionString }
        { name: 'Wa__Blob__AccountUrl', value: blobAccountUrl }
        { name: 'Wa__Blob__AccountKey', value: '${kvRef}/${secretNames.blobKey})' }
        { name: 'Wa__Jwt__Secret', value: '${kvRef}/${secretNames.jwt})' }
        { name: 'Wa__Zip__SignKey', value: '${kvRef}/${secretNames.zip})' }
        { name: 'Wa__ServiceBus__ConnectionString', value: '${kvRef}/${secretNames.sb})' }
        { name: 'Wa__CommunicationHub__Connection', value: '${kvRef}/${secretNames.ch})' }
        { name: 'Wa__Email__Sender', value: emailSender }
        // CHORE-deploy-hardening: Sender=log is refused outside Development/Local (EnsureSenderAllowed).
        { name: 'AZURE_FUNCTIONS_ENVIRONMENT', value: emailSender == 'log' ? 'Development' : aspnetEnvironment }
        { name: 'Wa__Email__FromAddress', value: emailFromAddress }
        { name: 'Wa__Url__Public', value: effectivePublicUrl }
      ]
    }
  }
}

output apiFqdn string = api.properties.configuration.ingress.fqdn
output webFqdn string = web.properties.configuration.ingress.fqdn
output functionsHost string = fa.properties.defaultHostName
output functionAppName string = fa.name
output migrateJobName string = migrate.name
output computedApiFqdn string = apiFqdn
output compactName string = compact
