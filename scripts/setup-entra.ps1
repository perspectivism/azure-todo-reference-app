#Requires -Version 7.0
<#
.SYNOPSIS
    Creates and finalizes the todo-api and todo-web Microsoft Entra app registrations (idempotent).

.DESCRIPTION
    Interactive, user-authorized operation. Uses the signed-in Azure CLI user's permissions (Microsoft Graph).
    CI never runs this script; the todo-deployer identity has no Graph permissions.

    -Phase Create (before infrastructure):
        todo-api-<env>: single-tenant, identifier URI api://<appId>, v2 access tokens, delegated scope access_as_user,
                        service principal.
        todo-web-<env>: single-tenant web app with delegated permission to access_as_user, service principal.
        Pre-authorizes todo-web and the Azure CLI (04b07795-8ddb-461a-bbee-02f9e1bf7b46, used to obtain test tokens)
        for access_as_user so no consent prompt is needed for that scope.
        Outputs TenantId, ApiClientId, WebClientId, ApiScope.

    -Phase Finalize (after infrastructure):
        Adds <WebBaseUrl>/signin-oidc to todo-web's redirect URIs (existing URIs are kept) and sets the front-channel
        logout URL. Creates or updates the federated identity credential that lets the App Service's user-assigned
        managed identity act as todo-web's client credential (no client secret).

    -DryRun performs only read operations and prints every write it would make.

.EXAMPLE
    pwsh ./scripts/setup-entra.ps1 -Phase Create -Environment dev -DryRun
.EXAMPLE
    pwsh ./scripts/setup-entra.ps1 -Phase Finalize -Environment dev -WebBaseUrl https://todo-dev-web.azurewebsites.net -WebManagedIdentityPrincipalId <principal id>
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('Create', 'Finalize')]
    [string] $Phase,

    [Parameter(Mandatory)]
    [ValidateSet('dev', 'prod')]
    [string] $Environment,

    [string] $ApiDisplayName = "todo-api-$Environment",

    [string] $WebDisplayName = "todo-web-$Environment",

    # Finalize: public base URL of the deployed Blazor app, for example https://<name>.azurewebsites.net
    [string] $WebBaseUrl,

    # Finalize: principal (object) id of the web app's user-assigned managed identity.
    [string] $WebManagedIdentityPrincipalId,

    # Optional extra base URLs to register as redirect URIs (for example https://localhost:7070 for optional local Entra sign-in).
    [string[]] $AdditionalRedirectBaseUrls = @(),

    [switch] $DryRun
)

$ErrorActionPreference = 'Stop'

$ScopeValue = 'access_as_user'
$AzureCliAppId = '04b07795-8ddb-461a-bbee-02f9e1bf7b46'
$TokenExchangeAudience = 'api://AzureADTokenExchange'
$FederatedCredentialName = "appservice-uami-$Environment"
$GraphApplications = 'https://graph.microsoft.com/v1.0/applications'
$tempDir = Join-Path ([IO.Path]::GetTempPath()) "setup-entra-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $tempDir | Out-Null

function Write-Step([string] $Message) { Write-Host "==> $Message" -ForegroundColor Cyan }

function Invoke-AzJson {
    param([string[]] $Arguments)
    $output = & az @Arguments --only-show-errors --output json 2>&1
    if ($LASTEXITCODE -ne 0) { throw "az $($Arguments -join ' ') failed: $output" }
    $text = ($output | Out-String).Trim()
    if (-not $text) { return $null }
    return $text | ConvertFrom-Json -Depth 32
}

# Every Entra write goes through this function so -DryRun can never write.
function Invoke-EntraWrite {
    param([string] $Description, [scriptblock] $Action)
    if ($DryRun) {
        Write-Host "[dry-run] would $Description" -ForegroundColor Yellow
        return $null
    }
    Write-Host "  - $Description"
    return & $Action
}

function Invoke-GraphPatch {
    param([string] $ObjectId, [hashtable] $Body, [string] $Description)
    Invoke-EntraWrite $Description {
        $file = Join-Path $tempDir "$([guid]::NewGuid().ToString('N')).json"
        $Body | ConvertTo-Json -Depth 16 | Set-Content -Path $file -Encoding utf8NoBOM
        Invoke-AzJson @('rest', '--method', 'PATCH', '--uri', "$GraphApplications/$ObjectId", '--headers', 'Content-Type=application/json', '--body', "@$file") | Out-Null
    } | Out-Null
}

function Get-Application([string] $DisplayName) {
    $apps = @(Invoke-AzJson @('ad', 'app', 'list', '--display-name', $DisplayName))
    $apps = @($apps | Where-Object { $_ -and $_.displayName -eq $DisplayName })
    if ($apps.Count -gt 1) { throw "More than one app registration is named '$DisplayName'. Remove the duplicates or pass a different display name." }
    if ($apps.Count -eq 1) { return Invoke-AzJson @('rest', '--method', 'GET', '--uri', "$GraphApplications/$($apps[0].id)") }
    return $null
}

function Initialize-Application([string] $DisplayName) {
    $app = Get-Application $DisplayName
    if ($app) {
        Write-Host "  - found '$DisplayName' (appId $($app.appId))"
        return $app
    }
    $created = Invoke-EntraWrite "create single-tenant app registration '$DisplayName'" {
        Invoke-AzJson @('ad', 'app', 'create', '--display-name', $DisplayName, '--sign-in-audience', 'AzureADMyOrg')
    }
    if (-not $created) {
        return [pscustomobject]@{ id = "<new $DisplayName object id>"; appId = "<new $DisplayName appId>"; identifierUris = @(); api = [pscustomobject]@{ oauth2PermissionScopes = @(); preAuthorizedApplications = @() }; web = [pscustomobject]@{ redirectUris = @() }; requiredResourceAccess = @() }
    }
    return Invoke-AzJson @('rest', '--method', 'GET', '--uri', "$GraphApplications/$($created.id)")
}

function Initialize-ServicePrincipal($App) {
    if (-not $DryRun -or $App.appId -notlike '<*') {
        $sp = @(Invoke-AzJson @('ad', 'sp', 'list', '--filter', "appId eq '$($App.appId)'"))
        if ($sp | Where-Object { $_ }) { return }
    }
    Invoke-EntraWrite "create service principal for appId $($App.appId)" {
        Invoke-AzJson @('ad', 'sp', 'create', '--id', $App.appId) | Out-Null
    } | Out-Null
}

try {
    Write-Step "setup-entra.ps1 -Phase $Phase -Environment $Environment$(if ($DryRun) { ' (dry run: read-only)' })"

    $account = $null
    try { $account = Invoke-AzJson @('account', 'show') } catch { $account = $null }
    if (-not $account) {
        if ($DryRun) {
            Write-Host 'Not signed in to Azure CLI: current Entra state cannot be read. Planned operations:' -ForegroundColor Yellow
            if ($Phase -eq 'Create') {
                @(
                    "find or create '$ApiDisplayName' (single tenant), identifier URI api://<appId>, requestedAccessTokenVersion 2",
                    "ensure delegated scope '$ScopeValue' on '$ApiDisplayName' and its service principal",
                    "find or create '$WebDisplayName' (single tenant) with delegated permission to '$ScopeValue' and its service principal",
                    "pre-authorize '$WebDisplayName' and Azure CLI ($AzureCliAppId) for '$ScopeValue'"
                ) | ForEach-Object { Write-Host "[dry-run] would $_" -ForegroundColor Yellow }
            }
            else {
                @(
                    "add '<WebBaseUrl>/signin-oidc' to '$WebDisplayName' redirect URIs and set the logout URL",
                    "create or update federated credential '$FederatedCredentialName' (issuer https://login.microsoftonline.com/<tenant>/v2.0, subject <managed identity principal id>, audience $TokenExchangeAudience)"
                ) | ForEach-Object { Write-Host "[dry-run] would $_" -ForegroundColor Yellow }
            }
            exit 0
        }
        throw 'Not signed in to Azure CLI. Run az login (interactive, user-authorized) and retry.'
    }

    $tenantId = $account.tenantId
    Write-Host "Tenant: $tenantId  Subscription: $($account.name) ($($account.id))  User: $($account.user.name)"

    if ($Phase -eq 'Create') {
        Write-Step "API registration '$ApiDisplayName'"
        $api = Initialize-Application $ApiDisplayName
        $apiUri = "api://$($api.appId)"
        if (@($api.identifierUris) -notcontains $apiUri) {
            Invoke-EntraWrite "set identifier URI $apiUri" {
                Invoke-AzJson @('ad', 'app', 'update', '--id', $api.appId, '--identifier-uris', $apiUri) | Out-Null
            } | Out-Null
        }

        $scopes = @($api.api.oauth2PermissionScopes | Where-Object { $_ })
        $scope = $scopes | Where-Object { $_.value -eq $ScopeValue } | Select-Object -First 1
        $scopeId = if ($scope) { $scope.id } else { [guid]::NewGuid().ToString() }
        if (-not $scope -or $api.api.requestedAccessTokenVersion -ne 2) {
            $newScopes = @($scopes | Where-Object { $_.value -ne $ScopeValue })
            $newScopes += [ordered]@{
                id                      = $scopeId
                value                   = $ScopeValue
                type                    = 'User'
                isEnabled               = $true
                adminConsentDisplayName = 'Access the Todo API'
                adminConsentDescription = 'Allows the app to read and change the signed-in user''s todos.'
                userConsentDisplayName  = 'Access your todos'
                userConsentDescription  = 'Allows the app to read and change your todos.'
            }
            Invoke-GraphPatch $api.id @{ api = @{ requestedAccessTokenVersion = 2; oauth2PermissionScopes = $newScopes } } `
                "set v2 access tokens and delegated scope '$ScopeValue' on '$ApiDisplayName'"
        }
        Initialize-ServicePrincipal $api

        Write-Step "Web registration '$WebDisplayName'"
        $web = Initialize-Application $WebDisplayName
        $required = @($web.requiredResourceAccess | Where-Object { $_ -and $_.resourceAppId -ne $api.appId })
        $existingApiAccess = @($web.requiredResourceAccess | Where-Object { $_ -and $_.resourceAppId -eq $api.appId })
        $hasScope = $existingApiAccess | ForEach-Object { $_.resourceAccess } | Where-Object { $_.id -eq $scopeId -and $_.type -eq 'Scope' }
        if (-not $hasScope) {
            $required += @{ resourceAppId = $api.appId; resourceAccess = @(@{ id = $scopeId; type = 'Scope' }) }
            Invoke-GraphPatch $web.id @{ requiredResourceAccess = $required; web = @{ implicitGrantSettings = @{ enableIdTokenIssuance = $false; enableAccessTokenIssuance = $false } } } `
                "grant '$WebDisplayName' delegated permission '$ScopeValue' on '$ApiDisplayName'"
        }
        Initialize-ServicePrincipal $web

        Write-Step 'Pre-authorized clients'
        $preAuthorized = @($api.api.preAuthorizedApplications | Where-Object { $_ -and $_.appId -notin @($web.appId, $AzureCliAppId) })
        $desired = @(
            @{ appId = $web.appId; delegatedPermissionIds = @($scopeId) },
            @{ appId = $AzureCliAppId; delegatedPermissionIds = @($scopeId) }
        )
        $current = @($api.api.preAuthorizedApplications | Where-Object { $_ -and $_.appId -in @($web.appId, $AzureCliAppId) -and @($_.delegatedPermissionIds) -contains $scopeId })
        if ($current.Count -lt 2) {
            Invoke-GraphPatch $api.id @{ api = @{ preAuthorizedApplications = $preAuthorized + $desired } } `
                "pre-authorize '$WebDisplayName' and Azure CLI for '$ScopeValue'"
        }

        $result = [pscustomobject]@{
            TenantId    = $tenantId
            ApiClientId = $api.appId
            WebClientId = $web.appId
            ApiScope    = "$apiUri/$ScopeValue"
        }
        Write-Host ($result | Format-List | Out-String)
        return $result
    }

    # Finalize
    if (-not $WebBaseUrl -or -not ($WebBaseUrl -match '^https://')) { throw '-WebBaseUrl (https://...) is required for -Phase Finalize.' }
    $parsedPrincipalId = [guid]::Empty
    if (-not [guid]::TryParse($WebManagedIdentityPrincipalId, [ref] $parsedPrincipalId)) { throw '-WebManagedIdentityPrincipalId (a GUID) is required for -Phase Finalize.' }

    Write-Step "Finalize '$WebDisplayName'"
    $web = Get-Application $WebDisplayName
    if (-not $web) { throw "App registration '$WebDisplayName' not found. Run -Phase Create first." }

    $baseUrls = @($WebBaseUrl) + $AdditionalRedirectBaseUrls | ForEach-Object { $_.TrimEnd('/') }
    $redirects = @($web.web.redirectUris | Where-Object { $_ })
    $missing = @($baseUrls | ForEach-Object { "$_/signin-oidc" } | Where-Object { $redirects -notcontains $_ })
    $logoutUrl = "$($WebBaseUrl.TrimEnd('/'))/signout-callback-oidc"
    if ($missing.Count -gt 0 -or $web.web.logoutUrl -ne $logoutUrl) {
        Invoke-GraphPatch $web.id @{ web = @{ redirectUris = @($redirects + $missing); logoutUrl = $logoutUrl } } `
            "add redirect URI(s) $($missing -join ', ') and logout URL $logoutUrl"
    }

    $issuer = "https://login.microsoftonline.com/$tenantId/v2.0"
    $credentials = @(Invoke-AzJson @('ad', 'app', 'federated-credential', 'list', '--id', $web.appId))
    $existing = $credentials | Where-Object { $_ -and $_.name -eq $FederatedCredentialName } | Select-Object -First 1
    $parameters = [ordered]@{
        name        = $FederatedCredentialName
        issuer      = $issuer
        subject     = $WebManagedIdentityPrincipalId
        audiences   = @($TokenExchangeAudience)
        description = "App Service user-assigned managed identity used as the $WebDisplayName client credential"
    }
    $parametersFile = Join-Path $tempDir 'fic.json'
    $parameters | ConvertTo-Json -Depth 4 | Set-Content -Path $parametersFile -Encoding utf8NoBOM

    if (-not $existing) {
        Invoke-EntraWrite "create federated credential '$FederatedCredentialName' (subject $WebManagedIdentityPrincipalId)" {
            Invoke-AzJson @('ad', 'app', 'federated-credential', 'create', '--id', $web.appId, '--parameters', "@$parametersFile") | Out-Null
        } | Out-Null
    }
    elseif ($existing.subject -ne $WebManagedIdentityPrincipalId -or $existing.issuer -ne $issuer -or @($existing.audiences) -notcontains $TokenExchangeAudience) {
        Invoke-EntraWrite "update federated credential '$FederatedCredentialName' (subject $WebManagedIdentityPrincipalId)" {
            Invoke-AzJson @('ad', 'app', 'federated-credential', 'update', '--id', $web.appId, '--federated-credential-id', $existing.id, '--parameters', "@$parametersFile") | Out-Null
        } | Out-Null
    }
    else {
        Write-Host "  - federated credential '$FederatedCredentialName' is up to date"
    }

    Write-Host "Finalize complete for '$WebDisplayName'."
}
catch {
    Write-Host "setup-entra: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
finally {
    Remove-Item -Recurse -Force $tempDir -ErrorAction SilentlyContinue
}
