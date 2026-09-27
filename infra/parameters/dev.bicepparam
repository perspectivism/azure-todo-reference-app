using '../main.bicep'

// Identifying values come from environment variables (set by scripts/deploy.ps1 or the GitHub workflow).
// Nothing in this file is a secret. See docs/configuration.md.

param environmentName = 'dev'
param namePrefix = readEnvironmentVariable('TODOAPP_NAME_PREFIX', 'todo')
param location = readEnvironmentVariable('TODOAPP_LOCATION', 'eastus2')

// F1 (Free) by default. B1 (Basic) is a paid tier and only by explicit user choice.
param webPlanSku = readEnvironmentVariable('TODOAPP_WEB_PLAN_SKU', 'F1')

param logAnalyticsDailyCapGb = readEnvironmentVariable('TODOAPP_LOG_DAILY_CAP_GB', '0.1')
param budgetAmount = int(readEnvironmentVariable('TODOAPP_BUDGET_AMOUNT', '10'))
param budgetContactEmail = readEnvironmentVariable('TODOAPP_BUDGET_EMAIL', '')

param tenantId = readEnvironmentVariable('TODOAPP_TENANT_ID', '')
param apiClientId = readEnvironmentVariable('TODOAPP_API_CLIENT_ID', '')
param webClientId = readEnvironmentVariable('TODOAPP_WEB_CLIENT_ID', '')
