import { spawn } from 'node:child_process';
import { existsSync, readFileSync } from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

import { loadTestMatrix } from './run-dotnet-test-suite.mjs';
import { renderSummary, summarizeTrxOutcomes } from './summarize-trx-outcomes.mjs';

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

function reportTrxOutcomes(shardName) {
  const trxFile = `Full.NET.IntegrationTests-${shardName}.trx`;
  if (!existsSync(trxFile)) {
    return;
  }

  const xml = readFileSync(trxFile, 'utf8');
  const report = summarizeTrxOutcomes(xml);
  process.stdout.write(`\n${renderSummary(report)}\n`);
  if (shardName === 'messaging-heavy' && report.outcomes.Inconclusive > 0) {
    process.stdout.write(
      '注意：messaging-heavy 含 Inconclusive 项；SQL Server CDC 在 Testcontainers 上为已知环境债务，'
      + '不得当作双库验收 Pass。见 docs/verification/sqlserver-cdc-ci-debt.md\n'
    );
  }
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
    if (code === 0) {
      try {
        reportTrxOutcomes(shardName);
      } catch (error) {
        process.stderr.write(`${error.message}\n`);
        process.exitCode = 1;
        return;
      }
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
