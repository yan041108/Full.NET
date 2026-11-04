import assert from 'node:assert/strict';
import { mkdtemp, mkdir, readFile, rm, writeFile } from 'node:fs/promises';
import { execFile } from 'node:child_process';
import { tmpdir } from 'node:os';
import path from 'node:path';
import test from 'node:test';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { promisify } from 'node:util';
import { apiNativeAotPublishContract } from '../../scripts/testing/api-native-aot-publish-contract.mjs';

const repositoryRoot = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)),
  '../..'
);

async function read(relativePath) {
  return readFile(path.join(repositoryRoot, relativePath), 'utf8');
}

/** 在真实共享运行器上注入 SDK 边界，只验证编排、目录与参数，不声明 Native 业务已执行。 */
async function assertNativeResultDestination(runner, gateKey, gate, trxName) {
  const {runNativeSuite} = await import('../../scripts/testing/native-suite-runner.mjs');
  const {execFileSync} = await import('node:child_process');
  const originalCi = process.env.CI;
  for (const ci of [false, true]) {
    const root = await mkdtemp(path.join(tmpdir(), 'fullnet-native-output-contract-'));
    try {
      process.env.CI = String(ci);
      await mkdir(path.dirname(path.join(root, gate.project)), {recursive:true});
      await writeFile(path.join(root, gate.project), '<Project />');
      await writeFile(path.join(root, '.gitignore'), '.tmp/\nbin/\nobj/\nlocks/\nartifacts/\n');
      execFileSync('git', ['init', '--quiet'], {cwd:root, windowsHide:true});
      const assembly='bin/tests.dll';
      await mkdir(path.join(root,'eng/testing'),{recursive:true});
      await writeFile(path.join(root,'eng/testing/test-matrix.json'),JSON.stringify({integration:{assembly},[gateKey]:gate}));
      const calls=[];
      await runNativeSuite({suite:gateKey,results:'native-aot/linux-x64/test-results',trx:trxName}, {
        cwd:root,platform:'win32',sdkVersion:'test-sdk',lockRoot:path.join(root,'locks'),
        runCommand:async(args)=>{
          calls.push(args);
          if(args[0]==='build'){await mkdir(path.join(root,'bin'));await writeFile(path.join(root,assembly),'compiled');return '';}
          if(args.includes('--list-tests'))return JSON.stringify({tests:Array.from({length:gate.minimum},(_,index)=>({uid:String(index),type:{typeName:gate.requiredTestTypes?.[index%gate.requiredTestTypes.length]}}))});
          return '';
        }
      });
      const tests=calls.filter(args=>args.includes('--report-trx'));assert.equal(tests.length,1);
      const args=tests[0],directory=args[args.indexOf('--results-directory')+1];
      assert.ok(args[args.indexOf('--report-trx-filename')+1].startsWith(trxName.replace(/\.trx$/u,'-')));
      assert.equal(args[args.indexOf('--filter')+1],gate.filter);assert.equal(args[args.indexOf('--timeout')+1],gate.timeout);
      if(ci)assert.equal(directory,path.join(root,'artifacts/native-aot/linux-x64/test-results'));
      else assert.match(path.relative(tmpdir(),directory).replaceAll('\\','/'),/^fullnet-test-output-[^/]+\/native-aot\/linux-x64\/test-results$/);
    } finally {await rm(root,{recursive:true,force:true});}
  }
  if(originalCi===undefined)delete process.env.CI;else process.env.CI=originalCi;
}

async function readNativeRunner(relativePath) {
  const entry = await read(relativePath);
  // 入口必须实际接入共享运行器；契约检查随后沿调用链核对执行门禁。
  assert.match(entry, /import \{runNativeSuite\} from '\.\/native-suite-runner\.mjs'/);
  assert.match(entry, /runNativeSuite\(\{/);
  return entry + '\n' + await read('scripts/testing/native-suite-runner.mjs');
}

test('核心API跨宿主恢复与Worker专项均登记必需场景', async () => {
  const matrix = JSON.parse(await read('eng/testing/test-matrix.json'));
  // 核心API套件含当前跨Host恢复用例，须保留真实Worker程序；专项仍固定选择Worker用例。
  assert.ok(matrix.nativeAotIntegration.requiredTestTypes.includes('NativeWorkerPdfE2ETests'));
  assert.equal(matrix.workerNativeAotIntegration.filter, 'FullyQualifiedName~NativeWorker');
  assert.ok(matrix.nativeAotIntegration.requiredTestTypes.includes('NativeApiKafkaLogMySqlE2ETests'));
  assert.ok(matrix.workerNativeAotIntegration.requiredTestTypes.includes('NativeWorkerKafkaLogMySqlE2ETests'));
});

for (const role of ['api', 'worker']) {
  test(`${role} 成功发布后立即上传可复用产物，运行证据仍在原生测试之后`, async () => {
    const workflow = await read(`.github/workflows/${role}-native-aot-linux.yml`);
    const steps = workflow.split(/^      - /m).slice(1);
    const publish = steps.findIndex(step => step.includes(role === 'api'
      ? 'run: pnpm test:aot:publish:linux' : 'run: pnpm test:aot:worker:publish:linux'));
    const artifact = steps.findIndex(step => step.startsWith(role === 'api'
      ? 'name: Upload publish manifest' : 'name: Upload Worker Native AOT publish artifact'));
    const runtime = steps.findIndex(step => step.startsWith(role === 'api'
      ? 'name: Run Native AOT external-process E2E' : 'name: Run Worker Native AOT dual-database E2E'));
    assert.ok(publish >= 0 && artifact > publish && artifact < runtime, '可复用产物须在运行门禁之前上传');
    assert.ok(runtime > artifact, '保留后续原生运行门禁');
    assert.match(steps[artifact], /if: success\(\)/);
    assert.match(steps[artifact], /if-no-files-found: error/);
    const prefix = role === 'api' ? 'artifacts/native-aot/linux-x64' : 'artifacts/native-aot/worker/linux-x64';
    assert.ok(steps[artifact].includes(`${prefix}/publish-manifest.json`));
    assert.ok(steps[artifact].includes(`${prefix}/publish/Full.NET.Host.${role === 'api' ? 'Api' : 'Worker'}`));
    assert.ok(steps.findIndex(step => step.startsWith(role === 'api'
      ? 'name: Upload Native AOT test logs' : 'name: Upload Worker Native AOT evidence')) > runtime,
    '最终运行日志或回执必须保留到测试之后');
  });
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
/root/.nuget/packages/microsoft.netcore.app.runtime.nativeaot.linux-x64/10.0.12/System.Linq.Expressions.dll : warning IL3053: aot
`);
  assert.equal(accepted.length, 10);

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
  const script = await readNativeRunner('scripts/testing/run-native-aot-e2e.mjs');
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
    ['scripts/testing/run-native-aot-oidc-e2e.mjs', 'native-aot-oidc'],
  ];
  const processHost = await read(
    'tests/Full.NET.IntegrationTests/NativeAot/NativeApiProcessHost.cs'
  );

  for (const [runnerPath, reportName] of runners) {
    const runner = await readNativeRunner(runnerPath);
    assert.match(runner, /--results-directory/);
    assert.match(runner, /--report-trx/);
    if (reportName === 'native-aot') {
      // 核心入口每轮生成唯一文件名，防止旧成功报告填补本轮缺失；运行行为另有入口回归覆盖。
      assert.match(runner, /randomUUID\(\)/);
      assert.match(runner, /'--report-trx-filename',\s*path\.basename\(resultPath\)/);
    } else {
      assert.ok(
        runner.includes(`Full.NET.IntegrationTests-${reportName}.trx`),
        `${runnerPath} 必须生成独立 TRX。`
      );
    }
  }
  assert.match(processHost, /artifacts["'],\s*["']native-aot["']/);
  assert.match(processHost, /test-logs/);
});

test('Notifications Native AOT 门禁登记矩阵、脚本、工作流与专用 TRX', async () => {
  const matrix = JSON.parse(await read('eng/testing/test-matrix.json'));
  const packageJson = JSON.parse(await read('package.json'));
  const workflow = await read('.github/workflows/api-native-aot-linux.yml');
  const runner = await readNativeRunner('scripts/testing/run-native-aot-notifications-e2e.mjs');

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
  await assertNativeResultDestination(runner, 'nativeAotNotificationsIntegration', notificationsGate,
    'Full.NET.IntegrationTests-native-aot-notifications.trx');
  assert.match(runner, /--minimum-expected-tests/);

  assert.match(
    matrix.nativeAotIntegration.filter,
    /FullyQualifiedName!~NativeApiNotifications/
  );
  for (const required of [...new Set(['NativeApiCacheE2ETests', 'NativeApiLocalFileMySqlE2ETests', 'NativeApiKafkaLogMySqlE2ETests', 'NativeWorkerPdfE2ETests', 'NativeWorkerKnowledgeRetirementE2ETests', 'NativeWorkerTenantAuthorityE2ETests', 'NativeApiCacheColdStartE2ETests', 'NativeApiKnowledgeIncrementalIndexE2ETests', 'NativeApiMediaBindingTests'])]) assert.ok(matrix.nativeAotIntegration.requiredTestTypes.includes(required), required);
  assert.match(await readNativeRunner('scripts/testing/run-native-aot-e2e.mjs'),
    /discoveredTypes\.has\(requiredType\)/);
});

test('Settings/Jobs Native AOT 门禁登记矩阵、脚本、工作流与专用 TRX', async () => {
  const matrix = JSON.parse(await read('eng/testing/test-matrix.json'));
  const packageJson = JSON.parse(await read('package.json'));
  const workflow = await read('.github/workflows/api-native-aot-linux.yml');
  const runner = await readNativeRunner('scripts/testing/run-native-aot-settings-jobs-e2e.mjs');

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
  await assertNativeResultDestination(runner, 'nativeAotSettingsJobsIntegration', settingsJobsGate,
    'Full.NET.IntegrationTests-native-aot-settings-jobs.trx');
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

test('OIDC Native AOT 门禁登记矩阵、脚本、工作流与专用 TRX', async () => {
  const matrix = JSON.parse(await read('eng/testing/test-matrix.json'));
  const packageJson = JSON.parse(await read('package.json'));
  const workflow = await read('.github/workflows/api-native-aot-linux.yml');
  const runner = await readNativeRunner('scripts/testing/run-native-aot-oidc-e2e.mjs');

  const oidcGate = matrix.nativeAotOidcIntegration;
  assert.ok(
    oidcGate,
    'eng/testing/test-matrix.json 必须包含 nativeAotOidcIntegration 节'
  );
  assert.equal(
    oidcGate.project,
    'tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj'
  );
  assert.equal(oidcGate.filter, 'FullyQualifiedName~NativeApiOidc');
  assert.equal(oidcGate.minimum, 16);
  assert.equal(oidcGate.timeout, '45m');

  assert.equal(
    packageJson.scripts['test:aot:native:oidc:e2e'],
    'node scripts/testing/run-native-aot-oidc-e2e.mjs'
  );

  const settingsJobsE2EIndex = workflow.indexOf('Run Native AOT Settings Jobs E2E');
  const oidcE2EIndex = workflow.indexOf('Run Native AOT OIDC E2E');
  const s3E2EIndex = workflow.indexOf('Run Native AOT S3 Provider E2E');
  assert.ok(settingsJobsE2EIndex >= 0);
  assert.ok(oidcE2EIndex > settingsJobsE2EIndex);
  assert.ok(s3E2EIndex > oidcE2EIndex);
  assert.match(workflow, /pnpm test:aot:native:oidc:e2e/);

  assert.match(runner, /nativeAotOidcIntegration/);
  assert.match(runner, /matrix\.integration\.assembly/);
  assert.match(
    runner,
    /Full\.NET\.IntegrationTests-native-aot-oidc\.trx/
  );
  await assertNativeResultDestination(runner, 'nativeAotOidcIntegration', oidcGate,
    'Full.NET.IntegrationTests-native-aot-oidc.trx');
  assert.match(runner, /--minimum-expected-tests/);

  assert.match(
    matrix.nativeAotIntegration.filter,
    /FullyQualifiedName!~NativeApiOidc/
  );
});

test('Worker Native AOT 发现门禁包含 Kafka、PDF、旧代次清理与租户准入原生用例', async () => {
  const matrix = JSON.parse(await read('eng/testing/test-matrix.json'));
  const runner = await readNativeRunner('scripts/testing/run-native-aot-worker-e2e.mjs');
  for (const required of ['NativeWorkerKafkaLogMySqlE2ETests', 'NativeWorkerPdfE2ETests', 'NativeWorkerKnowledgeRetirementE2ETests', 'NativeWorkerTenantAuthorityE2ETests', 'NativeWorkerClaimSnapshotTests']) assert.ok(matrix.workerNativeAotIntegration.requiredTestTypes.includes(required), required);
  assert.match(runner, /discoveredTypes\.has\(requiredType\)/);
});
