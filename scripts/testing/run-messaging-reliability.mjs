import { spawn, execFileSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { readFile, writeFile, mkdir, mkdtemp, readdir } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { withTestRun, testRunEnvironment } from './test-run-context.mjs';
import { summarizeTrxOutcomes } from './summarize-trx-outcomes.mjs';
import { loadTestMatrix } from './run-dotnet-test-suite.mjs';

const repository = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const digest = data => createHash('sha256').update(data).digest('hex');

/** 过滤插件是实际 Connect 运行闭包的一部分；不得只验证调用方传入的摘要文本。 */
export async function streamFilterIdentity(environment = process.env, repositoryRoot = repository,
  inspectImage = () => execFileSync('docker', ['image', 'inspect', 'quay.io/debezium/connect:3.4.3.Final', '--format', '{{.Id}}'], { encoding: 'utf8' }).trim()) {
  const jar = environment.FULLNET_KAFKA_STREAM_FILTER_JAR;
  const expected = environment.FULLNET_KAFKA_STREAM_FILTER_SHA256;
  if (!jar || !path.isAbsolute(jar) || !/^[a-f0-9]{64}$/iu.test(expected ?? ''))
    throw new Error('长稳与TTL必须显式提供流过滤插件路径及SHA256。');
  const actual = digest(await readFile(jar));
  if (actual.toLowerCase() !== expected.toLowerCase()) throw new Error('流过滤插件摘要不匹配。');
  const manifest = JSON.parse(await readFile(path.join(path.dirname(jar), 'manifest.json'), 'utf8'));
  if (path.resolve(manifest.jar ?? '') !== path.resolve(jar) || manifest.sha256 !== actual
      || !Array.isArray(manifest.inputs) || !manifest.inputs.some(item => item.file === 'deploy/messaging/connect-filter/src/FullNetOutboxStreamFilter.java'))
    throw new Error('流过滤插件缺少同次构建来源证明。');
  if (!/^sha256:[a-f0-9]{64}$/u.test(manifest.image ?? '') || inspectImage() !== manifest.image)
    throw new Error('Connect 镜像与流过滤插件编译镜像不一致。');
  for (const item of manifest.inputs) {
    const file = path.resolve(repositoryRoot, item.file);
    const relative = path.relative(repositoryRoot, file);
    if (!relative || relative.startsWith('..') || path.isAbsolute(relative) || digest(await readFile(file)) !== item.sha256)
      throw new Error('流过滤插件构建输入已变化。');
  }
  return { file: 'connect/fullnet-outbox-stream-filter.jar', sha256: actual, image: manifest.image };
}

/** 已发布角色必须仍匹配实际 Messaging 源码集合，包括发布后新增的文件。 */
export async function nativeMessagingSourceIdentity(root, role, repositoryRoot = repository) {
  const manifest = JSON.parse(await readFile(path.join(root, 'manifest.json'), 'utf8'));
  if (manifest.host !== role.toLowerCase() || !manifest.toolingInputsUnchanged || !manifest.nativeInputsUnchanged
      || !manifest.managedInputsVerified || !manifest.managedArtifactsUnchanged || !manifest.headUnchanged)
    throw new Error('Native 角色发布身份或输入守卫不完整。');
  let source, attemptDirectory;
  for (const entry of await readdir(root, { withFileTypes: true })) {
    if (!entry.isDirectory() || !entry.name.startsWith('attempt-')) continue;
    try {
      const candidate = JSON.parse(await readFile(path.join(root, entry.name, 'source-input.json'), 'utf8'));
      if (digest(JSON.stringify(candidate)) === manifest.sourceSha256) { source = candidate; attemptDirectory = path.join(root, entry.name); break; }
    } catch (error) { if (error.code !== 'ENOENT') throw error; }
  }
  if (!source) throw new Error('缺少与 Native 发布摘要对应的源输入。');
  const tools = JSON.parse(await readFile(path.join(attemptDirectory, 'tooling-input.json'), 'utf8'));
  if (!Array.isArray(tools) || digest(JSON.stringify(tools)) !== manifest.toolingInputsSha256 || tools.length === 0)
    throw new Error('Native 发布工具输入证明不匹配。');
  for (const tool of tools) {
    if (path.basename(tool.file) !== tool.file || digest(await readFile(path.join(repositoryRoot, 'scripts/testing', tool.file))) !== tool.sha256)
      throw new Error(`Native 发布工具已变化：${tool.file}`);
  }
  const relevant = file => file.startsWith('src/') && /Messaging|Outbox|Inbox/u.test(file);
  const expected = new Map(source.filter(item => relevant(item.file)).map(item => [item.file, item.sha256]));
  if (expected.size === 0) throw new Error('Native 发布未记录 Messaging 源码。');
  const roots = new Set(source.filter(item => item.file.startsWith('src/') && item.file.endsWith('.csproj'))
    .map(item => path.posix.dirname(item.file)));
  const actual = new Map();
  async function visit(relative) {
    for (const entry of await readdir(path.join(repositoryRoot, relative), { withFileTypes: true })) {
      if (['bin', 'obj', '.git', 'artifacts', 'node_modules'].includes(entry.name)) continue;
      const file = `${relative}/${entry.name}`;
      if (entry.isSymbolicLink()) throw new Error('Native Messaging 源码存在未声明符号链接。');
      if (entry.isDirectory()) await visit(file);
      else if (entry.isFile() && relevant(file)) actual.set(file, digest(await readFile(path.join(repositoryRoot, file))));
    }
  }
  for (const project of roots) await visit(project);
  const changed = [...new Set([...expected.keys(), ...actual.keys()])].filter(file => expected.get(file) !== actual.get(file));
  if (changed.length) throw new Error(`Native ${role} 的 Messaging 源输入已漂移：${changed.join(', ')}`);
  return [...actual].sort(([a], [b]) => a.localeCompare(b)).map(([file, sha256]) => ({ file: `${role}/source/${file}`, sha256 }));
}

/** 可选聚焦控制器输入快照：排队、运行期间源文件不能换版。 */
export async function focusedControllerSourceIdentity(manifestPath) {
  if (!path.isAbsolute(manifestPath)) throw new Error('控制器源码快照必须为绝对路径。');
  const bytes = await readFile(manifestPath), entries = JSON.parse(bytes);
  if (!Array.isArray(entries) || entries.length === 0) throw new Error('控制器源码快照为空。');
  const identities = [{ file: 'controller/source-manifest', sha256: digest(bytes) }];
  for (const item of entries) {
    if (!path.isAbsolute(item.file) || !/^[a-f0-9]{64}$/iu.test(item.sha256 ?? '')) throw new Error('控制器源码快照格式无效。');
    const actual = digest(await readFile(item.file));
    if (actual !== item.sha256.toLowerCase()) throw new Error(`控制器源码已漂移：${item.file}`);
    identities.push({ file: `controller/source/${item.file}`, sha256: actual });
  }
  return identities;
}

// 测试程序集本身未变仍不足以证明依赖未变；同时冻结实际运行目录中的托管闭包和运行配置。
export async function binaryIdentity(directory) {
  const files = [];
  async function visit(folder, prefix = '') {
    for (const entry of await readdir(folder, { withFileTypes: true })) {
      const file = prefix + entry.name;
      if (entry.isSymbolicLink()) throw new Error('运行输入存在未声明链接，不能认证实际闭包。');
      if (entry.isDirectory()) await visit(path.join(folder, entry.name), `${file}/`);
      else if (/\.(?:dll|exe|dylib|deps\.json|runtimeconfig\.json)$/u.test(file) || /\.so(?:\.\d+)*$/u.test(file)
          || /^appsettings(?:\.[^/]+)?\.json$/u.test(file) || file.startsWith('contracts/') && file.endsWith('.json')) files.push(file);
    }
  }
  await visit(directory); files.sort();
  const entries = [];
  for (const file of files) entries.push({ file, sha256: digest(await readFile(path.join(directory, file))) });
  return entries;
}

// 长跑的计划速率是开发验收参数；先用同一冻结程序集的真实短测证明可以追上，避免明知持续积压仍启动两小时负载。
export async function verifySoakBaseline(output, assemblySha256) {
  const candidates = [];
  for (const entry of await readdir(output, { withFileTypes: true })) {
    if (!entry.isDirectory() || !entry.name.startsWith('capacity-smoke-')) continue;
    const directory = path.join(output, entry.name);
    try { candidates.push({ directory, result: JSON.parse(await readFile(path.join(directory, 'capacity-smoke-result.json'), 'utf8')) }); }
    catch (error) { if (error.code !== 'ENOENT') throw error; }
  }
  candidates.sort((left, right) => right.result.finishedAt.localeCompare(left.result.finishedAt));
  const baseline = candidates[0];
  if (!baseline || baseline.result.exit !== 0 || baseline.result.report?.outcomes.Passed !== 2
      || baseline.result.report.total !== 2 || !baseline.result.binariesUnchanged
      || baseline.result.assemblySha256 !== assemblySha256)
    throw new Error('Soak 前置拒绝：需要当前冻结程序集最近一次双库短测实际 2/2 通过。');
  const samples = [];
  for (const entry of await readdir(baseline.directory, { withFileTypes: true })) {
    if (!entry.isDirectory() || !entry.name.startsWith('fullnet-kafka-capacity-scope-c-')) continue;
    const rows = (await readFile(path.join(baseline.directory, entry.name, 'evidence/samples.ndjson'), 'utf8'))
      .trim().split('\n').filter(Boolean).map(JSON.parse);
    samples.push(...rows);
  }
  if (samples.length !== 2 || samples.some(sample => sample.scopeCode !== 'transaction_outbox_cdc'
      || !sample.integrity.correctnessPassed || sample.failureCodes.length !== 0
      || !Number.isFinite(sample.performance.consumedMessagesPerSecond) || sample.performance.consumedMessagesPerSecond < 2))
    throw new Error('Soak 前置拒绝：真实双库短测尚未证明能追上本轮计划的 2 条/秒；不属于生产 SLO。');
  return { directory: baseline.directory, arrivalRatePerSecond: 2, measuredConsumedRates: samples.map(sample => sample.performance.consumedMessagesPerSecond) };
}

// 长跑必须使用相同测试、API 和 Worker 的实际闭包，避免拿另一进程模型的吞吐作为准入证据。
export async function verifyDualWorkerSoakBaseline(output, assemblySha256, identities, executionKind = 'jit', partitionBuffer = 1) {
  if (![1, 8].includes(partitionBuffer) || partitionBuffer === 8 && executionKind !== 'native_linux')
    throw new Error('预取8长稳只接受真实Native闭包。');
  const baselineStage = partitionBuffer === 8 ? 'component-native-prefetch8-smoke'
    : executionKind === 'native_linux' ? 'component-native-dual-worker-smoke' : 'component-dual-worker-smoke';
  const candidates = [];
  for (const entry of await readdir(output, { withFileTypes: true })) {
    if (!entry.isDirectory() || !entry.name.startsWith(baselineStage + '-')) continue;
    const directory = path.join(output, entry.name);
    try {
      const result = JSON.parse(await readFile(path.join(directory, baselineStage + '-result.json'), 'utf8'));
      if (result.exit === 0 && result.report?.total === 2 && result.report.outcomes.Passed === 2 && result.binariesUnchanged
          && result.assemblySha256 === assemblySha256 && JSON.stringify(JSON.parse(await readFile(path.join(directory, 'binaries.json'), 'utf8'))) === JSON.stringify(identities))
        candidates.push(directory);
    } catch (error) { if (error.code !== 'ENOENT') throw error; }
  }
  for (const directory of candidates.reverse()) {
    const samples = await Promise.all(['MySql', 'SqlServer'].map(provider => readFile(path.join(directory, `${provider}-dual-worker`, 'result.json'), 'utf8').then(JSON.parse)));
    if (samples.every(sample => sample.PlannedSeconds === 60 && sample.MeasuredSeconds >= 60 && sample.MeasuredSeconds < 75
        && sample.Succeeded === 60 && sample.Processed === 60 && sample.BrokerDrainVerified === true
        && sample.Failures === 0 && sample.Pending === 0 && (sample.MessagesPerSecond ?? 1) === 1
        && (sample.PartitionBufferHighWatermark ?? 1) === partitionBuffer
        && (partitionBuffer !== 8 || sample.PartitionBufferLowWatermark === 0 && sample.PartitionKeyConcurrencySlots === 1
          && sample.OffsetCommitMode === 'PerMessage' && sample.ProxyNoDelay === true && sample.ProxySingleWrite === true
          && sample.Prefetch8SoakProfile === true && sample.InboxProcessedTimeNondecreasingVerified === true
          && sample.SyntheticIndependentStreamVerified === true)
        && (executionKind === 'native_linux' ? sample.ExecutionKind === 'native_linux' && sample.WorkerResourceKind === 'linux_pid1_rss'
          && sample.NativeWorkerContainers?.length === 3 && new Set(sample.NativeWorkerContainers).size === 3
          : sample.WorkerPids.length === 3))) return directory;
  }
  throw new Error('双 Worker 长跑拒绝：需要相同实际闭包的双库 60 秒短测全部通过，含真实进程替换、中断和业务排空。');
}

export async function verifyDualWorkerStageAdmission(stage, output, assemblySha256, identities) {
  if (stage === 'component-dual-worker-soak') return verifyDualWorkerSoakBaseline(output, assemblySha256, identities, 'jit');
  if (stage === 'component-native-dual-worker-soak') return verifyDualWorkerSoakBaseline(output, assemblySha256, identities, 'native_linux');
  if (stage === 'component-native-prefetch8-soak') return verifyDualWorkerSoakBaseline(output, assemblySha256, identities, 'native_linux', 8);
  return null;
}

// 预取深度是独立的开发测量参数；默认长稳和普通入口不能隐式继承容量对照配置。
export function resolveCapacityPartitionBuffer(stage, requested) {
  if (['component-native-prefetch8-smoke', 'component-native-prefetch8-soak'].includes(stage)) {
    if (requested !== undefined && requested !== '8') throw new Error('显式预取8入口固定8/0/1。');
    return 8;
  }
  if (requested === undefined) return 1;
  if (!['component-native-capacity-5', 'component-native-capacity-10'].includes(stage) || !['1', '8'].includes(requested))
    throw new Error('只有Native短时容量入口允许显式 --partition-buffer 1|8；默认长稳保持原配置。');
  return Number(requested);
}

// 编译由正式资源入口单独完成；此入口只执行明确选择，不自动构建、扩大套件或启动生产切流。
export async function runMessagingReliability(args) {
  // 脚本与机器矩阵在进程开始时加载；排队后不能给旧实现附上新文件的摘要。
  const toolingFiles = ['scripts/testing/run-messaging-reliability.mjs', 'scripts/testing/test-run-context.mjs',
    'scripts/testing/build-resource-pool.mjs', 'eng/testing/test-matrix.json'];
  const startupTooling = await Promise.all(toolingFiles.map(async file => ({ file: `orchestration/${file}`,
    sha256: digest(await readFile(path.join(repository, file))) })));
  const flags = Object.fromEntries(args.reduce((pairs, value, index) => index % 2 ? pairs : [...pairs, [value, args[index + 1]]], []));
  const stage = flags['--stage'];
  const partitionBuffer = resolveCapacityPartitionBuffer(stage, flags['--partition-buffer']);
  const prefetch8Soak = ['component-native-prefetch8-smoke', 'component-native-prefetch8-soak'].includes(stage);
  const automaticTtl = stage === 'component-native-auto-ttl-formal' ? 'formal' : stage === 'component-native-auto-ttl-retry' ? 'retry' : null;
  const nativeProcess = Boolean(automaticTtl) || prefetch8Soak || ['native-process', 'component-native-process', 'component-native-dual-worker-smoke', 'component-native-dual-worker-soak', 'component-native-capacity-5', 'component-native-capacity-10'].includes(stage);
  const matrix = loadTestMatrix();
  const selections = {
    unit: { assembly: 'Full.NET.UnitTests', ...matrix.dotnetSuites.unit.selections['messaging-observability'], timeout: '5m' },
    platform: { assembly: 'Full.NET.IntegrationTests', ...matrix.integration.shards['messaging-platform-health'] },
    process: { assembly: 'Full.NET.IntegrationTests', ...matrix.integration.shards['messaging-process-recovery'] },
    'source-alerts': { assembly: 'Full.NET.IntegrationTests', ...matrix.integration.shards['messaging-source-window-alerts'] },
    quorum: { assembly: 'Full.NET.IntegrationTests', ...matrix.integration.shards['messaging-three-broker-recovery'] },
    alerts: { assembly: 'Full.NET.IntegrationTests', ...matrix.integration.shards['messaging-live-alerts'] },
    soak: { assembly: 'Full.NET.IntegrationTests', ...matrix.integration.shards['messaging-bounded-soak'] },
    'capacity-smoke': { assembly: 'Full.NET.IntegrationTests', ...matrix.integration.shards['messaging-owned-capacity-smoke'] },
    'native-api': { assembly: 'Full.NET.IntegrationTests', ...matrix.integration.shards['messaging-native-configuration'] },
    'native-process': { assembly: 'Full.NET.IntegrationTests', ...matrix.integration.shards['messaging-process-recovery'] },
    'component-native-dual-worker-smoke': { assembly: 'Full.NET.IntegrationTests', project: 'MessagingProcessFocused', ...matrix.integration.shards['messaging-dual-worker-smoke'] },
    'component-native-dual-worker-soak': { assembly: 'Full.NET.IntegrationTests', project: 'MessagingProcessFocused', ...matrix.integration.shards['messaging-dual-worker-soak'] },
    'component-native-prefetch8-smoke': { assembly: 'Full.NET.IntegrationTests', project: 'MessagingProcessFocused', ...matrix.integration.shards['messaging-dual-worker-smoke'] },
    'component-native-prefetch8-soak': { assembly: 'Full.NET.IntegrationTests', project: 'MessagingProcessFocused', ...matrix.integration.shards['messaging-dual-worker-soak'] },
    'component-native-capacity-5': { assembly: 'Full.NET.IntegrationTests', project: 'MessagingProcessFocused', ...matrix.integration.shards['messaging-dual-worker-smoke'] },
    'component-native-capacity-10': { assembly: 'Full.NET.IntegrationTests', project: 'MessagingProcessFocused', ...matrix.integration.shards['messaging-dual-worker-smoke'] },
    'component-native-auto-ttl-formal': { assembly: 'Full.NET.IntegrationTests', project: 'MessagingProcessFocused', ...matrix.integration.shards['messaging-process-recovery'] },
    'component-native-auto-ttl-retry': { assembly: 'Full.NET.IntegrationTests', project: 'MessagingProcessFocused', ...matrix.integration.shards['messaging-process-recovery'] },
    'component-native-process': { assembly: 'Full.NET.IntegrationTests', project: 'MessagingProcessFocused', ...matrix.integration.shards['messaging-process-recovery'] },
    'component-native-api': { assembly: 'Full.NET.IntegrationTests', project: 'MessagingNativeFocused', ...matrix.integration.shards['messaging-native-configuration'] },
    'component-unit': { assembly: 'Full.NET.UnitTests', project: 'MessagingFocused', ...matrix.dotnetSuites.unit.selections['messaging-observability-components'], timeout: '5m' },
    'component-platform': { assembly: 'Full.NET.IntegrationTests', project: 'MessagingPlatformFocused', ...matrix.integration.shards['messaging-platform-health'] },
    'component-time': { assembly: 'Full.NET.IntegrationTests', project: 'MessagingPlatformFocused', ...matrix.integration.shards['messaging-time-observability'] },
    'component-time-alerts': { assembly: 'Full.NET.IntegrationTests', project: 'MessagingPlatformFocused', ...matrix.integration.shards['messaging-time-alerts'] },
    'component-topology': { assembly: 'Full.NET.IntegrationTests', project: 'MessagingPlatformFocused', ...matrix.integration.shards['messaging-rollback-topology'] },
    'component-process': { assembly: 'Full.NET.IntegrationTests', project: 'MessagingProcessFocused', ...matrix.integration.shards['messaging-process-recovery'] },
    'component-dual-worker-smoke': { assembly: 'Full.NET.IntegrationTests', project: 'MessagingProcessFocused', ...matrix.integration.shards['messaging-dual-worker-smoke'] },
    'component-dual-worker-soak': { assembly: 'Full.NET.IntegrationTests', project: 'MessagingProcessFocused', ...matrix.integration.shards['messaging-dual-worker-soak'] },
    'component-replay': { assembly: 'Full.NET.IntegrationTests', project: 'MessagingProcessFocused', ...matrix.integration.shards['messaging-replay-retention'] },
    'component-alerts': { assembly: 'Full.NET.IntegrationTests', project: 'MessagingPlatformFocused', ...matrix.integration.shards['messaging-live-alerts'] },
    'component-quorum': { assembly: 'Full.NET.IntegrationTests', project: 'MessagingQuorumFocused', ...matrix.integration.shards['messaging-three-broker-recovery'] },
    'component-control': { assembly: 'Full.NET.UnitTests', project: 'MessagingControlFocused', ...matrix.dotnetSuites.unit.selections['messaging-control-components'], timeout: '5m' },
  };
  if (args.length % 2 || Object.keys(flags).some(key => !['--stage', '--build-root', '--output', '--native-root', '--native-api-root', '--native-worker-root', '--partition-buffer'].includes(key)) || !selections[stage])
    throw new Error(`用法：node scripts/testing/run-messaging-reliability.mjs --stage ${Object.keys(selections).join('|')} --build-root <本轮隔离构建目录> --output <任务系统临时目录> [--native-root <发布目录> | --native-api-root <API发布根> --native-worker-root <Worker发布根>] [--partition-buffer 1|8]`);
  if (!flags['--build-root'] || !flags['--output']) throw new Error('必须提供已构建目录和任务证据目录。');
  if (['native-api', 'component-native-api'].includes(stage) !== Boolean(flags['--native-root'])) throw new Error('Native API 验收必须且只能使用明确的 --native-root。');
  if (nativeProcess ? !flags['--native-api-root'] || !flags['--native-worker-root'] : flags['--native-api-root'] || flags['--native-worker-root'])
    throw new Error('Native 故障矩阵必须且只能同时提供 --native-api-root 和 --native-worker-root。');
  const buildRoot = path.resolve(flags['--build-root']);
  let output = path.resolve(flags['--output']);
  const within = (parent, child) => { const relative = path.relative(parent, child); return relative === '' || !path.isAbsolute(relative) && relative !== '..' && !relative.startsWith(`..${path.sep}`); };
  if (within(repository, output) || !within(os.tmpdir(), output))
    throw new Error('证据目录必须位于仓库外的系统临时目录。');
  const selection = selections[stage];
  const assembly = path.join(buildRoot, 'bin', selection.project ?? selection.assembly, 'release', `${selection.assembly}.dll`);
  async function runtimeIdentity() {
    const identities = (await binaryIdentity(path.dirname(assembly))).map(item => ({ ...item, file: `test/${item.file}` }));
    const currentTooling = await Promise.all(toolingFiles.map(async file => ({ file: `orchestration/${file}`,
      sha256: digest(await readFile(path.join(repository, file))) })));
    if (JSON.stringify(startupTooling) !== JSON.stringify(currentTooling)) throw new Error('验收工具或矩阵在排队期间变化，禁止旧进程使用新摘要。');
    identities.push(...startupTooling);
    if (process.env.FULLNET_MESSAGING_SOURCE_MANIFEST)
      identities.push(...await focusedControllerSourceIdentity(process.env.FULLNET_MESSAGING_SOURCE_MANIFEST));
    if (prefetch8Soak || automaticTtl) identities.push(await streamFilterIdentity());
    if (['process', 'component-process', 'component-dual-worker-smoke', 'component-dual-worker-soak'].includes(stage))
      for (const role of ['Api', 'Worker'])
        for (const item of await binaryIdentity(path.join(buildRoot, 'bin', `Full.NET.Host.${role}`, 'release')))
          identities.push({ ...item, file: `${role}/${item.file}` });
    if (nativeProcess)
      for (const [role, flag] of [['Api', '--native-api-root'], ['Worker', '--native-worker-root']]) {
        const root = path.resolve(flags[flag]);
        identities.push(...await nativeMessagingSourceIdentity(root, role));
        for (const item of await binaryIdentity(path.join(root, 'publish'))) identities.push({ ...item, file: `${role}/${item.file}` });
        for (const file of ['manifest.json', `publish/Full.NET.Host.${role}`])
          identities.push({ file: `${role}/${file}`, sha256: digest(await readFile(path.join(root, file))) });
      }
    return identities;
  }
  await readFile(assembly); await mkdir(output, { recursive: true });
  // 每轮独立报告目录；发现失败或进程未写 TRX 时，绝不能误用上次留下的通过报告。
  output = await mkdtemp(path.join(output, `${stage}-`));
  const manifest = { stage, selection, head: execFileSync('git', ['rev-parse', 'HEAD'], { cwd: repository, encoding: 'utf8' }).trim(),
    automaticTtlProfile: automaticTtl ? { target: automaticTtl, brokerCleanupIntervalMs: 1000, retentionMs: 2000, segmentMs: 1000, timestampType: 'LogAppendTime' } : null,
    capacityProfile: prefetch8Soak || ['component-native-capacity-5', 'component-native-capacity-10'].includes(stage)
      ? { partitionBufferHighWatermark: partitionBuffer, partitionBufferLowWatermark: 0, partitionKeyConcurrencySlots: 1, offsetCommitMode: 'PerMessage', proxyNoDelay: true, proxySingleWrite: true, prefetch8Soak } : null,
    diffSha256: digest(execFileSync('git', ['diff', '--binary'], { cwd: repository, maxBuffer: 64 * 1024 * 1024, stdio: ['ignore', 'pipe', 'ignore'] })),
    sdk: execFileSync('dotnet', ['--version'], { cwd: repository, encoding: 'utf8' }).trim(),
    hardware: { platform: os.platform(), arch: os.arch(), cpus: os.cpus().length, model: os.cpus()[0].model, memoryBytes: os.totalmem() },
    startedAt: new Date().toISOString(), assemblySha256: digest(await readFile(assembly)) };
  await writeFile(path.join(output, `${stage}-input.json`), JSON.stringify(manifest, null, 2));
  if (stage === 'soak') {
    try { await writeFile(path.join(output, 'soak-admission.json'), JSON.stringify(await verifySoakBaseline(path.dirname(output), manifest.assemblySha256), null, 2)); }
    catch (error) {
      await writeFile(path.join(output, 'soak-rejected.json'), JSON.stringify({ at: new Date().toISOString(), reason: error.message, testsExecuted: false }, null, 2));
      throw error;
    }
  }
  // 原生编译与长跑仍共用资源池；明确增加排队预算，不侵占或放宽每个测试的执行期限。
  await withTestRun({ cwd: buildRoot, build: false, heavy: !['unit', 'component-unit', 'component-control'].includes(stage), timeoutMs: 150 * 60_000 }, async () => {
    const binaries = await runtimeIdentity();
    const admission = await verifyDualWorkerStageAdmission(stage, path.resolve(flags['--output']), digest(await readFile(assembly)), binaries);
    if (admission) await writeFile(path.join(output, 'dual-worker-admission.json'), JSON.stringify({ stage, baseline: admission }));
    await writeFile(path.join(output, 'binaries.json'), JSON.stringify(binaries, null, 2));
    const env = { ...testRunEnvironment(), FULLNET_TEST_REPOSITORY_ROOT: repository, FULLNET_MESSAGING_BUILD_ROOT: buildRoot,
      FULLNET_MESSAGING_EVIDENCE_DIR: output, FULLNET_TESTCONTAINERS_REUSE: '0', FULLNET_API_SCHEMA_TEMPLATE: '0',
      FULLNET_MESSAGING_SOAK_SECONDS: stage === 'soak' ? '3600' : '',
      FULLNET_MESSAGING_DUAL_WORKER_SECONDS: ['component-dual-worker-soak', 'component-native-dual-worker-soak', 'component-native-prefetch8-soak'].includes(stage) ? '3600' : ['component-dual-worker-smoke', 'component-native-dual-worker-smoke', 'component-native-prefetch8-smoke', 'component-native-capacity-5', 'component-native-capacity-10'].includes(stage) ? '60' : '',
      FULLNET_MESSAGING_MESSAGES_PER_SECOND: stage === 'component-native-capacity-5' ? '5' : stage === 'component-native-capacity-10' ? '10' : '1',
      FULLNET_MESSAGING_CAPACITY_PARTITION_BUFFER: String(partitionBuffer),
      FULLNET_MESSAGING_PREFETCH8_SOAK: prefetch8Soak ? '1' : '',
      FULLNET_MESSAGING_AUTOMATIC_TTL: automaticTtl ?? '',
      FULLNET_MESSAGING_ACCEPTANCE_STAGE: stage,
      FULLNET_MESSAGING_OWNED_CAPACITY: ['capacity-smoke', 'soak'].includes(stage) ? '1' : '',
      FULLNET_MESSAGING_NATIVE_ROOT: flags['--native-root'] ? path.resolve(flags['--native-root']) : '',
      FULLNET_MESSAGING_NATIVE_API_ROOT: flags['--native-api-root'] ? path.resolve(flags['--native-api-root']) : '',
      FULLNET_MESSAGING_NATIVE_WORKER_ROOT: flags['--native-worker-root'] ? path.resolve(flags['--native-worker-root']) : '' };
    const trx = path.join(output, `${stage}.trx`);
    const command = [assembly, '--no-ansi', '--progress', 'off', '--filter', selection.filter,
      '--minimum-expected-tests', String(selection.minimum), '--timeout', selection.timeout, '--report-trx', '--report-trx-filename', trx];
    await writeFile(path.join(output, `${stage}-command.json`), JSON.stringify({ executable: 'dotnet', args: command }, null, 2));
    const exit = await new Promise((resolve, reject) => {
      const child = spawn('dotnet', command, { cwd: repository, env, stdio: 'inherit', windowsHide: true });
      child.on('error', reject); child.on('close', code => resolve(code));
    });
    let report;
    try { report = summarizeTrxOutcomes(await readFile(trx, 'utf8')); }
    catch (error) {
      await writeFile(path.join(output, `${stage}-result.json`), JSON.stringify({ ...manifest,
        finishedAt: new Date().toISOString(), exit, report: null, failure: 'test_report_unavailable', detail: error.code }, null, 2));
      throw new Error(`测试进程退出 ${exit} 且无可用 TRX；不得报告通过。`, { cause: error });
    }
    const binariesUnchanged = JSON.stringify(binaries) === JSON.stringify(await runtimeIdentity());
    await writeFile(path.join(output, `${stage}-result.json`), JSON.stringify({ ...manifest, finishedAt: new Date().toISOString(), exit, report,
      assemblyUnchanged: manifest.assemblySha256 === digest(await readFile(assembly)), binariesUnchanged }, null, 2));
    if (exit !== 0 || report.total < selection.minimum || report.outcomes.Passed !== report.total
        || !binariesUnchanged || manifest.assemblySha256 !== digest(await readFile(assembly)))
      throw new Error('Messaging 聚焦验收失败或存在未通过项目；检查原始 TRX。');
    process.stdout.write(`${stage} 完成：${JSON.stringify(report)}\n`);
  });
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href)
  runMessagingReliability(process.argv.slice(2)).catch(error => { process.stderr.write(`${error.stack}\n`); process.exitCode = 1; });
