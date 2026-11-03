import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { mkdtempSync, mkdirSync, readFileSync, rmSync, writeFileSync, copyFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import test from 'node:test';
import { createHash } from 'node:crypto';
import { createApplicationPackage } from '../../scripts/templates/application-package.mjs';
import { createApp } from '../../scripts/templates/create-app.mjs';

const application = JSON.parse(readFileSync(new URL('../../templates/fullnet-app/package.json', import.meta.url)));
const framework = JSON.parse(readFileSync(new URL('../../package.json', import.meta.url)));

test('application package keeps the dependency lock inputs and drops unavailable repository commands', () => {
  const original = JSON.stringify(framework);
  const result = createApplicationPackage(framework, application);
  for (const key of ['dependencies', 'devDependencies', 'optionalDependencies', 'engines', 'packageManager', 'pnpm']) {
    assert.deepEqual(result[key], framework[key], key + ' changed');
  }
  assert.equal(result.scripts['test:integration:affected'], undefined);
  assert.equal(result.scripts['test:task:start'], undefined);
  assert.ok(result.scripts['dev:api'] && result.scripts['db:migrate'] && result.scripts['diagnose:development']);
  assert.equal(JSON.stringify(framework), original);
  result.scripts['dev:api'] = 'manual';
  assert.notEqual(application.scripts['dev:api'], 'manual');
});

test('invalid application commands cannot fall back to repository scripts', () => {
  for (const scripts of [undefined, [], {}, { dev: '' }, { dev: 42 }]) {
    assert.throws(() => createApplicationPackage(framework, { scripts }), /nonempty commands/u);
  }
});

test('package integrity validates projected commands and every shipped dependency support file', () => {
  const root = mkdtempSync(join(tmpdir(), 'fullnet-app-package-integrity-'));
  try {
    const bundle = join(root, 'framework/fullnet');
    const sources = {
      'package.json': JSON.stringify(framework),
      'templates/fullnet-app/package.json': JSON.stringify(application),
      'patches/dependency.patch': 'frozen patch bytes',
      'packages/braces/package.json': '{"name":"@fullnet/braces"}',
    };
    const managedFiles = {};
    for (const [file, content] of Object.entries(sources)) {
      mkdirSync(join(bundle, file, '..'), { recursive: true });
      writeFileSync(join(bundle, file), content);
      managedFiles[file] = createHash('sha256').update(content).digest('hex');
      if (!file.startsWith('templates/')) {
        mkdirSync(join(root, file, '..'), { recursive: true });
        writeFileSync(join(root, file), file === 'package.json'
          ? JSON.stringify(createApplicationPackage(framework, application), null, 2) + '\n' : content);
      }
    }
    const manifest = JSON.stringify({ managedFiles });
    writeFileSync(join(root, 'framework-manifest.json'), manifest);
    writeFileSync(join(bundle, 'framework-manifest.json'), manifest);
    const output = join(root, 'app');
    const run = () => createApp({ packageRoot: root, output, name: 'Demo', ownerKey: 'acme' });
    const pkg = readFileSync(join(root, 'package.json'));
    writeFileSync(join(root, 'package.json'), JSON.stringify({ ...JSON.parse(pkg), scripts: { malicious: 'unexpected' } }));
    assert.throws(run, /Frontend skeleton digest mismatch: package.json/u);
    writeFileSync(join(root, 'package.json'), pkg);
    writeFileSync(join(root, 'patches/dependency.patch'), 'modified patch');
    assert.throws(run, /Frontend skeleton digest mismatch: patches\/dependency.patch/u);
    writeFileSync(join(root, 'patches/dependency.patch'), sources['patches/dependency.patch']);
    writeFileSync(join(root, 'packages/braces/package.json'), '{"name":"modified"}');
    assert.throws(run, /Frontend skeleton digest mismatch: packages\/braces\/package.json/u);
    writeFileSync(join(root, 'packages/braces/package.json'), sources['packages/braces/package.json']);
    // 用后续边界的未拥有文件证明投影校验已通过；本用例不启动模板引擎或构建。
    mkdirSync(join(root, 'ui'), { recursive: true });
    writeFileSync(join(root, 'ui/unowned.txt'), 'unowned');
    assert.throws(run, /Unexpected frontend skeleton file: ui\/unowned.txt/u);
    assert.equal(readFileSync(join(root, 'package.json'), 'utf8'), pkg.toString());
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test('shipped application client commands generate, check and reject drift through pnpm', () => {
  const root = mkdtempSync(join(tmpdir(), 'fullnet-app-commands-'));
  try {
    writeFileSync(join(root, 'package.json'), JSON.stringify(createApplicationPackage(framework, application)));
    for (const [source, target] of [
      ['scripts/openapi/generate-fullnet-client.mjs', '.fullnet-tools/openapi/generate-fullnet-client.mjs'],
      ['scripts/openapi/validate-client-generation-readiness.mjs', '.fullnet-tools/openapi/validate-client-generation-readiness.mjs'],
    ]) {
      mkdirSync(join(root, target, '..'), { recursive: true });
      copyFileSync(resolve(source), join(root, target));
    }
    mkdirSync(join(root, 'contracts/openapi'), { recursive: true });
    const input = { openapi: '3.0.1', info: { title: 'Business', version: '1.0' }, paths: {}, components: { schemas: {} } };
    writeFileSync(join(root, 'contracts/openapi/fullnet-client-v1.openapi.json'), JSON.stringify(input));
    writeFileSync(join(root, 'contracts/openapi/client-generation-manifest-v1.json'), '{"publicOperationIds":[]}');
    const run = (script, args = []) => spawnSync('pnpm', ['run', script, ...args], {
      cwd: root, encoding: 'utf8', timeout: 30_000, windowsHide: true, shell: process.platform === 'win32',
    });
    const generated = run('generate:client');
    assert.equal(generated.status, 0, generated.stderr || generated.stdout);
    const checked = run('check:client');
    assert.equal(checked.status, 0, checked.stderr || checked.stdout);
    const custom = run('generate:client', ['--output', 'business-client', '--http-module', '@fullnet/client-contracts']);
    assert.equal(custom.status, 0, custom.stderr || custom.stdout);
    assert.match(readFileSync(join(root, 'business-client/operations.generated.ts'), 'utf8'), /@fullnet\/client-contracts/u);
    writeFileSync(join(root, 'packages/client-contracts/src/generated/models.generated.ts'), '// drift\n');
    const drift = run('check:client');
    assert.notEqual(drift.status, 0);
    assert.match(drift.stderr + drift.stdout, /models.generated.ts/u);
  } finally {
    // 只回收本测试新建的独立系统临时目录，不触碰应用或共享构建输出。
    rmSync(root, { recursive: true, force: true });
  }
});
