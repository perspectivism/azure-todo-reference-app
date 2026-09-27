// Cosmos DB for NoSQL in serverless capacity mode. Key-based auth is disabled; the Function App's managed identity
// gets the built-in data contributor role scoped to the todo database. Bicep owns the database and container because
// Entra-based data-plane access cannot create them.

param location string
param accountName string
param tags object

@description('Principal id of the Function App system-assigned managed identity.')
param dataContributorPrincipalId string

var databaseName = 'todo'
var containerName = 'todos'
// Built-in "Cosmos DB Built-in Data Contributor" data-plane role.
var dataContributorRoleId = '00000000-0000-0000-0000-000000000002'

resource account 'Microsoft.DocumentDB/databaseAccounts@2025-04-15' = {
  name: accountName
  location: location
  tags: tags
  kind: 'GlobalDocumentDB'
  properties: {
    databaseAccountOfferType: 'Standard'
    capabilities: [
      {
        name: 'EnableServerless'
      }
    ]
    locations: [
      {
        locationName: location
        failoverPriority: 0
        isZoneRedundant: false
      }
    ]
    consistencyPolicy: {
      defaultConsistencyLevel: 'Session'
    }
    disableLocalAuth: true
    disableKeyBasedMetadataWriteAccess: true
    minimalTlsVersion: 'Tls12'
    publicNetworkAccess: 'Enabled'
    enableFreeTier: false
  }
}

resource database 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases@2025-04-15' = {
  parent: account
  name: databaseName
  properties: {
    resource: {
      id: databaseName
    }
  }
}

// No throughput settings: serverless containers do not use provisioned throughput.
resource container 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2025-04-15' = {
  parent: database
  name: containerName
  properties: {
    resource: {
      id: containerName
      partitionKey: {
        paths: [
          '/userId'
        ]
        kind: 'Hash'
        version: 2
      }
    }
  }
}

resource dataContributor 'Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments@2025-04-15' = {
  parent: account
  name: guid(account.id, dataContributorPrincipalId, dataContributorRoleId, databaseName)
  properties: {
    principalId: dataContributorPrincipalId
    roleDefinitionId: '${account.id}/sqlRoleDefinitions/${dataContributorRoleId}'
    scope: '${account.id}/dbs/${databaseName}'
  }
  dependsOn: [
    database
  ]
}

output accountName string = account.name
output endpoint string = account.properties.documentEndpoint
