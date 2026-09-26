import assert from 'node:assert/strict';
import { mkdtempSync, readFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import test from 'node:test';
import { verifyApplicationModuleEndpoint } from './support/application-module-http.mjs';

for (const scenario of [
  { name: 'missing route', status: 404, body: 'not found' },
  { name: 'incorrect marker', status: 200, body: 'framework host active' },
]) {
  test(`application module HTTP acceptance rejects ${scenario.name}`, async () => {
    const root = mkdtempSync(join(tmpdir(), 'fullnet-module-http-'));
    try {
      await assert.rejects(() => verifyApplicationModuleEndpoint('http://example.test', {
        logPath: join(root, 'response.json'),
        request: async () => new Response(scenario.body, { status: scenario.status }),
      }));
      const evidence = JSON.parse(readFileSync(join(root, 'response.json'), 'utf8'));
      assert.equal(evidence.status, scenario.status);
      assert.equal(evidence.body, scenario.body);
    } finally {
      rmSync(root, { recursive: true, force: true });
    }
  });
}

test('application module HTTP acceptance requests the mapped route and verifies its exact marker', async () => {
  const root = mkdtempSync(join(tmpdir(), 'fullnet-module-http-'));
  let observed;
  try {
    await verifyApplicationModuleEndpoint('http://example.test', {
      logPath: join(root, 'response.json'),
      request: async (url, options) => {
        observed = { url, options };
        return new Response('application-module-active', { status: 200 });
      },
    });
    assert.equal(observed.url, 'http://example.test/api/v1/application-composition-probe');
    assert.ok(observed.options.signal instanceof AbortSignal);
    assert.equal(observed.options.redirect, 'error');
    assert.equal(JSON.parse(readFileSync(join(root, 'response.json'), 'utf8')).body, 'application-module-active');
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test('application module HTTP acceptance preserves request failure evidence', async () => {
  const root = mkdtempSync(join(tmpdir(), 'fullnet-module-http-'));
  const failure = new Error('request aborted');
  try {
    await assert.rejects(() => verifyApplicationModuleEndpoint('http://example.test', {
      logPath: join(root, 'response.json'),
      request: async () => { throw failure; },
    }), (error) => error === failure);
    assert.equal(JSON.parse(readFileSync(join(root, 'response.json'), 'utf8')).error, 'request aborted');
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});
