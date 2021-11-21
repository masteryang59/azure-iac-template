// Three metric alerts to one email action group: server errors, failing health checks, hot database.

param tags object
param actionGroupName string
param alertEmail string
param siteId string
param siteName string
param sqlDatabaseId string
param sqlDatabaseName string

resource actionGroup 'Microsoft.Insights/actionGroups@2023-01-01' = {
  name: actionGroupName
  location: 'global'
  tags: tags
  properties: {
    groupShortName: take(replace(actionGroupName, '-', ''), 12)
    enabled: true
    emailReceivers: [
      {
        name: 'owner'
        emailAddress: alertEmail
        useCommonAlertSchema: true
      }
    ]
  }
}

var alerts = [
  {
    name: 'alert-${siteName}-http5xx'
    description: 'More than 10 HTTP 5xx responses in 5 minutes.'
    scope: siteId
    severity: 2
    metricNamespace: 'Microsoft.Web/sites'
    metric: 'Http5xx'
    aggregation: 'Total'
    operator: 'GreaterThan'
    threshold: 10
  }
  {
    name: 'alert-${siteName}-health'
    description: 'Fewer than all instances pass /healthz.'
    scope: siteId
    severity: 1
    metricNamespace: 'Microsoft.Web/sites'
    metric: 'HealthCheckStatus'
    aggregation: 'Average'
    operator: 'LessThan'
    threshold: 100
  }
  {
    name: 'alert-${sqlDatabaseName}-cpu'
    description: 'Database CPU above 85% for 15 minutes.'
    scope: sqlDatabaseId
    severity: 3
    metricNamespace: 'Microsoft.Sql/servers/databases'
    metric: 'cpu_percent'
    aggregation: 'Average'
    operator: 'GreaterThan'
    threshold: 85
  }
]

resource metricAlerts 'Microsoft.Insights/metricAlerts@2026-01-01' = [
  for a in alerts: {
    name: a.name
    location: 'global'
    tags: tags
    properties: {
      description: a.description
      severity: a.severity
      enabled: true
      scopes: [a.scope]
      evaluationFrequency: 'PT5M'
      windowSize: a.metric == 'cpu_percent' ? 'PT15M' : 'PT5M'
      criteria: {
        'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
        allOf: [
          {
            criterionType: 'StaticThresholdCriterion'
            name: a.metric
            metricNamespace: a.metricNamespace
            metricName: a.metric
            timeAggregation: a.aggregation
            operator: a.operator
            threshold: a.threshold
          }
        ]
      }
      actions: [
        { actionGroupId: actionGroup.id }
      ]
    }
  }
]
# cancel_job
# update_cache
# cancel_job
# update_cache
# cancel_job