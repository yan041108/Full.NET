#!/usr/bin/env node
import { testOutputDirectory } from './test-output-directory.mjs';
/** 运行 Worker Native AOT 双库一次性外部进程 E2E。 */
import { spawnSync } from 'node:child_process';
import { randomUUID } from 'node:crypto';
import { validateNativeAotRuntimeResults } from './native-aot-runtime-results.mjs';
import { readFileSync } from 'node:fs';
import path from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const matrix = JSON.parse(readFileSync(path.join(repositoryRoot, 'eng/testing/test-matrix.json'), 'utf8'));
const gate = matrix.workerNativeAotIntegration;
const assembly = matrix.integration.assembly;
const build = spawnSync('dotnet', ['build', gate.project, '-c', 'Release', '--nologo'], {
  cwd: repositoryRoot,
  stdio: 'inherit',
  shell: false,
});
if (build.status !== 0) process.exit(build.status ?? 1);

let expectedUids;
// 所有平台都保留发现门禁；Linux 还需将完整结果绑定到同一次发现。
{
  const discovery = spawnSync('dotnet', [assembly, '--list-tests', 'json', '--no-ansi', '--filter', gate.filter], {
    cwd: repositoryRoot,
    encoding: 'utf8',
    stdio: ['ignore', 'pipe', 'inherit'],
    shell: false,
  });
  if (discovery.status !== 0) process.exit(discovery.status ?? 1);
  const discovered = JSON.parse(discovery.stdout).tests ?? [];
  expectedUids = discovered.map(item => item.uid);
  const count = discovered.length;
  if (count < gate.minimum) {
    console.error(`Worker Native AOT E2E 发现数不足：${count} < ${gate.minimum}。`);
    process.exit(1);
  }
  const discoveredTypes = new Set(discovered.map((item) => item.type?.typeName));
  for (const requiredType of gate.requiredTestTypes ?? []) {
    if (!discoveredTypes.has(requiredType)) {
      console.error(`Worker Native AOT E2E 缺少必需用例：${requiredType}。`);
      process.exit(1);
    }
  }
  console.log(`Worker Native AOT E2E 发现门禁：${count} 项。`);
}

const resultsDirectory = testOutputDirectory(repositoryRoot, 'native-aot/worker/linux-x64/test-results');
const policy = process.platform === 'linux'
  ? ['--minimum-expected-tests', String(gate.minimum)]
  : ['--zero-tests-policy', 'allow-skipped'];
// 每轮使用独立文件名，不能用前次成功 TRX 填补本轮缺失结果。
const reportFilename = `Full.NET.IntegrationTests-native-aot-worker-${randomUUID()}.trx`;

const tests = spawnSync('dotnet', [
  assembly,
  '--no-ansi',
  '--progress', 'off',
  '--timeout', gate.timeout,
  '--filter', gate.filter,
  '--results-directory', resultsDirectory,
  '--report-trx',
  '--report-trx-filename', reportFilename,
  ...policy,
], {
  cwd: repositoryRoot,
  stdio: 'inherit',
  shell: false,
});
if (tests.error || tests.status !== 0) process.exit(tests.status || 1);
if (process.platform === 'linux') {
  try {
    const result = validateNativeAotRuntimeResults(readFileSync(path.join(resultsDirectory, reportFilename), 'utf8'), expectedUids);
    console.log(`Native AOT 完整 TRX 核对：${result.passed} 项，零失败/跳过。`);
  } catch (error) {
    console.error(error.message);
    process.exit(1);
  }
}
process.exit(0);
