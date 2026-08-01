#Requires -Version 7.0
<#
.SYNOPSIS
  Full.NET 生产发布顺序：Migrator Job 完成 -> Worker 就绪 -> API 滚动就绪。
.DESCRIPTION
  使用同一 Chart 的三个独立 Release（单一角色）。任一阶段失败立即停止。
  数据库变更遵循 Expand/Contract；本脚本不自动回滚已完成的 Migrator。
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Namespace,

    [Parameter(Mandatory = $true)]
    [string] $ImageTag,

    [string] $ChartPath = (Join-Path $PSScriptRoot '..\..\deploy\helm\fullnet'),

    [string] $ValuesFile = (Join-Path $PSScriptRoot '..\..\deploy\helm\fullnet\values.yaml'),

    [string] $ProviderValuesFile = (Join-Path $PSScriptRoot '..\..\deploy\helm\fullnet\ci\values-provider-sqlserver.yaml'),

    [string] $EdgeAndDpValuesFile = (Join-Path $PSScriptRoot '..\..\deploy\helm\fullnet\ci\values-role-api.yaml'),

    [int] $TimeoutSeconds = 1800
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-Command {
    param([string] $Name)
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command '$Name' was not found on PATH."
    }
}

function Invoke-HelmUpgrade {
    param(
        [string] $ReleaseName,
        [string] $RoleValuesFile,
        [string[]] $ExtraSet = @()
    )

    $args = @(
        'upgrade', '--install', $ReleaseName, $ChartPath,
        '--namespace', $Namespace,
        '--create-namespace',
        '-f', $ValuesFile,
        '-f', $ProviderValuesFile,
        '-f', $EdgeAndDpValuesFile,
        '-f', $RoleValuesFile,
        '--set', "image.tag=$ImageTag",
        '--wait',
        '--timeout', "${TimeoutSeconds}s"
    ) + $ExtraSet

    Write-Host ">>> helm $($args -join ' ')"
    & helm @args
    if ($LASTEXITCODE -ne 0) {
        throw "Helm release '$ReleaseName' failed with exit code $LASTEXITCODE."
    }
}

Assert-Command helm
Assert-Command kubectl

$chart = Resolve-Path $ChartPath
$ValuesFile = (Resolve-Path $ValuesFile).Path
$ProviderValuesFile = (Resolve-Path $ProviderValuesFile).Path
$EdgeAndDpValuesFile = (Resolve-Path $EdgeAndDpValuesFile).Path
$ChartPath = $chart.Path

$roleRoot = Join-Path $ChartPath 'ci'
$migratorValues = Join-Path $roleRoot 'values-role-migrator.yaml'
$workerValues = Join-Path $roleRoot 'values-role-worker.yaml'
$apiValues = Join-Path $roleRoot 'values-role-api.yaml'

Write-Host 'Stage 1/3: Migrator Job (Expand/Contract gate)'
Invoke-HelmUpgrade -ReleaseName 'fullnet-migrator' -RoleValuesFile $migratorValues

Write-Host 'Stage 2/3: Worker consumers'
Invoke-HelmUpgrade -ReleaseName 'fullnet-worker' -RoleValuesFile $workerValues

Write-Host 'Stage 3/3: API rolling readiness'
Invoke-HelmUpgrade -ReleaseName 'fullnet-api' -RoleValuesFile $apiValues

Write-Host 'Full.NET release order completed successfully.'
Write-Host 'Capacity status remains Capacity-not-verified until dedicated hardware certification.'
