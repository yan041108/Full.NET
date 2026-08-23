#!/usr/bin/env node
/**
 * 在 Linux 环境执行 Full.NET.Host.Api 完整闭包的 linux-x64 Native AOT publish。
 * Windows 开发机通过官方 SDK 容器执行真实链接，禁止以 Windows analyzer build 代替。
 */
import { spawnSync } from 'node:child_process';
import {
  existsSync,
  mkdirSync,
  readdirSync,
  statSync,
  writeFileSync,
} from 'node:fs';
import path from 'node:path';
import process from 'node:process';
import {
  apiNativeAotPublishContract,
  resolveRepositoryPath,
} from './api-native-aot-publish-contract.mjs';

const contract = apiNativeAotPublishContract;
const outputDir = resolveRepositoryPath(contract.outputRelativeDir);
const manifestPath = resolveRepositoryPath(contract.manifestRelativePath);
const projectPath = resolveRepositoryPath(contract.projectRelativePath);
const executablePath = path.join(outputDir, contract.executableName);

function run(command, args, options = {}) {
  const result = spawnSync(command, args, {
    encoding: 'utf8',
    stdio: ['ignore', 'pipe', 'pipe'],
    ...options,
  });
  return result;
}

function dockerAvailable() {
  const result = run('docker', ['version', '--format', '{{.Server.Version}}']);
  return result.status === 0 && result.stdout.trim().length > 0;
}

function shellQuote(value) {
  return `'${value.replace(/'/g, `'\\''`)}'`;
}

function buildPublishShellCommand() {
  const propertyArgs = Object.entries(contract.publishMsBuildProperties)
    .map(([key, value]) => `-p:${key}=${value}`)
    .join(' ');

  return [
    'set -euo pipefail',
    'dotnet --info | head -n 20',
    `dotnet publish ${shellQuote(`/src/${contract.projectRelativePath.replace(/\\/g, '/')}`)}`,
    `-c ${contract.publishMsBuildProperties.Configuration}`,
    `-r ${contract.runtimeIdentifier}`,
    '--self-contained true',
    propertyArgs,
    `-o ${shellQuote(`/src/${contract.outputRelativeDir.replace(/\\/g, '/')}`)}`,
    '--nologo',
  ].join(' ');
}

function publishOnLinuxHost() {
  const propertyArgs = Object.entries(contract.publishMsBuildProperties).flatMap(
    ([key, value]) => ['-p', `${key}=${value}`]
  );

  return run(
    'dotnet',
    [
      'publish',
      projectPath,
      '-c',
      contract.publishMsBuildProperties.Configuration,
      '-r',
      contract.runtimeIdentifier,
      '--self-contained',
      'true',
      ...propertyArgs,
      '-o',
      outputDir,
      '--nologo',
    ],
    { cwd: contract.repositoryRoot }
  );
}

function publishViaDocker() {
  const repoMount = contract.repositoryRoot.replace(/\\/g, '/');
  if (process.platform === 'win32' && /^[a-zA-Z]:/.test(repoMount)) {
    const drive = repoMount[0].toLowerCase();
    const tail = repoMount.slice(2);
    const dockerRepo = `/${drive}${tail}`;
    return run('docker', [
      'run',
      '--rm',
      '-v',
      `${dockerRepo}:/src`,
      '-w',
      '/src',
      contract.sdkImage,
      'bash',
      '-lc',
      buildPublishShellCommand(),
    ]);
  }

  return run('docker', [
    'run',
    '--rm',
    '-v',
    `${repoMount}:/src`,
    '-w',
    '/src',
    contract.sdkImage,
    'bash',
    '-lc',
    buildPublishShellCommand(),
  ]);
}

function ensureOutputDirectory() {
  mkdirSync(path.dirname(outputDir), { recursive: true });
}

function verifyArtifact() {
  if (!existsSync(executablePath)) {
    const listing = existsSync(outputDir)
      ? readdirSync(outputDir).join(', ')
      : '<missing output directory>';
    console.error(
      `Native AOT publish 未生成 Linux 可执行文件：${executablePath}\n` +
        `输出目录内容：${listing}`
    );
    process.exit(1);
  }

  const executableStat = statSync(executablePath);
  if (!executableStat.isFile()) {
    console.error(`产物路径不是文件：${executablePath}`);
    process.exit(1);
  }

  if (executableStat.size < contract.minimumExecutableBytes) {
    console.error(
      `Linux 原生可执行文件过小（${executableStat.size} < ${contract.minimumExecutableBytes}）：${executablePath}`
    );
    process.exit(1);
  }

  return executableStat;
}

function writeManifest(executableStat, publishDurationMs, publishMode) {
  const manifest = {
    generatedAtUtc: new Date().toISOString(),
    runtimeIdentifier: contract.runtimeIdentifier,
    publishMode,
    sdkImage: publishMode === 'docker' ? contract.sdkImage : null,
    sdkImageLabel: contract.sdkImageLabel,
    outputRelativeDir: contract.outputRelativeDir,
    executableRelativePath: path
      .join(contract.outputRelativeDir, contract.executableName)
      .replace(/\\/g, '/'),
    executableBytes: executableStat.size,
    publishDurationMs,
    msBuildProperties: contract.publishMsBuildProperties,
    note:
      'publishDurationMs 仅记录单次执行耗时，不得作为性能或容量结论。',
  };

  mkdirSync(path.dirname(manifestPath), { recursive: true });
  writeFileSync(manifestPath, `${JSON.stringify(manifest, null, 2)}\n`, 'utf8');
  console.log(`Publish manifest: ${manifestPath}`);
  console.log(
    `Artifact: ${executablePath} (${executableStat.size} bytes, ${publishDurationMs} ms)`
  );
}

function main() {
  ensureOutputDirectory();
  const startedAt = Date.now();
  let publishMode;
  let result;

  if (process.platform === 'linux') {
    publishMode = 'linux-host';
    console.log('Publishing Native AOT on Linux host SDK...');
    result = publishOnLinuxHost();
  } else if (dockerAvailable()) {
    publishMode = 'docker';
    console.log(
      `Publishing Native AOT via ${contract.sdkImage} (repository mounted at /src)...`
    );
    result = publishViaDocker();
  } else {
    console.error(
      '当前平台不是 Linux 且 Docker 不可用；无法执行 linux-x64 Native AOT publish。'
    );
    process.exit(1);
  }

  if (result.stdout) {
    process.stdout.write(result.stdout);
  }

  if (result.stderr) {
    process.stderr.write(result.stderr);
  }

  if (result.status !== 0) {
    process.exit(result.status ?? 1);
  }

  const executableStat = verifyArtifact();
  writeManifest(executableStat, Date.now() - startedAt, publishMode);
}

main();
