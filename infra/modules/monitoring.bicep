// Log Analytics workspace (with a daily ingestion cap) and workspace-based Application Insights.

param location string
param namePrefix string
param tags object

@description('Log Analytics daily ingestion cap in GB. Data above the cap is dropped until the next day.')
param dailyCapGb string

resource workspace 'Microsoft.OperationalInsights/workspaces@2025-02-01' = {
  name: '${namePrefix}-log'
  location: location
  tags: tags
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
    workspaceCapping: {
      dailyQuotaGb: json(dailyCapGb)
    }
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: '${namePrefix}-appi'
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: workspace.id
    IngestionMode: 'LogAnalytics'
    RetentionInDays: 30
  }
}

output workspaceName string = workspace.name
output appInsightsName string = appInsights.name
