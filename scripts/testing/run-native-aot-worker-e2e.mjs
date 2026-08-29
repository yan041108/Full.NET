#!/usr/bin/env node
/** 运行 Worker Native AOT 双库一次性外部进程 E2E。 */
import { spawnSync } from 'node:child_process';
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

if (process.platform !== 'linux') {
  const discovery = spawnSync('dotnet', [assembly, '--list-tests', 'json', '--no-ansi', '--filter', gate.filter], {
    cwd: repositoryRoot,
    encoding: 'utf8',
    stdio: ['ignore', 'pipe', 'inherit'],
    shell: false,
  });
  if (discovery.status !== 0) process.exit(discovery.status ?? 1);
  const count = JSON.parse(discovery.stdout).tests?.length ?? 0;
  if (count < gate.minimum) {
    console.error(`Worker Native AOT E2E 发现数不足：${count} < ${gate.minimum}。`);
    process.exit(1);
  }
  console.log(`Worker Native AOT E2E 非 Linux 发现门禁：${count} 项。`);
}

const resultsDirectory = path.join(repositoryRoot, 'artifacts/native-aot/worker/linux-x64/test-results');
const policy = process.platform === 'linux'
  ? ['--minimum-expected-tests', String(gate.minimum)]
  : ['--zero-tests-policy', 'allow-skipped'];
const tests = spawnSync('dotnet', [
  assembly,
  '--no-ansi',
  '--progress', 'off',
  '--timeout', gate.timeout,
  '--filter', gate.filter,
  '--results-directory', resultsDirectory,
  '--report-trx',
  '--report-trx-filename', 'Full.NET.IntegrationTests-native-aot-worker.trx',
  ...policy,
], {
  cwd: repositoryRoot,
  stdio: 'inherit',
  shell: false,
});
process.exit(tests.status ?? 1);
