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
  combineFilterTargets,
  collectChangedPaths,
  createTaskSnapshot,
  estimateSelectionSeconds,
  parseArguments,
  targetsForPhase,
  verifyFocusedDiscovery
} from '../../scripts/testing/run-affected-integration.mjs';
import * as affectedIntegration
  from '../../scripts/testing/run-affected-integration.mjs';

const execFileAsync = promisify(execFile);

test('纯文档和客户端改动不启动 Integration', () => {
  const selection = classifyChangedPaths([
    'docs/development/getting-started.md',
    'ui/admin/src/views/home/index.vue'
  ]);

  assert.equal(selection.mode, 'none');
  assert.deepEqual(selection.targets, []);
});

test('宿主运行时 App_Data 不扩大 Integration 影响集', () => {
  const selection = classifyChangedPaths([
    'docs/development/getting-started.md',
    'src/Hosts/Full.NET.Host.Api/App_Data/files/runtime.bin'
  ]);

  assert.equal(selection.mode, 'none');
  assert.deepEqual(selection.targets, []);
});

test('Api assertion 支持文件映射到所属模块而不是 Smoke', () => {
  const selection = classifyChangedPaths([
    'tests/Full.NET.IntegrationTests/Api/OpenApiSettingsTenantDictTypesContractAssertions.cs'
  ]);

  assert.equal(selection.mode, 'focused');
  assert.deepEqual(
    selection.targets.map(target => target.name),
    ['Settings']
  );
});

test('普通单模块改动选择双库聚焦测试', () => {
  for (const moduleName of [
    'Auditing',
    'Files',
    'Jobs',
    'Notifications',
    'Organization',
    'SerialNumbers',
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

test('CodeGeneration 元数据变化只选择对应双库专项测试', () => {
  for (const filePath of [
    'src/BuildingBlocks/Full.NET.Data.CodeGeneration/Schema/DatabaseCrudSchemaImporter.cs',
    'src/Tools/Full.NET.CodeGeneration.Cli/CodeGenerationCli.cs',
    'tests/Full.NET.IntegrationTests/CodeGeneration/DatabaseCrudSchemaImporterIntegrationTests.cs'
  ]) {
    const selection = classifyChangedPaths([filePath]);

    assert.equal(selection.mode, 'focused');
    assert.deepEqual(selection.targets, [
      {
        filter: 'FullyQualifiedName~CodeGeneration',
        kind: 'filter',
        name: 'CodeGeneration'
      }
    ]);
    assert.deepEqual(
      targetsForPhase(selection.targets, 'inner'),
      selection.targets
    );
  }
});

test('CodeGeneration 模块变化选择完整 CodeGeneration 聚焦集而不是 smoke', () => {
  const selection = classifyChangedPaths([
    'src/Modules/Full.NET.Modules.CodeGeneration/CodeGenerationModule.cs'
  ]);

  assert.equal(selection.mode, 'focused');
  assert.deepEqual(selection.targets, [
    {
      filter: 'FullyQualifiedName~CodeGeneration',
      kind: 'filter',
      name: 'CodeGeneration'
    }
  ]);
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

test('未登记迁移安全降级到迁移分片并追加受影响模块', () => {
  const selection = classifyChangedPaths([
    'src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/MySql/033_SettingsScope.sql',
    'src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/SqlServer/033_SettingsScope.sql'
  ]);

  assert.equal(selection.mode, 'focused');
  assert.deepEqual(selection.targets, [
    { kind: 'shard', name: 'migrations' },
    {
      filter: 'FullyQualifiedName~SettingsApi',
      kind: 'filter',
      name: 'Settings'
    }
  ]);
});

test('已登记迁移选择对应恢复测试并追加受影响模块', () => {
  const selection = classifyChangedPaths([
    'src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/MySql/011_NamingContract.sql',
    'src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/SqlServer/011_NamingContract.sql'
  ]);

  assert.equal(selection.mode, 'focused');
  assert.deepEqual(selection.targets, [
    {
      filter:
        'FullyQualifiedName~MySqlMigrationTests'
        + '|FullyQualifiedName~SqlServerMigrationTests'
        + '|FullyQualifiedName~NamingContractMigrationTests'
        + '|FullyQualifiedName~NamingContractPartialRecoveryTests'
        + '|FullyQualifiedName~NamingReleaseCandidateUpgradeDrillTests',
      kind: 'filter',
      name: 'migration-011'
    }
  ]);
  assert.deepEqual(
    targetsForPhase(selection.targets, 'inner').map(target => target.name),
    ['migration-011']
  );
});

test('已登记迁移的独立恢复夹具不会升级到完整迁移分片', () => {
  const selection = classifyChangedPaths([
    'tests/Full.NET.IntegrationTests/Migrations/Migration034OrganizationPositionUnitRecoveryTests.cs'
  ]);

  assert.equal(selection.mode, 'focused');
  assert.deepEqual(selection.targets, [
    {
      filter:
        'FullyQualifiedName~Migration034OrganizationPositionUnitRecoveryTests.MySql_'
        + '|FullyQualifiedName~Migration034OrganizationPositionUnitRecoveryTests.SqlServer_',
      kind: 'filter',
      name: 'migration-034'
    }
  ]);
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

test('验证阶段只在切片边界运行普通模块并在合并阶段追加 Smoke', () => {
  const settings = classifyChangedPaths([
    'src/Modules/Full.NET.Modules.Settings/Persistence/Queries.cs'
  ]);
  const identity = classifyChangedPaths([
    'src/Modules/Full.NET.Modules.Identity/Security/AccessSessionValidator.cs'
  ]);

  assert.deepEqual(targetsForPhase(settings.targets, 'inner'), []);
  assert.deepEqual(
    targetsForPhase(identity.targets, 'inner').map(target => target.name),
    ['Identity']
  );
  assert.deepEqual(
    targetsForPhase(settings.targets, 'slice').map(target => target.name),
    ['Settings']
  );
  assert.deepEqual(
    targetsForPhase(settings.targets, 'merge'),
    [
      {
        filter: 'FullyQualifiedName~SettingsApi',
        kind: 'filter',
        name: 'Settings'
      },
      {
        filter:
          'FullyQualifiedName~SqlServer_migration_is_idempotent_and_creates_binary_outbox_schema'
          + '|FullyQualifiedName~MySql_migration_is_idempotent_and_creates_binary_outbox_schema'
          + '|FullyQualifiedName~Login_and_current_user_follow_secure_http_contract'
          + '|FullyQualifiedName~Anonymous_current_tenant_endpoint_returns_minimal_standard_http_contract'
          + '|FullyQualifiedName~SqlServer_provisioning_is_atomic_without_cache_outbox'
          + '|FullyQualifiedName~MySql_provisioning_is_atomic_without_cache_outbox',
        kind: 'filter',
        name: 'smoke'
      }
    ]
  );
});

test('多个聚焦目标按 UID 去重并合并为一次过滤执行', () => {
  const targets = classifyChangedPaths([
    'src/Modules/Full.NET.Modules.Auditing/Persistence/Queries.cs',
    'src/Modules/Full.NET.Modules.Settings/Persistence/Queries.cs'
  ]).targets;
  const sqlServer = {
    uid: 'sql',
    displayName: 'query_with_sql_server',
    type: { typeName: 'ApiSqlServerTests' }
  };
  const mySql = {
    uid: 'mysql',
    displayName: 'query_with_mysql',
    type: { typeName: 'ApiMySqlTests' }
  };
  const combined = combineFilterTargets(targets, new Map([
    ['Auditing', [sqlServer, mySql]],
    ['Settings', [sqlServer, mySql]]
  ]));

  assert.equal(
    combined.filter,
    'FullyQualifiedName~AuditingApi|FullyQualifiedName~SettingsApi'
  );
  assert.equal(combined.discoveredCount, 2);
  assert.deepEqual(combined.targetNames, ['Auditing', 'Settings']);
});

test('合并阶段将重叠 Smoke 与模块测试放入一次 UID 去重执行', () => {
  const targets = targetsForPhase(
    classifyChangedPaths([
      'src/Modules/Full.NET.Modules.Identity/Security/AccessSessionValidator.cs'
    ]).targets,
    'merge'
  );
  const sharedSqlServer = {
    uid: 'shared-sql',
    displayName: 'Login_and_current_user_follow_secure_http_contract',
    type: { typeName: 'IdentityApiSqlServerTests' }
  };
  const sharedMySql = {
    uid: 'shared-mysql',
    displayName: 'Login_and_current_user_follow_secure_http_contract',
    type: { typeName: 'IdentityApiMySqlTests' }
  };
  const combined = combineFilterTargets(targets, new Map([
    ['Identity', [sharedSqlServer, sharedMySql]],
    ['smoke', [sharedSqlServer, sharedMySql]]
  ]));

  assert.ok(targets.every(target => target.kind === 'filter'));
  assert.equal(combined.discoveredCount, 2);
  assert.deepEqual(combined.targetNames, ['Identity', 'smoke']);
});

test('计划预算对重复目标只计算一次并标识超出切片预算', () => {
  const budget = estimateSelectionSeconds([
    { kind: 'filter', name: 'Settings' },
    { kind: 'filter', name: 'Settings' },
    { kind: 'shard', name: 'migrations' }
  ]);

  assert.equal(budget.seconds, 1920);
  assert.equal(budget.exceedsSliceBudget, true);
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

test('测试矩阵改动进入工具链并追加分片和治理验证', () => {
  const paths = ['eng/testing/test-matrix.json'];
  const selection = classifyChangedPaths(paths);

  assert.equal(selection.mode, 'tooling');
  assert.deepEqual(selection.targets, [
    { kind: 'tooling', name: 'integration-matrix' }
  ]);
  assert.equal(
    typeof affectedIntegration.toolingVerificationScopes,
    'function'
  );
  assert.deepEqual(
    affectedIntegration.toolingVerificationScopes(selection.targets),
    ['tooling', 'partitions', 'governance']
  );
});

test('任务快照内容比较使用有界并发并保持结果顺序', async () => {
  assert.equal(typeof affectedIntegration.mapWithConcurrency, 'function');

  let active = 0;
  let maximumActive = 0;
  const results = await affectedIntegration.mapWithConcurrency(
    [1, 2, 3, 4, 5, 6],
    3,
    async value => {
      active += 1;
      maximumActive = Math.max(maximumActive, active);
      await new Promise(resolve => setTimeout(resolve, 10));
      active -= 1;
      return value * 2;
    }
  );

  assert.equal(maximumActive, 3);
  assert.deepEqual(results, [2, 4, 6, 8, 10, 12]);
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
    {
      baseRef: 'abc123',
      phase: 'slice',
      planOnly: true,
      snapshotId: null
    }
  );
  assert.deepEqual(
    parseArguments(['--snapshot', 'task-123', '--phase', 'inner']),
    {
      baseRef: null,
      phase: 'inner',
      planOnly: false,
      snapshotId: 'task-123'
    }
  );
  assert.throws(() => parseArguments([]), /--base/);
  assert.throws(() => parseArguments(['--base']), /--base/);
  assert.throws(
    () => parseArguments(['--base', 'abc123', '--snapshot', 'task-123']),
    /只能选择一种/
  );
  assert.throws(
    () => parseArguments(['--base', 'abc123', '--phase', 'unknown']),
    /--phase/
  );
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

test('任务快照排除开始前未变化的脏文件并保留任务新增改动', async () => {
  const repository = await mkdtemp(path.join(os.tmpdir(), 'fullnet-snapshot-'));
  const runGit = async args =>
    execFileAsync('git', args, { cwd: repository, encoding: 'utf8' });

  try {
    await runGit(['init']);
    await runGit(['config', 'user.email', 'tests@fullnet.local']);
    await runGit(['config', 'user.name', 'Full.NET Tests']);
    await mkdir(path.join(repository, 'src'), { recursive: true });
    await writeFile(path.join(repository, 'src', 'existing.cs'), 'base\n');
    await runGit(['add', '.']);
    await runGit(['commit', '-m', 'base']);

    await writeFile(path.join(repository, 'src', 'existing.cs'), 'user work\n');
    const snapshot = await createTaskSnapshot({
      cwd: repository,
      id: 'feature-slice'
    });

    await mkdir(
      path.join(
        repository,
        'src',
        'Modules',
        'Full.NET.Modules.Settings'
      ),
      { recursive: true }
    );
    await writeFile(
      path.join(
        repository,
        'src',
        'Modules',
        'Full.NET.Modules.Settings',
        'Queries.cs'
      ),
      'task work\n'
    );

    assert.equal(snapshot.id, 'feature-slice');
    await assert.rejects(
      createTaskSnapshot({
        cwd: repository,
        id: 'feature-slice'
      }),
      /已存在/
    );
    assert.deepEqual(
      await collectChangedPaths({
        snapshotId: snapshot.id,
        cwd: repository
      }),
      ['src/Modules/Full.NET.Modules.Settings/Queries.cs']
    );

    await writeFile(
      path.join(repository, 'src', 'existing.cs'),
      'task content staged only\n'
    );
    await runGit(['add', 'src/existing.cs']);
    await writeFile(path.join(repository, 'src', 'existing.cs'), 'user work\n');
    assert.deepEqual(
      await collectChangedPaths({
        snapshotId: snapshot.id,
        cwd: repository
      }),
      [
        'src/Modules/Full.NET.Modules.Settings/Queries.cs',
        'src/existing.cs'
      ]
    );

    await writeFile(
      path.join(repository, 'src', 'existing.cs'),
      'user work changed by task\n'
    );
    assert.deepEqual(
      await collectChangedPaths({
        snapshotId: snapshot.id,
        cwd: repository
      }),
      [
        'src/Modules/Full.NET.Modules.Settings/Queries.cs',
        'src/existing.cs'
      ]
    );
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
