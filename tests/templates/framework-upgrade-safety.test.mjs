import assert from 'node:assert/strict';
import { existsSync, mkdirSync, readFileSync, rmSync, symlinkSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import fs from 'node:fs';
import { syncBuiltinESMExports } from 'node:module';
import test from 'node:test';
import { planFrameworkUpgrade, applyFrameworkUpgrade, upgradeFramework } from '../../scripts/templates/upgrade-framework.mjs';
import { composition, digest, fixture, saveManifest } from './support/framework-upgrade-fixture.mjs';

test('upgrade rejects a package whose file bytes disagree with the manifest', (t) => {
  const f = fixture(t);
  writeFileSync(join(f.packageRoot, 'framework/fullnet/global.json'), 'tampered');
  assert.throws(() => upgradeFramework({ ...f, dryRun: false }), /digest mismatch/i);
  assert.equal(readFileSync(join(f.appRoot, 'framework/fullnet/global.json'), 'utf8'), f.oldFiles['global.json']);
});

test('upgrade rejects traversal paths before reading or writing outside the framework', (t) => {
  const f = fixture(t);
  const manifest = JSON.parse(readFileSync(join(f.packageRoot, 'framework-manifest.json')));
  manifest.managedFiles['../../escape.txt'] = digest('outside');
  saveManifest(f.packageRoot, manifest);
  assert.throws(() => upgradeFramework({ ...f, dryRun: false }), /managed path/i);
  assert.equal(existsSync(join(f.appRoot, 'escape.txt')), false);
});

test('upgrade refuses incomplete packages without applying earlier files', (t) => {
  const f = fixture(t);
  rmSync(join(f.packageRoot, 'framework/fullnet/Directory.Packages.props'));
  const before = readFileSync(join(f.appRoot, 'framework-manifest.json'));
  assert.throws(() => upgradeFramework({ ...f, dryRun: false }), /missing|ENOENT/i);
  assert.equal(readFileSync(join(f.appRoot, 'framework/fullnet/global.json'), 'utf8'), f.oldFiles['global.json']);
  assert.deepEqual(readFileSync(join(f.appRoot, 'framework-manifest.json')), before);
});

test('upgrade revalidates edits made after preview and preserves them', (t) => {
  const f = fixture(t);
  const plan = planFrameworkUpgrade(f);
  writeFileSync(join(f.appRoot, 'framework/fullnet/global.json'), 'edited after preview');
  assert.throws(() => applyFrameworkUpgrade({ ...f, plan }), /changed|conflict|customized/i);
  assert.equal(readFileSync(join(f.appRoot, 'framework/fullnet/global.json'), 'utf8'), 'edited after preview');
});

test('upgrade refuses a forged preview instead of trusting its paths and manifest', (t) => {
  const f = fixture(t);
  const plan = planFrameworkUpgrade(f);
  plan.nextManifest.frameworkVersion = '9.9.9';
  assert.throws(() => applyFrameworkUpgrade({ ...f, plan }), /changed|plan|preview/i);
  assert.equal(JSON.parse(readFileSync(join(f.appRoot, 'framework-manifest.json'))).frameworkVersion, '0.1.0');
});

test('upgrade reports missing previously managed files as conflicts', (t) => {
  const f = fixture(t);
  rmSync(join(f.appRoot, 'framework/fullnet/Directory.Packages.props'));
  const plan = planFrameworkUpgrade(f);
  assert.ok(plan.conflicts.some((entry) => entry.path === 'Directory.Packages.props'));
});

test('upgrade refuses managed file removal until a deletion recovery policy exists', (t) => {
  const f = fixture(t);
  const manifest = JSON.parse(readFileSync(join(f.packageRoot, 'framework-manifest.json')));
  delete manifest.managedFiles['Directory.Packages.props'];
  rmSync(join(f.packageRoot, 'framework/fullnet/Directory.Packages.props'));
  saveManifest(f.packageRoot, manifest);
  assert.ok(planFrameworkUpgrade(f).conflicts.some((entry) => entry.path === 'Directory.Packages.props'));
});

test('upgrade preserves selected modules and scoped migrations', (t) => {
  const f = fixture(t, { projected: true });
  upgradeFramework({ ...f, dryRun: false });
  const manifest = JSON.parse(readFileSync(join(f.appRoot, 'framework-manifest.json')));
  assert.equal(manifest.projectedPreset, 'minimal');
  assert.equal(manifest.migrationInventory.selectionStatus, 'preset-minimal');
  assert.deepEqual(manifest.migrationInventory.scripts.map((entry) => entry.name), ['001_Foundation.sql']);
  assert.doesNotMatch(readFileSync(join(f.appRoot, 'framework/fullnet', composition + 'Full.NET.Composition.csproj'), 'utf8'), /Full\.NET\.Modules\.Workflow\.csproj/);
});

test('upgrade rejects mismatched manifest copies', (t) => {
  const f = fixture(t);
  writeFileSync(join(f.packageRoot, 'framework/fullnet/framework-manifest.json'), '{}');
  assert.throws(() => planFrameworkUpgrade(f), /manifest copies/i);
});

test('upgrade never follows a framework directory link outside the application', (t) => {
  const f = fixture(t);
  const outside = join(f.workspace, 'outside');
  mkdirSync(outside);
  writeFileSync(join(outside, 'value.txt'), 'keep');
  symlinkSync(outside, join(f.appRoot, 'framework/fullnet/linked'), process.platform === 'win32' ? 'junction' : 'dir');
  const manifest = JSON.parse(readFileSync(join(f.packageRoot, 'framework-manifest.json')));
  manifest.managedFiles['linked/value.txt'] = digest('replace');
  mkdirSync(join(f.packageRoot, 'framework/fullnet/linked'));
  writeFileSync(join(f.packageRoot, 'framework/fullnet/linked/value.txt'), 'replace');
  saveManifest(f.packageRoot, manifest);
  assert.throws(() => upgradeFramework({ ...f, dryRun: false }), /link/i);
  assert.equal(readFileSync(join(outside, 'value.txt'), 'utf8'), 'keep');
});

test('upgrade preserves custom application files and retains the original framework for recovery', (t) => {
  const f = fixture(t);
  mkdirSync(join(f.appRoot, 'src/Business'), { recursive: true });
  writeFileSync(join(f.appRoot, 'src/Business/Program.cs'), 'custom business');
  writeFileSync(join(f.appRoot, 'framework/fullnet/local-notes.txt'), 'custom notes');
  const plan = upgradeFramework({ ...f, dryRun: false });
  assert.equal(readFileSync(join(f.appRoot, 'src/Business/Program.cs'), 'utf8'), 'custom business');
  assert.equal(readFileSync(join(f.appRoot, 'framework/fullnet/local-notes.txt'), 'utf8'), 'custom notes');
  assert.ok(plan.recoveryRoot, 'successful upgrade must retain a recovery location');
  assert.equal(readFileSync(join(plan.recoveryRoot, 'previous/global.json'), 'utf8'), f.oldFiles['global.json']);
});

test('upgrade refuses a package changed after preview', (t) => {
  const f = fixture(t);
  const plan = planFrameworkUpgrade(f);
  writeFileSync(join(f.packageRoot, 'framework/fullnet/global.json'), 'tampered after preview');
  assert.throws(() => applyFrameworkUpgrade({ ...f, plan }), /digest mismatch/i);
  assert.equal(readFileSync(join(f.appRoot, 'framework/fullnet/global.json'), 'utf8'), f.oldFiles['global.json']);
});

test('upgrade rejects unlisted dependency files in the target bundle', (t) => {
  const f = fixture(t);
  writeFileSync(join(f.packageRoot, 'framework/fullnet/pnpm-lock.yaml'), 'unlisted lock file');
  assert.throws(() => planFrameworkUpgrade(f), /Unexpected package framework file/i);
});

test('upgrade rejects Windows path aliases even when running on Linux', (t) => {
  const f = fixture(t);
  const manifest = JSON.parse(readFileSync(join(f.packageRoot, 'framework-manifest.json')));
  manifest.managedFiles['GLOBAL.json'] = digest(f.newFiles['global.json']);
  saveManifest(f.packageRoot, manifest);
  assert.throws(() => planFrameworkUpgrade(f), /case alias/i);
});

test('upgrade preserves an existing recovery directory', (t) => {
  const f = fixture(t);
  const oldRecovery = join(f.workspace, '.fullnet-upgrade-app');
  mkdirSync(oldRecovery);
  writeFileSync(join(oldRecovery, 'keep.txt'), 'previous recovery');
  upgradeFramework({ ...f, dryRun: false });
  assert.equal(readFileSync(join(oldRecovery, 'keep.txt'), 'utf8'), 'previous recovery');
});

test('upgrade refuses a pending interruption marker and preserves its recovery location', (t) => {
  const f = fixture(t);
  const lockPath = join(f.appRoot, '.fullnet-upgrade.lock');
  const marker = JSON.stringify({ recoveryRoot: join(f.appRoot, 'framework/recovery'), state: 'pending' });
  writeFileSync(lockPath, marker);
  assert.throws(() => upgradeFramework({ ...f, dryRun: false }), /interrupted/i);
  assert.equal(readFileSync(lockPath, 'utf8'), marker);
});

for (const failurePoint of ['framework-install', 'manifest-install']) {
  test(`upgrade rolls back and retains recovery material after ${failurePoint} I/O failure`, (t) => {
    const f = fixture(t);
    const plan = planFrameworkUpgrade(f);
    const beforeManifest = readFileSync(join(f.appRoot, 'framework-manifest.json'));
    const originalRename = fs.renameSync;
    let injected = false;
    // 故障注入限定在发布阶段，验证实际文件系统回滚，不模拟升级规则。
    const renameMock = t.mock.method(fs, 'renameSync', (source, target) => {
      const failTarget = failurePoint === 'framework-install'
        ? join(f.appRoot, 'framework/fullnet') : join(f.appRoot, 'framework-manifest.json');
      if (!injected && target === failTarget) {
        injected = true;
        throw new Error('injected upgrade I/O failure');
      }
      return originalRename(source, target);
    });
    syncBuiltinESMExports();
    try {
      assert.throws(() => applyFrameworkUpgrade({ ...f, plan }), /injected upgrade I\/O failure/);
    } finally {
      renameMock.mock.restore();
      syncBuiltinESMExports();
    }
    assert.equal(injected, true);
    assert.equal(readFileSync(join(f.appRoot, 'framework/fullnet/global.json'), 'utf8'), f.oldFiles['global.json']);
    assert.deepEqual(readFileSync(join(f.appRoot, 'framework-manifest.json')), beforeManifest);
    assert.deepEqual(readFileSync(join(f.appRoot, 'framework/fullnet/framework-manifest.json')), beforeManifest);
    assert.equal(existsSync(join(f.appRoot, '.fullnet-upgrade.lock')), false);
  });
}

test('upgrade rejects a removed seed inventory instead of disabling its validation', (t) => {
  const f = fixture(t);
  const manifest = JSON.parse(readFileSync(join(f.packageRoot, 'framework-manifest.json')));
  delete manifest.seedInventory;
  saveManifest(f.packageRoot, manifest);
  assert.throws(() => planFrameworkUpgrade(f), /seed|manifest/i);
});

test('upgrade rejects an emptied seed inventory whose managed registrations still exist', (t) => {
  const f = fixture(t);
  const manifest = JSON.parse(readFileSync(join(f.packageRoot, 'framework-manifest.json')));
  manifest.seedInventory = { contributors: [], presets: {} };
  saveManifest(f.packageRoot, manifest);
  assert.throws(() => planFrameworkUpgrade(f), /seed|manifest/i);
});

for (const timing of ['after-copy', 'after-claim', 'before-manifest-install']) {
  test(`upgrade preserves edits introduced ${timing} and refuses to report success`, (t) => {
    const f = fixture(t);
    const notesPath = join(f.appRoot, 'framework/fullnet/local-notes.txt');
    writeFileSync(notesPath, 'before copy');
    const plan = planFrameworkUpgrade(f);
    const originalCopy = fs.cpSync;
    const originalRename = fs.renameSync;
    let injected = false;
    const copyMock = t.mock.method(fs, 'cpSync', (source, target, options) => {
      originalCopy(source, target, options);
      if (timing === 'after-copy' && source === join(f.appRoot, 'framework/fullnet')) {
        injected = true;
        writeFileSync(notesPath, 'edited during staging');
      }
    });
    let previousRoot;
    const renameMock = t.mock.method(fs, 'renameSync', (source, target) => {
      if (source === join(f.appRoot, 'framework/fullnet') && target.endsWith('previous')) previousRoot = target;
      if (!injected && timing === 'before-manifest-install' && target === join(f.appRoot, 'framework-manifest.json')) {
        injected = true;
        writeFileSync(join(previousRoot, 'global.json'), 'edited after claim');
      }
      originalRename(source, target);
      if (!injected && timing === 'after-claim' && source === join(f.appRoot, 'framework/fullnet') && target.endsWith('previous')) {
        injected = true;
        writeFileSync(join(target, 'global.json'), 'edited after claim');
      }
    });
    syncBuiltinESMExports();
    try {
      assert.throws(() => applyFrameworkUpgrade({ ...f, plan }), /changed|snapshot/i);
    } finally {
      copyMock.mock.restore();
      renameMock.mock.restore();
      syncBuiltinESMExports();
    }
    assert.equal(injected, true);
    if (timing === 'after-copy') assert.equal(readFileSync(notesPath, 'utf8'), 'edited during staging');
    else assert.equal(readFileSync(join(f.appRoot, 'framework/fullnet/global.json'), 'utf8'), 'edited after claim');
    assert.equal(JSON.parse(readFileSync(join(f.appRoot, 'framework-manifest.json'))).frameworkVersion, '0.1.0');
  });
}

test('upgrade refuses a missing projected preset while the application profile still freezes it', (t) => {
  const f = fixture(t, { projected: true });
  const manifest = JSON.parse(readFileSync(join(f.appRoot, 'framework-manifest.json')));
  delete manifest.projectedPreset;
  saveManifest(f.appRoot, manifest);
  assert.throws(() => planFrameworkUpgrade(f), /preset disagrees/i);
});

for (const preset of ['platform', 'saas', 'enterprise']) {
  test(`upgrade reprojects ${preset} without changing its module or migration closure`, (t) => {
    const f = fixture(t, { projected: preset });
    const old = JSON.parse(readFileSync(join(f.appRoot, 'framework-manifest.json')));
    const plan = upgradeFramework({ ...f, dryRun: false });
    assert.equal(plan.nextManifest.projectedPreset, preset);
    assert.deepEqual(plan.nextManifest.migrationInventory, old.migrationInventory);
    assert.equal(readFileSync(join(f.appRoot, 'framework/fullnet', composition + 'Full.NET.Composition.csproj'), 'utf8'),
      readFileSync(join(plan.recoveryRoot, 'previous', composition + 'Full.NET.Composition.csproj'), 'utf8'));
  });
}

test('upgrade refuses edits to the installed framework and retains them in failed recovery', (t) => {
  const f = fixture(t);
  const plan = planFrameworkUpgrade(f);
  const originalRename = fs.renameSync;
  const renameMock = t.mock.method(fs, 'renameSync', (source, target) => {
    originalRename(source, target);
    if (source.endsWith('next') && target === join(f.appRoot, 'framework/fullnet')) {
      writeFileSync(join(target, 'global.json'), 'edited in new framework');
    }
  });
  syncBuiltinESMExports();
  try {
    assert.throws(() => applyFrameworkUpgrade({ ...f, plan }), /snapshot changed/i);
  } finally {
    renameMock.mock.restore();
    syncBuiltinESMExports();
  }
  assert.equal(readFileSync(join(f.appRoot, 'framework/fullnet/global.json'), 'utf8'), f.oldFiles['global.json']);
});

test('upgrade snapshots an unmanaged __proto__ file like any other file', (t) => {
  const f = fixture(t);
  const path = join(f.appRoot, 'framework/fullnet/__proto__');
  writeFileSync(path, 'original');
  const plan = planFrameworkUpgrade(f);
  writeFileSync(path, 'edited before apply');
  assert.throws(() => applyFrameworkUpgrade({ ...f, plan }), /preview changed/i);
  assert.equal(readFileSync(path, 'utf8'), 'edited before apply');
});
