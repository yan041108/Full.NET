import assert from 'node:assert/strict';
import test from 'node:test';
import { nativeSourceInputs, assertToolIndexesNotRuntimeInputs, assertManagedResourcesNotRuntimeInputs } from '../../scripts/testing/messaging-native-inputs.mjs';

test('机器码输入保留真实契约资源和任务源码，仅精确排除四个工具索引', () => {
  const files = ['contracts/architecture/global-sql-statements.json', 'contracts/openapi/client-generation-manifest-v1.json',
    'contracts/openapi/fullnet-client-v1.openapi.json', 'contracts/database/object-comments.json', 'contracts/workflow/gateway-limits.json',
    'contracts/naming/fullnet-naming-profile.json', 'contracts/openapi/new-runtime.json',
    'src/BuildingBlocks/Full.NET.Messaging.Abstractions/CdcKafkaDeliveryBinding.cs', 'Directory.Build.props'];
  assert.deepEqual(nativeSourceInputs(files.map(file => ({ file }))).map(item => item.file), files.slice(4));
});

test('注释目录若进入真实ILC或发布内容则拒绝排除，迁移SQL和其它数据库契约继续冻结', () => {
  for (const host of ['Full.NET.Host.Api', 'Full.NET.Host.Worker']) {
    const ilc = '/work/' + host + '.dll';
    assert.throws(() => assertToolIndexesNotRuntimeInputs(ilc + ' /src/contracts/database/object-comments.json', [{ Identity: '/work/appsettings.json' }], host));
    assert.throws(() => assertToolIndexesNotRuntimeInputs(ilc, [{ Identity: '/src/contracts/database/object-comments.json', RelativePath: 'renamed.json' }], host));
    assert.throws(() => assertToolIndexesNotRuntimeInputs(ilc, ['OBJECT-COMMENTS.JSON'], host));
  }
  const files = ['contracts/database/object-comment-overrides.json', 'contracts/database/runtime.json',
    'src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/SqlServer/999_Comments.sql'];
  assert.deepEqual(nativeSourceInputs(files.map(file => ({ file }))).map(item => item.file), files);
});

test('缺少真实 ILC 和发布内容证据时不允许工具索引排除', () => {
  assert.throws(() => assertToolIndexesNotRuntimeInputs('', [{ Identity: '/work/appsettings.json' }]));
  assert.throws(() => assertToolIndexesNotRuntimeInputs('/work/Full.NET.Host.Api.dll', []));
});

test('工具索引作为发布内容即使 Link 改名也拒绝排除', () => {
  assert.throws(() => assertToolIndexesNotRuntimeInputs('/work/Full.NET.Host.Api.dll', [{
    Identity: '/src/contracts/architecture/global-sql-statements.json', RelativePath: 'renamed.json',
  }]));
  assert.throws(() => assertToolIndexesNotRuntimeInputs('/work/Full.NET.Host.Api.dll contracts/openapi/fullnet-client-v1.openapi.json',
    [{ Identity: '/work/appsettings.json' }]));
  assert.throws(() => assertToolIndexesNotRuntimeInputs('/work/Full.NET.Host.Api.dll', [{
    FullPath: 'C:\\SRC\\CONTRACTS\\ARCHITECTURE\\GLOBAL-SQL-STATEMENTS.JSON', RelativePath: 'renamed.json',
  }]));
});

test('实际 IL 引用和发布内容未引用工具索引时可验证精确边界', () => {
  assert.doesNotThrow(() => assertToolIndexesNotRuntimeInputs('/work/Full.NET.Host.Api.dll -r:/work/Full.NET.Modules.Workflow.dll',
    [{ Identity: '/work/appsettings.json', FullPath: '/work/appsettings.json' }]));
});

test('Worker 必须提供自己的实际 ILC 证据，原连接锁和两宿主源码继续冻结', () => {
  const files = ['src/BuildingBlocks/Full.NET.Data.Abstractions/IDatabaseSessionLock.cs',
    'src/Hosts/Full.NET.Host.Worker/Program.cs', 'src/Hosts/Full.NET.Host.Api/Program.cs'];
  assert.deepEqual(nativeSourceInputs(files.map(file => ({ file }))).map(item => item.file), files);
  const published = [{ Identity: '/work/appsettings.json' }];
  assert.throws(() => assertToolIndexesNotRuntimeInputs('/work/Full.NET.Host.Api.dll', published, 'Full.NET.Host.Worker'));
  assert.doesNotThrow(() => assertToolIndexesNotRuntimeInputs('/work/Full.NET.Host.Migrator.dll', published, 'Full.NET.Host.Migrator'));
  assert.doesNotThrow(() => assertToolIndexesNotRuntimeInputs('/work/Full.NET.Host.LogConsumer.dll', published, 'Full.NET.Host.LogConsumer'));
  assert.throws(() => assertToolIndexesNotRuntimeInputs('/work/Full.NET.Host.Api.dll', published, 'Full.NET.Host.Migrator'));
  assert.throws(() => assertToolIndexesNotRuntimeInputs('/work/Full.NET.Host.AppHost.dll', published, 'Full.NET.Host.AppHost'));
  assert.doesNotThrow(() => assertToolIndexesNotRuntimeInputs('/work/Full.NET.Host.Worker.dll', published, 'Full.NET.Host.Worker'));
  assert.throws(() => assertToolIndexesNotRuntimeInputs('/work/Full.NET.Host.Worker.dll',
    [{ Identity: '/src/contracts/architecture/global-sql-statements.json' }], 'Full.NET.Host.Worker'));
});

test('没有托管资源证据时AI资源仍冻结，实际证据只排除指定AI resx', () => {
  const ai = 'src/Modules/Full.NET.Modules.Ai/Resources/AiKnowledgeErrors.resx';
  const files = [ai, 'src/Modules/Full.NET.Modules.Workflow/Resources/WorkflowErrors.resx',
    'src/Modules/Full.NET.Modules.Document/Resources/DocumentErrors.resx',
    'src/Modules/Full.NET.Modules.Ai/runtime.json', 'Directory.Build.props'].map(file => ({ file }));
  assert.deepEqual(nativeSourceInputs(files), files);
  assert.deepEqual(nativeSourceInputs(files, [ai]), files.slice(1));
  assert.throws(() => nativeSourceInputs(files, [files[1].file]));
  assert.throws(() => nativeSourceInputs(files, ['src/Modules/Full.NET.Modules.Ai/../Full.NET.Modules.Workflow/Errors.resx']));
  assert.throws(() => nativeSourceInputs(files, ['src/Modules/Full.NET.Modules.Ai/runtime.json']));
});

test('AI资源如果成为ILC或发布内容必须失败，改名和反斜杠路径也不能隐藏', () => {
  const resources = ['src/Modules/Full.NET.Modules.Ai/Resources/AiKnowledgeErrors.resx'];
  for (const host of ['Full.NET.Host.Api', 'Full.NET.Host.Worker']) {
    const ilc = '/work/' + host + '.dll';
    assert.throws(() => assertManagedResourcesNotRuntimeInputs(ilc + ' /src/' + resources[0], [{ Identity: '/work/appsettings.json' }], resources, host));
    assert.throws(() => assertManagedResourcesNotRuntimeInputs(ilc, [{ FullPath: 'C:\\SRC\\' + resources[0].replaceAll('/', '\\'), RelativePath: 'renamed.bin' }], resources, host));
    assert.doesNotThrow(() => assertManagedResourcesNotRuntimeInputs(ilc + ' -r:/work/Full.NET.Modules.Ai.dll',
      [{ Identity: '/work/Full.NET.Modules.Ai.dll' }, { Identity: '/work/en-US/Full.NET.Modules.Ai.resources.dll' }], resources, host));
  }
});

test('资源分类不能绕过真实ILC、宿主和发布内容的必需证据', () => {
  const resources = ['src/Modules/Full.NET.Modules.Ai/Resources/AiKnowledgeErrors.resx'];
  assert.throws(() => assertManagedResourcesNotRuntimeInputs('', [{ Identity: '/work/appsettings.json' }], resources));
  assert.throws(() => assertManagedResourcesNotRuntimeInputs('/work/Full.NET.Host.Api.dll', [], resources));
  assert.throws(() => assertManagedResourcesNotRuntimeInputs('/work/Full.NET.Host.Api.dll', [{ Identity: '/work/appsettings.json' }], resources, 'Full.NET.Host.Worker'));
});
