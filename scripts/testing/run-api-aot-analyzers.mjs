import { spawnSync } from 'node:child_process';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)),
  '../..'
);

const apiProject = path.join(
  repositoryRoot,
  'src',
  'Hosts',
  'Full.NET.Host.Api',
  'Full.NET.Host.Api.csproj'
);

const result = spawnSync(
  'dotnet',
  [
    'build',
    apiProject,
    '--configuration',
    'Release',
    '--nologo',
    '-p:FullNetAotAnalysis=true',
    '-clp:ErrorsOnly'
  ],
  {
    cwd: repositoryRoot,
    encoding: 'utf8',
    stdio: ['ignore', 'pipe', 'pipe']
  }
);

if (result.stdout) {
  process.stdout.write(result.stdout);
}

if (result.stderr) {
  process.stderr.write(result.stderr);
}

if (result.status !== 0) {
  process.exit(result.status ?? 1);
}

// AOT 分析会切换条件编译评估；恢复默认 JIT 还原图，避免后续 --no-restore 构建缺少 JIT 依赖。
const restoreResult = spawnSync(
  'dotnet',
  ['restore', apiProject, '--nologo'],
  {
    cwd: repositoryRoot,
    encoding: 'utf8',
    stdio: ['ignore', 'pipe', 'pipe']
  }
);

if (restoreResult.stdout) {
  process.stdout.write(restoreResult.stdout);
}

if (restoreResult.stderr) {
  process.stderr.write(restoreResult.stderr);
}

process.exit(restoreResult.status ?? 1);
