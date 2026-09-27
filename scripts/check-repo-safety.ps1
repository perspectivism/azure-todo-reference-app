#Requires -Version 7.0
<#
.SYNOPSIS
    Static repository safety checks. Read-only. Exit code 0 on success, 1 on any failure.

.DESCRIPTION
    Scans tracked files and untracked files that are not ignored (so it also works before a commit) for:
      - committed local-settings / secret-bearing files
      - secret patterns (storage/Cosmos keys, private keys, tokens, Entra client secrets)
      - the Azure Cosmos DB Emulator key (detected by SHA-256 hash, so the key itself is not stored here)
      - unresolved placeholder markers
      - forbidden patterns in GitHub workflows (long-lived Azure credentials, pull_request_target, unpinned actions)
      - forbidden patterns in infrastructure files (Dev identity mode, account keys, shared-key storage access)
      - Postman collection coverage of every API route
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot
try {
    $script:failures = [System.Collections.Generic.List[string]]::new()
    $script:warnings = [System.Collections.Generic.List[string]]::new()

    function Add-Failure([string] $Check, [string] $Message) { $script:failures.Add("[$Check] $Message") }
    function Add-Warning([string] $Check, [string] $Message) { $script:warnings.Add("[$Check] $Message") }

    $files = @(git ls-files --cached --others --exclude-standard) |
        Where-Object { $_ -and (Test-Path -LiteralPath $_ -PathType Leaf) } |
        Sort-Object -Unique
    if ($LASTEXITCODE -ne 0) { throw 'git ls-files failed; run this script inside the repository.' }

    $binaryExtensions = @('.png', '.jpg', '.jpeg', '.gif', '.ico', '.dll', '.exe', '.pdb', '.zip', '.woff', '.woff2', '.ttf', '.eot')
    $textFiles = $files | Where-Object { $binaryExtensions -notcontains [IO.Path]::GetExtension($_).ToLowerInvariant() }

    # 1. Secret-bearing files that must never be committed.
    $forbiddenFiles = $files | Where-Object {
        $name = [IO.Path]::GetFileName($_)
        $name -eq 'local.settings.json' -or
        $name -like 'appsettings*.Local.json' -or
        ($name -like '.env*' -and $name -ne '.env.example') -or
        $_ -match '(^|/)secrets/' -or
        $name -match '\.(pfx|p12|key|pem|publishsettings)$'
    }
    foreach ($f in $forbiddenFiles) { Add-Failure 'local-settings' "Secret-bearing file must not be committed: $f" }

    # 2. Secret patterns.
    $secretPatterns = [ordered]@{
        'storage/Cosmos account key'  = 'AccountKey\s*=\s*[A-Za-z0-9+/]{20,}'
        'shared access key'           = 'SharedAccessKey\s*=\s*[A-Za-z0-9+/]{20,}'
        'private key block'           = '-----BEGIN [A-Z ]*PRIVATE KEY-----'
        'GitHub token'                = '\b(gh[pousr]_[A-Za-z0-9]{36,}|github_pat_[A-Za-z0-9_]{40,})\b'
        'JWT / bearer token'          = '\beyJ[A-Za-z0-9_-]{10,}\.eyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}'
        'Entra client secret'         = '\b[A-Za-z0-9_\-]{3}8Q~[A-Za-z0-9_.~\-]{31,34}\b'
        'AWS access key'              = '\bAKIA[0-9A-Z]{16}\b'
    }
    # SHA-256 of the publicly documented Cosmos DB Emulator key. The key itself must not appear in any tracked file.
    $emulatorKeySha256 = 'aa6547439583c3a275d4542a0df6c1ad6a863971be9f2a2220f027cc02aef615'
    $candidateKeyPattern = '[A-Za-z0-9+/]{86}=='

    # 3. Placeholder markers. Built from parts so this script does not match itself.
    $markerPattern = '\b(' + 'TO' + 'DO' + '|' + 'FIX' + 'ME' + ')\b'
    # The specification and agent instructions define the policy and legitimately name the markers.
    $markerExemptFiles = @('SPEC.md', 'AGENTS.md')

    foreach ($file in $textFiles) {
        $content = Get-Content -LiteralPath $file -Raw -ErrorAction SilentlyContinue
        if ([string]::IsNullOrEmpty($content)) { continue }
        if ($content.Contains([char]0)) { continue }

        foreach ($entry in $secretPatterns.GetEnumerator()) {
            if ($content -match $entry.Value) {
                Add-Failure 'secrets' "Possible $($entry.Key) in $file"
            }
        }

        foreach ($m in [regex]::Matches($content, $candidateKeyPattern)) {
            $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($m.Value))).ToLowerInvariant()
            if ($hash -eq $emulatorKeySha256) {
                Add-Failure 'secrets' "Cosmos DB Emulator key found in $file"
            }
        }

        if ($markerExemptFiles -notcontains $file) {
            $lineNumber = 0
            foreach ($line in ($content -split "`n")) {
                $lineNumber++
                if ($line -cmatch $markerPattern) {
                    Add-Failure 'placeholders' "Unresolved placeholder marker in ${file}:$lineNumber"
                }
            }
        }
    }

    # 4. GitHub workflow checks.
    $workflowFiles = $textFiles | Where-Object { $_ -match '^\.github/workflows/.+\.ya?ml$' }
    $workflowForbidden = [ordered]@{
        'pull_request_target trigger'         = '\bpull_request_target\b'
        'long-lived Azure credential (creds)' = '(?m)^\s*creds\s*:'
        'AZURE_CREDENTIALS secret'            = 'AZURE_CREDENTIALS'
        'client secret'                       = '(?i)(client[-_]?secret|ARM_CLIENT_SECRET)'
        'write-all permissions'               = '(?i)permissions\s*:\s*write-all'
    }
    foreach ($wf in $workflowFiles) {
        $content = Get-Content -LiteralPath $wf -Raw
        foreach ($entry in $workflowForbidden.GetEnumerator()) {
            if ($content -match $entry.Value) { Add-Failure 'workflows' "$($entry.Key) in $wf" }
        }
        if ($content -notmatch '(?m)^permissions\s*:') {
            Add-Failure 'workflows' "Top-level 'permissions:' block missing in $wf"
        }
        foreach ($m in [regex]::Matches($content, '(?m)^\s*-?\s*uses\s*:\s*([^\s#]+)')) {
            $ref = $m.Groups[1].Value
            if ($ref.StartsWith('./')) { continue }
            if ($ref -notmatch '@.+' -or $ref -match '@(main|master|latest)$') {
                Add-Failure 'workflows' "Action not pinned to a version tag or SHA in ${wf}: $ref"
            }
        }
    }

    # 5. Infrastructure checks.
    $infraFiles = $textFiles | Where-Object { $_ -match '^infra/.+\.(bicep|bicepparam)$' }
    $infraForbidden = [ordered]@{
        'Dev identity mode'             = '(?i)[''"]Dev[''"]|X-Dev-User-Id'
        'account key usage (listKeys)'  = '(?i)listKeys\s*\('
        'connection string with key'    = '(?i)AccountKey'
        'shared-key storage access'     = '(?i)allowSharedKeyAccess\s*:\s*true'
        'Cosmos key-based auth enabled' = '(?i)disableLocalAuth\s*:\s*false'
    }
    foreach ($inf in $infraFiles) {
        $content = Get-Content -LiteralPath $inf -Raw
        foreach ($entry in $infraForbidden.GetEnumerator()) {
            if ($content -match $entry.Value) { Add-Failure 'infra' "$($entry.Key) in $inf" }
        }
    }

    # 6. Postman collection coverage.
    $requiredRoutes = @('GET /health', 'GET /todos', 'GET /todos/{id}', 'POST /todos', 'PUT /todos/{id}', 'DELETE /todos/{id}')
    $collections = $textFiles | Where-Object { $_ -match '^postman/.+\.json$' }
    if (-not $collections) {
        Add-Warning 'postman' 'No Postman collection under postman/ yet.'
    }
    foreach ($col in $collections) {
        try {
            $json = Get-Content -LiteralPath $col -Raw | ConvertFrom-Json -Depth 64
        }
        catch {
            Add-Failure 'postman' "$col is not valid JSON: $($_.Exception.Message)"
            continue
        }
        if (-not $json.info -or -not $json.item) { continue }

        $found = [System.Collections.Generic.HashSet[string]]::new()
        $stack = [System.Collections.Generic.Stack[object]]::new()
        foreach ($i in $json.item) { $stack.Push($i) }
        while ($stack.Count -gt 0) {
            $node = $stack.Pop()
            if ($node.item) { foreach ($i in $node.item) { $stack.Push($i) } }
            if (-not $node.request) { continue }
            $url = $node.request.url
            $path = if ($url -is [string]) { $url } elseif ($url.path) { '/' + ($url.path -join '/') } else { $url.raw }
            $path = $path -replace '^\{\{[^}]+\}\}', '' -replace '\?.*$', '' -replace '^https?://[^/]+', ''
            $segments = $path.Trim('/') -split '/' | ForEach-Object { if ($_ -match '^(\{\{.+\}\}|:.+)$') { '{id}' } else { $_ } }
            [void] $found.Add(('{0} /{1}' -f $node.request.method.ToUpperInvariant(), ($segments -join '/')))
        }
        foreach ($route in $requiredRoutes) {
            if (-not $found.Contains($route)) { Add-Failure 'postman' "$col has no request for $route" }
        }
    }

    Write-Host "Scanned $($textFiles.Count) file(s)."
    foreach ($w in $script:warnings) { Write-Host "[WARN] $w" -ForegroundColor Yellow }
    if ($script:failures.Count -gt 0) {
        foreach ($f in $script:failures) { Write-Host "[FAIL] $f" -ForegroundColor Red }
        Write-Host "Repository safety check failed with $($script:failures.Count) issue(s)." -ForegroundColor Red
        exit 1
    }
    Write-Host '[PASS] Repository safety checks passed.' -ForegroundColor Green
    exit 0
}
finally {
    Pop-Location
}
