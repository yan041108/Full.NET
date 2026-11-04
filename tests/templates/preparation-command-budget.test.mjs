import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { existsSync, mkdirSync, mkdtempSync, readdirSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import test from 'node:test';
import { buildAppTemplate } from '../../scripts/templates/build-app-template.mjs';
import { buildSourceBundle } from '../../scripts/templates/build-source-bundle.mjs';
import { createApp } from '../../scripts/templates/create-app.mjs';

for (const [name, build] of [['源码包', buildSourceBundle], ['应用模板', buildAppTemplate]]) {
  test(`${name}的 Git 与解包命令都使用准备预算入口`, t => {
    const root = mkdtempSync(join(tmpdir(), 'fullnet-command-budget-'));
    t.after(() => rmSync(root, { recursive: true, force: true }));
    const commands = [];
    const expired = new Error('expected preparation deadline');
    assert.throws(() => build({ output: join(root, 'package'), run: (binary, args) => {
      commands.push([binary, args[0]]);
      if (binary === 'tar') throw expired;
      return { status: 0, stdout: args[0] === 'rev-parse' ? 'a'.repeat(40) : '', stderr: '' };
    } }), error => error === expired);
    assert.deepEqual(commands, [['git', 'rev-parse'], ['git', 'status'], ['git', 'ls-tree'], ['git', 'archive'], ['tar', '-xf']]);
  });
}

test('应用创建命令使用准备预算入口且逾期后回收 staging 与 hive', t => {
  const root = mkdtempSync(join(tmpdir(), 'fullnet-create-budget-'));
  t.after(() => rmSync(root, { recursive: true, force: true }));
  const packageRoot = join(root, 'package');
  const bundleRoot = join(packageRoot, 'framework', 'fullnet');
  mkdirSync(bundleRoot, { recursive: true });
  const content = '{}';
  const manifest = JSON.stringify({ managedFiles: { 'global.json': createHash('sha256').update(content).digest('hex') } });
  writeFileSync(join(packageRoot, 'framework-manifest.json'), manifest);
  writeFileSync(join(bundleRoot, 'framework-manifest.json'), manifest);
  writeFileSync(join(bundleRoot, 'global.json'), content);
  const commands = [];
  let hive;
  const expired = new Error('expected creation deadline');
  assert.throws(() => createApp({ packageRoot, output: join(root, 'app'), name: 'Demo', ownerKey: 'acme', run: (binary, args) => {
    commands.push([binary, args[1]]);
    hive = args[args.indexOf('--debug:custom-hive') + 1];
    if (args[1] !== 'install') throw expired;
    return { status: 0 };
  } }), error => error === expired);
  assert.deepEqual(commands, [['dotnet', 'install'], ['dotnet', 'fullnet-app']]);
  assert.equal(existsSync(hive), false);
  assert.equal(existsSync(join(root, 'app')), false);
  assert.deepEqual(readdirSync(root), ['package']);
});
