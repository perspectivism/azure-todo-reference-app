#Requires -Version 7.0
<#
.SYNOPSIS
    Fast local pre-commit checks. A developer guardrail, not the authoritative quality gate.

.DESCRIPTION
    Runs only the checks whose artifacts exist in the repository:
      - repository safety checks (always)
      - formatting validation, build, and unit tests (when the solution exists)
      - Bicep build (when infra/main.bicep exists)
    Never deploys, never runs cloud or long-running integration tests.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot
try {
    function Invoke-Step([string] $Name, [scriptblock] $Action) {
        Write-Host "==> $Name" -ForegroundColor Cyan
        & $Action
        if ($LASTEXITCODE -ne 0) {
            Write-Host "pre-commit: '$Name' failed." -ForegroundColor Red
            exit 1
        }
    }

    Invoke-Step 'Repository safety' { pwsh -NoProfile -File ./scripts/check-repo-safety.ps1 }

    $solution = Get-ChildItem -Path . -Filter '*.slnx' -File | Select-Object -First 1
    if ($solution) {
        Invoke-Step 'Build' { dotnet build $solution.Name --nologo -v quiet }
        Invoke-Step 'Formatting (dotnet format --verify-no-changes)' { dotnet format $solution.Name --verify-no-changes --no-restore -v quiet }
        $unitTests = 'tests/Todo.UnitTests/Todo.UnitTests.csproj'
        if (Test-Path $unitTests) {
            Invoke-Step 'Unit tests' { dotnet test $unitTests --no-build --nologo -v quiet }
        }
    }
    else {
        Write-Host 'No solution yet; skipping build, format, and unit tests.'
    }

    if (Test-Path ./infra/main.bicep) {
        Invoke-Step 'Bicep build' { az bicep build --file ./infra/main.bicep --stdout | Out-Null }
    }
    else {
        Write-Host 'No infra/main.bicep yet; skipping Bicep validation.'
    }

    Write-Host 'pre-commit: all checks passed.' -ForegroundColor Green
    exit 0
}
finally {
    Pop-Location
}
