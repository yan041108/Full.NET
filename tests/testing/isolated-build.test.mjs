import assert from 'node:assert/strict';
import test from 'node:test';
import { mkdir, mkdtemp, readdir, readFile, rm, stat, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { parseBuildArguments, runBuild } from '../../scripts/testing/run-build-with-resources.mjs';
import { prepareIsolatedBuild } from '../../scripts/testing/isolated-build-output.mjs';

test('构建入口支持同源码工作区的真实隔离输出，不再只能按源码目录串行', () => {
  const parsed = parseBuildArguments(['--isolated-output', '../isolated-output', '--', 'dotnet', 'build', 'App.csproj']);
  assert.ok(parsed.isolatedOutput);
});

test('隔离入口拒绝伪隔离、覆盖路径、来源不符和不明非空目录', async t => {
  const root = await mkdtemp(path.join(os.tmpdir(), 'fullnet-output-check-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  const cwd = path.join(root, 'source'), output = path.join(root, 'output');
  await mkdir(cwd);
  const valid = { cwd, output, command: 'dotnet', args: ['build', 'App.csproj'] };
  for (const args of [['build', '@hidden.rsp'], ['build', '-o', 'elsewhere'],
    ['build', '-p:BaseIntermediateOutputPath=shared'], ['build', '--artifacts-path=shared']]) {
    await assert.rejects(prepareIsolatedBuild({ ...valid, args }), /不能另外覆盖/);
  }
  await assert.rejects(prepareIsolatedBuild({ ...valid, command: 'node' }), /只支持/);
  await assert.rejects(prepareIsolatedBuild({ ...valid, output: path.join(cwd, 'out') }), /源码工作区/);
  await mkdir(output); await writeFile(path.join(output, 'unowned.txt'), 'not ours');
  await assert.rejects(prepareIsolatedBuild(valid), /非空/);
  const owned = { ...valid, output: path.join(root, 'owned') };
  const results = await Promise.all([prepareIsolatedBuild(owned), prepareIsolatedBuild(owned)]);
  assert.deepEqual(results[0], results[1]);
  const other = path.join(root, 'other'); await mkdir(other);
  await assert.rejects(prepareIsolatedBuild({ ...owned, cwd: other }), /属于其他/);
});
