import assert from 'node:assert/strict';
import { mkdtemp, mkdir, rm, unlink, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import test from 'node:test';
import { diagnosticsInputs, inspectDiagnosticsBuild, invalidateDiagnosticsBuild, recordDiagnosticsBuild } from '../../scripts/database/build-state.mjs';

async function fixture(t) {
  const root = await mkdtemp(path.join(tmpdir(), 'fullnet-diagnostics-build-'));
  // 仅回收当前测试创建的唯一目录，不触碰真实工具缓存。
  t.after(() => rm(root, { force: true, recursive: true }));
  const source = path.join(root, 'src/Tools/Full.NET.DatabaseDiagnostics.Cli');
  const scripts = path.join(root, 'scripts/database');
  const output = path.join(root, 'cache');
  const paths = { output, dll: path.join(output, 'bin/diagnostics.dll') };
  await Promise.all([mkdir(source, { recursive: true }), mkdir(scripts, { recursive: true }), mkdir(path.dirname(paths.dll), { recursive: true })]);
  await writeFile(path.join(source, 'Program.cs'), 'source-v1');
  await writeFile(path.join(scripts, 'build.mjs'), 'builder-v1');
  await writeFile(path.join(root, 'Directory.Packages.props'), 'packages-v1');
  await writeFile(paths.dll, 'assembly-v1');
  await writeFile(path.join(path.dirname(paths.dll), 'driver.dll'), 'driver-v1');
  const options = { paths, sdkVersion: '10.0.402' };
  const before = await diagnosticsInputs(root, options);
  await recordDiagnosticsBuild(root, before, options);
  return { root, source, scripts, paths, options };
}

test('有效记录可复用，源码、SDK 或中央依赖变化必须拒绝旧产物', async t => {
  const f = await fixture(t);
  assert.equal((await inspectDiagnosticsBuild(f.root, f.options)).ok, true);
  assert.equal((await inspectDiagnosticsBuild(f.root, { ...f.options, sdkVersion: '10.0.403' })).ok, false);
  await writeFile(path.join(f.root, 'Directory.Packages.props'), 'packages-v2');
  assert.equal((await inspectDiagnosticsBuild(f.root, f.options)).reason, 'inputs_changed');
  await writeFile(path.join(f.root, 'Directory.Packages.props'), 'packages-v1');
  await writeFile(path.join(f.source, 'Program.cs'), 'source-v2');
  assert.equal((await inspectDiagnosticsBuild(f.root, f.options)).reason, 'inputs_changed');
});

test('源码新增、删除与构建入口修改均参与失效', async t => {
  const f = await fixture(t);
  const extra = path.join(f.source, 'Queries.cs');
  await writeFile(extra, 'new source');
  assert.equal((await inspectDiagnosticsBuild(f.root, f.options)).ok, false);
  await unlink(extra);
  assert.equal((await inspectDiagnosticsBuild(f.root, f.options)).ok, true);
  await unlink(path.join(f.source, 'Program.cs'));
  assert.equal((await inspectDiagnosticsBuild(f.root, f.options)).ok, false);
  await writeFile(path.join(f.source, 'Program.cs'), 'source-v1');
  await writeFile(path.join(f.scripts, 'build.mjs'), 'builder-v2');
  assert.equal((await inspectDiagnosticsBuild(f.root, f.options)).ok, false);
});

test('DLL 和运行依赖被替换、丢失时不能复用', async t => {
  const f = await fixture(t);
  await writeFile(f.paths.dll, 'assembly-tampered');
  assert.equal((await inspectDiagnosticsBuild(f.root, f.options)).reason, 'outputs_changed');
  await writeFile(f.paths.dll, 'assembly-v1');
  await writeFile(path.join(path.dirname(f.paths.dll), 'driver.dll'), 'driver-tampered');
  assert.equal((await inspectDiagnosticsBuild(f.root, f.options)).reason, 'outputs_changed');
  await unlink(f.paths.dll);
  assert.equal((await inspectDiagnosticsBuild(f.root, f.options)).ok, false);
});

test('失败构建撤销证明，损坏或缺失的记录不能放行', async t => {
  const f = await fixture(t);
  const before = await diagnosticsInputs(f.root, f.options);
  await invalidateDiagnosticsBuild(f.paths);
  assert.equal((await inspectDiagnosticsBuild(f.root, f.options)).ok, false);
  await writeFile(path.join(f.source, 'Program.cs'), 'changed-during-build');
  await assert.rejects(recordDiagnosticsBuild(f.root, before, f.options), /构建期间/);
  assert.equal((await inspectDiagnosticsBuild(f.root, f.options)).ok, false);
  await writeFile(path.join(f.paths.output, 'diagnostics-build.json'), '{corrupt');
  assert.equal((await inspectDiagnosticsBuild(f.root, f.options)).ok, false);
});
