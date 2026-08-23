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

export function parseSuiteOptions(options) {
  const parsed = {
    noBuild: false,
    selection: null,
    filter: null,
    minimumExpectedTests: null
  };

  for (let index = 0; index < options.length; index += 1) {
    const option = options[index];
    if (option === '--no-build') {
      parsed.noBuild = true;
      continue;
    }

    if (option === '--filter') {
      const value = options[index + 1];
      if (!value) {
        throw new Error('--filter 需要参数。');
      }
      parsed.filter = value;
      index += 1;
      continue;
    }

    if (option === '--selection') {
      const value = options[index + 1];
      if (!value) {
        throw new Error('--selection 需要参数。');
      }
      parsed.selection = value;
      index += 1;
      continue;
    }

    if (option === '--minimum-expected-tests') {
      const value = options[index + 1];
      if (!value) {
        throw new Error('--minimum-expected-tests 需要参数。');
      }
      const minimumExpectedTests = Number(value);
      if (!Number.isInteger(minimumExpectedTests) || minimumExpectedTests <= 0) {
        throw new Error('--minimum-expected-tests 必须是正整数。');
      }
      parsed.minimumExpectedTests = minimumExpectedTests;
      index += 1;
      continue;
    }

    throw new Error(
      `测试套件只支持 --no-build、--selection、--filter 与 --minimum-expected-tests，收到：${option}`
    );
  }

  if (parsed.selection
      && (parsed.filter || parsed.minimumExpectedTests !== null)) {
    throw new Error('--selection 不能与 --filter 或 --minimum-expected-tests 同时使用。');
  }
  if (parsed.filter && parsed.minimumExpectedTests === null) {
    throw new Error('--filter 必须同时提供 --minimum-expected-tests。');
  }
  if (!parsed.filter && parsed.minimumExpectedTests !== null) {
    throw new Error('--minimum-expected-tests 必须与 --filter 同时使用。');
  }

  return parsed;
}

export function argumentsForSuite(suiteName, suiteOptions = {}) {
  const suite = loadTestMatrix().dotnetSuites[suiteName];
  if (!suite) {
    throw new Error(`未知测试套件“${suiteName}”。`);
  }

  if (suiteOptions.selection
      && (suiteOptions.filter || suiteOptions.minimumExpectedTests != null)) {
    throw new Error('--selection 不能与原始聚焦参数同时使用。');
  }

  const selection = suiteOptions.selection
    ? suite.selections?.[suiteOptions.selection]
    : null;
  if (suiteOptions.selection && !selection) {
    throw new Error(
      `测试套件“${suiteName}”收到未知聚焦集“${suiteOptions.selection}”。`
    );
  }

  const filter = selection?.filter ?? suiteOptions.filter;
  const minimumExpectedTests = selection?.minimum
    ?? suiteOptions.minimumExpectedTests
    ?? suite.minimum;
  if (filter && !Number.isInteger(minimumExpectedTests)) {
    throw new Error('聚焦测试必须提供正整数最低发现数。');
  }

  const args = [
    suite.assembly,
    '--no-ansi',
    '--progress',
    'off',
    '--timeout',
    suite.timeout
  ];

  if (filter) {
    args.push('--filter', filter);
  }

  args.push('--minimum-expected-tests', String(minimumExpectedTests));

  return args;
}

export function commandsForSuite(suiteName, suiteOptions = {}) {
  const matrix = loadTestMatrix();
  const suite = matrix.dotnetSuites[suiteName];
  if (!suite) {
    throw new Error(`未知测试套件“${suiteName}”。`);
  }

  const commands = [];
  if (!suiteOptions.noBuild) {
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
    args: argumentsForSuite(suiteName, suiteOptions)
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
  const suiteOptions = parseSuiteOptions(args.slice(1));
  for (const entry of commandsForSuite(suiteName, suiteOptions)) {
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
