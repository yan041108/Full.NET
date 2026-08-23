#!/usr/bin/env node
/**
 * 运行 Native AOT 外部进程集成测试（需先完成 linux-x64 publish）。
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
const nativeGate = matrix.nativeAotIntegration;

const result = spawnSync(
  'dotnet',
  [
    'test',
    'tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj',
    '-c',
    'Release',
    '--no-restore',
    '--filter',
    nativeGate.filter,
    '--',
    'MSTest',
    `MinimumExpectedTests=${nativeGate.minimum}`,
  ],
  {
    cwd: repositoryRoot,
    encoding: 'utf8',
    stdio: 'inherit',
    shell: process.platform === 'win32',
  }
);

process.exit(result.status ?? 1);
