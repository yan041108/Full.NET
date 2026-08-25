import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import path from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';
import { apiNativeAotPublishContract } from '../../scripts/testing/api-native-aot-publish-contract.mjs';

const repositoryRoot = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)),
  '../..'
);

async function read(relativePath) {
  return readFile(path.join(repositoryRoot, relativePath), 'utf8');
}

test('package.json 暴露稳定的 linux Native AOT publish 命令', async () => {
  const packageJson = JSON.parse(await read('package.json'));
  assert.equal(
    packageJson.scripts['test:aot:publish:linux'],
    'node scripts/testing/run-api-aot-publish-linux.mjs'
  );
});

test('test-matrix 登记 Native AOT publish 产物下限', async () => {
  const matrix = JSON.parse(await read('eng/testing/test-matrix.json'));
  const publishGate = matrix.nativeAotPublish;
  assert.ok(publishGate, 'eng/testing/test-matrix.json 必须包含 nativeAotPublish 节');
  assert.equal(
    publishGate.minimumExecutableBytes,
    apiNativeAotPublishContract.minimumExecutableBytes
  );
  assert.equal(
    publishGate.runtimeIdentifier,
    apiNativeAotPublishContract.runtimeIdentifier
  );
  assert.equal(
    publishGate.executableRelativePath.replace(/\\/g, '/'),
    path
      .join(
        apiNativeAotPublishContract.outputRelativeDir,
        apiNativeAotPublishContract.executableName
      )
      .replace(/\\/g, '/')
  );
});

test('publish 脚本与契约包含完整 MSBuild 参数', async () => {
  const script = await read('scripts/testing/run-api-aot-publish-linux.mjs');
  const contractSource = await read(
    'scripts/testing/api-native-aot-publish-contract.mjs'
  );
  assert.match(script, /api-native-aot-publish-contract\.mjs/);
  assert.match(contractSource, /FullNetPublishMode:\s*'NativeAot'/);
  assert.match(contractSource, /linux-x64/);
  assert.match(contractSource, /SelfContained:\s*'true'/);
});

test('publish 脚本在链接前删除旧产物与 manifest', async () => {
  const script = await read('scripts/testing/run-api-aot-publish-linux.mjs');
  assert.match(script, /rmSync\(outputDir,\s*\{ recursive: true, force: true \}\)/);
  assert.match(script, /rmSync\(manifestPath,\s*\{ force: true \}\)/);
  assert.ok(
    script.indexOf('clearPreviousPublishEvidence();')
      < script.indexOf('const startedAt = Date.now();'),
    '旧发布证据必须在本轮计时和 publish 前清理'
  );
});

test('publish warning 门禁只接受 ADR 登记的程序集与告警码', async () => {
  let validatePublishWarnings;
  try {
    ({ validatePublishWarnings } = await import(
      '../../scripts/testing/api-native-aot-publish-warnings.mjs'
    ));
  } catch {
    // 缺少门禁模块时由下方函数断言给出稳定失败，而不是泄漏模块加载异常。
  }
  assert.equal(typeof validatePublishWarnings, 'function');

  const accepted = validatePublishWarnings(`
/root/.nuget/packages/memorypack.core/1.21.4/lib/net8.0/MemoryPack.Core.dll : warning IL2104: trim
/root/.nuget/packages/memorypack.core/1.21.4/lib/net8.0/MemoryPack.Core.dll : warning IL3053: aot
/root/.nuget/packages/dapper/2.1.79/lib/net10.0/Dapper.dll : warning IL2104: trim
/root/.nuget/packages/dapper/2.1.79/lib/net10.0/Dapper.dll : warning IL3053: aot
/root/.nuget/packages/microsoft.data.sqlclient/7.0.2/Microsoft.Data.SqlClient.dll : warning IL2104: trim
/root/.nuget/packages/microsoft.data.sqlclient/7.0.2/Microsoft.Data.SqlClient.dll : warning IL3053: aot
/root/.nuget/packages/microsoft.data.sqlclient.internal.logging/7.0.2/Microsoft.Data.SqlClient.Internal.Logging.dll : warning IL2104: trim
/root/.nuget/packages/system.configuration.configurationmanager/9.0.13/System.Configuration.ConfigurationManager.dll : warning IL2104: trim
/root/.nuget/packages/confluent.kafka/2.15.0/lib/net10.0/Confluent.Kafka.dll : warning IL2104: trim
`);
  assert.equal(accepted.length, 9);

  assert.throws(
    () => validatePublishWarnings(
      '/src/Full.NET.Custom.dll : warning IL2104: trim'
    ),
    /未登记的 Native AOT publish warning/
  );
  assert.throws(
    () => validatePublishWarnings(
      '/root/.nuget/packages/dapper/2.1.79/Dapper.dll : warning IL2026: new warning'
    ),
    /未登记的 Native AOT publish warning/
  );
});

test('Docker publish 将 bin 与 obj 隔离在容器临时目录', async () => {
  const script = await read('scripts/testing/run-api-aot-publish-linux.mjs');
  assert.match(script, /UseArtifactsOutput=true/);
  assert.match(script, /ArtifactsPath=\/tmp\/fullnet-native-aot\/artifacts/);
  assert.doesNotMatch(script, /BaseIntermediateOutputPath/);
  assert.doesNotMatch(script, /BaseOutputPath/);
  assert.doesNotMatch(script, /clearProjectObjFolders/);
});

test('Native E2E 直接运行已构建程序集并执行最低发现数门禁', async () => {
  const script = await read('scripts/testing/run-native-aot-e2e.mjs');
  assert.match(script, /nativeGate\.project/);
  assert.match(script, /'build'/);
  assert.match(script, /matrix\.integration\.assembly/);
  assert.match(script, /--list-tests/);
  assert.match(script, /JSON\.parse/);
  assert.match(script, /--zero-tests-policy/);
  assert.match(script, /--minimum-expected-tests/);
  assert.doesNotMatch(script, /'test',\s*'tests\/Full\.NET\.IntegrationTests/);
});

test('Native E2E 将 TRX 与原生进程日志写入可上传的 artifacts 目录', async () => {
  const runners = [
    ['scripts/testing/run-native-aot-e2e.mjs', 'native-aot'],
    ['scripts/testing/run-native-aot-notifications-e2e.mjs', 'native-aot-notifications'],
    ['scripts/testing/run-native-aot-settings-jobs-e2e.mjs', 'native-aot-settings-jobs'],
    ['scripts/testing/run-native-aot-s3-e2e.mjs', 'native-aot-s3'],
    ['scripts/testing/run-native-aot-kafka-replay-e2e.mjs', 'native-aot-kafka-replay'],
  ];
  const processHost = await read(
    'tests/Full.NET.IntegrationTests/NativeAot/NativeApiProcessHost.cs'
  );

  for (const [runnerPath, reportName] of runners) {
    const runner = await read(runnerPath);
    assert.match(runner, /--results-directory/);
    assert.match(runner, /--report-trx/);
    assert.ok(
      runner.includes(`Full.NET.IntegrationTests-${reportName}.trx`),
      `${runnerPath} 必须生成独立 TRX。`
    );
  }
  assert.match(processHost, /artifacts["'],\s*["']native-aot["']/);
  assert.match(processHost, /test-logs/);
});

test('Notifications Native AOT 门禁登记矩阵、脚本、工作流与专用 TRX', async () => {
  const matrix = JSON.parse(await read('eng/testing/test-matrix.json'));
  const packageJson = JSON.parse(await read('package.json'));
  const workflow = await read('.github/workflows/api-native-aot-linux.yml');
  const runner = await read('scripts/testing/run-native-aot-notifications-e2e.mjs');

  const notificationsGate = matrix.nativeAotNotificationsIntegration;
  assert.ok(
    notificationsGate,
    'eng/testing/test-matrix.json 必须包含 nativeAotNotificationsIntegration 节'
  );
  assert.equal(
    notificationsGate.project,
    'tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj'
  );
  assert.equal(notificationsGate.filter, 'FullyQualifiedName~NativeApiNotifications');
  assert.equal(notificationsGate.minimum, 2);
  assert.equal(notificationsGate.timeout, '45m');

  assert.equal(
    packageJson.scripts['test:aot:native:notifications:e2e'],
    'node scripts/testing/run-native-aot-notifications-e2e.mjs'
  );

  const externalE2EIndex = workflow.indexOf('Run Native AOT external-process E2E');
  const notificationsE2EIndex = workflow.indexOf('Run Native AOT Notifications E2E');
  const s3E2EIndex = workflow.indexOf('Run Native AOT S3 Provider E2E');
  assert.ok(externalE2EIndex >= 0);
  assert.ok(notificationsE2EIndex > externalE2EIndex);
  assert.ok(s3E2EIndex > notificationsE2EIndex);
  assert.match(workflow, /pnpm test:aot:native:notifications:e2e/);

  assert.match(runner, /nativeAotNotificationsIntegration/);
  assert.match(runner, /matrix\.integration\.assembly/);
  assert.match(
    runner,
    /Full\.NET\.IntegrationTests-native-aot-notifications\.trx/
  );
  assert.match(runner, /artifacts\/native-aot\/linux-x64\/test-results/);
  assert.match(runner, /--minimum-expected-tests/);

  assert.match(
    matrix.nativeAotIntegration.filter,
    /FullyQualifiedName!~NativeApiNotifications/
  );
});

test('Settings/Jobs Native AOT 门禁登记矩阵、脚本、工作流与专用 TRX', async () => {
  const matrix = JSON.parse(await read('eng/testing/test-matrix.json'));
  const packageJson = JSON.parse(await read('package.json'));
  const workflow = await read('.github/workflows/api-native-aot-linux.yml');
  const runner = await read('scripts/testing/run-native-aot-settings-jobs-e2e.mjs');

  const settingsJobsGate = matrix.nativeAotSettingsJobsIntegration;
  assert.ok(
    settingsJobsGate,
    'eng/testing/test-matrix.json 必须包含 nativeAotSettingsJobsIntegration 节'
  );
  assert.equal(
    settingsJobsGate.project,
    'tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj'
  );
  assert.equal(
    settingsJobsGate.filter,
    'FullyQualifiedName~NativeApiSettings|FullyQualifiedName~NativeApiJobs'
  );
  assert.equal(settingsJobsGate.minimum, 4);
  assert.equal(settingsJobsGate.timeout, '45m');

  assert.equal(
    packageJson.scripts['test:aot:native:settings-jobs:e2e'],
    'node scripts/testing/run-native-aot-settings-jobs-e2e.mjs'
  );

  const notificationsE2EIndex = workflow.indexOf('Run Native AOT Notifications E2E');
  const settingsJobsE2EIndex = workflow.indexOf('Run Native AOT Settings Jobs E2E');
  const s3E2EIndex = workflow.indexOf('Run Native AOT S3 Provider E2E');
  assert.ok(notificationsE2EIndex >= 0);
  assert.ok(settingsJobsE2EIndex > notificationsE2EIndex);
  assert.ok(s3E2EIndex > settingsJobsE2EIndex);
  assert.match(workflow, /pnpm test:aot:native:settings-jobs:e2e/);

  assert.match(runner, /nativeAotSettingsJobsIntegration/);
  assert.match(runner, /matrix\.integration\.assembly/);
  assert.match(
    runner,
    /Full\.NET\.IntegrationTests-native-aot-settings-jobs\.trx/
  );
  assert.match(runner, /artifacts\/native-aot\/linux-x64\/test-results/);
  assert.match(runner, /--minimum-expected-tests/);

  assert.match(
    matrix.nativeAotIntegration.filter,
    /FullyQualifiedName!~NativeApiSettings/
  );
  assert.match(
    matrix.nativeAotIntegration.filter,
    /FullyQualifiedName!~NativeApiJobs/
  );
});
