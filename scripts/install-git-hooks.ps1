#Requires -Version 7.0
<#
.SYNOPSIS
    Points this repository's Git hooks at the versioned .githooks folder (repository-local setting only).
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

git -C $repoRoot config --local core.hooksPath .githooks
if ($LASTEXITCODE -ne 0) {
    Write-Error 'Failed to set core.hooksPath.'
    exit 1
}

Write-Host "core.hooksPath set to .githooks for $repoRoot"
Write-Host 'The pre-commit hook runs scripts/pre-commit.ps1.'
exit 0
