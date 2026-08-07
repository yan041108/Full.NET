import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const contractPath = path.join(repositoryRoot, 'contracts/openapi/identity-totp-enrollment-v1.json');
const contractsSourcePath = path.join(repositoryRoot, 'src/Modules/Full.NET.Modules.Identity.Contracts/TotpEnrollmentContracts.cs');
const endpointSourcePath = path.join(repositoryRoot, 'src/Modules/Full.NET.Modules.Identity/Features/ManageTotp/Endpoint.cs');

test('TOTP 登记 OpenAPI 夹具与 C# 契约和端点一致', async () => {
  const contract = JSON.parse(await readFile(contractPath, 'utf8'));
  const contractsSource = await readFile(contractsSourcePath, 'utf8');
  const endpointSource = await readFile(endpointSourcePath, 'utf8');
  assert.equal(contract.id, 'identity-totp-enrollment-v1');
  assert.match(endpointSource, /MapGroup\("\/api\/v1\/identity\/me\/mfa\/totp"\)/u);
  assert.match(contractsSource, /record TotpEnrollmentStatusResponse/u);
  assert.ok(contract.paths.some((entry) => entry.path.endsWith('/confirm')));
});