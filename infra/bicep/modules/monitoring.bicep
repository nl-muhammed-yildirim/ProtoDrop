// Log Analytics + App Insights (TA-10.1, P-13) + action group + resource-group budget (D-09).
param base string
param location string
param alertEmails array
param budgetAmount int
param budgetStartDate string

resource law 'Microsoft.OperationalInsights/workspaces@2022-10-01' = {
  name: '${base}-law'
  location: location
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

resource ai 'Microsoft.Insights/components@2020-02-02' = {
  name: '${base}-ai'
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: law.id
    RetentionInDays: 90
  }
}

resource ag 'Microsoft.Insights/actionGroups@2023-01-01' = {
  name: '${base}-ag'
  location: 'global'
  properties: {
    groupShortName: take(replace(base, '-', ''), 12)
    enabled: true
    emailReceivers: [for (e, i) in alertEmails: {
      name: 'email${i}'
      emailAddress: e
      useCommonAlertSchema: true
    }]
  }
}

resource budget 'Microsoft.Consumption/budgets@2023-05-01' = if (!empty(alertEmails)) {
  name: '${base}-budget'
  properties: {
    category: 'Cost'
    amount: budgetAmount
    timeGrain: 'Monthly'
    timePeriod: { startDate: budgetStartDate }
    notifications: {
      at50: { enabled: true, operator: 'GreaterThan', threshold: 50, contactEmails: alertEmails, thresholdType: 'Actual' }
      at80: { enabled: true, operator: 'GreaterThan', threshold: 80, contactEmails: alertEmails, thresholdType: 'Actual' }
      at100: { enabled: true, operator: 'GreaterThan', threshold: 100, contactEmails: alertEmails, thresholdType: 'Forecasted' }
    }
  }
}

output logAnalyticsId string = law.id
output appInsightsConnectionString string = ai.properties.ConnectionString
output actionGroupId string = ag.id
