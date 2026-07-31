import { spawn } from 'node:child_process';
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const repositoryRoot = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)),
  '../..'
);
const matrixPath = path.join(
  repositoryRoot,
  'eng',
  'testing',
  'test-matrix.json'
);

export function loadTestMatrix() {
  return JSON.parse(readFileSync(matrixPath, 'utf8'));
}

export function argumentsForSuite(suiteName) {
  const suite = loadTestMatrix().dotnetSuites[suiteName];
  if (!suite) {
    throw new Error(`未知测试套件“${suiteName}”。`);
  }

  return [
    suite.assembly,
    '--no-ansi',
    '--progress',
    'off',
    '--minimum-expected-tests',
    String(suite.minimum),
    '--timeout',
    suite.timeout
  ];
}

export function commandsForSuite(suiteName, { noBuild = false } = {}) {
  const matrix = loadTestMatrix();
  const suite = matrix.dotnetSuites[suiteName];
  if (!suite) {
    throw new Error(`未知测试套件“${suiteName}”。`);
  }

  const commands = [];
  if (!noBuild) {
    commands.push({
      command: 'dotnet',
      args: [
        'build',
        suite.project,
        '--configuration',
        'Release',
        '--nologo'
      ]
    });
  }
  commands.push({
    command: 'dotnet',
    args: argumentsForSuite(suiteName)
  });
  return commands;
}

function runProcess(command, args) {
  return new Promise((resolve, reject) => {
    const child = spawn(command, args, {
      cwd: repositoryRoot,
      stdio: 'inherit',
      shell: false
    });
    child.on('error', reject);
    child.on('exit', (code, signal) => {
      if (signal) {
        reject(new Error(`测试命令被信号 ${signal} 终止。`));
        return;
      }
      if (code !== 0) {
        reject(new Error(`测试命令退出码为 ${code ?? 'unknown'}。`));
        return;
      }
      resolve();
    });
  });
}

async function run(args) {
  const suiteName = args[0] ?? '';
  const options = args.slice(1);
  if (options.some(option => option !== '--no-build')) {
    throw new Error('测试套件只支持 --no-build 参数。');
  }
  const noBuild = options.includes('--no-build');
  for (const entry of commandsForSuite(suiteName, { noBuild })) {
    await runProcess(entry.command, entry.args);
  }
}

if (
  process.argv[1]
  && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href
) {
  run(process.argv.slice(2)).catch(error => {
    process.stderr.write(`${error.message}\n`);
    process.exitCode = 1;
  });
}
