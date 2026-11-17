#Requires -Version 7.0

[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $EvidencePath,
    [Parameter(Mandatory)] [string] $WorkbookPath,
    [guid] $FileId
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$spreadsheetNamespace = 'http://schemas.openxmlformats.org/spreadsheetml/2006/main'
$officeRelationships = 'http://schemas.openxmlformats.org/officeDocument/2006/relationships'
$packageRelationships = 'http://schemas.openxmlformats.org/package/2006/relationships'

function Read-Metadata {
    param([IO.Compression.ZipArchiveEntry] $Entry)
    $settings = [Xml.XmlReaderSettings]::new()
    $settings.MaxCharactersInDocument = 16KB
    $settings.DtdProcessing = [Xml.DtdProcessing]::Prohibit
    $settings.XmlResolver = $null
    $stream = $Entry.Open()
    try {
        $reader = [Xml.XmlReader]::Create($stream, $settings)
        try { return [Xml.Linq.XDocument]::Load($reader) }
        finally { $reader.Dispose() }
    } finally { $stream.Dispose() }
}

function Measure-WorksheetRows {
    param([IO.Compression.ZipArchiveEntry] $Entry)
    $settings = [Xml.XmlReaderSettings]::new()
    # 允许由 5000 行、256 列及 16 MiB 输入预算产生的 XML，限制异常膨胀。
    $settings.MaxCharactersInDocument = 160MB
    $settings.DtdProcessing = [Xml.DtdProcessing]::Prohibit
    $settings.XmlResolver = $null
    $stream = $Entry.Open()
    $rows = 0
    $cells = 0
    $columns = 0
    $rowNumber = ''
    $hasSheetData = $false
    $cellHasInline = $false
    $cellHasText = $false
    # 预计算渲染器支持的 Excel 列名，逐单元格核对坐标与表头布局。
    $columnNames = for ($column = 0; $column -lt 256; $column++) {
        $prefix = if ($column -ge 26) { [char] (64 + [int] [math]::Floor($column / 26)) } else { '' }
        "$prefix$([char] (65 + ($column % 26)))"
    }
    try {
        $reader = [Xml.XmlReader]::Create($stream, $settings)
        try {
            while ($reader.Read()) {
                if ($reader.NodeType -eq [Xml.XmlNodeType]::EndElement -and $reader.Depth -eq 2) {
                    if ($rows -eq 1) { $columns = $cells }
                    if ($cells -lt 1 -or $cells -ne $columns) { throw 'Unexpected reporting worksheet column count.' }
                }
                if ($reader.NodeType -eq [Xml.XmlNodeType]::EndElement -and $reader.Depth -eq 3 -and -not $cellHasText) {
                    throw 'Unexpected reporting worksheet cell.'
                }
                if ($reader.NodeType -in [Xml.XmlNodeType]::Text, [Xml.XmlNodeType]::CDATA -and $reader.Depth -ne 6) {
                    throw 'Unexpected reporting worksheet cell content.'
                }
                if ($reader.NodeType -ne [Xml.XmlNodeType]::Element) { continue }
                if ($reader.NamespaceURI -cne $spreadsheetNamespace) { throw 'Unexpected reporting worksheet namespace.' }
                switch ($reader.Depth) {
                    0 { if ($reader.LocalName -cne 'worksheet') { throw 'Unexpected worksheet root.' } }
                    1 {
                        if ($reader.LocalName -cne 'sheetData' -or $hasSheetData) { throw 'Unexpected reporting worksheet data.' }
                        $hasSheetData = $true
                    }
                    2 {
                        $rows++
                        $rowNumber = $rows.ToString([Globalization.CultureInfo]::InvariantCulture)
                        if ($reader.LocalName -cne 'row' -or $reader.IsEmptyElement -or $rows -gt 5001 -or
                            $reader.GetAttribute('r') -cne $rowNumber) {
                            throw 'Unexpected reporting worksheet row sequence.'
                        }
                        $cells = 0
                    }
                    3 {
                        if ($reader.LocalName -cne 'c' -or $reader.IsEmptyElement -or
                            $reader.GetAttribute('t') -cne 'inlineStr' -or $cells -ge $columnNames.Count -or
                            $reader.GetAttribute('r') -cne ($columnNames[$cells] + $rowNumber)) {
                            throw 'Unexpected reporting worksheet cell.'
                        }
                        $cells++
                        $cellHasInline = $false
                        $cellHasText = $false
                    }
                    4 {
                        if ($reader.LocalName -cne 'is' -or $reader.IsEmptyElement -or $cellHasInline) {
                            throw 'Unexpected reporting worksheet cell.'
                        }
                        $cellHasInline = $true
                    }
                    5 {
                        # 空文本仍有一个 t 元素。等到 c 结束时确认它已经出现。
                        if ($reader.LocalName -cne 't' -or $cellHasText) { throw 'Unexpected reporting worksheet cell.' }
                        $cellHasText = $true
                    }
                    default { throw 'Unexpected reporting worksheet structure.' }
                }
            }
        } finally { $reader.Dispose() }
    } finally { $stream.Dispose() }
    if ($rows -eq 0) { throw 'Reporting worksheet header is missing.' }
    return $rows - 1
}

$selectCandidate = $PSBoundParameters.ContainsKey('FileId')
if ($selectCandidate -and $FileId -eq [guid]::Empty) { throw 'FileId must be a non-empty UUID.' }
$evidenceLimit = if ($selectCandidate) { 64KB } else { 8KB }
if ((Get-Item -LiteralPath $EvidencePath).Length -gt $evidenceLimit) { throw 'Reporting cutover evidence exceeds the expected size.' }
$evidence = Get-Content -LiteralPath $EvidencePath -Raw | ConvertFrom-Json -AsHashtable -NoEnumerate
if ($evidence -isnot [Collections.IDictionary]) { throw 'Invalid reporting cutover evidence.' }

if ($selectCandidate) {
    if ($evidence['items'] -isnot [array] -or $evidence['items'].Length -gt 100) { throw 'Invalid reporting cutover file page.' }
    $selectedFileId = $FileId.ToString('D')
    # 多份就绪文件仅按显式文件 ID 取证。摘要与行数不能作为分配或绑定依据。
    $candidates = @($evidence['items'] | Where-Object {
        $_ -is [Collections.IDictionary] -and $_['fileId'] -is [string] -and $_['fileId'] -eq $selectedFileId
    })
    if ($candidates.Count -ne 1 -or $candidates[0]['statusKey'] -cne 'ready') {
        throw 'FileId must identify exactly one ready workbook in the evidence page.'
    }
    $expectedSize = $candidates[0]['sizeBytes']
    $expectedDigest = $candidates[0]['sha256']
} else {
    if (-not $evidence.Contains('outputFileId') -or $evidence['statusKey'] -cne 'processing' -or
        $null -ne $evidence['outputFileId'] -or $evidence['hasRowIdentityPrefix'] -isnot [bool] -or $evidence['hasRowIdentityPrefix'] -or
        $evidence['fileCount'] -ne 1 -or $evidence['readyFileCount'] -ne 1 -or $evidence['pendingFileCount'] -ne 0) {
        throw 'Evidence must identify a single unbound legacy reporting workbook.'
    }
    $selectedFileId = $evidence['latestReadyFileId']
    $expectedSize = $evidence['latestReadyBytes']
    $expectedDigest = $evidence['latestReadySha256']
}
if (($expectedSize -isnot [int] -and $expectedSize -isnot [long]) -or $expectedSize -lt 1 -or
    $expectedDigest -isnot [string] -or $expectedDigest -notmatch '\A[0-9a-fA-F]{64}\z') {
    throw 'Invalid reporting workbook size or digest in the evidence.'
}
foreach ($identity in $evidence['tenantId'], $evidence['taskId'], $selectedFileId) {
    [guid] $id = [guid]::Empty
    if (-not [guid]::TryParseExact($identity, 'D', [ref] $id) -or $id -eq [guid]::Empty) {
        throw 'Evidence contains an invalid reporting identity.'
    }
}

# 在同一只读句柄上核对摘要和结构，避免从不同版本的本地文件取证。
$file = [IO.File]::Open((Resolve-Path -LiteralPath $WorkbookPath).ProviderPath, 'Open', 'Read', 'Read')
try {
    if ($file.Length -lt 1 -or $file.Length -gt 10MB -or $file.Length -ne $expectedSize) {
        throw 'Reporting workbook size does not match the evidence.'
    }
    $digest = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($file))
    if ($digest -ne $expectedDigest) { throw 'Reporting workbook digest does not match the evidence.' }
    $file.Position = 0
    $archive = [IO.Compression.ZipArchive]::new($file, 'Read', $true)
    try {
        $expectedEntries = @('[Content_Types].xml', '_rels/.rels', 'xl/workbook.xml', 'xl/_rels/workbook.xml.rels', 'xl/worksheets/sheet1.xml')
        $names = @($archive.Entries | ForEach-Object FullName)
        if ($names.Count -ne $expectedEntries.Count -or (Compare-Object $expectedEntries $names -CaseSensitive)) {
            throw 'Unexpected reporting workbook package layout.'
        }
        $workbook = Read-Metadata -Entry $archive.GetEntry('xl/workbook.xml')
        $sheets = @($workbook.Descendants([Xml.Linq.XName]::Get('sheet', $spreadsheetNamespace)))
        $relationName = [Xml.Linq.XName]::Get('Relationship', $packageRelationships)
        $rootRelations = Read-Metadata -Entry $archive.GetEntry('_rels/.rels')
        $rootParts = @($rootRelations.Root.Elements($relationName))
        if ($rootParts.Count -ne 1 -or $rootParts[0].Attribute('Type').Value -cne "$officeRelationships/officeDocument" -or
            $rootParts[0].Attribute('Target').Value -cne 'xl/workbook.xml' -or $null -ne $rootParts[0].Attribute('TargetMode')) {
            throw 'Unexpected reporting workbook document mapping.'
        }
        $relations = Read-Metadata -Entry $archive.GetEntry('xl/_rels/workbook.xml.rels')
        $parts = @($relations.Root.Elements($relationName))
        if ($sheets.Count -ne 1 -or $parts.Count -ne 1 -or $sheets[0].Attribute('name').Value -cne 'Export' -or
            $sheets[0].Attribute([Xml.Linq.XName]::Get('id', $officeRelationships)).Value -cne 'rId1' -or
            $parts[0].Attribute('Id').Value -cne 'rId1' -or $parts[0].Attribute('Type').Value -cne "$officeRelationships/worksheet" -or
            $parts[0].Attribute('Target').Value -cne 'worksheets/sheet1.xml' -or $null -ne $parts[0].Attribute('TargetMode')) {
            throw 'Unexpected reporting workbook worksheet mapping.'
        }
        $rows = Measure-WorksheetRows -Entry $archive.GetEntry('xl/worksheets/sheet1.xml')
    } finally { $archive.Dispose() }
} finally { $file.Dispose() }

[ordered] @{
    tenantId = $evidence['tenantId']
    taskId = $evidence['taskId']
    fileId = $selectedFileId
    sizeBytes = $expectedSize
    sha256 = $digest
    dataRowCount = $rows
} | ConvertTo-Json
