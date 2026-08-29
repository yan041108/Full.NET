import { spawnSync } from 'node:child_process';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)),
  '../..'
);

const workerProject = path.join(
  repositoryRoot,
  'src',
  'Hosts',
  'Full.NET.Host.Worker',
  'Full.NET.Host.Worker.csproj'
);

function run(command, args) {
  const result = spawnSync(command, args, {
    cwd: repositoryRoot,
    encoding: 'utf8',
    stdio: ['ignore', 'pipe', 'pipe'],
  });
  if (result.stdout) {
    process.stdout.write(result.stdout);
  }
  if (result.stderr) {
    process.stderr.write(result.stderr);
  }
  return result.status ?? 1;
}

const buildStatus = run('dotnet', [
  'build',
  workerProject,
  '--configuration',
  'Release',
  '--nologo',
  '-p:FullNetAotAnalysis=true',
  '-clp:ErrorsOnly',
]);

// AOT 与默认构建共享输出目录；restore 不会替换已生成的条件编译 DLL，
// 因此无论分析是否成功都要强制恢复 JIT 产物，避免污染后续架构测试。
const restoreStatus = run('dotnet', ['restore', workerProject, '--nologo']);
const rebuildStatus = restoreStatus === 0
  ? run('dotnet', [
      'build',
      workerProject,
      '--configuration',
      'Release',
      '--no-restore',
      '--nologo',
      '-t:Rebuild',
      '-clp:ErrorsOnly',
    ])
  : restoreStatus;

process.exit(buildStatus !== 0 ? buildStatus : rebuildStatus);
