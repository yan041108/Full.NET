#Requires -Version 7.0

[CmdletBinding()]
param(
    [Parameter(Mandatory)] [ValidateSet('SqlServer', 'MySql')] [string] $Provider,
    [Parameter(Mandatory)] [string] $Server,
    [Parameter(Mandatory)] [string] $Database,
    [Parameter(Mandatory)] [guid] $TenantId,
    [Parameter(Mandatory)] [guid] $TaskId,
    [string] $MySqlLoginPath,
    [string] $MySqlCaFile,
    [ValidateRange(1, 65535)] [int] $MySqlPort = 3306
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Read-ReportingCutoverQuery.ps1')

if ($TenantId -eq [guid]::Empty -or $TaskId -eq [guid]::Empty) { throw 'TenantId and TaskId must be non-empty UUIDs.' }
$queryFile = Join-Path $PSScriptRoot ($Provider.ToLowerInvariant() + '-evidence.sql')
# 仅将经过类型绑定的标准 UUID 传给静态脚本的数据库变量，避免命令行传入任意 SQL 文本。
$query = (Get-Content -LiteralPath $queryFile -Raw).Replace('{{TENANT_ID}}', $TenantId.ToString('D')).Replace('{{TASK_ID}}', $TaskId.ToString('D'))
$result = Read-ReportingCutoverQuery -Provider $Provider -Server $Server -Database $Database `
    -MySqlLoginPath $MySqlLoginPath -MySqlCaFile $MySqlCaFile -MySqlPort $MySqlPort -EvidenceQuery $query
if ($result -ceq 'null') { throw 'No reporting task found for the specified tenant and task.' }
try { $evidence = $result | ConvertFrom-Json -AsHashtable -NoEnumerate }
catch { throw 'Invalid reporting cutover evidence result.' }
if ($evidence -isnot [System.Collections.IDictionary] -or $evidence['tenantId'] -ne $TenantId.ToString('D') `
    -or $evidence['taskId'] -ne $TaskId.ToString('D')) {
    throw 'Invalid reporting cutover evidence result.'
}
# MySQL 的条件结果为整数，SQL Server 的 bit 结果为布尔值。
$evidence['hasLease'] = [bool] $evidence['hasLease']
if ($null -ne $evidence['hasRowIdentityPrefix']) { $evidence['hasRowIdentityPrefix'] = [bool] $evidence['hasRowIdentityPrefix'] }
$evidence | ConvertTo-Json -Depth 3
