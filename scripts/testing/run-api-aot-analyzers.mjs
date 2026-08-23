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

process.exit(result.status ?? 1);
