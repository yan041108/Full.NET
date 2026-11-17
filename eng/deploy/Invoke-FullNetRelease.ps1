#Requires -Version 7.0
<#
.SYNOPSIS
  Full.NET 生产发布顺序：Migrator Job 完成 -> Worker 就绪 -> API 滚动就绪。
.DESCRIPTION
  使用同一 Chart 的三个独立 Release（单一角色）。任一阶段失败立即停止。
  数据库变更遵循 Expand/Contract；本脚本不自动回滚已完成的 Migrator。
  报表输出身份切版使用 -ReportingOutputCutover，新 API 保持暂停接收导出，放行步骤见 docs/operations/reporting-output-cutover.md。
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

    [int] $TimeoutSeconds = 1800,

    [string] $KubeContext,

    [ValidatePattern('\A(?=.{1,253}\z)(?-i:[a-z0-9](?:[a-z0-9-]*[a-z0-9])?(?:\.[a-z0-9](?:[a-z0-9-]*[a-z0-9])?)*)\z')]
    [string] $ApiDatabaseSecretName,

    [ValidatePattern('\A(?=.{1,253}\z)(?-i:[a-z0-9](?:[a-z0-9-]*[a-z0-9])?(?:\.[a-z0-9](?:[a-z0-9-]*[a-z0-9])?)*)\z')]
    [string] $WorkerDatabaseSecretName,

    [ValidatePattern('\A(?=.{1,253}\z)(?-i:[a-z0-9](?:[a-z0-9-]*[a-z0-9])?(?:\.[a-z0-9](?:[a-z0-9-]*[a-z0-9])?)*)\z')]
    [string] $MigratorDatabaseSecretName,

    [switch] $ReportingOutputCutover,

    [ValidateSet('SqlServer', 'MySql')] [string] $ReportingDatabaseProvider,
    [string] $ReportingDatabaseServer,
    [string] $ReportingDatabaseName,
    [string] $ReportingMySqlLoginPath,
    [string] $ReportingMySqlCaFile,
    [ValidateRange(1, 65535)] [int] $ReportingMySqlPort = 3306
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-Command {
    param([string] $Name)
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command '$Name' was not found on PATH."
    }
}

function Get-HelmRoleValues {
    param(
        [string] $ReleaseName,
        [string] $RoleValuesFile,
        [string] $DatabaseSecretName
    )

    $args = @(
        '-f', $ValuesFile,
        '-f', $ProviderValuesFile,
        '-f', $EdgeAndDpValuesFile,
        '-f', $RoleValuesFile,
        '--set', "image.tag=$ImageTag"
    )
    # 角色的连接串与各自连接池预算成对配置，Helm 仅接收 Secret 名称。
    if ($DatabaseSecretName) { $args += @('--set-string', "database.connectionSecretName=$DatabaseSecretName") }
    # 新 API 在原结果复核期间维持暂停，运行就绪后由发布负责人单独恢复接收。
    if ($ReportingOutputCutover -and $ReleaseName -ceq 'fullnet-api') {
        $args += @('--set', 'api.reportingExportAdmissionEnabled=false')
    }
    return $args
}

function Assert-ReportingCutoverChart {
    param(
        [string] $ReleaseName,
        [string] $RoleValuesFile,
        [string] $DatabaseSecretName,
        [string] $WorkloadTemplate
    )

    # 与实际发布使用同一组角色参数。只在本地渲染，校验失败时隐藏清单和 Helm 原始诊断。
    $args = @('template', $ReleaseName, $ChartPath, '--namespace', $Namespace, '--is-upgrade',
        '--show-only', 'templates/configmap.yaml', '--show-only', "templates/$WorkloadTemplate")
    $args += @(Get-HelmRoleValues $ReleaseName $RoleValuesFile $DatabaseSecretName)
    $PSNativeCommandUseErrorActionPreference = $false
    $manifest = & helm @args 2>$null
    if ($LASTEXITCODE -ne 0) { throw "Reporting cutover could not render the chart for '$ReleaseName'." }

    $rendered = $manifest -join "`n"
    $providers = [regex]::Matches($rendered, '(?m)^  Database__Provider:[ \t]*(?<value>[^\r\n]*)\r?$')
    $expectedProvider = '"{0}"' -f $ReportingDatabaseProvider
    if ($providers.Count -ne 1 -or $providers[0].Groups['value'].Value.Trim() -ne $expectedProvider) {
        throw "Reporting cutover chart database provider does not match the probe for '$ReleaseName'."
    }
    if ($ReleaseName -ceq 'fullnet-api') {
        $admission = [regex]::Matches($rendered, '(?m)^(?<indent> +)- name: FullNet__Reporting__Export__AdmissionEnabled[ \t]*\r?$(?:\n\k<indent>  value:[ \t]*(?<value>[^\r\n]*))?')
        if ($admission.Count -ne 1 -or $admission[0].Groups['value'].Value.Trim() -cne '"false"') {
            throw 'Reporting cutover chart must render exactly one disabled API export admission setting.'
        }
    }
}

function Invoke-HelmUpgrade {
    param(
        [string] $ReleaseName,
        [string] $RoleValuesFile,
        [string] $DatabaseSecretName
    )

    $args = @('upgrade', '--install', $ReleaseName, $ChartPath, '--namespace', $Namespace, '--create-namespace',
        '--wait', '--wait-for-jobs', '--timeout', "${TimeoutSeconds}s")
    $args += @(Get-HelmRoleValues $ReleaseName $RoleValuesFile $DatabaseSecretName)
    if ($KubeContext) { $args += @('--kube-context', $KubeContext) }

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

if ($ReportingOutputCutover) {
    if (-not $KubeContext -or -not $ReportingDatabaseProvider -or -not $ReportingDatabaseServer -or -not $ReportingDatabaseName) {
        throw 'Reporting cutover requires an explicit Kubernetes context, database provider, server and database name.'
    }
    Assert-ReportingCutoverChart 'fullnet-migrator' $migratorValues $MigratorDatabaseSecretName 'migrator-job.yaml'
    Assert-ReportingCutoverChart 'fullnet-worker' $workerValues $WorkerDatabaseSecretName 'worker-deployment.yaml'
    Assert-ReportingCutoverChart 'fullnet-api' $apiValues $ApiDatabaseSecretName 'api-deployment.yaml'
    & (Join-Path $PSScriptRoot 'reporting-cutover/Assert-ReportingCutover.ps1') `
        -Context $KubeContext -Namespace $Namespace -Provider $ReportingDatabaseProvider `
        -Server $ReportingDatabaseServer -Database $ReportingDatabaseName `
        -MySqlLoginPath $ReportingMySqlLoginPath -MySqlCaFile $ReportingMySqlCaFile -MySqlPort $ReportingMySqlPort
}

Write-Host 'Stage 1/3: Migrator Job (Expand/Contract gate)'
Invoke-HelmUpgrade -ReleaseName 'fullnet-migrator' -RoleValuesFile $migratorValues -DatabaseSecretName $MigratorDatabaseSecretName

Write-Host 'Stage 2/3: Worker consumers'
Invoke-HelmUpgrade -ReleaseName 'fullnet-worker' -RoleValuesFile $workerValues -DatabaseSecretName $WorkerDatabaseSecretName

Write-Host 'Stage 3/3: API rolling readiness'
Invoke-HelmUpgrade -ReleaseName 'fullnet-api' -RoleValuesFile $apiValues -DatabaseSecretName $ApiDatabaseSecretName

Write-Host 'Full.NET release order completed successfully.'
Write-Host 'Capacity status remains Capacity-not-verified until dedicated hardware certification.'
