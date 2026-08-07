import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const contractPath = path.join(repositoryRoot, 'contracts/openapi/serial-numbers-rules-v1.json');
const contractsSourcePath = path.join(repositoryRoot, 'src/Modules/Full.NET.Modules.SerialNumbers/Contracts/SerialNumberContracts.cs');
const endpointSourcePath = path.join(repositoryRoot, 'src/Modules/Full.NET.Modules.SerialNumbers/Features/ManageHostSerialRules/Endpoint.cs');

test('流水号规则 OpenAPI 夹具与 C# 契约和端点一致', async () => {
  const contract = JSON.parse(await readFile(contractPath, 'utf8'));
  const contractsSource = await readFile(contractsSourcePath, 'utf8');
  const endpointSource = await readFile(endpointSourcePath, 'utf8');
  assert.equal(contract.id, 'serial-numbers-rules-v1');
  assert.match(endpointSource, /MapGroup\("\/api\/v1\/serial-numbers\/rules"\)/u);
  assert.match(contractsSource, /record SerialNumberRuleResponse/u);
  assert.ok(contract.paths.some((entry) => entry.path.endsWith('/preview')));
});