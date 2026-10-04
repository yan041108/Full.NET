import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { mkdtempSync, readFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import test from 'node:test';
import { buildAppTemplate } from '../../scripts/templates/build-app-template.mjs';
import { areBundleInputsClean } from '../../scripts/templates/build-source-bundle.mjs';

const skip = areBundleInputsClean() ? false : 'source bundle inputs have uncommitted changes';
test('validated application preserves diagnostic scripts and frozen workspace settings', { skip }, () => {
  const workspace = mkdtempSync(join(tmpdir(), 'fullnet-packaged-diagnose-'));
  try {
    const { templateRoot } = buildAppTemplate({ output: join(workspace, 'package') });
    const frozenPath = join(templateRoot, 'framework/fullnet/package.json');
    const frozenBytes = readFileSync(frozenPath);
    const frozen = JSON.parse(frozenBytes);
    const declared = JSON.parse(readFileSync(new URL('../../templates/fullnet-app/package.json', import.meta.url)));
    const check = root => {
      const packaged = JSON.parse(readFileSync(join(root, 'package.json')));
      for (const profile of ['development', 'production']) {
        const script = 'diagnose:' + profile;
        assert.equal(typeof packaged.scripts[script], 'string', 'missing application entry: ' + script);
        assert.equal(packaged.scripts[script], declared.scripts[script]);
      }
      for (const [name, command] of Object.entries(frozen.scripts)) {
        if (!Object.hasOwn(declared.scripts, name)) assert.equal(packaged.scripts[name], command);
      }
      const { scripts: packagedScripts, ...packagedSettings } = packaged;
      const { scripts: frozenScripts, ...frozenSettings } = frozen;
      assert.deepEqual(packagedSettings, frozenSettings, 'workspace settings or dependency policy changed');
      for (const file of ['pnpm-lock.yaml', 'pnpm-workspace.yaml']) {
        assert.deepEqual(readFileSync(join(root, file)), readFileSync(join(templateRoot, 'framework/fullnet', file)));
      }
    };
    assert.deepEqual(readFileSync(join(templateRoot, 'package.json')), frozenBytes, 'package integrity requires frozen workspace bytes');
    const appRoot = join(workspace, 'application');
    const created = spawnSync(process.execPath, [join(templateRoot, '.fullnet-tools/create-app.mjs'),
      '--output', appRoot, '--name', 'Demo', '--owner-key', 'acme', '--database', 'sqlserver',
      '--preset', 'minimal', '--http-port', '5198'], { encoding: 'utf8', timeout: 150_000, windowsHide: true });
    assert.equal(created.status, 0, created.stderr || created.stdout);
    check(appRoot);
    assert.deepEqual(readFileSync(frozenPath), frozenBytes, 'packaging changed managed framework configuration');
  } finally {
    // 夹具只清理本次创建的临时包和应用，不触碰调用方工作区。
    rmSync(workspace, { recursive: true, force: true });
  }
});
