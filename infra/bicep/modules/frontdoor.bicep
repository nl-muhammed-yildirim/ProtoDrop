// Front Door Standard + WAF (TA-11.3, TA-9.5). One endpoint host serves the SPA (/*) and the API (/api/*),
// so the browser talks to a single origin (cookies first-party, no CORS preflight in staging/prod).
// Custom domains (P-04/P-05) are added later; the default *.azurefd.net host works without DNS.
// WAF rate limits per client IP, verbatim TA-9.5: auth 10/min, draft 10/min, public 30/min, admin 60/min.
param base string
param webHost string
param apiHost string

resource profile 'Microsoft.Cdn/profiles@2023-05-01' = {
  name: '${base}-fd'
  location: 'global'
  sku: { name: 'Standard_AzureFrontDoor' }
}

resource endpoint 'Microsoft.Cdn/profiles/afdEndpoints@2023-05-01' = {
  parent: profile
  name: '${base}-ep'
  location: 'global'
  properties: { enabledState: 'Enabled' }
}

resource webGroup 'Microsoft.Cdn/profiles/originGroups@2023-05-01' = {
  parent: profile
  name: 'web'
  properties: {
    loadBalancingSettings: { sampleSize: 4, successfulSamplesRequired: 3 }
    healthProbeSettings: { probePath: '/', probeRequestType: 'HEAD', probeProtocol: 'Https', probeIntervalInSeconds: 120 }
  }
}

resource apiGroup 'Microsoft.Cdn/profiles/originGroups@2023-05-01' = {
  parent: profile
  name: 'api'
  properties: {
    loadBalancingSettings: { sampleSize: 4, successfulSamplesRequired: 3 }
    // TA-12.2: Front Door health probe on GET /health.
    healthProbeSettings: { probePath: '/health', probeRequestType: 'GET', probeProtocol: 'Https', probeIntervalInSeconds: 60 }
  }
}

resource webOrigin 'Microsoft.Cdn/profiles/originGroups/origins@2023-05-01' = {
  parent: webGroup
  name: 'web'
  properties: {
    hostName: webHost
    originHostHeader: webHost
    httpsPort: 443
    priority: 1
    weight: 1000
  }
}

resource apiOrigin 'Microsoft.Cdn/profiles/originGroups/origins@2023-05-01' = {
  parent: apiGroup
  name: 'api'
  properties: {
    hostName: apiHost
    originHostHeader: apiHost
    httpsPort: 443
    priority: 1
    weight: 1000
  }
}

resource apiRoute 'Microsoft.Cdn/profiles/afdEndpoints/routes@2023-05-01' = {
  parent: endpoint
  name: 'api'
  dependsOn: [apiOrigin]
  properties: {
    originGroup: { id: apiGroup.id }
    patternsToMatch: ['/api/*', '/health']
    supportedProtocols: ['Https']
    httpsRedirect: 'Enabled'
    forwardingProtocol: 'HttpsOnly'
    linkToDefaultDomain: 'Enabled'
  }
}

resource webRoute 'Microsoft.Cdn/profiles/afdEndpoints/routes@2023-05-01' = {
  parent: endpoint
  name: 'web'
  dependsOn: [webOrigin, apiRoute]
  properties: {
    originGroup: { id: webGroup.id }
    patternsToMatch: ['/*']
    supportedProtocols: ['Http', 'Https']
    httpsRedirect: 'Enabled'
    forwardingProtocol: 'HttpsOnly'
    linkToDefaultDomain: 'Enabled'
  }
}

var limits = [
  { name: 'AuthPerIp', path: '/api/v1/auth/', threshold: 10, priority: 10 }
  { name: 'DraftPerIp', path: '/api/v1/transfers/draft', threshold: 10, priority: 20 }
  { name: 'PublicPerIp', path: '/api/v1/public/', threshold: 30, priority: 30 }
  { name: 'AdminPerIp', path: '/api/v1/admin/', threshold: 60, priority: 40 }
]

resource waf 'Microsoft.Network/FrontDoorWebApplicationFirewallPolicies@2022-05-01' = {
  name: replace('${base}waf', '-', '')
  location: 'global'
  sku: { name: 'Standard_AzureFrontDoor' }
  properties: {
    policySettings: { enabledState: 'Enabled', mode: 'Prevention' }
    customRules: {
      rules: [for l in limits: {
        name: l.name
        priority: l.priority
        enabledState: 'Enabled'
        ruleType: 'RateLimitRule'
        rateLimitDurationInMinutes: 1
        rateLimitThreshold: l.threshold
        matchConditions: [
          {
            matchVariable: 'RequestUri'
            operator: 'Contains'
            matchValue: [l.path]
            transforms: ['Lowercase']
          }
        ]
        action: 'Block'
      }]
    }
  }
}

resource security 'Microsoft.Cdn/profiles/securityPolicies@2023-05-01' = {
  parent: profile
  name: 'waf'
  properties: {
    parameters: {
      type: 'WebApplicationFirewall'
      wafPolicy: { id: waf.id }
      associations: [
        {
          domains: [{ id: endpoint.id }]
          patternsToMatch: ['/*']
        }
      ]
    }
  }
}

output profileId string = profile.id
output endpointHost string = endpoint.properties.hostName
