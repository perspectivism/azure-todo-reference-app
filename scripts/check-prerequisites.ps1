#Requires -Version 7.0
<#
.SYNOPSIS
    Read-only validation of the local development prerequisites.

.DESCRIPTION
    Prints PASS / FAIL / WARN for each check. Never installs software.
    Exit code 0 when every required check passes, 1 otherwise.

.PARAMETER RequireEmulator
    Treat an unreachable Azure Cosmos DB Emulator as a failure instead of a warning.
    Used by milestone gates that run local integration tests.
#>
[CmdletBinding()]
param(
    [switch] $RequireEmulator
)

$ErrorActionPreference = 'Stop'
$script:failures = 0

function Write-Result {
    param(
        [ValidateSet('PASS', 'FAIL', 'WARN')] [string] $Status,
        [string] $Name,
        [string] $Detail,
        [string] $Remediation
    )
    $color = @{ PASS = 'Green'; FAIL = 'Red'; WARN = 'Yellow' }[$Status]
    Write-Host ("[{0}] {1}" -f $Status, $Name) -ForegroundColor $color -NoNewline
    if ($Detail) { Write-Host " - $Detail" } else { Write-Host '' }
    if ($Remediation -and $Status -ne 'PASS') {
        Write-Host "       Fix: $Remediation"
    }
    if ($Status -eq 'FAIL') { $script:failures++ }
}

function Invoke-Tool {
    param([string] $Command, [string[]] $Arguments)
    if (-not (Get-Command $Command -ErrorAction SilentlyContinue)) {
        return $null
    }
    try {
        $output = & $Command @Arguments 2>&1 | Out-String
        if ($LASTEXITCODE -ne 0) { return $null }
        return $output.Trim()
    }
    catch {
        return $null
    }
}

Write-Host 'Checking local prerequisites (read-only)...'
Write-Host ''

# .NET 10 SDK
$sdks = Invoke-Tool 'dotnet' @('--list-sdks')
if ($sdks -and ($sdks -split "`n" | Where-Object { $_ -match '^10\.' })) {
    Write-Result PASS '.NET 10 SDK' (Invoke-Tool 'dotnet' @('--version'))
}
else {
    Write-Result FAIL '.NET 10 SDK' 'not found' 'winget install Microsoft.DotNet.SDK.10  (or https://dotnet.microsoft.com/download/dotnet/10.0)'
}

# Git
$git = Invoke-Tool 'git' @('--version')
if ($git) { Write-Result PASS 'Git' $git }
else { Write-Result FAIL 'Git' 'not found' 'winget install Git.Git' }

# PowerShell 7+
if ($PSVersionTable.PSVersion.Major -ge 7) {
    Write-Result PASS 'PowerShell 7+' "PowerShell $($PSVersionTable.PSVersion)"
}
else {
    Write-Result FAIL 'PowerShell 7+' "PowerShell $($PSVersionTable.PSVersion)" 'winget install Microsoft.PowerShell'
}

# Azure CLI
$azJson = Invoke-Tool 'az' @('version', '--output', 'json')
if ($azJson) {
    try { $azVersion = ($azJson | ConvertFrom-Json).'azure-cli' } catch { $azVersion = 'installed' }
    Write-Result PASS 'Azure CLI' $azVersion
}
else {
    Write-Result FAIL 'Azure CLI' 'not found' 'winget install Microsoft.AzureCLI'
}

# Bicep (managed through Azure CLI)
if ($azJson) {
    $bicep = Invoke-Tool 'az' @('bicep', 'version')
    if ($bicep -and $bicep -match 'Bicep CLI version') {
        Write-Result PASS 'Bicep (az bicep)' (($bicep -split "`n")[0])
    }
    else {
        Write-Result FAIL 'Bicep (az bicep)' 'not installed' 'az bicep install'
    }
}
else {
    Write-Result FAIL 'Bicep (az bicep)' 'requires Azure CLI' 'Install Azure CLI, then run: az bicep install'
}

# Azure Functions Core Tools v4
$func = Invoke-Tool 'func' @('--version')
if ($func -and $func -match '^4\.') {
    Write-Result PASS 'Azure Functions Core Tools v4' $func
}
elseif ($func) {
    Write-Result FAIL 'Azure Functions Core Tools v4' "found $func" 'winget install Microsoft.Azure.FunctionsCoreTools'
}
else {
    Write-Result FAIL 'Azure Functions Core Tools v4' 'not found' 'winget install Microsoft.Azure.FunctionsCoreTools'
}

# VS Code (recommended)
$code = Invoke-Tool 'code' @('--version')
if ($code) { Write-Result PASS 'Visual Studio Code (recommended)' (($code -split "`n")[0]) }
else { Write-Result WARN 'Visual Studio Code (recommended)' 'not found on PATH' 'winget install Microsoft.VisualStudioCode' }

# Azure Cosmos DB Emulator: installed?
$emulatorPaths = @(
    (Join-Path $env:ProgramFiles 'Azure Cosmos DB Emulator\Microsoft.Azure.Cosmos.Emulator.exe')
) | Where-Object { $_ }
$emulatorInstalled = $IsWindows -and ($emulatorPaths | Where-Object { Test-Path $_ })
$emulatorHelp = 'Install and start the emulator: https://learn.microsoft.com/en-us/azure/cosmos-db/emulator'
if ($emulatorInstalled) { Write-Result PASS 'Cosmos DB Emulator installed' }
else { Write-Result WARN 'Cosmos DB Emulator installed' 'not found in the default install location' $emulatorHelp }

# Azure Cosmos DB Emulator: reachable on its documented local endpoint (https://localhost:8081)?
$reachable = $false
$client = [System.Net.Sockets.TcpClient]::new()
try {
    $reachable = $client.ConnectAsync('localhost', 8081).Wait(3000) -and $client.Connected
}
catch {
    $reachable = $false
}
finally {
    $client.Dispose()
}
$emulatorStatus = if ($reachable) { 'PASS' } elseif ($RequireEmulator) { 'FAIL' } else { 'WARN' }
$emulatorDetail = if ($reachable) { 'https://localhost:8081' } else { 'not reachable on https://localhost:8081' }
Write-Result $emulatorStatus 'Cosmos DB Emulator reachable' $emulatorDetail "Start the Azure Cosmos DB Emulator. $emulatorHelp"

Write-Host ''
if ($script:failures -gt 0) {
    Write-Host "$($script:failures) required check(s) failed." -ForegroundColor Red
    exit 1
}
Write-Host 'All required checks passed.' -ForegroundColor Green
exit 0
