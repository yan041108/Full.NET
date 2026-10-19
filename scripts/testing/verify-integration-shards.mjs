import { execFile } from 'node:child_process';
import path from 'node:path';
import { promisify } from 'node:util';
import { pathToFileURL } from 'node:url';

import { shards } from './run-integration-shard.mjs';
import { targetsForExecutionGroup, verifyFocusedDiscovery } from './run-affected-integration.mjs';

const execFileAsync = promisify(execFile);
const assembly = path.join(
  'tests',
  'Full.NET.IntegrationTests',
  'bin',
  'Release',
  'net10.0',
  'Full.NET.IntegrationTests.dll'
);
const partitionNames = [
  'api-sqlserver',
  'api-mysql',
  'migrations',
  'infrastructure',
  'messaging-heavy'
];

/** 显式选择已验收的隔离程序集，默认路径与既有命令一致。 */
export function parseAssemblyArguments(args) {
  if (args.length === 0) return assembly;
  if (args.length !== 2 || args[0] !== '--assembly' || !args[1]?.trim()) {
    throw new Error('用法：node scripts/testing/verify-integration-shards.mjs [--assembly 程序集路径]');
  }
  return path.resolve(args[1]);
}

export function verifyPartitionSets(fullTests, partitions) {
  const fullIds = new Set();
  for (const test of fullTests) {
    if (fullIds.has(test.uid)) {
      throw new Error(
        `全量测试 UID 重复：${test.uid}（${test.displayName}）。`
      );
    }
    fullIds.add(test.uid);
  }

  const assigned = new Map();
  for (const [name, tests] of Object.entries(partitions)) {
    for (const test of tests) {
      const existing = assigned.get(test.uid);
      if (existing) {
        throw new Error(
          `测试“${test.displayName}”同时落入 ${existing} 与 ${name} 分片。`
        );
      }
      assigned.set(test.uid, name);
    }
  }

  const missing = fullTests.filter(test => !assigned.has(test.uid));
  const unexpected = [...assigned.keys()].filter(uid => !fullIds.has(uid));
  if (missing.length > 0 || unexpected.length > 0) {
    throw new Error(
      `分片未形成全量集合：遗漏 ${missing.length} 项，额外 ${unexpected.length} 项。`
    );
  }
}

async function discover(filter, selectedAssembly = assembly) {
  const args = [selectedAssembly, '--list-tests', 'json', '--no-ansi', '--progress', 'off'];
  if (filter) {
    args.push('--filter', filter);
  }
  const { stdout } = await execFileAsync('dotnet', args, {
    cwd: process.cwd(),
    encoding: 'utf8',
    maxBuffer: 20 * 1024 * 1024
  });
  return JSON.parse(stdout.trim()).tests;
}

async function runCli() {
  const selectedAssembly = parseAssemblyArguments(process.argv.slice(2));
  const fullTests = await discover(shards.full.filter, selectedAssembly);
  if (fullTests.length !== shards.full.minimum) {
    throw new Error(
      `全量发现 ${fullTests.length} 项，与 canonical ${shards.full.minimum} 不一致。`
    );
  }

  const partitions = {};
  for (const name of partitionNames) {
    const tests = await discover(shards[name].filter, selectedAssembly);
    if (tests.length !== shards[name].minimum) {
      throw new Error(
        `${name} 发现 ${tests.length} 项，与配置 ${shards[name].minimum} 不一致。`
      );
    }
    partitions[name] = tests;
  }

  verifyPartitionSets(fullTests, partitions);
  // PR 的两个迁移恢复作业必须形成原迁移集合的互斥完整分区，并各自包含正式双库用例。
  const migrationGroups = {};
  for (const group of ['migrations-legacy', 'migrations-current']) {
    const [target] = targetsForExecutionGroup([{ kind: 'shard', name: 'migrations' }], group);
    migrationGroups[group] = await discover(target.filter, selectedAssembly);
    verifyFocusedDiscovery(migrationGroups[group], { phase: 'merge' });
  }
  verifyPartitionSets(partitions.migrations, migrationGroups);
  for (const [name, tests] of Object.entries(migrationGroups)) {
    process.stdout.write(`${name}: ${tests.length}\n`);
  }
  process.stdout.write(
    `PASS Integration 分片：${partitionNames
      .map(name => `${name}=${partitions[name].length}`)
      .join(', ')}，合计 ${fullTests.length} 项，无遗漏或重复。\n`
  );
}

if (
  process.argv[1]
  && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href
) {
  runCli().catch(error => {
    process.stderr.write(`${error.message}\n`);
    process.exitCode = 1;
  });
}
