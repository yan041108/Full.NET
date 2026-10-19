import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import {createHash} from 'node:crypto';
import {pathToFileURL} from 'node:url';
import {apiRole, sqlCounterDelta, readMetrics, selectSqlCounter} from './metrics.mjs';
import {cleanVerifiedCapacityOutput} from './output.mjs';
import {logEvidence} from './log-evidence.mjs';
import {sampleIdentityEvidence} from './reuse.mjs';

// 独立重读原始样本和真实进程终态，不把运行器自己写的 acceptance 当作充分证据。
const output = path.resolve(process.argv[2]), terminalPath = path.resolve(process.argv[3]);
const json = file => JSON.parse(fs.readFileSync(file, 'utf8').replace(/^\uFEFF/, ''));
const sha = bytes => createHash('sha256').update(bytes).digest('hex');
const state = json(path.join(output, 'state.json')), recipe = json(path.join(output, 'recipe.json'));
const parent = json(terminalPath);
const boundaryPath = process.argv[4] ? path.resolve(process.argv[4]) : null;
const boundary = boundaryPath ? json(boundaryPath) : null;
if (recipe.providerCeilings) {
  assert.ok(boundary, 'Reduced provider profile requires the preserved diagnosed boundary');
  assert.equal(boundary.source, state.source); assert.equal(boundary.accepted, false); assert.equal(boundary.actualParentExit, 1);
  assert.deepEqual(recipe.providerCeilings, boundary.subsequentBounds);
  const producer = state.sampleReuseProducer.directory;
  assert.equal(sha(fs.readFileSync(path.join(producer, 'state.json'))), boundary.stateSha256);
}
assert.equal(parent.Pid, state.pid); assert.equal(parent.ExitCode, 0); assert.equal(parent.ObservedViaLiveProcessHandle, true);
assert.equal(state.status, 'completed-locks-released');
for (const field of ['acceptance', 'cleanupConfirmed', 'resourceReleaseConfirmed', 'workloadCompleted']) assert.equal(state[field], true, field);
assert.deepEqual(state.remainingNewContainers, []); assert.equal(state.volumeOwners, '');
assert.equal(sha(fs.readFileSync(path.join(output, 'recipe.json'))), state.recipeSha256);
for (const [file, expected] of Object.entries(state.helperHashes)) assert.equal(sha(fs.readFileSync(path.join(output, 'harness', file))), expected, file);
const {summarizeSample, verifyLifecycle, missingGridSqlRoles} = await import(pathToFileURL(path.join(output, 'harness/metrics.mjs')));
const budgeted = !!state.helperHashes['database-budget.json'];
const databaseBudget = budgeted ? json(path.join(output, 'harness/database-budget.json')) : null;
const budgetTools = budgeted ? await import(pathToFileURL(path.join(output, 'harness/database-budget.mjs'))) : null;
if (budgeted) {
  budgetTools.validateDatabaseBudget(databaseBudget);
  assert.deepEqual(state.databaseBudget, {profile: databaseBudget, sha256: state.helperHashes['database-budget.json']});
  assert.equal(fs.readFileSync(path.join(output, 'native-log-copy.exit'), 'utf8').trim(), '0');
}
const activeScope = 'Maximum sum of separately exported API gauges; node timestamps are asynchronous; not a simultaneous global peak or a guaranteed lower bound, and never VUs';
assert.equal(sha(fs.readFileSync(recipe.apiExecutable)), recipe.apiSha256);
const manifest = json(recipe.manifest); assert.equal(sha(JSON.stringify(manifest.entries)), state.source);
for (const [file, expected] of manifest.entries) {
  const target = path.join(manifest.source, file);
  if (expected === null) assert.equal(fs.existsSync(target), false, file);
  else assert.equal(sha(fs.readFileSync(target)), expected, file);
}
for (const process of state.processes) {
  const actual = json(path.join(output, process.name + '-terminal.json'));
  assert.equal(actual.observedViaLiveChildHandle, true); assert.equal(actual.pid, process.pid);
  assert.equal(actual.exitCode, process.exitCode);
  assert.ok(process.name.startsWith('k6-') ? [0, 99].includes(actual.exitCode) : actual.exitCode === 0);
}
const lifecycle = verifyLifecycle(fs.readFileSync(path.join(output, 'discovery.log'), 'utf8'),
  fs.readFileSync(path.join(output, 'lifecycle/lifecycle.trx'), 'utf8'));
const samples = [];
function reportedBytes(value) {
  const m = /^([\d.]+)\s*(B|kB|KB|KiB|MB|MiB|GB|GiB|TB|TiB)$/.exec(value.trim());
  if (!m) return null;
  return Number(m[1]) * ({B: 1, kB: 1e3, KB: 1e3, KiB: 1024, MB: 1e6, MiB: 1024 ** 2, GB: 1e9, GiB: 1024 ** 3, TB: 1e12, TiB: 1024 ** 4}[m[2]]);
}
for (const row of state.samples) {
  const dir = path.join(output, row.folder), bytes = fs.readFileSync(path.join(dir, 'summary.json'));
  assert.equal(sha(bytes), row.summarySha256);
  const identityEvidence = sampleIdentityEvidence(output, row);
  let sampleParser = summarizeSample, sampleReadMetrics = readMetrics;
  if (row.reusedFrom) {
    assert.equal(sha(fs.readFileSync(path.join(dir, 'producer-metrics.mjs'))), row.reusedFrom.parserSha256);
    assert.equal(sha(fs.readFileSync(path.join(dir, 'reused-k6-terminal.json'))), row.reusedFrom.terminalSha256);
    assert.equal(sha(fs.readFileSync(path.join(row.reusedFrom.directory, 'state.json'))), row.reusedFrom.stateSha256);
    const terminal = json(path.join(dir, 'reused-k6-terminal.json'));
    assert.equal(terminal.observedViaLiveChildHandle, true); assert.equal(terminal.exitCode, row.result.exitCode);
    assert.equal(row.reusedFrom.acceptedParentClaim, false);
    const originalParser = await import(pathToFileURL(path.join(dir, 'producer-metrics.mjs')));
    assert.equal(typeof originalParser.apiIdentityMap, 'function');
    originalParser.apiIdentityMap(identityEvidence.identities, row.context);
    sampleParser = originalParser.summarizeSample; sampleReadMetrics = originalParser.readMetrics;
  }
  const parse = raw => sampleReadMetrics(raw, identityEvidence.identities, row.context);
  const prom = file => parse(fs.readFileSync(path.join(dir, file), 'utf8'));
  const snapshots = fs.readFileSync(path.join(dir, 'metrics-samples.prom'), 'utf8').split(/^# sampled_at .*$/m).filter(s => s.trim()).map(parse);
  const before = prom('before.prom'), after = prom('after.prom');
  const rawSummary = JSON.parse(bytes);
  const result = sampleParser(rawSummary, row.result.exitCode, snapshots, before, after, recipe.sqlCounterMetric);
  assert.deepEqual(result, row.result);
  if (row.name !== 'warmup') assert.ok(['within-local-budget', 'budget-exceeded'].includes(result.classification));
  assert.ok(fs.statSync(path.join(dir, 'container-stats.jsonl')).size > 0);
  for (const name of ['database-before.json', 'database-after.json']) assert.ok(json(path.join(dir, name)).at);
  if (row.name === 'mixed-writes') {
    assert.equal(json(path.join(dir, 'write-warmup.json')).observations.length, 2);
    assert.ok(row.interventions.length > 0);
    assert.ok(row.interventions.every(r => r.propagationMs >= 0 && r.propagationMs < 10_000 && r.requests >= 3));
  }
  if (row.name === 'redis-delay') {
    assert.equal(row.interventions.length, 1); assert.equal(row.interventions[0].result, 'OK');
  }
  const sqlMetric = selectSqlCounter(result.sqlCounterMetric);
  const sqlStatements = Object.fromEntries([...new Set(after.filter(r => apiRole(r) && r.name === sqlMetric).map(r => r.labels.statement_name))]
    .map(name => {
      try { return [name, sqlCounterDelta(before, after, sqlMetric, r => apiRole(r) && r.labels.statement_name === name)]; }
      catch { return [name, null]; }
    }));
  const containerPeaks = {};
  const stats = fs.readFileSync(path.join(dir, 'container-stats.jsonl'), 'utf8').trim().split(/\r?\n/).map(JSON.parse);
  for (const snapshot of stats) for (const container of snapshot.rows) {
    const entry = containerPeaks[container.Name] ??= {cpuPercentPeak: null, reportedMemoryBytesPeak: null};
    const cpu = Number(container.CPUPerc.replace('%', '')), mem = reportedBytes(container.MemUsage.split('/')[0]);
    if (Number.isFinite(cpu)) entry.cpuPercentPeak = Math.max(entry.cpuPercentPeak ?? -Infinity, cpu);
    if (mem !== null) entry.reportedMemoryBytesPeak = Math.max(entry.reportedMemoryBytesPeak ?? -Infinity, mem);
  }
  const max = rawSummary.metrics?.http_req_duration?.values?.max;
  samples.push({...row, result: {...result, rawScopeMetadata: result.activeScope, activeScope},
    observedHttpMaxMs: Number.isFinite(max) ? max : null, sqlStatements, sampledContainerPeaks: containerPeaks});
}
for (const provider of ['SqlServer', 'MySql']) {
  const control = samples.filter(s => s.context === provider + '-C0'), cached = samples.filter(s => s.context === provider + '-S2');
  for (const name of ['warmup', 'control-16-1', 'control-16-2']) assert.ok(control.some(s => s.name === name));
  const firstLevels = recipe.selection === 'connection-budget-recheck'
    ? {closed: 64, open: provider === 'MySql' ? 200 : 500} : {closed: 1, open: 50};
  for (const name of ['warmup', 'closed-' + firstLevels.closed, 'open-' + firstLevels.open, 'mixed-writes', 'redis-delay'])
    assert.ok(cached.some(s => s.name === name));
  for (const mode of ['C0', 'S2']) {
    const dir = path.join(output, provider + '-' + mode);
    const identities = json(path.join(dir, 'api-identities.json'));
    for (const identity of identities) {
      const log = logEvidence(path.join(output, 'native-aot/linux-x64/test-logs', identity.logFileName), identity.announcement);
      assert.equal(log.occurrences, 1);
    }
    assert.deepEqual(json(path.join(dir, 'grid-baseline-readiness.json')).missing, []);
    assert.deepEqual(missingGridSqlRoles(readMetrics(fs.readFileSync(path.join(dir, 'grid-baseline-readiness.prom'), 'utf8'), identities, path.basename(dir)), recipe.sqlCounterMetric), []);
    assert.equal(json(path.join(dir, 'lifecycle-completed.json')).completed, true);
    for (const suffix of ['', '.tmp']) assert.equal(fs.existsSync(path.join(dir, 'private-ready.json' + suffix)), false);
    const policy = json(path.join(dir, 'runtime-policies.json')).find(p => p.entryName === 'settings.grid-preference');
    assert.equal(policy.consistencyClass.toUpperCase(), mode); assert.equal(policy.failSafeEnabled, false);
    const experiment = json(path.join(dir, 'experiment-config.json'));
    assert.equal(experiment.globalApiLimitEnabled, true); assert.equal(experiment.globalApiPermitLimitPerMinute, 1_000_000);
    assert.equal(experiment.apiInstances, 2); assert.equal(experiment.workerInstances, 0);
    if (budgeted) {
      budgetTools.verifyExperimentDatabaseBudget(experiment, databaseBudget, state.helperHashes['database-budget.json']);
      budgetTools.verifyDatabaseAdmissionRoles(readMetrics(fs.readFileSync(path.join(dir, 'telemetry-readiness.prom'), 'utf8'), identities, path.basename(dir)));
      if (provider === 'MySql') for (const row of [...control, ...cached]) {
        const snapshot = json(path.join(output, row.folder, 'database-after.json'));
        assert.ok(Number.isSafeInteger(snapshot.serverConnectionLimit) && snapshot.serverConnectionLimit >= databaseBudget.TotalBudget);
        const peak = Number(snapshot.database.find(r => r.Variable_name === 'Max_used_connections')?.Value);
        assert.ok(Number.isFinite(peak) && peak <= databaseBudget.TotalBudget, 'Observed server connection peak exceeds declared application budget');
      }
    }
  }
}
const proof = {at: new Date().toISOString(), experimentCompleted: true, capacityStatus: 'Capacity-not-verified', source: state.source,
  consumerInputFingerprint: state.consumerInputFingerprint, consumerOverlay: state.consumerOverlay,
  databaseBudget: state.databaseBudget ?? null, selection: recipe.selection ?? 'full',
  apiSha256: recipe.apiSha256, scope: state.scope, host: state.host, images: state.images, lifecycle,
  measuredRequests: samples.filter(s => s.name !== 'warmup').reduce((n, s) => n + s.result.requests, 0),
  measuredSamples: samples.filter(s => s.name !== 'warmup').length,
  sameRunAllSamples: !samples.some(s => s.reusedFrom), reusedSamples: samples.filter(s => s.reusedFrom).length,
  sampleReuseProducer: state.sampleReuseProducer ?? null,
  providerCeilings: recipe.providerCeilings ?? null,
  diagnosedBoundary: boundary ? {file: boundaryPath, sha256: sha(fs.readFileSync(boundaryPath)), ...boundary} : null,
  budgetExceededSamples: samples.filter(s => s.result.classification === 'budget-exceeded').map(s => ({context: s.context, name: s.name})),
  samples, stateSha256: sha(fs.readFileSync(path.join(output, 'state.json'))), supervisorSha256: sha(fs.readFileSync(terminalPath)),
  limitations: ['One authenticated hot key, two Native APIs, Worker=0; no multi-tenant working-set or L1 memory slope measurement',
    'Shared host and generator CPU budget; open-loop drops may reflect generator or service limits',
    activeScope,
    'C0/S2 configure the same binary; both use isolated 1,000,000/min/node quota; production default quota unchanged; no historical-version improvement claim',
    'SQL totals include authorization, auditing and control calls; peer timing includes PUT and polling',
    'Redis command delay is not a disconnect or lost-message test; no 2K/5K/10K/Soak certification']};
fs.writeFileSync(path.join(output, 'capacity-baseline-proof.json'), JSON.stringify(proof, null, 2));
const n = value => value === null ? 'unknown' : Number(value).toFixed(2);
const budgetNote = budgeted ? `连接预算：每 API 池上限 ${databaseBudget.ApiMaxPoolSize}，普通许可 ${databaseBudget.PermitLimit}、健康预留 ${databaseBudget.HealthReserve}、独立授权预留 ${databaseBudget.AuthorizationReadReserve}；两个 API 池合计 ${databaseBudget.ApiMaxReplicas * databaseBudget.ApiMaxPoolSize}。声明未运行 Worker 的 ${databaseBudget.WorkerMaxReplicas * databaseBudget.WorkerMaxPoolSize} 个池连接和运维预留 ${databaseBudget.MigrationReserve}，总预算 ${databaseBudget.TotalBudget}，声明服务端上限 ${databaseBudget.ServerConnectionLimit}，实际 MySQL 上限另经快照核对。实验队列 ${databaseBudget.QueueLimit}、等待 ${databaseBudget.AcquireTimeoutMilliseconds}ms；生产 Helm API 默认队列为零，不能声称生产配置等价。预算 SHA-256：\`${state.helperHashes['database-budget.json']}\`。` : '';
const selectionNote = recipe.selection === 'connection-budget-recheck'
  ? '本批为连接预算聚焦复测：SQL Server 闭环 64 VU、开环 500 RPS；MySQL 闭环 64/128 VU、开环 200/500 RPS，首次超预算即停止升压，复测最高通过档位，另保留对照与写入/Redis 暂停场景。未重跑完整低档位矩阵或 MySQL 1000 RPS。'
  : '';
const sourceNote = proof.sameRunAllSamples ? '全部样本均为本批实跑。'
  : `其中 ${proof.reusedSamples} 个来自前批有效样本，不能称为同轮全部通过；前批父进程失败状态保留。`;
const boundaryNote = boundary ? `**必须保留的失败边界：** MySQL ${boundary.cases.map(c => `${c.row.name} 在有效凭据下出现 ${c.row.result.authorizationErrors}/${c.row.result.requests} 次 401${c.row.result.droppedIterations ? '、' + c.row.result.droppedIterations + ' 次调度丢弃' : ''}`).join('；')}。权威会话查询记录数据库错误 1040，原父批实际均退出 1，未通过且未计入下表的完整样本。本批将 MySQL 后续负载限定为闭环不超过 ${recipe.providerCeilings.MySql.closed} VU、开环不超过 ${recipe.providerCeilings.MySql.open} RPS，实际预算见表；MySQL 开环 1000 RPS 未执行，不能宣称原矩阵全通过。参见[独立失败边界凭证](${boundaryPath.replaceAll('\\', '/')})。` : '';
const rows = samples.filter(s => s.name !== 'warmup').map(s => `| ${s.context} | ${s.name} | ${s.reusedFrom ? '前批有效样本' : '本批实跑'} | ${s.result.requests} | ${n(s.result.successfulThroughputRps)} | ${s.result.rateLimitedResponses} | ${n(s.result.latencyMs['p(50)'])} | ${n(s.result.latencyMs['p(95)'])} | ${n(s.result.latencyMs['p(99)'])} | ${n(s.observedHttpMaxMs)} | ${n(s.result.sampledApiActivePeak)} | ${n(s.result.errorRate * 100)}% | ${s.result.classification} |`);
const md = `# 缓存本地容量基线\n\n状态：实验完成，**Capacity-not-verified**。容量实验输入 \`${state.source}\`，Native API SHA-256 \`${recipe.apiSha256}\`。测试消费者正常编译当前 Main，仅显式链接容量生命周期驱动，输入指纹 \`${state.consumerInputFingerprint}\`。正式样本 ${proof.measuredSamples} 个，实测请求 ${proof.measuredRequests} 次，其中 ${proof.reusedSamples} 个来自前批有效样本，不能称为同轮全部通过；前批父进程失败状态保留。生命周期 ${lifecycle.counters.passed}/2 通过只代表驱动与排空完成，压力预算分别见下表。两个实验实例的限流均为每分钟 1,000,000 次，认证、会话及租户校验保留，生产默认额度未修改。\n\n| 上下文 | 样本 | 来源 | 请求数 | 成功 RPS | 429 | P50 ms | P95 ms | P99 ms | Max ms | 异步在途值之和最大值 | 错误率 | 本地预算 |\n| --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |\n${rows.join('\n')}\n\n${proof.limitations.map(s => '- ' + s).join('\n')}\n\n原始 OTLP、k6、SQL/Redis、容器和主机资源证据在各样本目录。缺失的可选指标见每个 result.json 的 unavailableOptionalMetrics，不能按零解释。预算超限的原始退出99被保留，不算成功样本。\n`;
const report = md.replace(`其中 ${proof.reusedSamples} 个来自前批有效样本，不能称为同轮全部通过；前批父进程失败状态保留。`, sourceNote);
fs.writeFileSync(path.join(output, 'capacity-baseline-report.md'), report.replace('\n\n| 上下文',
  '\n\n' + [budgetNote, selectionNote, boundaryNote].filter(Boolean).join('\n\n') + '\n\n| 上下文'));
console.log(JSON.stringify({experimentCompleted: true, measuredSamples: proof.measuredSamples, measuredRequests: proof.measuredRequests,
  budgetExceededSamples: proof.budgetExceededSamples, capacityStatus: proof.capacityStatus}));
if (fs.existsSync(path.join(output, 'output-owner.json'))) {
  // 原始证据全部重验后才输出报告并回收；调用方读取标准输出，不依赖本地产物永久存在。
  fs.writeSync(1, JSON.stringify(proof) + '\n' + fs.readFileSync(path.join(output, 'capacity-baseline-report.md'), 'utf8') + '\n');
  cleanVerifiedCapacityOutput(output, state, parent);
}
