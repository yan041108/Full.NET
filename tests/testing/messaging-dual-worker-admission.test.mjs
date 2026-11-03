import assert from 'node:assert/strict';
import test from 'node:test';
import { mkdtemp, mkdir, writeFile, rm } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { createHash } from 'node:crypto';
import { binaryIdentity, verifyDualWorkerSoakBaseline, verifyDualWorkerStageAdmission, resolveCapacityPartitionBuffer, runMessagingReliability, streamFilterIdentity, nativeMessagingSourceIdentity, focusedControllerSourceIdentity } from '../../scripts/testing/run-messaging-reliability.mjs';

test('聚焦控制器输入在等待和运行时变化必须拒绝', async () => {
  const root = await mkdtemp(path.join(os.tmpdir(), 'fullnet-controller-source-'));
  try {
    const file = path.join(root, 'fixture.cs'), manifest = path.join(root, 'manifest.json');
    await writeFile(file, 'original');
    await writeFile(manifest, JSON.stringify([{ file, sha256: createHash('sha256').update('original').digest('hex') }]));
    assert.equal((await focusedControllerSourceIdentity(manifest)).length, 2);
    await writeFile(file, 'changed');
    await assert.rejects(focusedControllerSourceIdentity(manifest), /fixture.cs/);
  } finally { await rm(root, { recursive: true, force: true }); }
});

test('原生产物必须匹配现行 Messaging 源码，并拒绝发布后新增文件', async () => {
  const root = await mkdtemp(path.join(os.tmpdir(), 'fullnet-native-source-'));
  try {
    const project = 'src/BuildingBlocks/Full.NET.Messaging.Kafka';
    await mkdir(path.join(root, project), { recursive: true });
    await mkdir(path.join(root, 'native', 'attempt-1'), { recursive: true });
    const contents = new Map([[`${project}/Full.NET.Messaging.Kafka.csproj`, 'project'], [`${project}/Kafka.cs`, 'original']]);
    const sha = value => createHash('sha256').update(value).digest('hex');
    for (const [file, value] of contents) await writeFile(path.join(root, file), value);
    const source = [...contents].map(([file, value]) => ({ file, sha256: sha(value) }));
    await mkdir(path.join(root, 'scripts/testing'), { recursive: true });
    await writeFile(path.join(root, 'scripts/testing/publish.mjs'), 'tool');
    const tools = [{ file: 'publish.mjs', sha256: sha('tool') }];
    await writeFile(path.join(root, 'native/attempt-1/tooling-input.json'), JSON.stringify(tools));
    await writeFile(path.join(root, 'native/attempt-1/source-input.json'), JSON.stringify(source));
    await writeFile(path.join(root, 'native/manifest.json'), JSON.stringify({ host: 'api', toolingInputsUnchanged: true,
      nativeInputsUnchanged: true, managedInputsVerified: true, managedArtifactsUnchanged: true, headUnchanged: true,
      sourceSha256: sha(JSON.stringify(source)), toolingInputsSha256: sha(JSON.stringify(tools)) }));
    assert.equal((await nativeMessagingSourceIdentity(path.join(root, 'native'), 'Api', root)).length, 2);
    await writeFile(path.join(root, project, 'Added.cs'), 'new');
    await assert.rejects(nativeMessagingSourceIdentity(path.join(root, 'native'), 'Api', root), /Added.cs/);
    await rm(path.join(root, project, 'Added.cs'));
    await writeFile(path.join(root, project, 'Kafka.cs'), 'changed');
    await assert.rejects(nativeMessagingSourceIdentity(path.join(root, 'native'), 'Api', root), /Kafka.cs/);
    await writeFile(path.join(root, project, 'Kafka.cs'), 'original');
    await writeFile(path.join(root, 'scripts/testing/publish.mjs'), 'changed');
    await assert.rejects(nativeMessagingSourceIdentity(path.join(root, 'native'), 'Api', root), /publish.mjs/);
  } finally { await rm(root, { recursive: true, force: true }); }
});

test('真实流过滤插件必须绑定实际字节，缺失或运行中替换均拒绝', async () => {
  const root = await mkdtemp(path.join(os.tmpdir(), 'fullnet-filter-identity-'));
  try {
    const jar = path.join(root, 'filter.jar');
    await writeFile(jar, 'original');
    const sha = createHash('sha256').update('original').digest('hex');
    const source = 'deploy/messaging/connect-filter/src/FullNetOutboxStreamFilter.java';
    const image = 'sha256:' + '1'.repeat(64);
    await mkdir(path.join(root, path.dirname(source)), { recursive: true });
    await writeFile(path.join(root, source), 'source');
    await writeFile(path.join(root, 'manifest.json'), JSON.stringify({ jar, sha256: sha, image,
      inputs: [{ file: source, sha256: createHash('sha256').update('source').digest('hex') }] }));
    const environment = { FULLNET_KAFKA_STREAM_FILTER_JAR: jar, FULLNET_KAFKA_STREAM_FILTER_SHA256: sha };
    const inspect = () => image;
    assert.deepEqual(await streamFilterIdentity(environment, root, inspect), { file: 'connect/fullnet-outbox-stream-filter.jar', sha256: sha, image });
    await assert.rejects(streamFilterIdentity(environment, root, () => 'sha256:' + '2'.repeat(64)));
    await assert.rejects(streamFilterIdentity({}));
    await writeFile(path.join(root, source), 'changed');
    await assert.rejects(streamFilterIdentity(environment, root, inspect));
    await writeFile(path.join(root, source), 'source');
    await writeFile(jar, 'replacement');
    await assert.rejects(streamFilterIdentity(environment, root, inspect));
  } finally { await rm(root, { recursive: true, force: true }); }
});

test('预取配置只允许显式Native入口，普通长稳不能隐式继承', () => {
  assert.equal(resolveCapacityPartitionBuffer('component-native-dual-worker-soak', undefined), 1);
  for (const stage of ['component-native-capacity-5', 'component-native-capacity-10']) {
    assert.equal(resolveCapacityPartitionBuffer(stage, '1'), 1);
    assert.equal(resolveCapacityPartitionBuffer(stage, '8'), 8);
    for (const invalid of ['0', '-1', '2', '256', '']) assert.throws(() => resolveCapacityPartitionBuffer(stage, invalid));
  }
  for (const stage of ['component-native-dual-worker-soak', 'component-native-dual-worker-smoke', 'component-dual-worker-smoke', 'process'])
    assert.throws(() => resolveCapacityPartitionBuffer(stage, '8'));
  for (const stage of ['component-native-prefetch8-smoke', 'component-native-prefetch8-soak']) {
    assert.equal(resolveCapacityPartitionBuffer(stage, undefined), 8);
    assert.equal(resolveCapacityPartitionBuffer(stage, '8'), 8);
    assert.throws(() => resolveCapacityPartitionBuffer(stage, '1'));
  }
});

test('自动TTL入口缺任一Native宿主根时先拒绝，不能退回JIT或普通恢复矩阵', async () => {
  for (const stage of ['component-native-auto-ttl-formal', 'component-native-auto-ttl-retry']) {
    const common = ['--stage', stage, '--build-root', os.tmpdir(), '--output', os.tmpdir()];
    for (const roots of [[], ['--native-api-root', 'unused'], ['--native-worker-root', 'unused']])
      await assert.rejects(runMessagingReliability([...common, ...roots]), /Native.*同时提供/);
  }
});

async function fixture(run) {
  const root = await mkdtemp(path.join(os.tmpdir(), 'fullnet-dual-worker-admission-'));
  const baseline = path.join(root, 'component-dual-worker-smoke-fixture');
  const identities = [{ file: 'test/fixture.dll', sha256: 'test' }, { file: 'Worker/fixture.dll', sha256: 'worker' }];
  try {
    await mkdir(baseline);
    const result = { exit: 0, report: { total: 2, outcomes: { Passed: 2 } }, binariesUnchanged: true, assemblySha256: 'test' };
    await writeFile(path.join(baseline, 'component-dual-worker-smoke-result.json'), JSON.stringify(result));
    await writeFile(path.join(baseline, 'binaries.json'), JSON.stringify(identities));
    for (const provider of ['MySql', 'SqlServer']) {
      const directory = path.join(baseline, `${provider}-dual-worker`);
      await mkdir(directory);
      await writeFile(path.join(directory, 'result.json'), JSON.stringify({ PlannedSeconds: 60, MeasuredSeconds: 60.1,
        Succeeded: 60, Processed: 60, BrokerDrainVerified: true, Failures: 0, Pending: 0, WorkerPids: [101, 102, 103] }));
    }
    await run({ root, baseline, identities, result });
  } finally { await rm(root, { recursive: true, force: true }); }
}

test('双库实际闭包一致且短测通过才准入双 Worker 长跑', async () => fixture(async ({ root, baseline, identities }) => {
  assert.equal(await verifyDualWorkerSoakBaseline(root, 'test', identities), baseline);
}));

test('预取8长稳必须由相同8/0/1原生闭包的双库一请求每秒短测准入', async () => fixture(async ({ root, identities, result }) => {
  await assert.rejects(verifyDualWorkerStageAdmission('component-native-prefetch8-soak', root, 'test', identities));
  const stage = 'component-native-prefetch8-smoke', baseline = path.join(root, stage + '-fixture');
  await mkdir(baseline);
  await writeFile(path.join(baseline, stage + '-result.json'), JSON.stringify(result));
  await writeFile(path.join(baseline, 'binaries.json'), JSON.stringify(identities));
  const sample = { PlannedSeconds: 60, MeasuredSeconds: 60.1, MessagesPerSecond: 1, Succeeded: 60, Processed: 60,
    BrokerDrainVerified: true, Failures: 0, Pending: 0, ExecutionKind: 'native_linux', WorkerResourceKind: 'linux_pid1_rss',
    NativeWorkerContainers: ['a', 'b', 'replacement'], PartitionBufferHighWatermark: 8, PartitionBufferLowWatermark: 0,
    PartitionKeyConcurrencySlots: 1, OffsetCommitMode: 'PerMessage', ProxyNoDelay: true, ProxySingleWrite: true,
    Prefetch8SoakProfile: true, InboxProcessedTimeNondecreasingVerified: true, SyntheticIndependentStreamVerified: true };
  for (const provider of ['MySql', 'SqlServer']) {
    await mkdir(path.join(baseline, provider + '-dual-worker'));
    await writeFile(path.join(baseline, provider + '-dual-worker/result.json'), JSON.stringify(sample));
  }
  assert.equal(await verifyDualWorkerStageAdmission('component-native-prefetch8-soak', root, 'test', identities), baseline);
  await assert.rejects(verifyDualWorkerSoakBaseline(root, 'test', identities, 'native_linux'));
  for (const patch of [{ PartitionBufferHighWatermark: 1 }, { PartitionKeyConcurrencySlots: 2 },
    { ProxySingleWrite: false }, { Prefetch8SoakProfile: false }, { SyntheticIndependentStreamVerified: false }, { MessagesPerSecond: 5 }]) {
    await writeFile(path.join(baseline, 'MySql-dual-worker/result.json'), JSON.stringify({ ...sample, ...patch }));
    await assert.rejects(verifyDualWorkerStageAdmission('component-native-prefetch8-soak', root, 'test', identities));
  }
}));

test('Worker 二进制改变或测试程序集改变都拒绝旧短测', async () => fixture(async ({ root, identities }) => {
  await assert.rejects(verifyDualWorkerSoakBaseline(root, 'changed', identities));
  await assert.rejects(verifyDualWorkerSoakBaseline(root, 'test', [identities[0], { ...identities[1], sha256: 'changed' }]));
}));

test('失败、未排空或实际时长不能追上计划速率时拒绝长跑', async () => fixture(async ({ root, baseline, identities, result }) => {
  await writeFile(path.join(baseline, 'component-dual-worker-smoke-result.json'), JSON.stringify({ ...result, exit: 1 }));
  await assert.rejects(verifyDualWorkerSoakBaseline(root, 'test', identities));
  await writeFile(path.join(baseline, 'component-dual-worker-smoke-result.json'), JSON.stringify(result));
  for (const patch of [{ Pending: 1 }, { MeasuredSeconds: 80 }, { WorkerPids: [101] }, { BrokerDrainVerified: false }]) {
    await writeFile(path.join(baseline, 'MySql-dual-worker', 'result.json'), JSON.stringify({ PlannedSeconds: 60,
      MeasuredSeconds: 60.1, Succeeded: 60, Processed: 60, BrokerDrainVerified: true, Failures: 0, Pending: 0, WorkerPids: [101, 102, 103], ...patch }));
    await assert.rejects(verifyDualWorkerSoakBaseline(root, 'test', identities));
  }
}));

test('配置、契约与嵌套原生依赖属于实际运行闭包', async () => fixture(async ({ root }) => {
  await mkdir(path.join(root, 'runtimes/linux-x64/native'), { recursive: true });
  await mkdir(path.join(root, 'contracts/workflow'), { recursive: true });
  for (const file of ['appsettings.json', 'appsettings.Development.json', 'contracts/workflow/runtime.json', 'runtimes/linux-x64/native/library.so.1'])
    await writeFile(path.join(root, file), 'first');
  const initial = await binaryIdentity(root);
  assert.equal(initial.length, 4);
  await writeFile(path.join(root, 'appsettings.json'), 'changed');
  assert.notDeepEqual(await binaryIdentity(root), initial);
  await writeFile(path.join(root, 'appsettings.json'), 'first');
  await writeFile(path.join(root, 'runtimes/linux-x64/native/library.so.1'), 'changed');
  assert.notDeepEqual(await binaryIdentity(root), initial);
}));

test('运行日志与测试结果不会被误认作静态依赖', async () => fixture(async ({ root }) => {
  await mkdir(path.join(root, 'logs'));
  await writeFile(path.join(root, 'fixture.dll'), 'managed');
  const initial = await binaryIdentity(root);
  await writeFile(path.join(root, 'logs/runtime.ndjson'), '{}');
  await writeFile(path.join(root, 'result.json'), '{}');
  assert.deepEqual(await binaryIdentity(root), initial);
}));

test('Native长稳不能使用JIT短测，必须绑定真实容器资源及同一ELF闭包', async () => fixture(async ({ root, baseline, identities, result }) => {
  await assert.rejects(verifyDualWorkerStageAdmission('component-native-dual-worker-soak', root, 'test', identities));
  assert.equal(await verifyDualWorkerStageAdmission('component-native-dual-worker-smoke', root, 'test', identities), null);
  await assert.rejects(verifyDualWorkerSoakBaseline(root, 'test', identities, 'native_linux'));
  const native = path.join(root, 'component-native-dual-worker-smoke-fixture');
  await mkdir(native);
  await writeFile(path.join(native, 'component-native-dual-worker-smoke-result.json'), JSON.stringify(result));
  await writeFile(path.join(native, 'binaries.json'), JSON.stringify(identities));
  for (const provider of ['MySql', 'SqlServer']) {
    await mkdir(path.join(native, `${provider}-dual-worker`));
    await writeFile(path.join(native, `${provider}-dual-worker`, 'result.json'), JSON.stringify({ PlannedSeconds: 60,
      MeasuredSeconds: 60.1, MessagesPerSecond: 1, Succeeded: 60, Processed: 60, Failures: 0, Pending: 0,
      BrokerDrainVerified: true, ExecutionKind: 'native_linux', WorkerResourceKind: 'linux_pid1_rss',
      NativeWorkerContainers: ['worker-a', 'worker-b', 'worker-replacement'] }));
  }
  assert.equal(await verifyDualWorkerSoakBaseline(root, 'test', identities, 'native_linux'), native);
  assert.equal(await verifyDualWorkerStageAdmission('component-native-dual-worker-soak', root, 'test', identities), native);
  await assert.rejects(verifyDualWorkerSoakBaseline(root, 'test', [...identities, { file: 'Worker/publish/Full.NET.Host.Worker', sha256: 'changed' }], 'native_linux'));
  const file = path.join(native, 'MySql-dual-worker/result.json');
  const sample = JSON.parse(await import('node:fs/promises').then(fs => fs.readFile(file, 'utf8')));
  await writeFile(file, JSON.stringify({ ...sample, NativeWorkerContainers: ['same', 'same', 'same'] }));
  await assert.rejects(verifyDualWorkerSoakBaseline(root, 'test', identities, 'native_linux'));
}));
