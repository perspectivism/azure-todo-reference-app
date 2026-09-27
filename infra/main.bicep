// Todo PoC infrastructure, deployed at resource-group scope. The resource group is created by scripts/deploy.ps1.
// Identifying values (tenant, client ids, contact email) come from the .bicepparam files via readEnvironmentVariable().
// No secrets are passed as parameters.

targetScope = 'resourceGroup'

@allowed([
  'dev'
  'prod'
])
param environmentName string

@description('Short lowercase prefix for resource names.')
@minLength(2)
@maxLength(10)
param namePrefix string = 'todo'

param location string = resourceGroup().location

@description('Blazor host plan. F1 (Free) by default; B1 (Basic) only by explicit user choice.')
@allowed([
  'F1'
  'B1'
])
param webPlanSku string = 'F1'

@description('Log Analytics daily ingestion cap in GB, as a decimal string (for example 0.1).')
param logAnalyticsDailyCapGb string = '0.1'

@description('Monthly budget amount in the billing currency.')
@minValue(1)
param budgetAmount int = 10

@description('Email address for budget alerts and the APIM publisher contact.')
param budgetContactEmail string

@description('Budget start date (first of a month). deploy.ps1 keeps the existing value on redeployment.')
param budgetStartDate string = utcNow('yyyy-MM-01')

@description('Microsoft Entra tenant id.')
param tenantId string

@description('Application (client) id of the todo-api registration.')
param apiClientId string

@description('Application (client) id of the todo-web registration.')
param webClientId string

@description('Flex Consumption maximum on-demand instance count (1 is the lowest allowed value).')
@minValue(1)
@maxValue(1000)
param functionMaximumInstanceCount int = 1

@description('Flex Consumption instance memory size in MB.')
@allowed([
  512
  2048
  4096
])
param functionInstanceMemoryMB int = 2048

var prefix = '${namePrefix}-${environmentName}'
var suffix = uniqueString(resourceGroup().id)
var tags = {
  project: 'azure-todo-reference-app'
  environment: environmentName
}
var cosmosAccountName = toLower('${prefix}-cosmos-${suffix}')
var storageAccountName = toLower(take('st${replace(namePrefix, '-', '')}${environmentName}${suffix}', 24))

module monitoring 'modules/monitoring.bicep' = {
  name: 'monitoring'
  params: {
    location: location
    namePrefix: prefix
    tags: tags
    dailyCapGb: logAnalyticsDailyCapGb
  }
}

module functions 'modules/functions.bicep' = {
  name: 'functions'
  params: {
    location: location
    namePrefix: prefix
    storageAccountName: storageAccountName
    tags: tags
    appInsightsName: monitoring.outputs.appInsightsName
    tenantId: tenantId
    apiClientId: apiClientId
    cosmosEndpoint: 'https://${cosmosAccountName}.documents.azure.com:443/'
    maximumInstanceCount: functionMaximumInstanceCount
    instanceMemoryMB: functionInstanceMemoryMB
  }
}

module cosmos 'modules/cosmos.bicep' = {
  name: 'cosmos'
  params: {
    location: location
    accountName: cosmosAccountName
    tags: tags
    dataContributorPrincipalId: functions.outputs.principalId
  }
}

module apim 'modules/apim.bicep' = {
  name: 'apim'
  params: {
    location: location
    serviceName: '${prefix}-apim-${suffix}'
    tags: tags
    publisherEmail: budgetContactEmail
    publisherName: 'Todo PoC'
    appInsightsName: monitoring.outputs.appInsightsName
    tenantId: tenantId
    apiClientId: apiClientId
    functionAppUrl: functions.outputs.functionAppUrl
  }
}

module web 'modules/web.bicep' = {
  name: 'web'
  params: {
    location: location
    namePrefix: prefix
    tags: tags
    appInsightsName: monitoring.outputs.appInsightsName
    planSku: webPlanSku
    tenantId: tenantId
    webClientId: webClientId
    apiClientId: apiClientId
    apiBaseUrl: apim.outputs.gatewayUrl
  }
}

module budget 'modules/budget.bicep' = {
  name: 'budget'
  params: {
    name: '${prefix}-budget'
    amount: budgetAmount
    contactEmail: budgetContactEmail
    startDate: budgetStartDate
  }
}

output functionAppName string = functions.outputs.functionAppName
output functionAppUrl string = functions.outputs.functionAppUrl
output webAppName string = web.outputs.webAppName
output webAppUrl string = web.outputs.webAppUrl
output webIdentityPrincipalId string = web.outputs.identityPrincipalId
output apimGatewayUrl string = apim.outputs.gatewayUrl
output cosmosAccountName string = cosmos.outputs.accountName
output appInsightsName string = monitoring.outputs.appInsightsName
output budgetName string = '${prefix}-budget'
