import assert from 'node:assert/strict';
import { cpSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import test from 'node:test';
import { spawnSync } from 'node:child_process';
import { verifyApplicationCrudClient } from './support/application-crud-client.mjs';

function fixture(action) {
  const appRoot = mkdtempSync(join(tmpdir(), 'fullnet-business-client-'));
  try {
    mkdirSync(join(appRoot, '.fullnet-tools/openapi'), { recursive: true });
    for (const name of ['generate-fullnet-client.mjs', 'validate-client-generation-readiness.mjs']) {
      cpSync(new URL('../../scripts/openapi/' + name, import.meta.url), join(appRoot, '.fullnet-tools/openapi', name));
    }
    mkdirSync(join(appRoot, 'contracts/openapi'), { recursive: true });
    const document = JSON.parse(readFileSync(new URL('../Full.NET.UnitTests/CodeGeneration/Fixtures/CatalogProduct/contracts/openapi/products.generated.openapi.json', import.meta.url), 'utf8'));
    // 黄金样例为旧禁用模式；独立应用明确采用 hard.delete，操作名和路由须与现代生成器一致。
    const deletion = document.paths['/api/v1/catalog/products/{productId}/disable'];
    deletion.post.operationId = 'catalogDeleteProduct';
    document.paths['/api/v1/catalog/products/{productId}/delete'] = deletion;
    delete document.paths['/api/v1/catalog/products/{productId}/disable'];
    writeFileSync(join(appRoot, 'contracts/openapi/products.generated.openapi.json'), JSON.stringify(document));
    cpSync(new URL('../../packages/client-contracts/src', import.meta.url), join(appRoot, 'packages/client-contracts/src'), { recursive: true });
    return action(appRoot);
  } finally { rmSync(appRoot, { recursive: true, force: true }); }
}

test('application business client generates and compiles all five operations without changing its inputs', () => fixture((appRoot) => {
  const result = verifyApplicationCrudClient(appRoot, { reportDirectory: join(appRoot, 'reports/client') });
  assert.deepEqual(result, { operations: 5, generatedFiles: 4, compiled: true, zeroDrift: true, inputsUnchanged: true });
  const report = JSON.parse(readFileSync(join(appRoot, 'reports/client/result.json'), 'utf8'));
  assert.deepEqual(report, result);
}));

test('application business client rejects a failed generator instead of reporting completion', () => fixture((appRoot) => {
  assert.throws(() => verifyApplicationCrudClient(appRoot, { reportDirectory: join(appRoot, 'reports/client'),
    run: () => ({ status: 1, stdout: '', stderr: 'generator failed' }) }), /generate failed/u);
}));

test('application business client refuses an occupied verification directory', () => fixture((appRoot) => {
  mkdirSync(join(appRoot, 'verification/ClientGeneration'), { recursive: true });
  const manual = join(appRoot, 'verification/ClientGeneration/manual.ts');
  writeFileSync(manual, 'human file');
  assert.throws(() => verifyApplicationCrudClient(appRoot, { reportDirectory: join(appRoot, 'reports/client') }), /unused application path/u);
  assert.equal(readFileSync(manual, 'utf8'), 'human file');
}));

for (const [stage, call] of [['compile', 2], ['check', 3]]) {
  test(`application business client rejects a failed ${stage}`, () => fixture((appRoot) => {
    let calls = 0;
    assert.throws(() => verifyApplicationCrudClient(appRoot, { reportDirectory: join(appRoot, 'reports/client'),
      run: (...args) => ++calls === call ? { status: 1, stdout: '', stderr: 'stage failed' } : spawnSync(...args) }),
    new RegExp(stage + ' failed', 'u'));
  }));
}

test('application business client detects a changed shared baseline', () => fixture((appRoot) => {
  let calls = 0;
  assert.throws(() => verifyApplicationCrudClient(appRoot, { reportDirectory: join(appRoot, 'reports/client'),
    run: (...args) => {
      const result = spawnSync(...args);
      if (++calls === 3) writeFileSync(join(appRoot, 'packages/client-contracts/src/generated/models.generated.ts'), '// changed');
      return result;
    } }), /changed input/u);
}));
