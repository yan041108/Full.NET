import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const contractPath = path.join(repositoryRoot, 'contracts/openapi/settings-diagnostic-policy-v1.json');
const contractsSourcePath = path.join(repositoryRoot, 'src/Modules/Full.NET.Modules.Settings.Contracts/DiagnosticPolicyManagementContracts.cs');
const endpointSourcePath = path.join(repositoryRoot, 'src/Modules/Full.NET.Modules.Settings/Features/ManageDiagnosticPolicy/Endpoint.cs');

test('限时诊断策略 OpenAPI 夹具与 C# 契约和端点一致', async () => {
  const contract = JSON.parse(await readFile(contractPath, 'utf8'));
  const contractsSource = await readFile(contractsSourcePath, 'utf8');
  const endpointSource = await readFile(endpointSourcePath, 'utf8');
  assert.equal(contract.id, 'settings-diagnostic-policy-v1');
  assert.match(endpointSource, /MapGroup\("\/api\/v1\/settings\/diagnostic-policy"\)/u);
  assert.match(endpointSource, /WithTags\("SettingsHostDiagnosticPolicy"\)/u);
  assert.match(endpointSource, /WithName\("settingsGetHostDiagnosticPolicy"\)/u);
  assert.match(endpointSource, /WithName\("settingsUpdateHostDiagnosticPolicy"\)/u);
  assert.match(endpointSource, /WithName\("settingsRestoreHostDiagnosticPolicy"\)/u);
  assert.match(contractsSource, /record DiagnosticPolicyResponse/u);
  assert.ok(contract.paths.some((entry) => entry.path === '/api/v1/settings/diagnostic-policy/restore'));
});