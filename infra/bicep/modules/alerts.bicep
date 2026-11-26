// TA-10.3 alerts that need only platform metrics (no app telemetry sink):
//  - DLQ (dlq_count > 0): Service Bus DeadletteredMessages, 5-min window.
//  - 5xx spike (> 5 % over 10 min): Front Door Percentage5XX (only when Front Door exists).
// Upload success low, Expiry lag and Email failures read App Insights customEvents/customMetrics
// (docs/alerts/*.json). Those stay with T-025 until an app telemetry sink is approved (Open-Decisions 2026-11-26).
param base string
param actionGroupId string
param serviceBusId string
param frontDoorProfileId string

resource dlq 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: '${base}-dlq'
  location: 'global'
  properties: {
    description: 'TA-10.3 DLQ: dead-lettered messages > 0 (P2)'
    severity: 1
    enabled: true
    scopes: [serviceBusId]
    evaluationFrequency: 'PT5M'
    windowSize: 'PT5M'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
      allOf: [
        {
          criterionType: 'StaticThresholdCriterion'
          name: 'dlq'
          metricName: 'DeadletteredMessages'
          metricNamespace: 'Microsoft.ServiceBus/namespaces'
          operator: 'GreaterThan'
          threshold: 0
          timeAggregation: 'Maximum'
        }
      ]
    }
    actions: [{ actionGroupId: actionGroupId }]
  }
}

resource fiveXx 'Microsoft.Insights/metricAlerts@2018-03-01' = if (!empty(frontDoorProfileId)) {
  name: '${base}-5xx'
  location: 'global'
  properties: {
    description: 'TA-10.3 5xx spike: > 5 % of requests (P1). Window 15 min: metric alerts allow no 10-min window'
    severity: 0
    enabled: true
    scopes: [frontDoorProfileId]
    evaluationFrequency: 'PT5M'
    windowSize: 'PT15M'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
      allOf: [
        {
          criterionType: 'StaticThresholdCriterion'
          name: 'pct5xx'
          metricName: 'Percentage5XX'
          metricNamespace: 'Microsoft.Cdn/profiles'
          operator: 'GreaterThan'
          threshold: 5
          timeAggregation: 'Average'
        }
      ]
    }
    actions: [{ actionGroupId: actionGroupId }]
  }
}
