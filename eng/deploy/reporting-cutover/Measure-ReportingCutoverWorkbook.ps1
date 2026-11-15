#Requires -Version 7.0

[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $EvidencePath,
    [Parameter(Mandatory)] [string] $WorkbookPath
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
    $hasSheetData = $false
    try {
        $reader = [Xml.XmlReader]::Create($stream, $settings)
        try {
            while ($reader.Read()) {
                if ($reader.NodeType -eq [Xml.XmlNodeType]::EndElement -and $reader.Depth -eq 2) {
                    if ($rows -eq 1) { $columns = $cells }
                    if ($cells -lt 1 -or $cells -ne $columns) { throw 'Unexpected reporting worksheet column count.' }
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
                        if ($reader.LocalName -cne 'row' -or $reader.IsEmptyElement -or $rows -gt 5001 -or
                            $reader.GetAttribute('r') -cne $rows.ToString([Globalization.CultureInfo]::InvariantCulture)) {
                            throw 'Unexpected reporting worksheet row sequence.'
                        }
                        $cells = 0
                    }
                    3 {
                        $cells++
                        if ($reader.LocalName -cne 'c' -or $reader.GetAttribute('t') -cne 'inlineStr' -or $cells -gt 256) {
                            throw 'Unexpected reporting worksheet cell.'
                        }
                    }
                    4 { if ($reader.LocalName -cne 'is') { throw 'Unexpected reporting worksheet cell.' } }
                    5 { if ($reader.LocalName -cne 't') { throw 'Unexpected reporting worksheet cell.' } }
                    default { throw 'Unexpected reporting worksheet structure.' }
                }
            }
        } finally { $reader.Dispose() }
    } finally { $stream.Dispose() }
    if ($rows -eq 0) { throw 'Reporting worksheet header is missing.' }
    return $rows - 1
}

if ((Get-Item -LiteralPath $EvidencePath).Length -gt 8KB) { throw 'Reporting cutover evidence exceeds the expected size.' }
$evidence = Get-Content -LiteralPath $EvidencePath -Raw | ConvertFrom-Json -AsHashtable -NoEnumerate
if ($evidence -isnot [Collections.IDictionary] -or -not $evidence.Contains('outputFileId') -or $evidence['statusKey'] -cne 'processing' -or
    $null -ne $evidence['outputFileId'] -or $evidence['hasRowIdentityPrefix'] -isnot [bool] -or $evidence['hasRowIdentityPrefix'] -or
    $evidence['fileCount'] -ne 1 -or $evidence['readyFileCount'] -ne 1 -or $evidence['pendingFileCount'] -ne 0 -or
    $evidence['latestReadySha256'] -notmatch '\A[0-9a-fA-F]{64}\z') {
    throw 'Evidence must identify a single unbound legacy reporting workbook.'
}
foreach ($key in 'tenantId', 'taskId', 'latestReadyFileId') {
    [guid] $id = [guid]::Empty
    if (-not [guid]::TryParseExact($evidence[$key], 'D', [ref] $id) -or $id -eq [guid]::Empty) {
        throw 'Evidence contains an invalid reporting identity.'
    }
}

# 在同一只读句柄上核对摘要和结构，避免从不同版本的本地文件取证。
$file = [IO.File]::Open((Resolve-Path -LiteralPath $WorkbookPath).ProviderPath, 'Open', 'Read', 'Read')
try {
    if ($file.Length -lt 1 -or $file.Length -gt 10MB -or $file.Length -ne $evidence['latestReadyBytes']) {
        throw 'Reporting workbook size does not match the evidence.'
    }
    $digest = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($file))
    if ($digest -ne $evidence['latestReadySha256']) { throw 'Reporting workbook digest does not match the evidence.' }
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
    fileId = $evidence['latestReadyFileId']
    sizeBytes = $evidence['latestReadyBytes']
    sha256 = $digest
    dataRowCount = $rows
} | ConvertTo-Json
