// Linux App Service (F1 by default) for the Blazor Web App, with a user-assigned managed identity. The identity backs
// the federated credential on the todo-web registration (SignedAssertionFromManagedIdentity), so no client secret exists.

param location string
param namePrefix string
param tags object
param appInsightsName string

@allowed([
  'F1'
  'B1'
])
param planSku string

param tenantId string
param webClientId string
param apiClientId string
param apiBaseUrl string

resource appInsights 'Microsoft.Insights/components@2020-02-02' existing = {
  name: appInsightsName
}

resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: '${namePrefix}-web-id'
  location: location
  tags: tags
}

resource plan 'Microsoft.Web/serverfarms@2024-11-01' = {
  name: '${namePrefix}-web-plan'
  location: location
  tags: tags
  kind: 'linux'
  sku: {
    name: planSku
  }
  properties: {
    reserved: true
  }
}

resource webApp 'Microsoft.Web/sites@2024-11-01' = {
  name: '${namePrefix}-web-${uniqueString(resourceGroup().id)}'
  location: location
  tags: tags
  kind: 'app,linux'
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${identity.id}': {}
    }
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    // Blazor Server keeps a SignalR circuit per user; ARR affinity keeps a browser on the same instance.
    clientAffinityEnabled: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      alwaysOn: false
      webSocketsEnabled: true
      http20Enabled: true
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      appSettings: [
        {
          name: 'ASPNETCORE_ENVIRONMENT'
          value: 'Production'
        }
        {
          // App Service terminates TLS; honour X-Forwarded-Proto so OIDC redirect URIs use https.
          name: 'ASPNETCORE_FORWARDEDHEADERS_ENABLED'
          value: 'true'
        }
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: appInsights.properties.ConnectionString
        }
        {
          name: 'Auth__Mode'
          value: 'Entra'
        }
        {
          name: 'AzureAd__TenantId'
          value: tenantId
        }
        {
          name: 'AzureAd__ClientId'
          value: webClientId
        }
        {
          name: 'AzureAd__ClientCredentials__0__SourceType'
          value: 'SignedAssertionFromManagedIdentity'
        }
        {
          name: 'AzureAd__ClientCredentials__0__ManagedIdentityClientId'
          value: identity.properties.clientId
        }
        {
          name: 'TodoApi__BaseUrl'
          value: apiBaseUrl
        }
        {
          name: 'TodoApi__Scope'
          value: 'api://${apiClientId}/access_as_user'
        }
      ]
    }
  }
}

resource scmBasicAuth 'Microsoft.Web/sites/basicPublishingCredentialsPolicies@2024-11-01' = {
  parent: webApp
  name: 'scm'
  properties: {
    allow: false
  }
}

resource ftpBasicAuth 'Microsoft.Web/sites/basicPublishingCredentialsPolicies@2024-11-01' = {
  parent: webApp
  name: 'ftp'
  properties: {
    allow: false
  }
}

output webAppName string = webApp.name
output webAppUrl string = 'https://${webApp.properties.defaultHostName}'
output identityPrincipalId string = identity.properties.principalId
output identityClientId string = identity.properties.clientId
