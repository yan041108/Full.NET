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

function Get-ControllerUid {
    param([System.Collections.IDictionary] $Resource, [string] $Kind)
    foreach ($owner in $Resource.metadata['ownerReferences']) {
        if ($owner['controller'] -eq $true -and $owner.kind -ceq $Kind) { return $owner.uid }
    }
}

function Test-ReplicaSetPod {
    param([System.Collections.IDictionary] $Pod, [array] $ReplicaSets)
    $owner = Get-ControllerUid -Resource $Pod -Kind ReplicaSet
    foreach ($replicaSet in $ReplicaSets) {
        # 脱离选择器的 Pod 会被控制器释放，生成时的名称前缀仍可用于阻断。
        if ($owner -ceq $replicaSet.metadata.uid `
            -or ($Pod.metadata['name'] -and $Pod.metadata.name.StartsWith("$($replicaSet.metadata.name)-", [StringComparison]::Ordinal))) {
            return $true
        }
    }
    return $false
}

function Assert-OldHostsStopped {
    $stoppedDeployments = @{}
    $autoscalers = @(Read-ClusterList -Resource hpa)
    $replicaSets = @(Read-ClusterList -Resource replicasets)
    $pods = @(Read-ClusterList -Resource pods)
    foreach ($role in 'api', 'worker') {
        $selector = "app.kubernetes.io/instance=fullnet-$role"
        $deployments = @(Read-ClusterList -Resource deployments -Selector $selector)
        if ($deployments.Count -ne 1) { throw "Reporting cutover requires exactly one existing $role deployment." }
        $deployment = $deployments[0]
        if ($deployment.metadata.labels['app.kubernetes.io/component'] -cne $role `
            -or -not $deployment.metadata['uid'] `
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
        # 标签可能在停稳前被改动，按控制器 UID 核对 Deployment → ReplicaSet → Pod 归属链。
        $instance = "fullnet-$role"
        $ownedReplicaSets = @($replicaSets | Where-Object {
            (Get-ControllerUid -Resource $_ -Kind Deployment) -ceq $deployment.metadata.uid `
                -or ($_.metadata['labels'] -and $_.metadata.labels['app.kubernetes.io/instance'] -ceq $instance)
        })
        if (@($pods | Where-Object {
            ($_.metadata['labels'] -and $_.metadata.labels['app.kubernetes.io/instance'] -ceq $instance) `
                -or (Test-ReplicaSetPod -Pod $_ -ReplicaSets $ownedReplicaSets)
        }).Count -ne 0) {
            throw "Reporting cutover requires all old $role pods to terminate."
        }
        if (@($ownedReplicaSets | Where-Object {
            $_.spec.replicas -ne 0 -or ($_['status'] -and ($_.status['replicas'] ?? 0) -ne 0)
        }).Count -ne 0) {
            throw "Reporting cutover requires all old $role replica sets to stop."
        }
        $stoppedDeployments[$role] = @{
            Uid = $deployment.metadata.uid
            Generation = $deployment.metadata.generation
        }
    }
    return $stoppedDeployments
}

$before = Assert-OldHostsStopped
$queryFile = Join-Path $PSScriptRoot ($Provider.ToLowerInvariant() + '.sql')
$count = Read-ReportingCutoverQuery -Provider $Provider -Server $Server -Database $Database `
    -MySqlLoginPath $MySqlLoginPath -MySqlCaFile $MySqlCaFile -MySqlPort $MySqlPort -QueryFile $queryFile
if ($count -eq '-1') { throw 'Reporting cutover requires a writable primary database endpoint.' }
if ($count -cnotmatch '\A[0-9]+\z') { throw 'Invalid reporting cutover database result.' }
if ($count -cne '0') { throw "Reporting cutover blocked by $count export resources with unfinished tasks, leases or unresolved files." }

# 查询后确认同一部署仍停稳，并拒绝两次检查间曾恢复又停下的旧部署。
$after = Assert-OldHostsStopped
foreach ($role in 'api', 'worker') {
    if ($before[$role].Uid -cne $after[$role].Uid -or $before[$role].Generation -ne $after[$role].Generation) {
        throw "Reporting cutover blocked because the $role deployment changed during the database check."
    }
}
Write-Host 'Reporting cutover preflight passed.'
