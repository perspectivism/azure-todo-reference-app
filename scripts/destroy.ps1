#Requires -Version 7.0
<#
.SYNOPSIS
    Removes a Todo PoC environment: deletes its resource group (and purges the soft-deleted API Management instance).

.DESCRIPTION
    Shows what will be removed and requires the operator to type the resource group name, unless -Yes is supplied
    for an intentional non-interactive teardown. Only the environment's resource group rg-<prefix>-<env> is deleted;
    nothing outside it is touched.

    The todo-api-<env> and todo-web-<env> Entra registrations are kept unless -RemoveEntraApps is supplied.
    Returns non-zero on failure.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('dev', 'prod')]
    [string] $Environment,

    [switch] $Yes,

    [switch] $RemoveEntraApps
)

$ErrorActionPreference = 'Stop'

function Invoke-Az {
    param([string[]] $Arguments, [switch] $Json)
    $all = @($Arguments) + @('--only-show-errors')
    if ($Json) { $all += @('--output', 'json') }
    $output = & az @all 2>&1
    if ($LASTEXITCODE -ne 0) { throw "az $($Arguments[0..1] -join ' ') failed: $($output | Out-String)" }
    if ($Json) {
        $text = ($output | Out-String).Trim()
        return $(if ($text) { $text | ConvertFrom-Json -Depth 32 } else { $null })
    }
    return $output
}

try {
    $account = $null
    try { $account = Invoke-Az @('account', 'show') -Json } catch { $account = $null }
    if (-not $account) { throw 'Not signed in to Azure CLI. Run az login and select the subscription.' }

    $namePrefix = if ($env:TODOAPP_NAME_PREFIX) { $env:TODOAPP_NAME_PREFIX } else { 'todo' }
    $resourceGroup = "rg-$namePrefix-$Environment"
    $rgExists = (Invoke-Az @('group', 'exists', '--name', $resourceGroup) | Out-String).Trim() -eq 'true'

    Write-Host "Subscription:   $($account.name) ($($account.id))"
    Write-Host "Environment:    $Environment"
    Write-Host "Resource group: $resourceGroup (exists: $rgExists)"

    $apimServices = @()
    if ($rgExists) {
        $resources = @(Invoke-Az @('resource', 'list', '--resource-group', $resourceGroup, '--query', '[].{name:name, type:type, location:location}') -Json)
        Write-Host ''
        Write-Host 'The following resources will be deleted:'
        $resources | ForEach-Object { Write-Host ("  {0,-55} {1}" -f $_.name, $_.type) }
        $apimServices = @($resources | Where-Object { $_.type -eq 'Microsoft.ApiManagement/service' })
        if ($apimServices) { Write-Host '  (the API Management instance is also purged from soft-delete so its name can be reused)' }
    }

    $entraApps = @()
    if ($RemoveEntraApps) {
        foreach ($name in @("todo-api-$Environment", "todo-web-$Environment")) {
            $entraApps += @(Invoke-Az @('ad', 'app', 'list', '--display-name', $name, '--query', '[].{appId:appId, displayName:displayName}') -Json)
        }
        Write-Host ''
        Write-Host 'The following Entra app registrations will be deleted (-RemoveEntraApps):'
        $entraApps | ForEach-Object { Write-Host "  $($_.displayName) ($($_.appId))" }
    }
    else {
        Write-Host ''
        Write-Host "Entra registrations todo-api-$Environment and todo-web-$Environment are kept (use -RemoveEntraApps to delete them)."
    }

    if (-not $rgExists -and $entraApps.Count -eq 0) {
        Write-Host 'Nothing to remove.'
        exit 0
    }

    if (-not $Yes) {
        Write-Host ''
        try { $typed = Read-Host "Type the resource group name ($resourceGroup) to confirm" } catch { $typed = '' }
        if ($typed -ne $resourceGroup) { throw 'Confirmation did not match. Nothing was removed.' }
    }

    if ($rgExists) {
        Write-Host "Deleting resource group '$resourceGroup' (this can take several minutes)..."
        Invoke-Az @('group', 'delete', '--name', $resourceGroup, '--yes') | Out-Null
        foreach ($apim in $apimServices) {
            Write-Host "Purging soft-deleted API Management instance '$($apim.name)'..."
            try {
                Invoke-Az @('apim', 'deletedservice', 'purge', '--service-name', $apim.name, '--location', $apim.location) | Out-Null
            }
            catch {
                Write-Warning "Could not purge '$($apim.name)': $($_.Exception.Message)"
            }
        }
    }

    foreach ($app in $entraApps) {
        Write-Host "Deleting app registration '$($app.displayName)'..."
        Invoke-Az @('ad', 'app', 'delete', '--id', $app.appId) | Out-Null
    }

    Write-Host 'Teardown complete.' -ForegroundColor Green
    exit 0
}
catch {
    Write-Host "destroy: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
