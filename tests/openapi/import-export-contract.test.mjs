import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const contractPath = path.join(repositoryRoot, 'contracts/openapi/import-export-v1.json');
const contractsSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.ImportExport.Contracts/ImportExportContracts.cs'
);
const endpointSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.ImportExport/Features/ManageImportTasks/Endpoint.cs'
);

test('ImportExport 导入任务 OpenAPI 夹具与 C# 契约和端点一致', async () => {
  const contract = JSON.parse(await readFile(contractPath, 'utf8'));
  const contractsSource = await readFile(contractsSourcePath, 'utf8');
  const endpointSource = await readFile(endpointSourcePath, 'utf8');
  assert.equal(contract.id, 'import-export-v1');
  assert.match(endpointSource, /MapGroup\("\/api\/v1\/import-export\/tasks"\)/u);
  assert.match(endpointSource, /\.WithTags\("ImportExportTasks"\)/u);
  assert.match(endpointSource, /\.WithName\("importExportCreateImportTask"\)/u);
  assert.match(endpointSource, /\.WithName\("importExportListImportTasks"\)/u);
  assert.match(endpointSource, /\.WithName\("importExportGetImportTask"\)/u);
  assert.match(endpointSource, /\.WithName\("importExportExecuteImportTask"\)/u);
  assert.match(endpointSource, /\.WithName\("importExportResumeImportTask"\)/u);
  assert.match(endpointSource, /\.WithName\("importExportRetryImportTask"\)/u);
  assert.match(endpointSource, /\.WithName\("importExportDownloadImportTaskErrorReceipt"\)/u);
  assert.match(contractsSource, /record ImportExportTaskDetailResponse/u);
  assert.match(contractsSource, /import_export\.static_schemas\.read/u);
  assert.match(contractsSource, /import_export\.import_tasks\.read/u);
  assert.match(contractsSource, /import_export\.import_tasks\.create/u);
  assert.match(contractsSource, /import_export\.import_tasks\.execute/u);
});
