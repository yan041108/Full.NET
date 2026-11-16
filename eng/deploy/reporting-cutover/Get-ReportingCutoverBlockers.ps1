#Requires -Version 7.0

[CmdletBinding()]
param(
    [Parameter(Mandatory)] [ValidateSet('SqlServer', 'MySql')] [string] $Provider,
    [Parameter(Mandatory)] [string] $Server,
    [Parameter(Mandatory)] [string] $Database,
    [ValidateRange(1, 100)] [int] $Limit = 25,
    [guid] $AfterTenantId,
    [guid] $AfterTaskId,
    [string] $MySqlLoginPath,
    [string] $MySqlCaFile,
    [ValidateRange(1, 65535)] [int] $MySqlPort = 3306
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Read-ReportingCutoverQuery.ps1')

$filter = ''
if ($PSBoundParameters.ContainsKey('AfterTenantId') -or $PSBoundParameters.ContainsKey('AfterTaskId')) {
    if (-not $PSBoundParameters.ContainsKey('AfterTenantId') -or -not $PSBoundParameters.ContainsKey('AfterTaskId') `
        -or $AfterTenantId -eq [guid]::Empty -or $AfterTaskId -eq [guid]::Empty) {
        throw 'AfterTenantId and AfterTaskId must be supplied together as non-empty UUIDs.'
    }
    # 游标采用与 ORDER BY 相同的原生 UUID 比较，避免字符串排序与 SQL Server 排序不一致。
    $uuid = if ($Provider -eq 'SqlServer') { "CONVERT(uniqueidentifier, '{0}')" } else { "UUID_TO_BIN('{0}', 0)" }
    $tenant = $uuid -f $AfterTenantId.ToString('D')
    $task = $uuid -f $AfterTaskId.ToString('D')
    $filter = " AND (b.TenantId > $tenant OR (b.TenantId = $tenant AND b.ResourceId > $task))"
}

# 与切版计数共用阻断条件，列表投影按游标过滤并限制输出行数。
$queryFile = Join-Path $PSScriptRoot ($Provider.ToLowerInvariant() + '.sql')
$parts = (Get-Content -LiteralPath $queryFile -Raw) -split '(?m)^-- Result projection\r?\n', 2
if ($parts.Count -ne 2) { throw 'Reporting cutover query is missing its result projection marker.' }
$take = $Limit + 1
$projection = if ($Provider -eq 'SqlServer') {
    @"
, primary_state AS (SELECT CASE WHEN DATABASEPROPERTYEX(DB_NAME(), 'Updateability') = 'READ_WRITE' THEN 1 ELSE 0 END AS IsWritable)
SELECT TOP ($take) CASE WHEN p.IsWritable = 0 THEN '-1'
    ELSE CONCAT(CONVERT(char(36), b.TenantId), CHAR(9), CONVERT(char(36), b.ResourceId), CHAR(9),
        b.HasUnfinishedTask, b.HasTaskLease, b.HasPendingUpload, b.HasUnboundReadyFile) END
FROM primary_state AS p
LEFT JOIN blocking_resources AS b ON p.IsWritable = 1
WHERE p.IsWritable = 0 OR (b.ResourceId IS NOT NULL$filter)
ORDER BY b.TenantId, b.ResourceId;
"@
} else {
    @"
, primary_state AS (SELECT @@global.read_only = 0 AND @@global.super_read_only = 0 AS IsWritable)
SELECT /*+ MAX_EXECUTION_TIME(15000) */ CASE WHEN p.IsWritable = 0 THEN '-1'
    ELSE CONCAT(BIN_TO_UUID(b.TenantId, 0), CHAR(9), BIN_TO_UUID(b.ResourceId, 0), CHAR(9),
        b.HasUnfinishedTask, b.HasTaskLease, b.HasPendingUpload, b.HasUnboundReadyFile) END
FROM primary_state AS p
LEFT JOIN blocking_resources AS b ON p.IsWritable = 1
WHERE p.IsWritable = 0 OR (b.ResourceId IS NOT NULL$filter)
ORDER BY b.TenantId, b.ResourceId LIMIT $take;
"@
}
$query = $parts[0] + $projection
$result = Read-ReportingCutoverQuery -Provider $Provider -Server $Server -Database $Database `
    -MySqlLoginPath $MySqlLoginPath -MySqlCaFile $MySqlCaFile -MySqlPort $MySqlPort -EvidenceQuery $query
if ($result -ceq '-1') { throw 'Reporting cutover requires a writable primary database endpoint.' }

$rows = @()
if ($result) {
    # 只公开稳定原因，数据库响应缺少有效标志或身份重复时拒绝输出整页。
    $reasonKeys = @('task_unfinished', 'task_has_lease', 'pending_upload', 'ready_file_unbound')
    $lines = $result -split '\r?\n'
    if ($lines.Count -gt $take) { throw 'Invalid reporting cutover blocker result.' }
    $seen = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($line in $lines) {
        $fields = $line.Split([char]9)
        $tenantId = [guid]::Empty
        $taskId = [guid]::Empty
        if ($fields.Count -ne 3 -or -not [guid]::TryParseExact($fields[0], 'D', [ref]$tenantId) `
            -or -not [guid]::TryParseExact($fields[1], 'D', [ref]$taskId) `
            -or $tenantId -eq [guid]::Empty -or $taskId -eq [guid]::Empty `
            -or $fields[2] -cnotmatch '\A[01]{4}\z' -or $fields[2] -ceq '0000' -or -not $seen.Add("$tenantId/$taskId")) {
            throw 'Invalid reporting cutover blocker result.'
        }
        $reasons = for ($index = 0; $index -lt $reasonKeys.Count; $index++) {
            if ($fields[2][$index] -ceq '1') { $reasonKeys[$index] }
        }
        $rows += @{ tenantId = $tenantId.ToString('D'); taskId = $taskId.ToString('D'); reasons = @($reasons) }
    }
}
@{ items = @($rows | Select-Object -First $Limit); hasMore = $rows.Count -gt $Limit } | ConvertTo-Json -Depth 3
