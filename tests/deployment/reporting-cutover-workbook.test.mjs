import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import test from 'node:test';

const root = fileURLToPath(new URL('../..', import.meta.url));

function verify(scenario) {
  const script = String.raw`
$ErrorActionPreference = 'Stop'
$fixture = [IO.Directory]::CreateTempSubdirectory('fullnet-reporting-workbook-')
try {
    $workbookPath = Join-Path $fixture.FullName 'original.xlsx'
    $evidencePath = Join-Path $fixture.FullName 'evidence.json'
    $scenario = $env:FULLNET_REPORTING_WORKBOOK_CASE
    $ns = 'http://schemas.openxmlformats.org/spreadsheetml/2006/main'
    $office = 'http://schemas.openxmlformats.org/officeDocument/2006/relationships'
    $package = 'http://schemas.openxmlformats.org/package/2006/relationships'
    $second = '<row r="2"><c r="A2" t="inlineStr"><is><t>private fixture</t></is></c></row>'
    if ($scenario -eq 'skipped-row') { $second = $second.Replace('r="2"', 'r="3"') }
    if ($scenario -eq 'formula') { $second = $second.Replace('<is><t>private fixture</t></is>', '<f>1+1</f>') }
    if ($scenario -eq 'wrong-cell') { $second = $second.Replace('r="A2"', 'r="B2"') }
    if ($scenario -eq 'missing-inline') { $second = '<row r="2"><c r="A2" t="inlineStr"/></row>' }
    if ($scenario -eq 'missing-text') { $second = $second.Replace('<is><t>private fixture</t></is>', '<is/>') }
    if ($scenario -eq 'duplicate-inline') { $second = $second.Replace('</c>', '<is><t>duplicate</t></is></c>') }
    if ($scenario -eq 'stray-text') { $second = $second.Replace('<is>', '<is>outside t') }
    if ($scenario -eq 'empty-text') { $second = $second.Replace('<t>private fixture</t>', '<t xml:space="preserve"/>') }
    if ($scenario -eq 'header-only') { $second = '' }
    $header = "<row r='1'><c r='A1' t='inlineStr'><is><t>Header</t></is></c></row>"
    if ($scenario -eq 'wide-row') {
        $columnNames = ('A'..'Z') + 'AA'
        $headerCells = ($columnNames | ForEach-Object { "<c r='$($_)1' t='inlineStr'><is><t>Header</t></is></c>" }) -join ''
        $dataCells = ($columnNames | ForEach-Object { "<c r='$($_)2' t='inlineStr'><is><t/></is></c>" }) -join ''
        $header = "<row r='1'>$headerCells</row>"
        $second = "<row r='2'>$dataCells</row>"
    }
    $target = if ($scenario -eq 'redirect') { 'worksheets/another.xml' } else { 'worksheets/sheet1.xml' }
    $parts = @{
        '[Content_Types].xml' = '<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"/>'
        '_rels/.rels' = "<Relationships xmlns='$package'><Relationship Id='rId1' Type='$office/officeDocument' Target='xl/workbook.xml'/></Relationships>"
        'xl/workbook.xml' = "<workbook xmlns='$ns' xmlns:r='$office'><sheets><sheet name='Export' sheetId='1' r:id='rId1'/></sheets></workbook>"
        'xl/_rels/workbook.xml.rels' = "<Relationships xmlns='$package'><Relationship Id='rId1' Type='$office/worksheet' Target='$target'/></Relationships>"
        'xl/worksheets/sheet1.xml' = "<worksheet xmlns='$ns'><sheetData>$header$second</sheetData></worksheet>"
    }
    if ($scenario -eq 'doctype') { $parts['xl/worksheets/sheet1.xml'] = '<!DOCTYPE worksheet []>' + $parts['xl/worksheets/sheet1.xml'] }
    if ($scenario -eq 'extra-part') { $parts['xl/worksheets/sheet2.xml'] = '<worksheet/>' }
    if ($scenario -eq 'document-redirect') { $parts['_rels/.rels'] = $parts['_rels/.rels'].Replace('xl/workbook.xml', 'xl/other.xml') }
    $file = [IO.File]::Create($workbookPath)
    try {
        $archive = [IO.Compression.ZipArchive]::new($file, 'Create', $true)
        try {
            foreach ($entry in $parts.GetEnumerator()) {
                $writer = [IO.StreamWriter]::new($archive.CreateEntry($entry.Key).Open())
                try { $writer.Write($entry.Value) } finally { $writer.Dispose() }
            }
        } finally { $archive.Dispose() }
    } finally { $file.Dispose() }
    $evidence = @{
        tenantId = '00000000-0000-7000-8000-000000000001'
        taskId = '00000000-0000-7000-8000-000000000002'
        latestReadyFileId = '00000000-0000-7000-8000-000000000003'
        statusKey = 'processing'
        outputFileId = $null
        hasRowIdentityPrefix = $false
        fileCount = 1
        readyFileCount = 1
        pendingFileCount = 0
        latestReadySha256 = (Get-FileHash -LiteralPath $workbookPath).Hash
        latestReadyBytes = (Get-Item -LiteralPath $workbookPath).Length
        recordedRowCount = 99
    }
    if ($scenario -eq 'different-digest') { $evidence.latestReadySha256 = 'A' * 64 }
    if ($scenario -eq 'size-mismatch') { $evidence.latestReadyBytes++ }
    if ($scenario -eq 'multiple-ready') { $evidence.fileCount = 2; $evidence.readyFileCount = 2 }
    $measure = @{ EvidencePath = $evidencePath; WorkbookPath = $workbookPath }
    if ($scenario -like 'candidate-*') {
        $original = @{ fileId=$evidence.latestReadyFileId; statusKey='ready'; sizeBytes=$evidence.latestReadyBytes; sha256=$evidence.latestReadySha256 }
        $other = @{ fileId='00000000-0000-7000-8000-000000000004'; statusKey='ready'; sizeBytes=$original.sizeBytes; sha256=('a' * 64) }
        $evidence = @{ tenantId=$evidence.tenantId; taskId=$evidence.taskId; items=@($original, $other); hasMore=$true }
        $measure.FileId = $original.fileId
        switch ($scenario) {
            'candidate-other' { $measure.FileId = $other.fileId }
            'candidate-missing' { $measure.FileId = '00000000-0000-7000-8000-000000000005' }
            'candidate-empty-id' { $measure.FileId = [guid]::Empty }
            'candidate-implicit' { $measure.Remove('FileId') }
            'candidate-pending' { $original.statusKey = 'pending' }
            'candidate-duplicate' { $evidence.items += $original }
            'candidate-invalid-size' { $original.sizeBytes = [string] $original.sizeBytes }
        }
    }
    $evidence | ConvertTo-Json | Set-Content -LiteralPath $evidencePath
    & ./eng/deploy/reporting-cutover/Measure-ReportingCutoverWorkbook.ps1 @measure
} finally {
    foreach ($name in 'original.xlsx', 'evidence.json') {
        $path = Join-Path $fixture.FullName $name
        if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path }
    }
    $fixture.Delete()
}
`;
  const result = spawnSync('pwsh', ['-NoProfile', '-NonInteractive', '-Command', script], {
    cwd: root,
    encoding: 'utf8',
    timeout: 20_000,
    windowsHide: true,
    env: { ...process.env, FULLNET_REPORTING_WORKBOOK_CASE: scenario },
  });
  assert.ifError(result.error);
  return result;
}

for (const [scenario, rowCount] of [['one-row', 1], ['header-only', 0], ['empty-text', 1], ['wide-row', 1]]) {
  test(`legacy workbook ${scenario} counts only fully matched data rows`, () => {
    const result = verify(scenario);
    assert.equal(result.status, 0, result.stderr);
    const evidence = JSON.parse(result.stdout);
    assert.equal(evidence.dataRowCount, rowCount);
    assert.equal(evidence.fileId, '00000000-0000-7000-8000-000000000003');
    assert.doesNotMatch(result.stdout, /private fixture|original.xlsx|recordedRowCount/);
  });
}

test('an explicit ready candidate can be measured even when a later candidate exists', () => {
  const { status, stdout, stderr } = verify('candidate-selected');
  assert.equal(status, 0, stderr);
  const evidence = JSON.parse(stdout);
  assert.equal(evidence.dataRowCount, 1);
  assert.equal(evidence.fileId, '00000000-0000-7000-8000-000000000003');
  assert.doesNotMatch(stdout, /private fixture|original.xlsx|000000000004/);
});

for (const [scenario, failure] of [
  ['different-digest', /digest does not match/],
  ['size-mismatch', /size does not match/],
  ['multiple-ready', /single unbound legacy/],
  ['skipped-row', /worksheet row sequence/],
  ['formula', /worksheet cell/],
  ['wrong-cell', /worksheet cell/],
  ['missing-inline', /worksheet cell/],
  ['missing-text', /worksheet cell/],
  ['duplicate-inline', /worksheet cell/],
  ['stray-text', /worksheet cell content/],
  ['doctype', /DTD/],
  ['redirect', /worksheet mapping/],
  ['document-redirect', /document mapping/],
  ['extra-part', /package layout/],
  ['candidate-other', /digest does not match/],
  ['candidate-missing', /exactly one ready workbook/],
  ['candidate-empty-id', /FileId must be a non-empty UUID/],
  ['candidate-implicit', /single unbound legacy/],
  ['candidate-pending', /exactly one ready workbook/],
  ['candidate-duplicate', /exactly one ready workbook/],
  ['candidate-invalid-size', /Invalid reporting workbook size or digest/],
]) {
  test(`workbook evidence ${scenario} fails without emitting a result`, () => {
    const result = verify(scenario);
    assert.notEqual(result.status, 0);
    assert.equal(result.stdout.trim(), '');
    assert.match(result.stderr, failure);
  });
}
