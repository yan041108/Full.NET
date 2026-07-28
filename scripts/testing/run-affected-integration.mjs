import { execFile, spawn } from 'node:child_process';
import { readdir } from 'node:fs/promises';
import path from 'node:path';
import { promisify } from 'node:util';
import { pathToFileURL } from 'node:url';

import { argumentsFor } from './run-integration-shard.mjs';

const execFileAsync = promisify(execFile);
const assembly = path.join(
  'tests',
  'Full.NET.IntegrationTests',
  'bin',
  'Release',
  'net10.0',
  'Full.NET.IntegrationTests.dll'
);

const focusedModules = new Set([
  'Auditing',
  'Files',
  'Jobs',
  'Notifications',
  'Organization',
  'Settings'
]);

const noIntegrationPrefixes = [
  '.agents/',
  '.github/',
  'docs/',
  'rules/',
  'ui/',
  'apps/',
  'clients/',
  'tests/Full.NET.ArchitectureTests/',
  'tests/Full.NET.CompatibilityTests/',
  'tests/Full.NET.UnitTests/'
];

const noIntegrationFiles = new Set([
  'AGENTS.md',
  'README.md'
]);

const localNoisePrefixes = [
  '.cache/',
  '.tmp/',
  'BenchmarkDotNet.Artifacts/'
];

function normalizePath(filePath) {
  return filePath.replaceAll('\\', '/').replace(/^\.\/+/, '');
}

function moduleFromSourcePath(filePath) {
  const match = /^src\/Modules\/Full\.NET\.Modules\.([^/]+)/.exec(filePath);
  return match?.[1]?.split('.')[0] ?? null;
}

function moduleFromIntegrationPath(filePath) {
  const apiMatch =
    /^tests\/Full\.NET\.IntegrationTests\/Api\/([A-Za-z]+)Api(?:MySql|SqlServer)Tests\.cs$/
      .exec(filePath);
  if (apiMatch) {
    return apiMatch[1];
  }

  const assertionMatch =
    /^tests\/Full\.NET\.IntegrationTests\/([A-Za-z]+)\//.exec(filePath);
  return assertionMatch?.[1] ?? null;
}

function filterTarget(name, filter = `FullyQualifiedName~${name}Api`) {
  return { kind: 'filter', name, filter };
}

function addTarget(targets, target) {
  targets.set(`${target.kind}:${target.name}`, target);
}

function addModuleTarget(targets, moduleName) {
  if (moduleName === 'Identity' || moduleName === 'Tenancy') {
    addTarget(
      targets,
      filterTarget(moduleName, `FullyQualifiedName~${moduleName}`)
    );
    return;
  }
  addTarget(targets, filterTarget(moduleName));
}

function classifyBuildingBlock(filePath, targets) {
  if (/Full\.NET\.Migrations|\/Migrations\./.test(filePath)) {
    addTarget(targets, { kind: 'shard', name: 'migrations' });
    return '迁移基础设施';
  }
  if (/Outbox/i.test(filePath)) {
    addTarget(
      targets,
      filterTarget('Outbox', 'FullyQualifiedName~Outbox')
    );
    return 'Outbox 基础设施';
  }
  if (/Full\.NET\.Caching/.test(filePath)) {
    addTarget(
      targets,
      filterTarget('Caching', 'FullyQualifiedName~Caching')
    );
    return '缓存基础设施';
  }
  if (/Full\.NET\.Realtime/.test(filePath)) {
    addTarget(
      targets,
      filterTarget('Realtime', 'FullyQualifiedName~Realtime')
    );
    return '实时基础设施';
  }
  if (/Full\.NET\.Seeding/.test(filePath)) {
    addTarget(
      targets,
      filterTarget('Seeding', 'FullyQualifiedName~Seeding')
    );
    return '播种基础设施';
  }
  addTarget(targets, { kind: 'shard', name: 'smoke' });
  return '共享 BuildingBlock';
}

function classifyIntegrationPath(filePath, targets) {
  if (filePath.startsWith('tests/Full.NET.IntegrationTests/Migrations/')) {
    addTarget(targets, { kind: 'shard', name: 'migrations' });
    return '迁移 Integration';
  }
  if (
    filePath === 'tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj'
    || filePath === 'tests/Full.NET.IntegrationTests/MSTestSettings.cs'
    || filePath === 'tests/Full.NET.IntegrationTests/SharedDatabaseFixture.cs'
    || filePath === 'tests/Full.NET.IntegrationTests/Api/FullNetApiFactory.cs'
  ) {
    addTarget(targets, { kind: 'shard', name: 'smoke' });
    return 'Integration 共享夹具';
  }

  const moduleName = moduleFromIntegrationPath(filePath);
  if (moduleName === 'Messaging') {
    addTarget(
      targets,
      filterTarget('Outbox', 'FullyQualifiedName~OutboxRecoveryTests')
    );
    return 'Outbox Integration';
  }
  if (
    focusedModules.has(moduleName)
    || moduleName === 'Identity'
    || moduleName === 'Tenancy'
  ) {
    addModuleTarget(targets, moduleName);
    return `模块 Integration：${moduleName}`;
  }
  if (['Caching', 'Realtime', 'Seeding', 'Data'].includes(moduleName)) {
    addTarget(
      targets,
      filterTarget(moduleName, `FullyQualifiedName~${moduleName}`)
    );
    return `基础设施 Integration：${moduleName}`;
  }

  addTarget(targets, { kind: 'shard', name: 'smoke' });
  return '未登记 Integration 路径';
}

export function classifyChangedPaths(paths) {
  const normalizedPaths = [...new Set(paths.map(normalizePath))]
    .filter(Boolean);
  const targets = new Map();
  const reasons = [];

  for (const filePath of normalizedPaths) {
    if (
      filePath === 'package.json'
      || filePath.startsWith('scripts/testing/')
      || filePath.startsWith('tests/testing/')
      || filePath === 'tests/governance/integration-test-feedback.test.mjs'
    ) {
      addTarget(targets, { kind: 'tooling', name: 'integration-tooling' });
      reasons.push(`Integration 工具链：${filePath}`);
      continue;
    }

    if (
      noIntegrationFiles.has(filePath)
      || noIntegrationPrefixes.some(prefix => filePath.startsWith(prefix))
    ) {
      continue;
    }

    const sourceModule = moduleFromSourcePath(filePath);
    if (sourceModule) {
      if (
        focusedModules.has(sourceModule)
        || sourceModule === 'Identity'
        || sourceModule === 'Tenancy'
      ) {
        addModuleTarget(targets, sourceModule);
        reasons.push(`后端模块：${sourceModule}`);
      } else {
        addTarget(targets, { kind: 'shard', name: 'smoke' });
        reasons.push(`未登记后端模块使用 Smoke：${sourceModule}`);
      }
      continue;
    }

    if (filePath.startsWith('src/BuildingBlocks/')) {
      reasons.push(`${classifyBuildingBlock(filePath, targets)}：${filePath}`);
      continue;
    }

    if (
      filePath.startsWith('src/Hosts/')
      || filePath.startsWith('src/Composition/')
    ) {
      if (/Outbox/i.test(filePath)) {
        addTarget(
          targets,
          filterTarget('Outbox', 'FullyQualifiedName~Outbox')
        );
        reasons.push(`Outbox 宿主：${filePath}`);
      } else {
        addTarget(targets, { kind: 'shard', name: 'smoke' });
        reasons.push(`共享宿主使用 Smoke：${filePath}`);
      }
      continue;
    }

    if (filePath.startsWith('tests/Full.NET.IntegrationTests/')) {
      reasons.push(`${classifyIntegrationPath(filePath, targets)}：${filePath}`);
      continue;
    }

    if (filePath.startsWith('src/')) {
      addTarget(targets, { kind: 'shard', name: 'smoke' });
      reasons.push(`未知服务端路径使用 Smoke：${filePath}`);
    }
  }

  const selectedTargets = [...targets.values()].sort((left, right) =>
    left.name.localeCompare(right.name)
  );
  if (selectedTargets.length === 0) {
    return {
      mode: 'none',
      targets: [],
      reasons: ['变更不影响 Integration']
    };
  }

  return {
    mode: selectedTargets.every(target => target.kind === 'tooling')
      ? 'tooling'
      : 'focused',
    targets: selectedTargets,
    reasons
  };
}

export function verifyFocusedDiscovery(tests) {
  if (tests.length === 0) {
    throw new Error('聚焦过滤器没有发现任何 Integration 测试。');
  }

  const identities = tests.map(test =>
    `${test.type?.typeName ?? ''} ${test.displayName ?? ''}`
  );
  if (!identities.some(identity => /sqlserver|sql_server/i.test(identity))) {
    throw new Error('聚焦测试缺少 SQL Server Provider 覆盖。');
  }
  if (!identities.some(identity => /mysql|my_sql/i.test(identity))) {
    throw new Error('聚焦测试缺少 MySQL Provider 覆盖。');
  }
}

export function parseArguments(args) {
  let baseRef = null;
  let planOnly = false;

  for (let index = 0; index < args.length; index += 1) {
    const argument = args[index];
    if (argument === '--plan') {
      planOnly = true;
      continue;
    }
    if (argument === '--base') {
      baseRef = args[index + 1] ?? null;
      index += 1;
      continue;
    }
    if (argument.startsWith('--base=')) {
      baseRef = argument.slice('--base='.length);
      continue;
    }
    throw new Error(`未知参数：${argument}`);
  }

  if (!baseRef) {
    throw new Error(
      '缺少 --base <task-base-sha>；任务开始时先运行 git rev-parse HEAD 记录基线。'
    );
  }

  return { baseRef, planOnly };
}

function lines(value) {
  return value
    .split(/\r?\n/)
    .map(item => normalizePath(item.trim()))
    .filter(Boolean);
}

async function git(args, cwd) {
  const { stdout } = await execFileAsync('git', args, {
    cwd,
    encoding: 'utf8',
    maxBuffer: 10 * 1024 * 1024
  });
  return stdout;
}

export async function collectChangedPaths({
  baseRef,
  cwd = process.cwd()
}) {
  await git(['rev-parse', '--verify', `${baseRef}^{commit}`], cwd);
  const mergeBase = (
    await git(['merge-base', baseRef, 'HEAD'], cwd)
  ).trim();
  const outputs = await Promise.all([
    git(
      ['diff', '--name-only', '--diff-filter=ACMRD', `${mergeBase}...HEAD`],
      cwd
    ),
    git(['diff', '--cached', '--name-only', '--diff-filter=ACMRD'], cwd),
    git(['diff', '--name-only', '--diff-filter=ACMRD'], cwd),
    git(['ls-files', '--others', '--exclude-standard'], cwd)
  ]);

  return [...new Set(outputs.flatMap(lines))]
    .filter(filePath =>
      !localNoisePrefixes.some(prefix => filePath.startsWith(prefix))
    )
    .sort();
}

export function argumentsForFocused(target, discoveredCount) {
  if (target.kind !== 'filter' || !target.name || !target.filter) {
    throw new Error('argumentsForFocused 只接受完整的 filter 目标。');
  }
  if (!Number.isInteger(discoveredCount) || discoveredCount <= 0) {
    throw new Error('聚焦测试发现数必须是正整数。');
  }

  return [
    assembly,
    '--no-ansi',
    '--progress',
    'off',
    '--filter',
    target.filter,
    '--minimum-expected-tests',
    String(discoveredCount),
    '--timeout',
    '30m',
    '--report-trx',
    '--report-trx-filename',
    `Full.NET.IntegrationTests-affected-${target.name.toLowerCase()}.trx`
  ];
}

function runProcess(command, args, cwd) {
  return new Promise((resolve, reject) => {
    const child = spawn(command, args, {
      cwd,
      stdio: 'inherit',
      shell: false
    });
    child.on('error', reject);
    child.on('exit', (code, signal) => {
      if (signal) {
        reject(new Error(`${command} 被信号 ${signal} 终止。`));
        return;
      }
      if (code !== 0) {
        reject(new Error(`${command} 退出码为 ${code ?? 'unknown'}。`));
        return;
      }
      resolve();
    });
  });
}

async function discover(filter, cwd) {
  const { stdout } = await execFileAsync(
    'dotnet',
    [
      assembly,
      '--list-tests',
      'json',
      '--filter',
      filter,
      '--no-ansi',
      '--progress',
      'off'
    ],
    {
      cwd,
      encoding: 'utf8',
      maxBuffer: 20 * 1024 * 1024
    }
  );
  return JSON.parse(stdout.trim()).tests;
}

function renderSelection(paths, selection, baseRef) {
  const output = [
    `Integration 任务基线：${baseRef}`,
    `变更文件：${paths.length}`,
    `本地模式：${selection.mode}`,
    `受影响目标：${selection.targets.map(target => target.name).join(', ') || 'none'}`
  ];
  for (const reason of selection.reasons) {
    output.push(`- ${reason}`);
  }
  output.push('完整 193 项仅由 main CI 并行分片执行。');
  return `${output.join('\n')}\n`;
}

async function runToolingTests(cwd) {
  const toolingDirectory = path.join(cwd, 'tests', 'testing');
  const toolingTests = (await readdir(toolingDirectory))
    .filter(fileName => fileName.endsWith('.test.mjs'))
    .map(fileName => path.join('tests', 'testing', fileName));
  await runProcess(
    process.execPath,
    [
      '--test',
      ...toolingTests,
      path.join('tests', 'governance', 'integration-test-feedback.test.mjs')
    ],
    cwd
  );
}

async function runCli(args, cwd = process.cwd()) {
  const { baseRef, planOnly } = parseArguments(args);
  const paths = await collectChangedPaths({ baseRef, cwd });
  if (paths.length === 0) {
    throw new Error(`任务基线 ${baseRef} 到当前工作区没有可验证变更。`);
  }

  const selection = classifyChangedPaths(paths);
  process.stdout.write(renderSelection(paths, selection, baseRef));
  if (planOnly || selection.mode === 'none') {
    return;
  }

  const toolingTargets = selection.targets.filter(
    target => target.kind === 'tooling'
  );
  if (toolingTargets.length > 0) {
    await runToolingTests(cwd);
  }

  const integrationTargets = selection.targets.filter(
    target => target.kind !== 'tooling'
  );
  if (integrationTargets.length === 0) {
    return;
  }

  await runProcess(
    'dotnet',
    [
      'build',
      'tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj',
      '--configuration',
      'Release',
      '--no-restore',
      '--nologo'
    ],
    cwd
  );

  for (const target of integrationTargets) {
    if (target.kind === 'shard') {
      if (target.name === 'full') {
        throw new Error('本地受影响测试选择器禁止执行 full。');
      }
      await runProcess('dotnet', argumentsFor(target.name), cwd);
      continue;
    }

    const tests = await discover(target.filter, cwd);
    verifyFocusedDiscovery(tests);
    process.stdout.write(
      `${target.name} 聚焦发现：${tests.length} 项，已确认双 Provider。\n`
    );
    await runProcess(
      'dotnet',
      argumentsForFocused(target, tests.length),
      cwd
    );
  }
}

if (
  process.argv[1]
  && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href
) {
  runCli(process.argv.slice(2)).catch(error => {
    process.stderr.write(`${error.message}\n`);
    process.exitCode = 1;
  });
}
