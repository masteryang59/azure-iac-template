<#
.SYNOPSIS
  Runs the pipeline's Build-stage checks locally, no Azure login needed.

.DESCRIPTION
  1. bicep lint            (bicepconfig.json turns rule violations into errors)
  2. bicep build           refreshes infra/main.json (the ARM template / Deploy to Azure source)
  3. bicep build-params    both environments
  4. PSRule for Azure      expands the param files offline and checks ~400 Well-Architected rules
  5. dotnet test           Infra.Tests + App.Tests (SQL tests use LocalDB if present)

  Works in Windows PowerShell 5.1 and PowerShell 7.

.PARAMETER SkipPSRule
  Skip step 4 (needs: Install-Module PSRule.Rules.Azure -Scope CurrentUser).
#>
param([switch]$SkipPSRule)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$bicep = $env:BICEP_PATH
if (-not $bicep) {
    $default = Join-Path $HOME '.azure\bin\bicep.exe'
    $bicep = if (Test-Path $default) { $default } else { (Get-Command bicep -ErrorAction Stop).Source }
}

# Placeholder for the offline builds; any GUID works.
if (-not $env:SQL_ADMIN_OBJECT_ID) { $env:SQL_ADMIN_OBJECT_ID = '00000000-0000-0000-0000-000000000001' }

function Step($name, [scriptblock]$body) {
    Write-Host "`n== $name" -ForegroundColor Cyan
    & $body
    if ($LASTEXITCODE -and $LASTEXITCODE -ne 0) { throw "$name failed (exit $LASTEXITCODE)" }
}

Step 'bicep lint' { & $bicep lint infra/main.bicep }
Step 'bicep build -> infra/main.json' { & $bicep build infra/main.bicep --outfile infra/main.json }
foreach ($envName in 'dev', 'prod') {
    Step "bicep build-params $envName" { & $bicep build-params "infra/env/$envName.bicepparam" --outfile "$env:TEMP\$envName.parameters.json" }
}

if (-not $SkipPSRule) {
    Step 'PSRule for Azure' {
        $env:PSRULE_AZURE_BICEP_PATH = $bicep
        Import-Module PSRule.Rules.Azure
        Assert-PSRule -InputPath . -Option ps-rule.yaml -Format File -OutputFormat None -WarningAction SilentlyContinue
    }
}

Step 'dotnet test' {
    dotnet test --solution AzureIacTemplate.slnx
}

Write-Host "`nAll checks passed." -ForegroundColor Green
# audit_action
# audit_action
# audit_action