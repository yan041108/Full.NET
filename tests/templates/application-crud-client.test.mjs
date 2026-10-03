import assert from 'node:assert/strict';
import { cpSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import test from 'node:test';
import { spawnSync } from 'node:child_process';
import { createServer } from 'node:http';
import { verifyApplicationCrudClient, verifyApplicationCrudClientRuntime, verifyApplicationCrudClientTenantRead, verifyApplicationCrudClientProductRead } from './support/application-crud-client.mjs';

async function fixture(action) {
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
    return await action(appRoot);
  } finally { rmSync(appRoot, { recursive: true, force: true }); }
}

for (const hostAccessToken of ['', null, 17]) {
  test(`invalid Host runtime credential is rejected: ${JSON.stringify(hostAccessToken)}`, async () => {
    await assert.rejects(() => verifyApplicationCrudClientRuntime('unused', 'http://localhost',
      { logPath: 'unused', hostAccessToken }), /valid Host credential required/u);
  });
}

for (const [validShape, status] of [[true, 200], [false, 200], [true, 201]]) {
  test(`generated client tenant list decodes successful HTTP: ${validShape}/${status}`, () => fixture(async (appRoot) => {
    verifyApplicationCrudClient(appRoot, { reportDirectory: join(appRoot, 'reports/client') });
    const token = 'secret-tenant-read-fixture';
    const requests = [];
    const server = createServer((request, response) => {
      requests.push({ method: request.method, url: request.url, authorization: request.headers.authorization });
      response.writeHead(status, { 'content-type': 'application/json' });
      response.end(JSON.stringify(validShape ? { items: [], page: 1, pageSize: 5, total: 0 } : { items: 'invalid' }));
    });
    await new Promise((resolve) => server.listen(0, '127.0.0.1', resolve));
    const logPath = join(appRoot, 'reports/client/tenant-read.json');
    try {
      const run = () => verifyApplicationCrudClientTenantRead(appRoot, `http://127.0.0.1:${server.address().port}`,
        { tenantAccessToken: token, logPath });
      if (validShape && status === 200) {
        assert.deepEqual(await run(), { requests: 1, readSucceeded: 1 });
        assert.equal(requests.length, 1);
        assert.equal(requests[0].authorization, `Bearer ${token}`);
        assert.equal(requests[0].method, 'GET');
        assert.match(requests[0].url, /^\/api\/v1\/catalog\/products\?/u);
        assert.match(requests[0].url, /page=1/u);
        assert.match(requests[0].url, /pageSize=5/u);
      } else await assert.rejects(run);
      const text = readFileSync(logPath, 'utf8');
      assert.equal(text.includes(token), false);
      assert.equal(JSON.parse(text).completed, validShape && status === 200);
    } finally { await new Promise((resolve) => server.close(resolve)); }
  }));
}

for (const wrongTenant of [false, true]) {
  test(`generated client reads a nonempty product with tenant identity: ${wrongTenant}`, () => fixture(async (appRoot) => {
    verifyApplicationCrudClient(appRoot, { reportDirectory: join(appRoot, 'reports/client') });
    const tenantId = '01900000-0000-7000-8000-000000000010';
    const id = '01900000-0000-7000-8000-000000000011';
    const expectedProduct = { id, tenantId, name: 'Application tenant CRUD probe', version: '1' };
    const token = 'secret-product-read-fixture';
    const requests = [];
    const server = createServer((request, response) => {
      requests.push({ url: request.url, method: request.method, authorization: request.headers.authorization });
      response.writeHead(200, { 'content-type': 'application/json' });
      response.end(JSON.stringify({ ...expectedProduct, tenantId: wrongTenant ? id : tenantId,
        displayName: expectedProduct.name, description: null, isActive: true, createdAtUtc: '2026-10-03T00:00:00Z' }));
    });
    await new Promise((resolve) => server.listen(0, '127.0.0.1', resolve));
    const logPath = join(appRoot, 'reports/client/product-read.json');
    try {
      const run = () => verifyApplicationCrudClientProductRead(appRoot, `http://127.0.0.1:${server.address().port}`,
        { tenantAccessToken: token, expectedProduct, logPath });
      if (wrongTenant) await assert.rejects(run, /tenant mismatch/u);
      else {
        assert.deepEqual(await run(), { requests: 1, productRead: 1 });
        assert.deepEqual(requests, [{ url: `/api/v1/catalog/products/${id}`, method: 'GET', authorization: `Bearer ${token}` }]);
      }
      const reportText = readFileSync(logPath, 'utf8');
      assert.equal(reportText.includes(token), false);
      assert.equal(JSON.parse(reportText).completed, !wrongTenant);
    } finally { await new Promise((resolve) => server.close(resolve)); }
  }));
}

test('application business client generates and compiles all five operations without changing its inputs', () => fixture((appRoot) => {
  const result = verifyApplicationCrudClient(appRoot, { reportDirectory: join(appRoot, 'reports/client') });
  assert.deepEqual(result, { operations: 5, generatedFiles: 4, compiled: true, zeroDrift: true, inputsUnchanged: true });
  const report = JSON.parse(readFileSync(join(appRoot, 'reports/client/result.json'), 'utf8'));
  assert.deepEqual(report, result);
}));

for (const [validCode, status, bodyStatus = status, host = false] of [[true, 401], [false, 401], [true, 403], [true, 200], [true, 500, 401],
  [true, 403, 403, true], [true, 500, 403, true], [false, 403, 403, true]]) {
  test(`compiled business client validates HTTP rejection: ${validCode}/${status}/${bodyStatus}/${host}`, () => fixture(async (appRoot) => {
    verifyApplicationCrudClient(appRoot, { reportDirectory: join(appRoot, 'reports/client') });
    const received = [];
    const hostAccessToken = 'secret-host-runtime-fixture';
    const expectedStatus = host ? 403 : 401;
    const server = createServer((request, response) => {
      received.push({ method: request.method, url: request.url, authorization: request.headers.authorization });
      response.writeHead(status, { 'content-type': 'application/problem+json' });
      response.end(JSON.stringify({ status: bodyStatus,
        code: validCode ? (host ? 'authorization.permission_denied' : 'identity.session_not_active') : (host ? hostAccessToken : 'wrong.code') }));
    });
    await new Promise((resolve) => server.listen(0, '127.0.0.1', resolve));
    const logPath = join(appRoot, 'reports/client/runtime.json');
    try {
      const run = () => verifyApplicationCrudClientRuntime(appRoot, `http://127.0.0.1:${server.address().port}`,
        { logPath, ...(host ? { hostAccessToken } : {}) });
      if (validCode && status === expectedStatus) {
        assert.deepEqual(await run(), host ? { requests: 5, hostDenied: 5 } : { requests: 5, anonymousDenied: 5 });
        assert.equal(received.length, 5);
        assert.ok(received.every((request) => request.authorization === (host ? `Bearer ${hostAccessToken}` : undefined)));
        assert.deepEqual(received.map((request) => request.method), ['GET', 'POST', 'GET', 'PUT', 'POST']);
        assert.match(received[4].url, /\/delete$/u);
      } else await assert.rejects(run, (error) => {
        assert.match(error.message, status === expectedStatus ? /machine code/u : /status mismatch|unexpectedly allowed/u);
        assert.equal(error.message.includes(hostAccessToken), false, 'error leaked credential');
        return true;
      });
      const reportText = readFileSync(logPath, 'utf8');
      assert.equal(reportText.includes(hostAccessToken), false, 'report leaked credential');
      assert.equal(JSON.parse(reportText).completed, validCode && status === expectedStatus);
    } finally { await new Promise((resolve) => server.close(resolve)); }
  }));
}

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
