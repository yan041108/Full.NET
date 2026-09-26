import assert from 'node:assert/strict';
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import test from 'node:test';
import { compareFrameworkVersions } from '../../scripts/templates/framework-manifest-utils.mjs';
import { applyFrameworkUpgrade, planFrameworkUpgrade, upgradeFramework } from '../../scripts/templates/upgrade-framework.mjs';
import { fixture } from './support/framework-upgrade-fixture.mjs';

test('compareFrameworkVersions orders semantic versions', () => {
  assert.equal(compareFrameworkVersions('0.1.0', '0.1.0'), 0);
  assert.ok(compareFrameworkVersions('0.1.1', '0.1.0') > 0);
  assert.ok(compareFrameworkVersions('0.1.0', '0.2.0') < 0);
});

test('compareFrameworkVersions rejects invalid input', () => {
  assert.throws(() => compareFrameworkVersions('bad', '0.1.0'), /Invalid framework version/);
});

test('upgrade-framework reports customized managed files as conflicts', (t) => {
  const f = fixture(t);
  writeFileSync(join(f.appRoot, 'framework/fullnet/global.json'), 'custom framework');
  const plan = planFrameworkUpgrade(f);
  assert.ok(plan.conflicts.some((entry) => entry.path === 'global.json'));
  assert.throws(() => applyFrameworkUpgrade({ ...f, plan }), /conflicts remain/);
});

test('upgrade-framework applies managed updates without touching customized app host files', (t) => {
  const f = fixture(t);
  mkdirSync(join(f.appRoot, 'src/Demo.Host.Api'), { recursive: true });
  writeFileSync(join(f.appRoot, 'src/Demo.Host.Api/Program.cs'), '// 定制应用宿主');
  const plan = upgradeFramework({ ...f, dryRun: false });
  assert.equal(plan.targetVersion, '0.1.1');
  assert.equal(readFileSync(join(f.appRoot, 'framework/fullnet/global.json'), 'utf8'), f.newFiles['global.json']);
  assert.match(readFileSync(join(f.appRoot, 'src/Demo.Host.Api/Program.cs'), 'utf8'), /定制应用宿主/);
});
