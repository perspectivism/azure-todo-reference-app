#Requires -Version 7.0
<#
.SYNOPSIS
    Runs the HTTP integration tests (tests/Todo.IntegrationTests) against a local Functions host or a deployed API.

.DESCRIPTION
    -Target Local
        Requires the Azure Cosmos DB Emulator and Cosmos__ConnectionString in src/Todo.Api/local.settings.json
        (or the environment). Builds the solution, then runs two phases against a local Azure Functions host:
          1. Auth:Mode=Dev   - full CRUD, validation, paging and cross-user suite (Target=Local tests),
                               then a Blazor app startup check: the app starts in Dev mode against the local API,
                               and GET / returns 200 with the development user and a todo seeded through the API
                               in the server-prerendered HTML (skip with -SkipWebCheck)
          2. Auth:Mode=Entra - anonymous and invalid-token requests must be rejected with 401
        For each phase the script starts the host, waits for /health, runs the tests, and stops the host.
        Host logs are written to TestResults/func-host-<phase>.log.

    -Target Azure
        Runs Target=Azure tests against -BaseUrl (or TODOAPP_API_BASE_URL), normally the APIM gateway URL.
        Authenticated tests need TODOAPP_TOKEN_A; the cross-user test also needs TODOAPP_TOKEN_B and reports
        SKIPPED without it. Tokens are read from the environment and never printed.

    Returns non-zero on any failure.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('Local', 'Azure')]
    [string] $Target,

    [string] $BaseUrl,

    [int] $Port = 7071,

    [int] $WebPort = 5230,

    [int] $StartupTimeoutSeconds = 120,

    # Skip the Blazor app startup check that follows the Dev-mode API tests.
    [switch] $SkipWebCheck
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$apiProject = Join-Path $repoRoot 'src/Todo.Api'
$testProject = Join-Path $repoRoot 'tests/Todo.IntegrationTests/Todo.IntegrationTests.csproj'
$webProject = Join-Path $repoRoot 'src/Todo.Web/Todo.Web.csproj'
$resultsDir = Join-Path $repoRoot 'TestResults'
New-Item -ItemType Directory -Force -Path $resultsDir | Out-Null

function Invoke-IntegrationTests {
    param([string] $Filter, [hashtable] $Environment, [string] $Label)

    $saved = @{}
    foreach ($key in $Environment.Keys) {
        $saved[$key] = [Environment]::GetEnvironmentVariable($key)
        [Environment]::SetEnvironmentVariable($key, $Environment[$key])
    }
    try {
        Write-Host "==> [$Label] dotnet test --filter `"$Filter`"" -ForegroundColor Cyan
        $output = & dotnet test $testProject --no-build --nologo --filter $Filter 2>&1
        $exitCode = $LASTEXITCODE
        $output | ForEach-Object { Write-Host $_ }
    }
    finally {
        foreach ($key in $saved.Keys) { [Environment]::SetEnvironmentVariable($key, $saved[$key]) }
    }

    if ($exitCode -ne 0) {
        throw "[$Label] integration tests failed (exit code $exitCode)."
    }

    $summary = ($output | Out-String) | Select-String -Pattern 'Passed:\s+(\d+)' -AllMatches
    $passed = ($summary.Matches | ForEach-Object { [int] $_.Groups[1].Value } | Measure-Object -Sum).Sum
    if ($passed -lt 1) {
        throw "[$Label] no integration test passed; the phase did not exercise the API."
    }
}

function Test-PortInUse([int] $PortNumber, [int] $TimeoutMilliseconds = 500) {
    $client = [System.Net.Sockets.TcpClient]::new()
    try { return $client.ConnectAsync('localhost', $PortNumber).Wait($TimeoutMilliseconds) -and $client.Connected }
    catch { return $false }
    finally { $client.Dispose() }
}

function Start-FunctionsHost {
    param([hashtable] $Environment, [string] $Label)

    if (Test-PortInUse $Port) {
        throw "Port $Port is already in use. Stop the process using it (for example a running 'func start') and retry."
    }

    $logFile = Join-Path $resultsDir "func-host-$Label.log"
    $saved = @{}
    foreach ($key in $Environment.Keys) {
        $saved[$key] = [Environment]::GetEnvironmentVariable($key)
        [Environment]::SetEnvironmentVariable($key, $Environment[$key])
    }
    try {
        Write-Host "==> [$Label] starting Functions host on port $Port (log: $logFile)" -ForegroundColor Cyan
        $process = Start-Process -FilePath 'func' -ArgumentList @('start', '--port', "$Port", '--no-build') `
            -WorkingDirectory (Join-Path $apiProject 'bin/Debug/net10.0') `
            -RedirectStandardOutput $logFile -RedirectStandardError "$logFile.err" -PassThru -NoNewWindow
    }
    finally {
        foreach ($key in $saved.Keys) { [Environment]::SetEnvironmentVariable($key, $saved[$key]) }
    }

    $deadline = (Get-Date).AddSeconds($StartupTimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if ($process.HasExited) {
            Get-Content $logFile -Tail 40 -ErrorAction SilentlyContinue | ForEach-Object { Write-Host $_ }
            throw "[$Label] Functions host exited during startup (exit code $($process.ExitCode))."
        }
        try {
            $response = Invoke-WebRequest -Uri "http://localhost:$Port/health" -TimeoutSec 5 -SkipHttpErrorCheck
            if ($response.StatusCode -eq 200) {
                Write-Host "[$Label] /health returned 200"
                return $process
            }
        }
        catch {
            Start-Sleep -Milliseconds 250
        }
        Start-Sleep -Seconds 1
    }

    Stop-FunctionsHost $process
    Get-Content $logFile -Tail 40 -ErrorAction SilentlyContinue | ForEach-Object { Write-Host $_ }
    throw "[$Label] Functions host did not answer /health within $StartupTimeoutSeconds seconds."
}

function Stop-FunctionsHost($Process) {
    if ($Process -and -not $Process.HasExited) {
        try { $Process.Kill($true) } catch { Write-Warning "Could not stop process $($Process.Id): $($_.Exception.Message)" }
        $Process.WaitForExit(15000) | Out-Null
    }
}

# Starts the Blazor app in Dev mode against the running local API and checks that the root URL responds with
# server-prerendered content that came from the API (profile name plus a todo seeded through the API).
function Invoke-WebStartupCheck {
    if (Test-PortInUse $WebPort) {
        throw "Port $WebPort is already in use. Stop the process using it and retry."
    }

    $webUrl = "http://localhost:$WebPort"
    $logFile = Join-Path $resultsDir 'web-app.log'
    $webEnvironment = @{
        ASPNETCORE_ENVIRONMENT = 'Development'
        ASPNETCORE_URLS        = $webUrl
        Auth__Mode             = 'Dev'
        TodoApi__BaseUrl       = "http://localhost:$Port/"
    }
    $saved = @{}
    foreach ($key in $webEnvironment.Keys) {
        $saved[$key] = [Environment]::GetEnvironmentVariable($key)
        [Environment]::SetEnvironmentVariable($key, $webEnvironment[$key])
    }
    try {
        Write-Host "==> [web] starting Blazor app on $webUrl (log: $logFile)" -ForegroundColor Cyan
        $web = Start-Process -FilePath 'dotnet' -ArgumentList @('run', '--project', "`"$webProject`"", '--no-build', '--no-launch-profile') `
            -WorkingDirectory (Split-Path $webProject) -RedirectStandardOutput $logFile -RedirectStandardError "$logFile.err" -PassThru -NoNewWindow
    }
    finally {
        foreach ($key in $saved.Keys) { [Environment]::SetEnvironmentVariable($key, $saved[$key]) }
    }

    # Seed a todo for the fixed development user (no X-Dev-User-Id header) through the API.
    $marker = "startup-check-$([guid]::NewGuid().ToString('N'))"
    $seeded = Invoke-RestMethod -Method Post -Uri "http://localhost:$Port/todos" -ContentType 'application/json' -Body (@{ title = $marker } | ConvertTo-Json)
    try {
        $deadline = (Get-Date).AddSeconds($StartupTimeoutSeconds)
        $response = $null
        while ((Get-Date) -lt $deadline -and -not $web.HasExited) {
            try {
                $response = Invoke-WebRequest -Uri "$webUrl/" -TimeoutSec 10 -SkipHttpErrorCheck
                if ($response.StatusCode -eq 200) { break }
            }
            catch {
                Start-Sleep -Milliseconds 500
            }
            Start-Sleep -Seconds 1
        }

        if (-not $response -or $response.StatusCode -ne 200) {
            Get-Content $logFile -Tail 40 -ErrorAction SilentlyContinue | ForEach-Object { Write-Host $_ }
            throw "[web] Blazor app root URL did not return 200 within $StartupTimeoutSeconds seconds."
        }

        $html = $response.Content
        $checks = [ordered]@{
            'Blazor script reference'           = $html -match '_framework/blazor\.web'
            'signed-in development user shown'  = $html.Contains('Development User')
            'todo from the API rendered'        = $html.Contains($marker)
        }
        foreach ($check in $checks.GetEnumerator()) {
            if (-not $check.Value) { throw "[web] startup check failed: $($check.Key)." }
            Write-Host "[web] PASS $($check.Key)"
        }
    }
    finally {
        Invoke-WebRequest -Method Delete -Uri "http://localhost:$Port/todos/$($seeded.id)" -SkipHttpErrorCheck | Out-Null
        Stop-FunctionsHost $web
    }
}

Push-Location $repoRoot
try {
    if ($Target -eq 'Azure') {
        if (-not $BaseUrl) { $BaseUrl = $env:TODOAPP_API_BASE_URL }
        if (-not $BaseUrl) { throw 'Azure target requires -BaseUrl or TODOAPP_API_BASE_URL (the APIM gateway URL).' }
        if (-not $env:TODOAPP_TOKEN_A) { Write-Warning 'TODOAPP_TOKEN_A is not set: authenticated tests will be skipped.' }
        if (-not $env:TODOAPP_TOKEN_B) { Write-Warning 'TODOAPP_TOKEN_B is not set: the cross-user test will be SKIPPED and the Azure user-isolation gate stays open.' }

        dotnet build $testProject --nologo -v quiet
        if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

        Invoke-IntegrationTests -Filter 'Target=Azure' -Label 'azure' -Environment @{
            TODOAPP_API_BASE_URL = $BaseUrl
            TODOAPP_TARGET       = 'Azure'
        }
        Write-Host 'Azure integration tests passed.' -ForegroundColor Green
        exit 0
    }

    if (-not (Get-Command func -ErrorAction SilentlyContinue)) {
        throw 'Azure Functions Core Tools (func) not found. Run: pwsh ./scripts/check-prerequisites.ps1'
    }

    # The local host uses the Cosmos DB Emulator. Fail fast with guidance instead of a host startup error.
    $emulatorDocs = 'https://learn.microsoft.com/en-us/azure/cosmos-db/emulator'
    if (-not (Test-PortInUse 8081 -TimeoutMilliseconds 5000)) {
        throw "Azure Cosmos DB Emulator is not reachable on https://localhost:8081. Start it; see $emulatorDocs"
    }
    $localSettings = Join-Path $apiProject 'local.settings.json'
    $hasConnectionString = [bool] $env:Cosmos__ConnectionString
    if (-not $hasConnectionString -and (Test-Path $localSettings)) {
        $hasConnectionString = (Get-Content $localSettings -Raw) -match '"Cosmos__ConnectionString"\s*:\s*"[^"]+"'
    }
    if (-not $hasConnectionString) {
        throw ("Cosmos__ConnectionString is not configured. Copy src/Todo.Api/local.settings.json.example to local.settings.json " +
            "and paste the emulator connection string from the Authentication section of $emulatorDocs")
    }

    Write-Host '==> dotnet build' -ForegroundColor Cyan
    dotnet build (Join-Path $repoRoot 'TodoApp.slnx') --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

    $localUrl = "http://localhost:$Port"
    $phases = @(
        @{
            Label = 'dev'
            Host  = @{ Auth__Mode = 'Dev' }
        },
        @{
            Label = 'entra'
            # Throwaway tenant/client ids: this phase only checks that requests without a valid token are rejected.
            Host  = @{ Auth__Mode = 'Entra'; Auth__TenantId = [guid]::NewGuid().ToString(); Auth__ClientId = [guid]::NewGuid().ToString() }
        }
    )

    foreach ($phase in $phases) {
        $process = Start-FunctionsHost -Environment $phase.Host -Label $phase.Label
        try {
            Invoke-IntegrationTests -Filter 'Target=Local' -Label $phase.Label -Environment @{
                TODOAPP_API_BASE_URL    = $localUrl
                TODOAPP_TARGET          = 'Local'
                TODOAPP_LOCAL_AUTH_MODE = if ($phase.Label -eq 'dev') { 'Dev' } else { 'Entra' }
            }
            if ($phase.Label -eq 'dev' -and -not $SkipWebCheck) {
                Invoke-WebStartupCheck
            }
        }
        finally {
            Stop-FunctionsHost $process
        }
    }

    Write-Host 'Local integration tests passed (Dev and Entra phases).' -ForegroundColor Green
    exit 0
}
catch {
    Write-Host "run-integration-tests: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
finally {
    Pop-Location
}
