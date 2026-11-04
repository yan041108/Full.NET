import assert from 'node:assert/strict';
import test from 'node:test';
import { spawnSync } from 'node:child_process';
import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { withTestRun, testRunEnvironment } from '../../scripts/testing/test-run-context.mjs';

const targets = readFileSync('templates/fullnet-app/Directory.Build.targets');
for (const host of ['Api', 'Worker', 'Migrator']) test('发布与构建交付冻结清单和应用声明：' + host, { timeout: 120_000 }, async () => {
  const root = mkdtempSync(join(tmpdir(), 'fullnet-template-manifest-assets-'));
  try {
    mkdirSync(join(root, 'framework/fullnet'), { recursive: true });
    writeFileSync(join(root, 'framework/fullnet/Directory.Build.targets'), '<Project />');
    writeFileSync(join(root, 'Directory.Build.targets'), targets);
    const metadata = { 'framework-manifest.json': '{"sourceCommit":"fixture"}', 'fullnet-app.json': '{"name":"fixture"}' };
    for (const [path, content] of Object.entries(metadata)) writeFileSync(join(root, path), content);
    const projectRoot = join(root, 'src', 'FullNetAppNameToken.Host.' + host); mkdirSync(projectRoot, { recursive: true });
    const project = join(projectRoot, 'FullNetAppNameToken.Host.' + host + '.csproj');
    writeFileSync(project, '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>');
    writeFileSync(join(projectRoot, 'Marker.cs'), 'public sealed class Marker {}');
    await withTestRun({ cwd: root, build: true, buildBudget: { cpu: 1, memoryGiB: 1 } }, async () => {
      const result = spawnSync('dotnet', ['publish', project, '-c', 'Release', '-m:1', '-v', 'quiet'],
        { cwd: root, env: testRunEnvironment(), encoding: 'utf8', windowsHide: true, timeout: 60_000 });
      assert.equal(result.error, undefined); assert.equal(result.status, 0, result.stderr + '\n' + result.stdout);
      for (const suffix of ['', 'publish']) for (const [name, content] of Object.entries(metadata))
        assert.equal(readFileSync(join(projectRoot, 'bin/Release/net10.0', suffix, name), 'utf8'), content);
    });
  } finally { rmSync(root, { recursive: true, force: true }); }
});
for (const missing of ['framework-manifest.json', 'fullnet-app.json']) test('宿主不得成功发布缺少的声明文件：' + missing, { timeout: 120_000 }, async () => {
  const root = mkdtempSync(join(tmpdir(), 'fullnet-template-manifest-missing-'));
  try {
    mkdirSync(join(root, 'framework/fullnet'), { recursive: true });
    writeFileSync(join(root, 'framework/fullnet/Directory.Build.targets'), '<Project />');
    writeFileSync(join(root, 'Directory.Build.targets'), targets);
    for (const name of ['framework-manifest.json', 'fullnet-app.json']) if (name !== missing) writeFileSync(join(root, name), '{}');
    const project = join(root, 'FullNetAppNameToken.Host.Worker.csproj');
    writeFileSync(project, '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>');
    writeFileSync(join(root, 'Marker.cs'), 'public sealed class Marker {}');
    await withTestRun({ cwd: root, build: true, buildBudget: { cpu: 1, memoryGiB: 1 } }, async () => {
      const result = spawnSync('dotnet', ['publish', project, '-c', 'Release', '-m:1', '-v', 'quiet'],
        { cwd: root, env: testRunEnvironment(), encoding: 'utf8', windowsHide: true, timeout: 60_000 });
      assert.equal(result.error, undefined); assert.notEqual(result.status, 0);
      assert.ok((result.stdout + result.stderr).includes(missing));
    });
  } finally { rmSync(root, { recursive: true, force: true }); }
});
