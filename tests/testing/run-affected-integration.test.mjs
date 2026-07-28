import assert from 'node:assert/strict';
import { execFile } from 'node:child_process';
import { mkdtemp, mkdir, rm, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import { promisify } from 'node:util';

import {
  argumentsForFocused,
  classifyChangedPaths,
  collectChangedPaths,
  parseArguments,
  verifyFocusedDiscovery
} from '../../scripts/testing/run-affected-integration.mjs';

const execFileAsync = promisify(execFile);

test('纯文档和客户端改动不启动 Integration', () => {
  const selection = classifyChangedPaths([
    'docs/development/getting-started.md',
    'ui/admin/src/views/home/index.vue'
  ]);

  assert.equal(selection.mode, 'none');
  assert.deepEqual(selection.targets, []);
});

test('普通单模块改动选择双库聚焦测试', () => {
  for (const moduleName of [
    'Auditing',
    'Files',
    'Jobs',
    'Notifications',
    'Organization',
    'Settings'
  ]) {
    const selection = classifyChangedPaths([
      `src/Modules/Full.NET.Modules.${moduleName}/Persistence/Queries.cs`
    ]);

    assert.equal(selection.mode, 'focused');
    assert.equal(selection.targets.length, 1);
    assert.equal(selection.targets[0].name, moduleName);
    assert.equal(selection.targets[0].kind, 'filter');
    assert.equal(
      selection.targets[0].filter,
      `FullyQualifiedName~${moduleName}Api`
    );
  }
});

test('单模块 Integration 夹具改动仍选择对应双库聚焦测试', () => {
  const selection = classifyChangedPaths([
    'tests/Full.NET.IntegrationTests/Api/AuditingApiSqlServerTests.cs',
    'tests/Full.NET.IntegrationTests/Auditing/AuditingAccessLogAssertions.cs'
  ]);

  assert.equal(selection.mode, 'focused');
  assert.equal(selection.targets[0].name, 'Auditing');
});

test('共享与安全关键改动选择对应影响集而不升级全量', () => {
  const cases = [
    [
      'src/Modules/Full.NET.Modules.Identity/Security/AccessSessionValidator.cs',
      'Identity',
      'filter'
    ],
    [
      'src/Modules/Full.NET.Modules.Tenancy/Persistence/TenantQueries.cs',
      'Tenancy',
      'filter'
    ],
    [
      'src/Hosts/Full.NET.Host.Api/Program.cs',
      'smoke',
      'shard'
    ],
    [
      'src/BuildingBlocks/Full.NET.Data.Dapper/Outbox/DapperOutboxStore.cs',
      'Outbox',
      'filter'
    ],
    [
      'tests/Full.NET.IntegrationTests/SharedDatabaseFixture.cs',
      'smoke',
      'shard'
    ],
    [
      'tests/Full.NET.IntegrationTests/Migrations/MySqlMigrationTests.cs',
      'migrations',
      'shard'
    ],
    [
      'scripts/testing/run-integration-shard.mjs',
      'integration-tooling',
      'tooling'
    ],
    [
      'src/Unknown/Service.cs',
      'smoke',
      'shard'
    ]
  ];

  for (const [filePath, targetName, targetKind] of cases) {
    const selection = classifyChangedPaths([filePath]);
    assert.notEqual(selection.mode, 'full');
    assert.equal(selection.targets[0].name, targetName);
    assert.equal(selection.targets[0].kind, targetKind);
  }
});

test('多个普通模块改动组合对应聚焦目标', () => {
  const selection = classifyChangedPaths([
    'src/Modules/Full.NET.Modules.Auditing/Persistence/Queries.cs',
    'src/Modules/Full.NET.Modules.Settings/Persistence/Queries.cs'
  ]);

  assert.equal(selection.mode, 'focused');
  assert.deepEqual(
    selection.targets.map(target => target.name),
    ['Auditing', 'Settings']
  );
});

test('测试工具与规则改动只运行工具契约', () => {
  const selection = classifyChangedPaths([
    'AGENTS.md',
    'README.md',
    'package.json',
    'rules/development-quality.md',
    'scripts/testing/run-affected-integration.mjs',
    'tests/testing/run-affected-integration.test.mjs',
    'tests/governance/integration-test-feedback.test.mjs'
  ]);

  assert.equal(selection.mode, 'tooling');
  assert.deepEqual(selection.targets, [
    { kind: 'tooling', name: 'integration-tooling' }
  ]);
});

test('聚焦发现必须同时包含 SQL Server 与 MySQL', () => {
  const sqlServer = {
    displayName: 'query_with_sql_server',
    type: { typeName: 'AuditingApiSqlServerTests' }
  };
  const mySql = {
    displayName: 'query_with_mysql',
    type: { typeName: 'AuditingApiMySqlTests' }
  };

  assert.doesNotThrow(() => verifyFocusedDiscovery([sqlServer, mySql]));
  assert.throws(() => verifyFocusedDiscovery([]), /没有发现/);
  assert.throws(() => verifyFocusedDiscovery([sqlServer]), /MySQL/);
  assert.throws(() => verifyFocusedDiscovery([mySql]), /SQL Server/);
});

test('命令参数要求显式任务基线并支持只规划模式', () => {
  assert.deepEqual(
    parseArguments(['--base', 'abc123', '--plan']),
    { baseRef: 'abc123', planOnly: true }
  );
  assert.deepEqual(
    parseArguments(['--base=abc123']),
    { baseRef: 'abc123', planOnly: false }
  );
  assert.throws(() => parseArguments([]), /--base/);
  assert.throws(() => parseArguments(['--base']), /--base/);
  assert.throws(() => parseArguments(['--unknown']), /未知参数/);
});

test('Git 任务基线收集提交、暂存、未暂存和未跟踪变更并排除本地噪声', async () => {
  const repository = await mkdtemp(path.join(os.tmpdir(), 'fullnet-affected-'));
  const runGit = async args =>
    execFileAsync('git', args, { cwd: repository, encoding: 'utf8' });

  try {
    await runGit(['init']);
    await runGit(['config', 'user.email', 'tests@fullnet.local']);
    await runGit(['config', 'user.name', 'Full.NET Tests']);
    await mkdir(path.join(repository, 'docs'), { recursive: true });
    await writeFile(path.join(repository, 'docs', 'base.md'), 'base\n');
    await runGit(['add', '.']);
    await runGit(['commit', '-m', 'base']);
    const { stdout: baseRef } = await runGit(['rev-parse', 'HEAD']);

    const sourceDirectory = path.join(
      repository,
      'src',
      'Modules',
      'Full.NET.Modules.Auditing'
    );
    await mkdir(sourceDirectory, { recursive: true });
    await writeFile(path.join(sourceDirectory, 'Queries.cs'), 'committed\n');
    await runGit(['add', '.']);
    await runGit(['commit', '-m', 'module change']);

    await mkdir(path.join(repository, 'rules'), { recursive: true });
    await writeFile(path.join(repository, 'rules', 'quality.md'), 'staged\n');
    await runGit(['add', 'rules/quality.md']);
    await writeFile(path.join(repository, 'docs', 'base.md'), 'unstaged\n');
    await mkdir(path.join(repository, 'ui', 'admin'), { recursive: true });
    await writeFile(path.join(repository, 'ui', 'admin', 'new.ts'), 'untracked\n');
    await mkdir(path.join(repository, '.tmp'), { recursive: true });
    await mkdir(path.join(repository, '.cache'), { recursive: true });
    await writeFile(path.join(repository, '.tmp', 'ignored.txt'), 'noise\n');
    await writeFile(path.join(repository, '.cache', 'ignored.txt'), 'noise\n');

    const paths = await collectChangedPaths({
      baseRef: baseRef.trim(),
      cwd: repository
    });

    assert.deepEqual(paths, [
      'docs/base.md',
      'rules/quality.md',
      'src/Modules/Full.NET.Modules.Auditing/Queries.cs',
      'ui/admin/new.ts'
    ]);
  } finally {
    await rm(repository, { recursive: true, force: true });
  }
});

test('聚焦执行参数使用发现数门槛、双库过滤器和独立 TRX', () => {
  const args = argumentsForFocused(
    classifyChangedPaths([
      'src/Modules/Full.NET.Modules.Auditing/Persistence/Queries.cs'
    ]).targets[0],
    6
  );

  assert.ok(args[0].endsWith('Full.NET.IntegrationTests.dll'));
  assert.deepEqual(
    args.slice(args.indexOf('--filter'), args.indexOf('--filter') + 2),
    ['--filter', 'FullyQualifiedName~AuditingApi']
  );
  assert.deepEqual(
    args.slice(
      args.indexOf('--minimum-expected-tests'),
      args.indexOf('--minimum-expected-tests') + 2
    ),
    ['--minimum-expected-tests', '6']
  );
  assert.ok(args.includes('--report-trx'));
  assert.ok(args.includes('Full.NET.IntegrationTests-affected-auditing.trx'));
  assert.throws(
    () => argumentsForFocused({ kind: 'shard' }, 6),
    /filter/
  );
  assert.throws(
    () => argumentsForFocused(
      {
        kind: 'filter',
        name: 'Auditing',
        filter: 'FullyQualifiedName~AuditingApi'
      },
      0
    ),
    /发现数/
  );
});
