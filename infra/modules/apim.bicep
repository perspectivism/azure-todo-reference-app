// API Management (Consumption) in front of the Function App. The API is imported from docs/openapi.yaml.
// validate-jwt checks every operation except GET /health; no subscription key is required.
// Consumption has no fixed outbound IP, so the Function also validates the token itself.

param location string
param serviceName string
param tags object
param publisherEmail string
param publisherName string
param appInsightsName string

param tenantId string
param apiClientId string
param functionAppUrl string

resource appInsights 'Microsoft.Insights/components@2020-02-02' existing = {
  name: appInsightsName
}

resource apim 'Microsoft.ApiManagement/service@2024-05-01' = {
  name: serviceName
  location: location
  tags: tags
  sku: {
    name: 'Consumption'
    capacity: 0
  }
  properties: {
    publisherEmail: publisherEmail
    publisherName: publisherName
  }
}

resource api 'Microsoft.ApiManagement/service/apis@2024-05-01' = {
  parent: apim
  name: 'todo-api'
  properties: {
    displayName: 'Todo API'
    // Empty suffix: gateway routes match the Function routes (/health, /todos, /todos/{id}).
    path: ''
    protocols: [
      'https'
    ]
    serviceUrl: functionAppUrl
    subscriptionRequired: false
    format: 'openapi'
    value: loadTextContent('../../docs/openapi.yaml')
  }
}

var jwtPolicy = '''
<policies>
  <inbound>
    <base />
    <validate-jwt header-name="Authorization" require-scheme="Bearer" failed-validation-httpcode="401" failed-validation-error-message="A valid access token is required." require-expiration-time="true" require-signed-tokens="true" clock-skew="120" output-token-variable-name="jwt">
      <openid-config url="{loginEndpoint}{tenantId}/v2.0/.well-known/openid-configuration" />
      <audiences>
        <audience>{apiClientId}</audience>
        <audience>api://{apiClientId}</audience>
      </audiences>
      <issuers>
        <issuer>{loginEndpoint}{tenantId}/v2.0</issuer>
      </issuers>
      <required-claims>
        <claim name="tid" match="all">
          <value>{tenantId}</value>
        </claim>
        <claim name="scp" match="any" separator=" ">
          <value>access_as_user</value>
        </claim>
      </required-claims>
    </validate-jwt>
    <choose>
      <when condition="@(!((Jwt)context.Variables[&quot;jwt&quot;]).Claims.ContainsKey(&quot;oid&quot;))">
        <return-response>
          <set-status code="401" reason="Unauthorized" />
        </return-response>
      </when>
    </choose>
  </inbound>
  <backend>
    <base />
  </backend>
  <outbound>
    <base />
  </outbound>
  <on-error>
    <base />
  </on-error>
</policies>
'''

resource apiPolicy 'Microsoft.ApiManagement/service/apis/policies@2024-05-01' = {
  parent: api
  name: 'policy'
  properties: {
    format: 'rawxml'
    // loginEndpoint is https://login.microsoftonline.com/ in the public cloud (trailing slash included).
    value: replace(replace(replace(jwtPolicy, '{loginEndpoint}', environment().authentication.loginEndpoint), '{tenantId}', tenantId), '{apiClientId}', apiClientId)
  }
}

// GET /health is anonymous: its operation policy omits <base /> so the API-level validate-jwt does not apply.
resource healthOperation 'Microsoft.ApiManagement/service/apis/operations@2024-05-01' existing = {
  parent: api
  name: 'getHealth'
}

resource healthPolicy 'Microsoft.ApiManagement/service/apis/operations/policies@2024-05-01' = {
  parent: healthOperation
  name: 'policy'
  properties: {
    format: 'rawxml'
    value: '<policies><inbound /><backend><base /></backend><outbound><base /></outbound><on-error><base /></on-error></policies>'
  }
}

// Request telemetry to Application Insights with W3C trace context. Headers and bodies are not logged.
resource logger 'Microsoft.ApiManagement/service/loggers@2024-05-01' = {
  parent: apim
  name: 'appinsights'
  properties: {
    loggerType: 'applicationInsights'
    resourceId: appInsights.id
    credentials: {
      connectionString: appInsights.properties.ConnectionString
    }
  }
}

resource diagnostics 'Microsoft.ApiManagement/service/apis/diagnostics@2024-05-01' = {
  parent: api
  name: 'applicationinsights'
  properties: {
    loggerId: logger.id
    alwaysLog: 'allErrors'
    httpCorrelationProtocol: 'W3C'
    logClientIp: false
    verbosity: 'information'
    sampling: {
      samplingType: 'fixed'
      percentage: 100
    }
    frontend: {
      request: {
        headers: []
        body: {
          bytes: 0
        }
      }
      response: {
        headers: []
        body: {
          bytes: 0
        }
      }
    }
    backend: {
      request: {
        headers: []
        body: {
          bytes: 0
        }
      }
      response: {
        headers: []
        body: {
          bytes: 0
        }
      }
    }
  }
}

output serviceName string = apim.name
output gatewayUrl string = apim.properties.gatewayUrl
