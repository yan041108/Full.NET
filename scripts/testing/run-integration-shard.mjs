import { spawn } from 'node:child_process';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

import { loadTestMatrix } from './run-dotnet-test-suite.mjs';

const matrix = loadTestMatrix();
const assembly = matrix.integration.assembly;
export const shards = matrix.integration.shards;

export function argumentsFor(shardName) {
  const shard = shards[shardName];
  if (!shard) {
    throw new Error(
      `未知 Integration 分片“${shardName}”；可选值：${Object.keys(shards).join(', ')}`
    );
  }

  const args = [assembly, '--no-ansi', '--progress', 'off'];
  if (shard.filter) {
    args.push('--filter', shard.filter);
  }
  args.push(
    '--minimum-expected-tests',
    String(shard.minimum),
    '--timeout',
    shard.timeout,
    '--report-trx',
    '--report-trx-filename',
    `Full.NET.IntegrationTests-${shardName}.trx`
  );
  return args;
}

function run(shardName) {
  const child = spawn('dotnet', argumentsFor(shardName), {
    cwd: process.cwd(),
    stdio: 'inherit',
    shell: false
  });
  child.on('error', error => {
    process.stderr.write(`无法启动 dotnet：${error.message}\n`);
    process.exitCode = 1;
  });
  child.on('exit', (code, signal) => {
    if (signal) {
      process.stderr.write(`Integration 分片被信号 ${signal} 终止。\n`);
      process.exitCode = 1;
      return;
    }
    process.exitCode = code ?? 1;
  });
}

if (
  process.argv[1]
  && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href
) {
  try {
    run(process.argv[2] ?? '');
  } catch (error) {
    process.stderr.write(`${error.message}\n`);
    process.exitCode = 1;
  }
}
