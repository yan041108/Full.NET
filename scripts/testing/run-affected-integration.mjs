import { execFile, spawn } from 'node:child_process';
import {
  mkdir,
  readFile,
  readdir,
  writeFile
} from 'node:fs/promises';
import path from 'node:path';
import { promisify } from 'node:util';
import { pathToFileURL } from 'node:url';

import { argumentsFor } from './run-integration-shard.mjs';
import { loadTestMatrix } from './run-dotnet-test-suite.mjs';

const execFileAsync = promisify(execFile);
const testMatrix = loadTestMatrix();
const assembly = testMatrix.integration.assembly;
const migrationSelections = testMatrix.integration.migrationSelections ?? {};
const smokeFilter = testMatrix.integration.shards.smoke.filter;
const identityFilter =
  'FullyQualifiedName~Full.NET.IntegrationTests.Api.IdentityApi'
  + '|FullyQualifiedName~Full.NET.IntegrationTests.Identity.TotpStrongReauthTests';
const tenancyFilter =
  'FullyQualifiedName~Full.NET.IntegrationTests.Api.TenancyApi';
const codeGenerationFilter =
  'FullyQualifiedName~Full.NET.IntegrationTests.Api.CodeGenerationApi'
  + '|FullyQualifiedName~Full.NET.IntegrationTests.CodeGeneration.';
const outboxFilter =
  'FullyQualifiedName~Full.NET.IntegrationTests.Messaging.MessagingOutbox'
  + '|FullyQualifiedName~Full.NET.IntegrationTests.Messaging.OutboxRecoveryTests';
const mergeDeferredShardNames = new Set(['messaging-heavy']);

const focusedModules = new Set([
  'Auditing',
  'CodeGeneration',
  'Document',
  'Files',
  'Jobs',
  'Notifications',
  'Organization',
  'SerialNumbers',
  'Settings'
]);

const integrationModules = [
  ...focusedModules,
  'Identity',
  'Tenancy',
  'CodeGeneration',
  'Caching',
  'Realtime',
  'Seeding',
  'Data'
].sort((left, right) => right.length - left.length);

const noIntegrationPrefixes = [
  '.agents/',
  '.github/',
  'benchmarks/',
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

const phases = new Set(['inner', 'slice', 'merge']);
const immediateTargetNames = new Set([
  'Caching',
  'CodeGeneration',
  'Data',
  'Identity',
  'Outbox',
  'Realtime',
  'Seeding',
  'Tenancy',
  'integration-matrix',
  'integration-tooling',
  'migrations',
  'smoke'
]);
const estimatedTargetSeconds =
  testMatrix.integration.feedback.targetEstimatedSeconds;
const defaultFocusedEstimateSeconds =
  testMatrix.integration.feedback.defaultFocusedEstimateSeconds;
const sliceBudgetSeconds =
  testMatrix.integration.feedback.sliceBudgetSeconds;
const snapshotHashConcurrency = 8;

function normalizePath(filePath) {
  return filePath.replaceAll('\\', '/').replace(/^\.\/+/, '');
}

function isLocalNoise(filePath) {
  return localNoisePrefixes.some(prefix => filePath.startsWith(prefix))
    || /^src\/Hosts\/[^/]+\/App_Data(?:\/|$)/.test(filePath);
}

function moduleFromSourcePath(filePath) {
  const match = /^src\/Modules\/Full\.NET\.Modules\.([^/]+)/.exec(filePath);
  return match?.[1]?.split('.')[0] ?? null;
}

function moduleFromIntegrationPath(filePath) {
  if (filePath ===
      'tests/Full.NET.IntegrationTests/Api/OpenApiHostUsersContractAssertions.cs') {
    return 'Identity';
  }

  const apiMatch =
    /^tests\/Full\.NET\.IntegrationTests\/Api\/([A-Za-z]+)Api(?:MySql|SqlServer)Tests\.cs$/
      .exec(filePath);
  if (apiMatch) {
    return apiMatch[1];
  }

  if (filePath.startsWith('tests/Full.NET.IntegrationTests/Api/')) {
    const fileName = path.posix.basename(filePath);
    const moduleName = integrationModules.find(name =>
      fileName.includes(name)
    );
    if (moduleName) {
      return moduleName;
    }
  }

  const assertionMatch =
    /^tests\/Full\.NET\.IntegrationTests\/([A-Za-z]+)\//.exec(filePath);
  return assertionMatch?.[1] ?? null;
}

function filterTarget(name, filter = `FullyQualifiedName~${name}Api`) {
  return { kind: 'filter', name, filter };
}

function addMessagingHeavyTarget(targets) {
  addTarget(targets, { kind: 'shard', name: 'messaging-heavy' });
}

function isMessagingHeavyIntegrationPath(filePath) {
  return /KafkaCapacity|KafkaOutbox|CdcDebezium|EventDelivery|KafkaReplay|KafkaFailure|KafkaSubscription|BinlogShadow|CdcShadow|CdcCrash/i
    .test(path.posix.basename(filePath));
}

function classifyMessagingPath(filePath, targets) {
  if (filePath.startsWith('deploy/messaging/')) {
    addMessagingHeavyTarget(targets);
    return 'Messaging 部署模板';
  }
  if (filePath.startsWith('tests/performance/kafka-capacity')) {
    addMessagingHeavyTarget(targets);
    return 'Kafka 容量 workflow 契约';
  }
  return null;
}

function addTarget(targets, target) {
  targets.set(`${target.kind}:${target.name}`, target);
}

function addModuleTarget(targets, moduleName) {
  if (moduleName === 'Identity') {
    addTarget(targets, filterTarget('Identity', identityFilter));
    return;
  }
  if (moduleName === 'Tenancy') {
    addTarget(targets, filterTarget('Tenancy', tenancyFilter));
    return;
  }
  if (moduleName === 'CodeGeneration') {
    addTarget(targets, filterTarget('CodeGeneration', codeGenerationFilter));
    return;
  }
  addTarget(targets, filterTarget(moduleName));
}

function applyInnerProviderFilter(filter) {
  return `(${filter})&FullyQualifiedName~MySql`;
}

function narrowToInnerProvider(target) {
  if (target.kind === 'shard' && target.name === 'smoke') {
    return filterTarget('smoke', applyInnerProviderFilter(smokeFilter));
  }

  if (target.kind === 'filter' && target.filter) {
    return {
      ...target,
      filter: applyInnerProviderFilter(target.filter)
    };
  }

  return target;
}

function migrationDetails(filePath) {
  const match =
    /^src\/BuildingBlocks\/Full\.NET\.Migrations\.DbUp\/Migrations\/(?:MySql|SqlServer)\/(\d+)_([A-Za-z0-9]+)\.sql$/i
      .exec(filePath);
  if (!match) {
    return null;
  }

  const [, number, name] = match;
  return {
    moduleName: integrationModules.find(module => name.startsWith(module))
      ?? null,
    number
  };
}

function classifyBuildingBlock(filePath, targets) {
  const migration = migrationDetails(filePath);
  if (migration) {
    const registered = migrationSelections[migration.number];
    if (registered?.filter) {
      addTarget(
        targets,
        filterTarget(`migration-${migration.number}`, registered.filter)
      );
    } else {
      addTarget(targets, { kind: 'shard', name: 'migrations' });
    }
    if (migration.moduleName) {
      addModuleTarget(targets, migration.moduleName);
    }
    return registered
      ? `已登记迁移恢复集 ${migration.number}`
      : `未登记迁移 ${migration.number} 安全降级`;
  }
  if (/Full\.NET\.Migrations|\/Migrations\./.test(filePath)) {
    addTarget(targets, { kind: 'shard', name: 'migrations' });
    return '迁移基础设施';
  }
  if (/Outbox/i.test(filePath)) {
    addTarget(
      targets,
      filterTarget('Outbox', outboxFilter)
    );
    return 'Outbox 基础设施';
  }
  if (/Full\.NET\.Messaging\.Kafka/.test(filePath)) {
    addTarget(
      targets,
      filterTarget('Outbox', outboxFilter)
    );
    addMessagingHeavyTarget(targets);
    return 'Kafka 消息基础设施';
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
  if (/Full\.NET\.Data\.CodeGeneration/.test(filePath)) {
    addTarget(
      targets,
      filterTarget('CodeGeneration', codeGenerationFilter)
    );
    return '代码生成基础设施';
  }
  addTarget(targets, { kind: 'shard', name: 'smoke' });
  return '共享 BuildingBlock';
}

function classifyIntegrationPath(filePath, targets) {
  const migrationRecovery = filePath.match(
    /^tests\/Full\.NET\.IntegrationTests\/Migrations\/Migration(\d{3})[A-Za-z0-9]*Tests\.cs$/i
  );
  if (migrationRecovery) {
    const number = migrationRecovery[1];
    const registered = migrationSelections[number];
    if (registered?.filter) {
      addTarget(
        targets,
        filterTarget(`migration-${number}`, registered.filter)
      );
      return `迁移 ${number} 聚焦恢复 Integration`;
    }
  }
  if (filePath.startsWith('tests/Full.NET.IntegrationTests/Migrations/')) {
    addTarget(targets, { kind: 'shard', name: 'migrations' });
    return '迁移 Integration';
  }
  if (
    filePath === 'tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj'
    || filePath === 'tests/Full.NET.IntegrationTests/MSTestSettings.cs'
    || filePath === 'tests/Full.NET.IntegrationTests/SharedDatabaseFixture.cs'
    || filePath === 'tests/Full.NET.IntegrationTests/ApiSchemaTemplate.cs'
    || filePath === 'tests/Full.NET.IntegrationTests/Api/FullNetApiFactory.cs'
  ) {
    addTarget(targets, { kind: 'shard', name: 'smoke' });
    return 'Integration 共享夹具';
  }

  if (filePath ===
      'tests/Full.NET.IntegrationTests/Api/OpenApiPilotContractAssertions.cs') {
    for (const moduleName of ['Identity', 'Files', 'Settings']) {
      addModuleTarget(targets, moduleName);
    }
    return '客户端生成试点共享 OpenAPI 断言';
  }

  const moduleName = moduleFromIntegrationPath(filePath);
  if (moduleName === 'Messaging') {
    if (isMessagingHeavyIntegrationPath(filePath)) {
      addMessagingHeavyTarget(targets);
      return 'Messaging 重测 Integration';
    }
    if (!filePath.endsWith('.cs')) {
      return 'Messaging 非测试资产';
    }
    addTarget(
      targets,
      filterTarget('Outbox', outboxFilter)
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
  if (
    ['Caching', 'CodeGeneration', 'Realtime', 'Seeding', 'Data']
      .includes(moduleName)
  ) {
    if (moduleName === 'CodeGeneration') {
      addModuleTarget(targets, moduleName);
    } else {
      addTarget(
        targets,
        filterTarget(moduleName, `FullyQualifiedName~${moduleName}`)
      );
    }
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
    if (isLocalNoise(filePath)) {
      continue;
    }

    const messagingReason = classifyMessagingPath(filePath, targets);
    if (messagingReason) {
      reasons.push(`${messagingReason}：${filePath}`);
      continue;
    }

    if (
      filePath === 'package.json'
      || filePath === 'eng/testing/test-matrix.json'
      || filePath.startsWith('scripts/testing/')
      || filePath.startsWith('tests/testing/')
      || filePath === 'tests/governance/integration-test-feedback.test.mjs'
    ) {
      addTarget(targets, {
        kind: 'tooling',
        name: filePath === 'eng/testing/test-matrix.json'
          ? 'integration-matrix'
          : 'integration-tooling'
      });
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

    if (filePath.startsWith('src/Tools/Full.NET.CodeGeneration.Cli/')) {
      addTarget(
        targets,
        filterTarget('CodeGeneration', codeGenerationFilter)
      );
      reasons.push(`代码生成 CLI：${filePath}`);
      continue;
    }

    if (
      filePath.startsWith('src/Hosts/')
      || filePath.startsWith('src/Composition/')
    ) {
      if (/Outbox/i.test(filePath)) {
        addTarget(
          targets,
          filterTarget('Outbox', outboxFilter)
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

export function verifyFocusedDiscovery(tests, { phase = 'slice' } = {}) {
  if (tests.length === 0) {
    throw new Error('聚焦过滤器没有发现任何 Integration 测试。');
  }

  const identities = tests.map(test =>
    `${test.type?.typeName ?? ''} ${test.displayName ?? ''}`
  );
  const requireSqlServer = phase !== 'inner';
  if (
    requireSqlServer
    && !identities.some(identity => /sqlserver|sql_server/i.test(identity))
  ) {
    throw new Error('聚焦测试缺少 SQL Server Provider 覆盖。');
  }
  if (!identities.some(identity => /mysql|my_sql/i.test(identity))) {
    throw new Error('聚焦测试缺少 MySQL Provider 覆盖。');
  }
}

export function targetsForPhase(
  targets,
  phase,
  { includeHeavy = false } = {}
) {
  if (!phases.has(phase)) {
    throw new Error('验证阶段只支持 inner、slice 或 merge。');
  }

  if (phase === 'slice') {
    return [...targets];
  }
  if (phase === 'inner') {
    return targets
      .filter(target =>
        immediateTargetNames.has(target.name)
        || target.name.startsWith('migration-')
      )
      .map(narrowToInnerProvider);
  }

  const selected = new Map();
  for (const target of targets) {
    if (
      !includeHeavy
      && target.kind === 'shard'
      && mergeDeferredShardNames.has(target.name)
    ) {
      continue;
    }
    const mergeTarget = target.kind === 'shard' && target.name === 'smoke'
      ? filterTarget('smoke', smokeFilter)
      : target;
    addTarget(selected, mergeTarget);
  }
  if ([...selected.values()].some(target => target.kind !== 'tooling')) {
    addTarget(selected, filterTarget('smoke', smokeFilter));
  }
  return [...selected.values()];
}

export function estimateSelectionSeconds(targets) {
  const names = [...new Set(targets.map(target => target.name))];
  const seconds = names.reduce(
    (total, name) =>
      total
      + (estimatedTargetSeconds[name] ?? defaultFocusedEstimateSeconds),
    0
  );
  return {
    seconds,
    exceedsSliceBudget: seconds > sliceBudgetSeconds
  };
}

function focusedTimeoutMinutes(discoveredCount) {
  // 双 worker 下按每用例约一分钟估算；30 分钟下限会截断 Identity 这类 100+ 聚焦集。
  return Math.max(30, Math.ceil(discoveredCount / 2));
}

function testIdentity(test) {
  return test.uid
    ?? [
      test.type?.namespace,
      test.type?.typeName,
      test.type?.methodName,
      test.displayName
    ].filter(Boolean).join('.');
}

export function combineFilterTargets(targets, discoveries, { phase = 'slice' } = {}) {
  const filterTargets = targets.filter(target => target.kind === 'filter');
  if (filterTargets.length === 0) {
    throw new Error('合并执行至少需要一个 filter 目标。');
  }

  const uniqueTests = new Map();
  for (const target of filterTargets) {
    const tests = discoveries.get(target.name) ?? [];
    verifyFocusedDiscovery(tests, { phase });
    for (const test of tests) {
      uniqueTests.set(testIdentity(test), test);
    }
  }

  return {
    filter: filterTargets.map(target => target.filter).join('|'),
    discoveredCount: uniqueTests.size,
    targetNames: filterTargets.map(target => target.name),
    tests: [...uniqueTests.values()]
  };
}

export function toolingVerificationScopes(targets) {
  const targetNames = new Set(targets.map(target => target.name));
  return targetNames.has('integration-matrix')
    ? ['tooling', 'partitions', 'governance']
    : ['tooling'];
}

export function parseArguments(args) {
  let baseRef = null;
  let snapshotId = null;
  let phase = 'slice';
  let planOnly = false;
  let includeHeavy = false;

  for (let index = 0; index < args.length; index += 1) {
    const argument = args[index];
    if (argument === '--plan') {
      planOnly = true;
      continue;
    }
    if (argument === '--include-heavy') {
      includeHeavy = true;
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
    if (argument === '--snapshot') {
      snapshotId = args[index + 1] ?? null;
      index += 1;
      continue;
    }
    if (argument.startsWith('--snapshot=')) {
      snapshotId = argument.slice('--snapshot='.length);
      continue;
    }
    if (argument === '--phase') {
      phase = args[index + 1] ?? '';
      index += 1;
      continue;
    }
    if (argument.startsWith('--phase=')) {
      phase = argument.slice('--phase='.length);
      continue;
    }
    throw new Error(`未知参数：${argument}`);
  }

  if (baseRef && snapshotId) {
    throw new Error('--base 与 --snapshot 只能选择一种任务边界。');
  }
  if (!baseRef && !snapshotId) {
    throw new Error(
      '缺少 --base <task-base-sha> 或 --snapshot <task-id>；任务开始时先记录任务边界。'
    );
  }
  if (!phases.has(phase)) {
    throw new Error('--phase 只支持 inner、slice 或 merge。');
  }

  return { baseRef, phase, planOnly, snapshotId, includeHeavy };
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

async function collectDirtyPaths(cwd) {
  const outputs = await Promise.all([
    git(['diff', '--cached', '--name-only', '--diff-filter=ACMRD'], cwd),
    git(['diff', '--name-only', '--diff-filter=ACMRD'], cwd),
    git(['ls-files', '--others', '--exclude-standard'], cwd)
  ]);
  return [...new Set(outputs.flatMap(lines))]
    .filter(filePath => !isLocalNoise(filePath))
    .sort();
}

async function worktreeHash(cwd, filePath) {
  try {
    return (
      await git(['hash-object', '--', filePath], cwd)
    ).trim() || null;
  } catch (error) {
    if (
      error.code === 'ENOENT'
      || /could not open|does not exist|outside repository/i.test(
        `${error.stderr ?? ''} ${error.message}`
      )
    ) {
      return null;
    }
    throw error;
  }
}

async function indexHashes(cwd, filePath) {
  const output = await git(['ls-files', '--stage', '--', filePath], cwd);
  return [...new Set(
    output
      .split(/\r?\n/)
      .filter(Boolean)
      .map(line => line.split(/\s+/)[1])
      .filter(Boolean)
  )].sort();
}

async function contentState(cwd, filePath) {
  const [worktreeHashValue, indexHashValues] = await Promise.all([
    worktreeHash(cwd, filePath),
    indexHashes(cwd, filePath)
  ]);
  return {
    worktreeHash: worktreeHashValue,
    indexHashes: indexHashValues
  };
}

export async function mapWithConcurrency(items, concurrency, mapper) {
  if (!Number.isInteger(concurrency) || concurrency <= 0) {
    throw new Error('并发数必须是正整数。');
  }

  const results = new Array(items.length);
  let nextIndex = 0;
  async function runWorker() {
    while (nextIndex < items.length) {
      const index = nextIndex;
      nextIndex += 1;
      results[index] = await mapper(items[index], index);
    }
  }

  const workerCount = Math.min(concurrency, items.length);
  await Promise.all(
    Array.from({ length: workerCount }, () => runWorker())
  );
  return results;
}

async function taskSnapshotDirectory(cwd) {
  const gitDirectory = (
    await git(['rev-parse', '--path-format=absolute', '--git-dir'], cwd)
  ).trim();
  return path.join(gitDirectory, 'fullnet-task-snapshots');
}

function validateSnapshotId(id) {
  if (!id || !/^[A-Za-z0-9._-]+$/.test(id)) {
    throw new Error('任务快照 ID 只能包含字母、数字、点、下划线和连字符。');
  }
  return id;
}

async function readTaskSnapshot({ cwd, id }) {
  const snapshotId = validateSnapshotId(id);
  const directory = await taskSnapshotDirectory(cwd);
  const content = await readFile(
    path.join(directory, `${snapshotId}.json`),
    'utf8'
  );
  return JSON.parse(content);
}

export async function createTaskSnapshot({
  cwd = process.cwd(),
  id = `task-${Date.now()}-${process.pid}`
} = {}) {
  const snapshotId = validateSnapshotId(id);
  const baseRef = (await git(['rev-parse', 'HEAD'], cwd)).trim();
  const dirtyPaths = await collectDirtyPaths(cwd);
  const files = Object.fromEntries(
    await mapWithConcurrency(
      dirtyPaths,
      snapshotHashConcurrency,
      async filePath => [
        filePath,
        await contentState(cwd, filePath)
      ]
    )
  );
  const snapshot = {
    schemaVersion: 2,
    id: snapshotId,
    baseRef,
    createdAt: new Date().toISOString(),
    files
  };
  const directory = await taskSnapshotDirectory(cwd);
  await mkdir(directory, { recursive: true });
  try {
    await writeFile(
      path.join(directory, `${snapshotId}.json`),
      `${JSON.stringify(snapshot, null, 2)}\n`,
      { encoding: 'utf8', flag: 'wx' }
    );
  } catch (error) {
    if (error.code === 'EEXIST') {
      throw new Error(
        `任务快照“${snapshotId}”已存在；请复用原边界或选择新 ID。`
      );
    }
    throw error;
  }
  return snapshot;
}

export async function collectChangedPaths({
  baseRef,
  snapshotId,
  cwd = process.cwd()
}) {
  let snapshot = null;
  if (snapshotId) {
    snapshot = await readTaskSnapshot({ cwd, id: snapshotId });
    if (snapshot.schemaVersion !== 2) {
      throw new Error(
        `任务快照“${snapshotId}”版本过旧；请使用新 ID 重新创建。`
      );
    }
    baseRef = snapshot.baseRef;
  }
  if (!baseRef) {
    throw new Error('收集变更时必须提供 baseRef 或 snapshotId。');
  }

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

  const candidates = [...new Set(outputs.flatMap(lines))]
    .filter(filePath => !isLocalNoise(filePath))
    .sort();
  if (!snapshot) {
    return candidates;
  }

  const changed = await mapWithConcurrency(
    candidates,
    snapshotHashConcurrency,
    async filePath => {
      if (!Object.hasOwn(snapshot.files, filePath)) {
        return filePath;
      }
      const current = await contentState(cwd, filePath);
      const original = snapshot.files[filePath];
      const knownHashes = new Set([
        original.worktreeHash,
        ...original.indexHashes
      ]);
      const worktreeChanged =
        current.worktreeHash !== original.worktreeHash;
      const indexIntroducedNewContent = current.indexHashes.some(
        hash => !knownHashes.has(hash)
      );
      return worktreeChanged || indexIntroducedNewContent
        ? filePath
        : null;
    }
  );
  return changed.filter(Boolean);
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
    `${focusedTimeoutMinutes(discoveredCount)}m`,
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

function renderSelection(paths, selection, taskBoundary, phase, targets) {
  const estimate = estimateSelectionSeconds(targets);
  const output = [
    `Integration 任务边界：${taskBoundary}`,
    `变更文件：${paths.length}`,
    `本地模式：${selection.mode}`,
    `验证阶段：${phase}`,
    `受影响目标：${selection.targets.map(target => target.name).join(', ') || 'none'}`,
    `本阶段执行：${targets.map(target => target.name).join(', ') || 'none'}`,
    `预计耗时：约 ${Math.ceil(estimate.seconds / 60)} 分钟`
  ];
  if (estimate.exceedsSliceBudget) {
    output.push(
      `预算提示：预计超过 ${Math.ceil(sliceBudgetSeconds / 60)} 分钟，`
      + '请拆分切片或交给合并门禁执行。'
    );
  }
  for (const reason of selection.reasons) {
    output.push(`- ${reason}`);
  }
  output.push('完整集合仅由 main CI 按测试矩阵并行分片执行。');
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

async function runMatrixVerification(cwd) {
  await runProcess(
    process.execPath,
    [path.join('scripts', 'testing', 'verify-integration-shards.mjs')],
    cwd
  );
  const governanceDirectory = path.join(cwd, 'tests', 'governance');
  const governanceTests = (await readdir(governanceDirectory))
    .filter(fileName => fileName.endsWith('.test.mjs'))
    .map(fileName => path.join('tests', 'governance', fileName));
  await runProcess(
    process.execPath,
    ['--test', ...governanceTests],
    cwd
  );
}

async function runCli(args, cwd = process.cwd()) {
  const {
    baseRef,
    phase,
    planOnly,
    snapshotId,
    includeHeavy
  } = parseArguments(args);
  const paths = await collectChangedPaths({ baseRef, snapshotId, cwd });
  if (paths.length === 0) {
    throw new Error(
      `任务边界 ${snapshotId ?? baseRef} 到当前工作区没有可验证变更。`
    );
  }

  const selection = classifyChangedPaths(paths);
  const executionTargets = targetsForPhase(selection.targets, phase, {
    includeHeavy
  });
  process.stdout.write(
    renderSelection(
      paths,
      selection,
      snapshotId ?? baseRef,
      phase,
      executionTargets
    )
  );
  if (planOnly || selection.mode === 'none') {
    return;
  }

  const toolingTargets = executionTargets.filter(
    target => target.kind === 'tooling'
  );
  const toolingScopes = toolingVerificationScopes(toolingTargets);
  if (toolingTargets.length > 0) {
    await runToolingTests(cwd);
  }

  const integrationTargets = executionTargets.filter(
    target => target.kind !== 'tooling'
  );
  const requiresFreshIntegrationAssembly =
    integrationTargets.length > 0 || toolingScopes.includes('partitions');
  if (!requiresFreshIntegrationAssembly) {
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

  if (toolingScopes.includes('partitions')) {
    await runMatrixVerification(cwd);
  }
  if (integrationTargets.length === 0) {
    return;
  }

  const shardTargets = integrationTargets.filter(
    target => target.kind === 'shard'
  );
  for (const target of shardTargets) {
    if (target.kind === 'shard') {
      if (target.name === 'full') {
        throw new Error('本地受影响测试选择器禁止执行 full。');
      }
      await runProcess('dotnet', argumentsFor(target.name), cwd);
    }
  }

  const filterTargets = integrationTargets.filter(
    target => target.kind === 'filter'
  );
  if (filterTargets.length > 0) {
    const discoveredEntries = await Promise.all(
      filterTargets.map(async target => [
        target.name,
        await discover(target.filter, cwd)
      ])
    );
    const combined = combineFilterTargets(
      filterTargets,
      new Map(discoveredEntries),
      { phase }
    );
    const providerNote = phase === 'inner'
      ? '已确认 MySQL Provider（inner 不要求 SQL Server）。'
      : '已确认各目标双 Provider。';
    process.stdout.write(
      `${combined.targetNames.join(', ')} 聚焦发现：`
      + `${combined.discoveredCount} 项（UID 去重），${providerNote}\n`
    );
    await runProcess(
      'dotnet',
      argumentsForFocused(
        {
          kind: 'filter',
          name: combined.targetNames.length === 1
            ? combined.targetNames[0]
            : 'combined',
          filter: combined.filter
        },
        combined.discoveredCount
      ),
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
