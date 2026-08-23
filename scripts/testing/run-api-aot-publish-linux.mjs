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
  const stdio = options.stdio ?? ['ignore', 'pipe', 'pipe'];
  const result = spawnSync(command, args, {
    encoding: stdio[1] === 'pipe' || stdio[2] === 'pipe' ? 'utf8' : undefined,
    stdio,
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

function ensureDockerSdkImage() {
  const inspect = run('docker', [
    'image',
    'inspect',
    contract.sdkImage,
    '--format',
    '{{.Id}}',
  ]);
  if (inspect.status === 0 && inspect.stdout.trim().length > 0) {
    return;
  }

  console.log(`Pulling ${contract.sdkImage}...`);
  const pull = run('docker', ['pull', contract.sdkImage]);
  if (pull.stdout) {
    process.stdout.write(pull.stdout);
  }
  if (pull.stderr) {
    process.stderr.write(pull.stderr);
  }
  if (pull.status !== 0) {
    console.error(`无法拉取 ${contract.sdkImage}；请检查 Docker 网络/代理后重试。`);
    process.exit(pull.status ?? 1);
  }
}

function buildPublishShellCommand() {
  const propertyArgs = Object.entries(contract.publishMsBuildProperties)
    .map(([key, value]) => `-p:${key}=${value}`)
    .join(' ');

  const project = shellQuote(
    `/src/${contract.projectRelativePath.replace(/\\/g, '/')}`
  );
  const output = shellQuote(
    `/src/${contract.outputRelativeDir.replace(/\\/g, '/')}`
  );
  const configuration = contract.publishMsBuildProperties.Configuration;

  // 必须用分号串联；空格拼接会让 head/dotnet publish 参数被 shell 误解析。
  return [
    'set -euo pipefail',
    'echo "SDK version: $(dotnet --version)"',
    `dotnet publish ${project} -c ${configuration} -r ${contract.runtimeIdentifier} --self-contained true ${propertyArgs} -o ${output} --nologo --no-restore`,
  ].join('; ');
}

function nugetPackagesVolumeMount() {
  const packagesRoot =
    process.env.NUGET_PACKAGES
    || path.join(
      process.env.USERPROFILE || process.env.HOME || '',
      '.nuget',
      'packages'
    );
  if (!existsSync(packagesRoot)) {
    return null;
  }

  const mountPath = packagesRoot.replace(/\\/g, '/');
  if (process.platform === 'win32' && /^[a-zA-Z]:/.test(mountPath)) {
    return `/${mountPath[0].toLowerCase()}${mountPath.slice(2)}:/root/.nuget/packages`;
  }

  return `${mountPath}:/root/.nuget/packages`;
}

function preRestoreOnHost() {
  console.log('Restoring Host.Api on host (linux-x64) for NuGet cache warm-up...');
  const propertyArgs = Object.entries(contract.publishMsBuildProperties).flatMap(
    ([key, value]) => ['-p', `${key}=${value}`]
  );
  const result = run(
    'dotnet',
    [
      'restore',
      projectPath,
      '-r',
      contract.runtimeIdentifier,
      ...propertyArgs,
      '--nologo',
    ],
    { cwd: contract.repositoryRoot, stdio: 'inherit' }
  );
  if (result.status !== 0) {
    console.error('Host 本机 restore 失败；容器内将继续尝试 restore。');
  }
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

function ensureDockerPublishImage() {
  const publishImage = contract.publishSdkImage;
  const inspect = run('docker', [
    'image',
    'inspect',
    publishImage,
    '--format',
    '{{.Id}}',
  ]);
  if (inspect.status === 0 && inspect.stdout.trim().length > 0) {
    return publishImage;
  }

  ensureDockerSdkImage();
  const dockerfile = resolveRepositoryPath(
    'eng/docker/Dockerfile.api-native-aot-linux-sdk'
  );
  const contextDir = path.dirname(dockerfile);
  console.log(
    `Building ${publishImage} with Native AOT linker prerequisites...`
  );
  const build = run(
    'docker',
    ['build', '-t', publishImage, '-f', dockerfile, contextDir],
    { stdio: 'inherit' }
  );
  if (build.status !== 0) {
    console.error(`无法构建 ${publishImage}。`);
    process.exit(build.status ?? 1);
  }

  return publishImage;
}

function publishViaDocker() {
  preRestoreOnHost();
  const publishImage = ensureDockerPublishImage();
  const repoMount = contract.repositoryRoot.replace(/\\/g, '/');
  const volumeMounts = [
    process.platform === 'win32' && /^[a-zA-Z]:/.test(repoMount)
      ? `/${repoMount[0].toLowerCase()}${repoMount.slice(2)}:/src`
      : `${repoMount}:/src`,
  ];
  const nugetMount = nugetPackagesVolumeMount();
  if (nugetMount) {
    volumeMounts.push(nugetMount);
  }

  const dockerArgs = [
    'run',
    '--rm',
    ...volumeMounts.flatMap((mount) => ['-v', mount]),
    '-w',
    '/src',
    publishImage,
    'bash',
    '-lc',
    buildPublishShellCommand(),
  ];

  const result = run('docker', dockerArgs, { stdio: 'inherit' });
  return {
    status: result.status,
    stdout: '',
    stderr: '',
  };
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
    sdkImage: publishMode === 'docker' ? contract.publishSdkImage : null,
    baseSdkImage: contract.sdkImage,
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
      `Publishing Native AOT via ${contract.publishSdkImage} (repository mounted at /src)...`
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
