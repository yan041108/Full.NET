import { spawn } from 'node:child_process';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

const assembly = path.join(
  'tests',
  'Full.NET.IntegrationTests',
  'bin',
  'Release',
  'net10.0',
  'Full.NET.IntegrationTests.dll'
);

const smokeFilter =
  'FullyQualifiedName~SqlServer_migration_is_idempotent_and_creates_binary_outbox_schema'
  + '|FullyQualifiedName~MySql_migration_is_idempotent_and_creates_binary_outbox_schema'
  + '|FullyQualifiedName~Login_and_current_user_follow_secure_http_contract'
  + '|FullyQualifiedName~Anonymous_current_tenant_endpoint_returns_minimal_standard_http_contract'
  + '|FullyQualifiedName~SqlServer_provisioning_is_atomic_and_writes_binary_outbox'
  + '|FullyQualifiedName~MySql_provisioning_is_atomic_and_writes_binary_outbox';

export const shards = {
  smoke: {
    filter: smokeFilter,
    minimum: 8,
    timeout: '15m'
  },
  'api-sqlserver': {
    filter: 'FullyQualifiedName~ApiSqlServerTests',
    minimum: 35,
    timeout: '60m'
  },
  'api-mysql': {
    filter: 'FullyQualifiedName~ApiMySqlTests',
    minimum: 35,
    timeout: '60m'
  },
  migrations: {
    filter: 'FullyQualifiedName~Full.NET.IntegrationTests.Migrations',
    minimum: 62,
    timeout: '90m'
  },
  infrastructure: {
    filter:
      'FullyQualifiedName!~ApiSqlServerTests'
      + '&FullyQualifiedName!~ApiMySqlTests'
      + '&FullyQualifiedName!~Full.NET.IntegrationTests.Migrations',
    minimum: 57,
    timeout: '60m'
  },
  full: {
    minimum: 189,
    timeout: '90m'
  }
};

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
