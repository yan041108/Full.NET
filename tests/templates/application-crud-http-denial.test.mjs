import assert from 'node:assert/strict';
import { test } from 'node:test';
import { mkdtempSync, readFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { verifyApplicationCrudHttpDenial } from './support/application-crud-http-denial.mjs';

const hostAccessToken = 'secret-host-access-token';
const id = '01900000-0000-7000-8000-000000000001';
const expected = [
  ['GET', '/?page=1&pageSize=5'], ['GET', `/${id}`], ['POST', '/'], ['PUT', `/${id}`], ['POST', `/${id}/delete`],
];
async function fixture(action) {
  const root = mkdtempSync(join(tmpdir(), 'fullnet-crud-http-denial-'));
  const logPath = join(root, 'result.json');
  try { await action(logPath); } finally { rmSync(root, { recursive: true, force: true }); }
}
function problem(status, code) {
  return new Response(JSON.stringify({ status, code, title: 'denied' }), { status, headers: { 'Content-Type': 'application/problem+json' } });
}
test('requests all five generated routes for anonymous and authenticated Host identities', async () => fixture(async (logPath) => {
  const calls = [];
  const result = await verifyApplicationCrudHttpDenial('http://example.test', { logPath, hostAccessToken, request: async (url, options) => {
    calls.push({ url, options });
    return options.headers.Authorization ? problem(403, 'authorization.permission_denied') : problem(401, 'identity.session_not_active');
  } });
  assert.deepEqual(result, { requests: 10, anonymousDenied: 5, hostDenied: 5 });
  assert.equal(calls.length, 10);
  for (let subject = 0; subject < 2; subject++) {
    for (let route = 0; route < 5; route++) {
      const { url, options } = calls[subject * 5 + route];
      assert.equal(url, 'http://example.test/api/v1/catalog/products' + expected[route][1]);
      assert.equal(options.method, expected[route][0]);
      assert.equal(options.headers.Authorization, subject ? `Bearer ${hostAccessToken}` : undefined);
      assert.equal(options.redirect, 'error');
      assert.ok(options.signal instanceof AbortSignal);
      assert.equal(options.headers.Origin, 'http://localhost');
      if (route >= 2) assert.ok(JSON.parse(options.body));
      else assert.equal(options.body, undefined);
    }
  }
  const evidence = readFileSync(logPath, 'utf8');
  assert.equal(JSON.parse(evidence).completed, true);
  assert.equal(JSON.parse(evidence).responses.length, 10);
  assert.equal(evidence.includes(hostAccessToken), false);
}));

for (const status of [200, 302, 404, 500]) {
  test(`rejects status ${status} and retains only executed request evidence`, async () => fixture(async (logPath) => {
    let calls = 0;
    await assert.rejects(() => verifyApplicationCrudHttpDenial('http://example.test', { logPath, hostAccessToken, request: async () => {
      calls++;
      return calls === 1 ? problem(401, 'identity.session_not_active') : new Response('unexpected', { status });
    } }));
    const result = JSON.parse(readFileSync(logPath, 'utf8'));
    assert.equal(calls, 2);
    assert.equal(result.completed, false);
    assert.equal(result.responses.length, 2);
    assert.equal(result.responses[1].status, status);
  }));
}
test('rejects incorrect authorization machine code', async () => fixture(async (logPath) => {
  await assert.rejects(() => verifyApplicationCrudHttpDenial('http://example.test', { logPath, hostAccessToken, request: async () => problem(401, 'unrelated.error') }));
  assert.equal(JSON.parse(readFileSync(logPath, 'utf8')).completed, false);
}));
test('Host identity authentication failure cannot count as permission denial', async () => fixture(async (logPath) => {
  let calls = 0;
  await assert.rejects(() => verifyApplicationCrudHttpDenial('http://example.test', { logPath, hostAccessToken, request: async () => {
    calls++;
    return problem(401, 'identity.session_not_active');
  } }));
  assert.equal(calls, 6);
  assert.equal(JSON.parse(readFileSync(logPath, 'utf8')).responses[5].subject, 'host-admin');
}));
test('a bare status without ProblemDetails is rejected', async () => fixture(async (logPath) => {
  await assert.rejects(() => verifyApplicationCrudHttpDenial('http://example.test', { logPath, hostAccessToken, request: async () => new Response('{}', { status: 401 }) }));
  assert.equal(JSON.parse(readFileSync(logPath, 'utf8')).completed, false);
}));
test('request exceptions retain partial evidence and redact the credential', async () => fixture(async (logPath) => {
  await assert.rejects(() => verifyApplicationCrudHttpDenial('http://example.test', { logPath, hostAccessToken, request: async () => { throw new Error(`redirect failure ${hostAccessToken}`); } }), (error) => !error.message.includes(hostAccessToken));
  const evidence = readFileSync(logPath, 'utf8');
  assert.equal(JSON.parse(evidence).completed, false);
  assert.equal(JSON.parse(evidence).responses.length, 1);
  assert.equal(evidence.includes(hostAccessToken), false);
}));
