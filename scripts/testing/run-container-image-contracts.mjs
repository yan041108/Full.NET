#!/usr/bin/env node
/**
 * 检查三个 Docker final target 的 User / Entrypoint / 标签，并用运行时 smoke 验证。
 */
import { spawnSync } from 'node:child_process';
import process from 'node:process';

const suffix = (() => {
  const index = process.argv.indexOf('--tag-suffix');
  return index >= 0 ? process.argv[index + 1] : 'contract';
})();

const targets = [
  {
    name: 'api',
    image: `fullnet-api:${suffix}`,
    entrypoint: '["dotnet","Full.NET.Host.Api.dll"]',
  },
  {
    name: 'worker',
    image: `fullnet-worker:${suffix}`,
    entrypoint: '["dotnet","Full.NET.Host.Worker.dll"]',
  },
  {
    name: 'migrator',
    image: `fullnet-migrator:${suffix}`,
    entrypoint: '["dotnet","Full.NET.Host.Migrator.dll"]',
  },
];

function run(command, args) {
  if (process.platform === 'win32') {
    const quoted = [command, ...args]
      .map((part) => (/\s/.test(part) ? `"${part}"` : part))
      .join(' ');
    return spawnSync(quoted, {
      encoding: 'utf8',
      shell: true,
    });
  }
  return spawnSync(command, args, {
    encoding: 'utf8',
    shell: false,
  });
}

function inspect(image) {
  const result = run('docker', [
    'image',
    'inspect',
    image,
    '--format',
    '{{json .Config}}',
  ]);
  if (result.status !== 0) {
    throw new Error(
      `docker image inspect failed for ${image}: ${result.stderr}`
    );
  }
  return JSON.parse(result.stdout);
}

for (const target of targets) {
  const config = inspect(target.image);
  const user = String(config.User ?? '');
  if (!user || user === '0' || user === 'root') {
    throw new Error(`${target.image} must run as non-root; Config.User='${user}'`);
  }

  const entrypoint = JSON.stringify(config.Entrypoint ?? []);
  if (entrypoint !== target.entrypoint) {
    throw new Error(
      `${target.image} Entrypoint mismatch: expected ${target.entrypoint}, got ${entrypoint}`
    );
  }

  const smoke = run('docker', [
    'run',
    '--rm',
    '--entrypoint',
    'dotnet',
    target.image,
    '--info',
  ]);
  if (smoke.status !== 0) {
    throw new Error(
      `${target.image} runtime smoke (dotnet --info) failed: ${smoke.stderr}`
    );
  }
  if (!String(smoke.stdout).includes('.NET')) {
    throw new Error(`${target.image} smoke output missing .NET banner`);
  }

  process.stdout.write(
    `OK ${target.image} user=${user} digest-check=local entrypoint=${entrypoint}\n`
  );
}

process.stdout.write('Container image contracts passed.\n');
