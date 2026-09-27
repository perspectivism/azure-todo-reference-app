#Requires -Version 7.0
<#
.SYNOPSIS
    Small deterministic health/API check against a deployed (or local) Todo API. Returns non-zero on failure.

.DESCRIPTION
    - GET /health returns 200 (retried while the Function App cold-starts)
    - GET /todos without a token returns 401
    - with -Token: create, read, and delete a todo

    Use -BaseUrl with the APIM gateway URL, and again with the Function App URL to confirm the Function hostname
    also rejects anonymous requests. The token is never printed.

.EXAMPLE
    pwsh ./scripts/smoke-test.ps1 -BaseUrl https://todo-dev-apim-xxxx.azure-api.net
.EXAMPLE
    pwsh ./scripts/smoke-test.ps1 -BaseUrl https://todo-dev-apim-xxxx.azure-api.net -Token $env:TODOAPP_TOKEN_A
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $BaseUrl,

    [string] $Token,

    [int] $HealthTimeoutSeconds = 180
)

$ErrorActionPreference = 'Stop'
$base = $BaseUrl.TrimEnd('/')
$failures = 0

function Assert-Status([string] $Name, [int] $Expected, [int] $Actual) {
    if ($Expected -eq $Actual) {
        Write-Host "[PASS] $Name ($Actual)" -ForegroundColor Green
    }
    else {
        Write-Host "[FAIL] $Name (expected $Expected, got $Actual)" -ForegroundColor Red
        $script:failures++
    }
}

function Invoke-Api([string] $Method, [string] $Path, [object] $Body, [switch] $Authenticated) {
    $headers = @{}
    if ($Authenticated) { $headers['Authorization'] = "Bearer $Token" }
    $params = @{ Method = $Method; Uri = "$base$Path"; Headers = $headers; SkipHttpErrorCheck = $true; TimeoutSec = 30 }
    if ($null -ne $Body) {
        $params['Body'] = ($Body | ConvertTo-Json)
        $params['ContentType'] = 'application/json'
    }
    return Invoke-WebRequest @params
}

Write-Host "Smoke test: $base$(if ($Token) { ' (authenticated checks enabled)' })"

# 1. Health (retry while cold-starting).
$deadline = (Get-Date).AddSeconds($HealthTimeoutSeconds)
$healthStatus = 0
do {
    try { $healthStatus = (Invoke-Api GET '/health').StatusCode } catch { $healthStatus = 0 }
    if ($healthStatus -ne 200) { Start-Sleep -Seconds 5 }
} while ($healthStatus -ne 200 -and (Get-Date) -lt $deadline)
Assert-Status 'GET /health' 200 $healthStatus

# 2. Anonymous rejection.
Assert-Status 'GET /todos without a token' 401 (Invoke-Api GET '/todos').StatusCode

# 3. Authenticated create/read/delete.
if ($Token) {
    $create = Invoke-Api POST '/todos' @{ title = "smoke-test $(Get-Date -Format o)" } -Authenticated
    Assert-Status 'POST /todos' 201 $create.StatusCode
    if ($create.StatusCode -eq 201) {
        $id = ($create.Content | ConvertFrom-Json).id
        Assert-Status 'GET /todos/{id}' 200 (Invoke-Api GET "/todos/$id" -Authenticated).StatusCode
        Assert-Status 'DELETE /todos/{id}' 204 (Invoke-Api DELETE "/todos/$id" -Authenticated).StatusCode
        Assert-Status 'GET /todos/{id} after delete' 404 (Invoke-Api GET "/todos/$id" -Authenticated).StatusCode
    }
}

if ($failures -gt 0) {
    Write-Host "Smoke test failed: $failures check(s)." -ForegroundColor Red
    exit 1
}
Write-Host 'Smoke test passed.' -ForegroundColor Green
exit 0
