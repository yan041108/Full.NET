#Requires -Version 7.0

param(
    [Parameter(Mandatory)] [string] $Context,
    [Parameter(Mandatory)] [string] $Namespace,
    [Parameter(Mandatory)] [ValidateSet('SqlServer', 'MySql')] [string] $Provider,
    [Parameter(Mandatory)] [string] $Server,
    [Parameter(Mandatory)] [string] $Database,
    [string] $MySqlLoginPath,
    [string] $MySqlCaFile,
    [ValidateRange(1, 65535)] [int] $MySqlPort = 3306
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
. (Join-Path $PSScriptRoot 'Read-ReportingCutoverQuery.ps1')

function Read-ClusterList {
    param([string] $Resource, [string] $Selector)

    $arguments = @('--context', $Context, '--namespace', $Namespace, '--request-timeout=15s', 'get', $Resource, '-o', 'json')
    if ($Selector) { $arguments += @('-l', $Selector) }
    $result = & kubectl @arguments 2>$null
    if ($LASTEXITCODE -ne 0) { throw "Reporting cutover cannot read Kubernetes $Resource." }
    $list = ($result -join "`n") | ConvertFrom-Json -AsHashtable
    if ($null -eq $list -or $list.items -isnot [array]) { throw "Invalid Kubernetes $Resource list." }
    return $list.items
}

function Assert-OldHostsStopped {
    $autoscalers = @(Read-ClusterList -Resource hpa)
    foreach ($role in 'api', 'worker') {
        $selector = "app.kubernetes.io/instance=fullnet-$role"
        $deployments = @(Read-ClusterList -Resource deployments -Selector $selector)
        if ($deployments.Count -ne 1) { throw "Reporting cutover requires exactly one existing $role deployment." }
        $deployment = $deployments[0]
        if ($deployment.metadata.labels['app.kubernetes.io/component'] -cne $role `
            -or $deployment.spec.replicas -ne 0 `
            -or $null -eq $deployment.metadata.generation `
            -or $deployment.status.observedGeneration -ne $deployment.metadata.generation `
            -or ($deployment.status['replicas'] ?? 0) -ne 0) {
            throw "Reporting cutover requires the $role deployment to be stopped and reconciled."
        }
        if (@($autoscalers | Where-Object { $_.spec.scaleTargetRef.kind -eq 'Deployment' `
                        -and $_.spec.scaleTargetRef.name -eq $deployment.metadata.name }).Count -ne 0) {
            throw "Reporting cutover requires removing the autoscaler for the old $role deployment."
        }
        if (@(Read-ClusterList -Resource pods -Selector $selector).Count -ne 0) {
            throw "Reporting cutover requires all old $role pods to terminate."
        }
    }
}

Assert-OldHostsStopped
$queryFile = Join-Path $PSScriptRoot ($Provider.ToLowerInvariant() + '.sql')
$count = Read-ReportingCutoverQuery -Provider $Provider -Server $Server -Database $Database `
    -MySqlLoginPath $MySqlLoginPath -MySqlCaFile $MySqlCaFile -MySqlPort $MySqlPort -QueryFile $queryFile
if ($count -eq '-1') { throw 'Reporting cutover requires a writable primary database endpoint.' }
if ($count -cnotmatch '\A[0-9]+\z') { throw 'Invalid reporting cutover database result.' }
if ($count -cne '0') { throw "Reporting cutover blocked by $count unfinished or leased export tasks." }

# 查询之后再核对一次旧实例，拒绝查询期间已恢复的部署。
Assert-OldHostsStopped
Write-Host 'Reporting cutover preflight passed.'
