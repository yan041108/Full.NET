#!/usr/bin/env node
/**
 * 运行 Native AOT Settings/Jobs 外部进程 E2E（需先完成 linux-x64 publish）。
 */
import { spawnSync } from 'node:child_process';
import { readFileSync } from 'node:fs';
import path from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)),
  '../..'
);
const matrixPath = path.join(repositoryRoot, 'eng/testing/test-matrix.json');
const matrix = JSON.parse(readFileSync(matrixPath, 'utf8'));
const nativeGate = matrix.nativeAotSettingsJobsIntegration;
const integrationAssembly = matrix.integration.assembly;
const resultsDirectory = path.join(
  repositoryRoot,
  'artifacts/native-aot/linux-x64/test-results'
);

const build = spawnSync(
  'dotnet',
  [
    'build',
    nativeGate.project,
    '--configuration',
    'Release',
    '--nologo',
  ],
  {
    cwd: repositoryRoot,
    encoding: 'utf8',
    stdio: 'inherit',
    shell: false,
  }
);
if (build.status !== 0) {
  process.exit(build.status ?? 1);
}

if (process.platform !== 'linux') {
  const discovery = spawnSync(
    'dotnet',
    [
      integrationAssembly,
      '--list-tests',
      'json',
      '--no-ansi',
      '--filter',
      nativeGate.filter,
    ],
    {
      cwd: repositoryRoot,
      encoding: 'utf8',
      stdio: ['ignore', 'pipe', 'inherit'],
      shell: false,
    }
  );
  if (discovery.status !== 0) {
    process.exit(discovery.status ?? 1);
  }

  const discoveryPayload = JSON.parse(discovery.stdout);
  const discoveredTests = discoveryPayload.tests?.length ?? 0;
  if (discoveredTests < nativeGate.minimum) {
    console.error(
      `Native AOT Settings/Jobs E2E 发现数不足：${discoveredTests} < ${nativeGate.minimum}。`
    );
    process.exit(1);
  }
  console.log(
    `Native AOT Settings/Jobs E2E 非 Linux 发现门禁：${discoveredTests} 项。`
  );
}

const executionPolicyArgs = process.platform === 'linux'
  ? ['--minimum-expected-tests', String(nativeGate.minimum)]
  : ['--zero-tests-policy', 'allow-skipped'];

const tests = spawnSync(
  'dotnet',
  [
    integrationAssembly,
    '--no-ansi',
    '--progress',
    'off',
    '--timeout',
    nativeGate.timeout,
    '--filter',
    nativeGate.filter,
    '--results-directory',
    resultsDirectory,
    '--report-trx',
    '--report-trx-filename',
    'Full.NET.IntegrationTests-native-aot-settings-jobs.trx',
    ...executionPolicyArgs,
  ],
  {
    cwd: repositoryRoot,
    encoding: 'utf8',
    stdio: 'inherit',
    shell: false,
  }
);

process.exit(tests.status ?? 1);
