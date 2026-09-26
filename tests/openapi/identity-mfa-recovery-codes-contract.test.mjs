import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const contractPath = path.join(
  repositoryRoot,
  'contracts/openapi/identity-mfa-recovery-codes-v1.json'
);
const endpointSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Identity/Features/ManageMfaRecoveryCodes/Endpoint.cs'
);
const contractsSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Identity.Contracts/MfaRecoveryCodeContracts.cs'
);

test('MFA 恢复码 OpenAPI 夹具与端点一致', async () => {
  const contract = JSON.parse(await readFile(contractPath, 'utf8'));
  const endpointSource = await readFile(endpointSourcePath, 'utf8');
  const contractsSource = await readFile(contractsSourcePath, 'utf8');
  assert.ok(contract.paths['/api/v1/identity/me/mfa/recovery-codes/regenerate']);
  assert.ok(contract.paths['/api/v1/identity/me/mfa/recovery-codes/consume']);
  assert.match(
    endpointSource,
    /MapGroup\("\/api\/v1\/identity\/me\/mfa\/recovery-codes"\)/u);
  assert.match(endpointSource, /WithName\("identityRegenerateMfaRecoveryCodes"\)/u);
  assert.match(endpointSource, /WithName\("identityConsumeMfaRecoveryCode"\)/u);
  assert.match(contractsSource, /record RegenerateMfaRecoveryCodesResponse/u);
  assert.match(contractsSource, /record ConsumeMfaRecoveryCodeRequest/u);
});
