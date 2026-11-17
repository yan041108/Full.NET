#Requires -Version 7.0

[CmdletBinding()]
param(
    [Parameter(Mandatory)] [ValidateSet('SqlServer', 'MySql')] [string] $Provider,
    [Parameter(Mandatory)] [string] $Server,
    [Parameter(Mandatory)] [string] $Database,
    [Parameter(Mandatory)] [guid] $TenantId,
    [Parameter(Mandatory)] [guid] $TaskId,
    [ValidateRange(1, 100)] [int] $Limit = 25,
    [guid] $AfterFileId,
    [string] $MySqlLoginPath,
    [string] $MySqlCaFile,
    [ValidateRange(1, 65535)] [int] $MySqlPort = 3306
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Read-ReportingCutoverQuery.ps1')

if ($TenantId -eq [guid]::Empty -or $TaskId -eq [guid]::Empty) { throw 'TenantId and TaskId must be non-empty UUIDs.' }
$cursor = ''
if ($PSBoundParameters.ContainsKey('AfterFileId')) {
    if ($AfterFileId -eq [guid]::Empty) { throw 'AfterFileId must be a non-empty UUID.' }
    $cursor = $AfterFileId.ToString('D')
}

# 只替换绑定后的 UUID 及固定上限。按提供程序原生 UUID 顺序继续读取。
$take = $Limit + 1
$queryFile = Join-Path $PSScriptRoot ($Provider.ToLowerInvariant() + '-files.sql')
$query = (Get-Content -LiteralPath $queryFile -Raw).Replace('{{TENANT_ID}}', $TenantId.ToString('D')).
    Replace('{{TASK_ID}}', $TaskId.ToString('D')).Replace('{{AFTER_FILE_ID}}', $cursor).
    Replace('{{TAKE}}', $take.ToString([Globalization.CultureInfo]::InvariantCulture))
$result = Read-ReportingCutoverQuery -Provider $Provider -Server $Server -Database $Database `
    -MySqlLoginPath $MySqlLoginPath -MySqlCaFile $MySqlCaFile -MySqlPort $MySqlPort -EvidenceQuery $query
if ($result -ceq '-1') { throw 'Reporting cutover requires a writable primary database endpoint.' }

$rows = @()
if ($result) {
    $lines = $result -split '\r?\n'
    if ($lines.Count -gt $take) { throw 'Invalid reporting cutover file result.' }
    $seen = [Collections.Generic.HashSet[guid]]::new()
    foreach ($line in $lines) {
        $fields = $line.Split([char]9)
        $fileId = [guid]::Empty
        $size = 0L
        if ($fields.Count -ne 6 -or -not [guid]::TryParseExact($fields[0], 'D', [ref] $fileId) `
            -or $fileId -eq [guid]::Empty -or $fileId -eq $AfterFileId -or -not $seen.Add($fileId) `
            -or $fields[1] -cnotin 'pending', 'ready' `
            -or -not [long]::TryParse($fields[2], [Globalization.NumberStyles]::None, [Globalization.CultureInfo]::InvariantCulture, [ref] $size) `
            -or $size -lt 1 -or $fields[3] -cnotmatch '\A[0-9a-fA-F]{64}\z' `
            -or $fields[4] -cnotmatch '\A[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(?:\.[0-9]{1,6})?Z\z' `
            -or $fields[5] -cnotin '0', '1') {
            throw 'Invalid reporting cutover file result.'
        }
        $rows += [ordered] @{
            fileId = $fileId.ToString('D')
            statusKey = $fields[1]
            sizeBytes = $size
            sha256 = $fields[3].ToLowerInvariant()
            createdAtUtc = $fields[4]
            isBound = $fields[5] -ceq '1'
        }
    }
}

[ordered] @{
    tenantId = $TenantId.ToString('D')
    taskId = $TaskId.ToString('D')
    items = @($rows | Select-Object -First $Limit)
    hasMore = $rows.Count -gt $Limit
} | ConvertTo-Json -Depth 3
