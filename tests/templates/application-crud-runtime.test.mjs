import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import test from 'node:test';
import { verifyApplicationCrudRuntime } from './support/application-crud-runtime.mjs';

const expected = { moduleRegistered: true, scopedServices: 2, protectedRoutes: 5, jsonRoundTrip: true };
const marker = 'FULLNET_APPLICATION_CRUD_RUNTIME ';
const source = 'src/Demo.Host.Api/Program.cs';
function workspace() {
  const root = mkdtempSync(join(tmpdir(), 'fullnet-crud-runtime-'));
  mkdirSync(join(root, 'src/Demo.Host.Api'), { recursive: true });
  writeFileSync(join(root, source), readFileSync(new URL('../../templates/fullnet-app/src/FullNetAppNameToken.Host.Api/Program.cs', import.meta.url)));
  writeFileSync(join(root, 'src/Demo.Host.Api/Demo.Host.Api.csproj'), '<Project />');
  mkdirSync(join(root, 'ui/admin/src/router'), { recursive: true });
  writeFileSync(join(root, 'ui/admin/src/router/index.ts'), 'human router');
  for (const path of ['fullnet-app.json', 'framework-manifest.json', 'appsettings.json']) writeFileSync(join(root, path), '{}');
  return root;
}

// 注入执行器只验证Node编排和失败门禁，不将模拟报告视为实际.NET运行证据。
function runner(root, failure, calls) {
  return (command, args, options) => {
    const stage = calls.length === 0 ? 'build' : 'run';
    calls.push({ stage, command, args, options });
    if (failure === stage) return { status: 1, stderr: 'failed', stdout: '' };
    if (failure === 'process-error') return { status: 0, error: new Error('process failed'), stdout: '' };
    if (failure === stage + '-mutates') writeFileSync(join(root, source), 'lost entry');
    let stdout = 'ordinary output\n' + marker + JSON.stringify(expected) + '\n';
    if (failure === 'missing-report') stdout = 'ordinary output\n';
    if (failure === 'duplicate-report') stdout += marker + JSON.stringify(expected) + '\n';
    if (failure === 'invalid-json') stdout = marker + '{bad}\n';
    if (failure === 'incomplete-report') stdout = marker + JSON.stringify({ ...expected, protectedRoutes: 0 }) + '\n';
    return { status: 0, stderr: '', stdout: stage === 'build' ? '' : stdout };
  };
}

test('application CRUD runtime builds and executes an isolated probe without editing its API', () => {
  const root = workspace();
  const calls = [];
  const original = readFileSync(join(root, source));
  try {
    assert.deepEqual(verifyApplicationCrudRuntime(root, { reportDirectory: join(root, 'evidence'), run: runner(root, null, calls) }), expected);
    assert.equal(calls.length, 2);
    assert.equal(calls[0].command, 'dotnet');
    assert.deepEqual(calls[0].args.slice(0, 4), ['build', join(root, 'verification/CrudRuntimeProbe/CrudRuntimeProbe.csproj'), '-c', 'Release']);
    assert.equal(calls[1].args[0], 'exec');
    assert.ok(calls[1].args.includes('Development'));
    assert.ok(calls.every(({ options }) => options.cwd === root && options.windowsHide === true));
    const program = readFileSync(join(root, 'verification/CrudRuntimeProbe/Program.cs'), 'utf8');
    assert.match(program, /ValidateOnBuild = true/);
    assert.match(program, /ValidateScopes = true/);
    assert.doesNotMatch(program, /app\.Run\(\);/);
    assert.match(program, /app\.MapFullNetModules\(\);/);
    assert.match(program, /CreateAsyncScope\(\)/);
    assert.match(program, /GetOrderedModules\(\)/);
    assert.match(program, /GetMetadata<.*IAuthorizeData/);
    assert.match(program, /JsonSerializer\.Deserialize<ProductResponse>/);
    assert.deepEqual(readFileSync(join(root, source)), original);
    assert.deepEqual(JSON.parse(readFileSync(join(root, 'evidence/result.json'), 'utf8')), expected);
  } finally { rmSync(root, { recursive: true, force: true }); }
});

for (const failure of ['build', 'run', 'process-error', 'build-mutates', 'run-mutates',
  'missing-report', 'duplicate-report', 'invalid-json', 'incomplete-report']) {
  test(`application CRUD runtime rejects ${failure}`, () => {
    const root = workspace();
    const calls = [];
    try {
      assert.throws(() => verifyApplicationCrudRuntime(root, { reportDirectory: join(root, 'evidence'), run: runner(root, failure, calls) }));
      assert.ok(calls.length > 0);
      if (failure === 'build' || failure === 'process-error' || failure === 'build-mutates') assert.equal(calls.length, 1);
    } finally { rmSync(root, { recursive: true, force: true }); }
  });
}

for (const failure of ['occupied', 'missing-builder', 'duplicate-run']) {
  test(`application CRUD runtime refuses ${failure} before execution`, () => {
    const root = workspace();
    const calls = [];
    try {
      if (failure === 'occupied') mkdirSync(join(root, 'verification/CrudRuntimeProbe'), { recursive: true });
      else {
        const program = readFileSync(join(root, source), 'utf8');
        writeFileSync(join(root, source), failure === 'missing-builder' ? program.replace('var builder = WebApplication.CreateBuilder(args);', '') : program + '\napp.Run();');
      }
      assert.throws(() => verifyApplicationCrudRuntime(root, { reportDirectory: join(root, 'evidence'), run: runner(root, null, calls) }));
      assert.equal(calls.length, 0);
    } finally { rmSync(root, { recursive: true, force: true }); }
  });
}
