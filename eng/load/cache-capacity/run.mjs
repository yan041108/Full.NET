import fs from 'node:fs';
import path from 'node:path';
import {createHash, randomUUID} from 'node:crypto';
import {spawn, spawnSync} from 'node:child_process';
import {setTimeout as delay} from 'node:timers/promises';
import {pathToFileURL} from 'node:url';
import {apiRole, readMetrics, summarizeSample, verifyLifecycle, missingGridSqlRoles, selectSqlCounter} from './metrics.mjs';
import {guestMemory, memoryWithinBudget, imageIdFromInspect, collectorEndpoint, profileLevels, privateVolumeOwnerLabel, collectorDnsAlias, collectorOtlpEndpoint} from './resource-budget.mjs';
import {eligibleSample, reusableSamples, verifyContextPolicies, copySampleIdentityEvidence} from './reuse.mjs';
import {writeControl} from './control-files.mjs';
import {validateDatabaseBudget, verifyExperimentDatabaseBudget, verifyDatabaseAdmissionRoles} from './database-budget.mjs';
import {createCapacityOutput} from './output.mjs';
import {readEngineGuestMemory,preserveUnknownBoundary} from './guest-memory.mjs';

// 显式清单绑定现有源码、已验收发布物和可复用构建卷；不复制整仓或从脏工作区猜测证据身份。
const configPath = path.resolve(process.argv[2] ?? '');
if (!fs.statSync(configPath).isFile()) throw Error('Expected an explicit capacity recipe JSON');
const configBytes = fs.readFileSync(configPath), c = JSON.parse(configBytes);
const sqlCounterMetric = selectSqlCounter(c.sqlCounterMetric);
const root = path.resolve(c.workspace), output = path.resolve(c.output), source = JSON.parse(fs.readFileSync(c.manifest));
if (path.resolve(source.source) !== root) throw Error('Current Main source manifest required; no copied source workspace');
const volumeOwnerLabel = privateVolumeOwnerLabel(c.volumeOwnerLabel);
const sha = bytes => createHash('sha256').update(bytes).digest('hex');
const json = p => JSON.parse(fs.readFileSync(p));
const slash = p => path.resolve(p).replaceAll('\\', '/');
const script = import.meta.dirname, files = ['guest-memory.mjs', 'driver.targets', 'NativeCacheCapacityDriver.cs', 'NativeApiProcessHost.cs', 'scenario.js', 'metrics.mjs', 'resource-budget.mjs', 'reuse.mjs', 'log-evidence.mjs', 'control-files.mjs', 'database-budget.mjs', 'database-budget.json', 'output.mjs', 'driver.sh', 'run.mjs'];
const helperSource = f => f === 'NativeApiProcessHost.cs' ? path.join(root, 'tests/Full.NET.IntegrationTests/NativeAot', f) : path.join(script, f);
const helperHashes = Object.fromEntries(files.map(f => [f, sha(fs.readFileSync(helperSource(f)))]));
const guardsPath = path.join(root, 'tests/performance/cache-capacity.test.mjs');
const outputGuardsPath = path.join(root, 'tests/performance/cache-capacity-output.test.mjs');
const phaseGuardsPath = path.join(root, 'tests/performance/cache-connection-phases.test.mjs');
const guestMemoryGuardsPath = path.join(root, 'tests/performance/cache-capacity-guest-memory.test.mjs');
const guardPaths = [guardsPath, outputGuardsPath, phaseGuardsPath, guestMemoryGuardsPath];
const guardIdentity = () => sha(JSON.stringify(guardPaths.map(file => sha(fs.readFileSync(file)))));
const guardsHash = guardIdentity();
const databaseBudget = json(path.join(script, 'database-budget.json'));
validateDatabaseBudget(databaseBudget);
for (const provider of ['SqlServer', 'MySql']) for (const model of ['closed', 'open'])
  profileLevels(provider, model, c.selection, c.providerCeilings?.[provider]?.[model]);
if (!/^fullnet-cache-capacity-[a-z0-9-]+$/.test(c.owner)) throw Error('Invalid isolated resource owner');
if (c.consumerReuse) {
  const previousState = JSON.parse(fs.readFileSync(c.consumerReuse.state));
  const previousRecipe = JSON.parse(fs.readFileSync(path.join(path.dirname(c.consumerReuse.state), 'recipe.json')));
  if (previousState.source !== c.source || previousState.cleanupConfirmed !== true || previousRecipe.volume !== c.volume
    || previousRecipe.volumeOwner !== c.volumeOwner) throw Error('Reused consumer source/volume provenance mismatch');
  if (['driver.targets', 'NativeCacheCapacityDriver.cs', 'NativeApiProcessHost.cs'].some(f => previousState.helperHashes[f] !== helperHashes[f]))
    throw Error('Reused consumer inputs differ from current driver');
  if (fs.readFileSync(c.consumerReuse.buildExit, 'utf8').trim() !== '0') throw Error('Consumer build did not actually pass');
}
if (c.referenceReuse) throw Error('Reference-only reuse is unsupported; use a fresh full-reference build or verified consumer reuse');
const state = {pid: process.pid, startedAt: new Date().toISOString(), status: 'preflight',
  owner: c.owner, volumeOwnerLabel, recipeSha256: sha(configBytes), source: c.source, helperHashes, guardsHash, samples: [], processes: [],
  scope: 'Frozen Native API, two instances, one authenticated hot Grid key; Worker=0; same-host local experiment',
  capacityStatus: 'Capacity-not-verified', acceptance: false, cleanupConfirmed: false, resourceReleaseConfirmed: false};
state.databaseBudget = {profile: databaseBudget, sha256: helperHashes['database-budget.json']};
state.consumerInputFingerprint = sha(JSON.stringify({baseSource: c.source, helperHashes,
  build: {sdk: '10.0.402', configuration: 'Release', buildProjectReferences: true}}));
state.consumerOverlay = {nativeHostHelper: 'tests/Full.NET.IntegrationTests/NativeAot/NativeApiProcessHost.cs',
  sha256: helperHashes['NativeApiProcessHost.cs'], scope: 'Current Main normal compilation; only explicit capacity driver is linked'};
const handles = [];
const reused = c.reuseSamples ? await reusableSamples(c.reuseSamples, c, helperHashes, c.reuseParentTerminal) : null;
if (reused) state.sampleReuseProducer = {directory: reused.directory, stateSha256: reused.stateSha256,
  parentTerminal: reused.parentTerminal, parentTerminalSha256: reused.parentTerminalSha256, actualParentExit: reused.actualParentExit,
  previousAcceptance: reused.previousAcceptance, acceptedParentClaim: false};
// 来源和复用关系先通过只读检查，再创建少量本轮证据；无效配方不留下构建/源码副本。
createCapacityOutput(c.output, root);
fs.mkdirSync(path.join(output, 'harness'));
for (const f of files) fs.copyFileSync(helperSource(f), path.join(output, 'harness', f));
fs.copyFileSync(configPath, path.join(output, 'recipe.json'));
fs.copyFileSync(c.sourceHashes, path.join(output, 'source.sha256'));
fs.copyFileSync(c.runSettings, path.join(output, 'sequential.runsettings'));
if (c.consumerReuse) fs.copyFileSync(c.consumerReuse.checksums, path.join(output, 'reuse-consumer.sha256'));
const save = () => fs.writeFileSync(path.join(output, 'state.json'), JSON.stringify(state, null, 2));
const write = (p, value) => { fs.writeFileSync(p + '.tmp', JSON.stringify(value, null, 2)); fs.renameSync(p + '.tmp', p); };
function command(executable, args, options = {}) {
  const p = spawnSync(executable, args, {encoding: 'utf8', windowsHide: true, timeout: 30_000, maxBuffer: 8 * 1024 * 1024, ...options});
  if (p.error || p.status !== 0) throw Error(executable + ' failed: ' + (p.error?.message ?? p.stderr));
  return p.stdout.trim();
}
const docker = args => command('docker', args);
function inputs() {
  const listed = command('git', ['ls-files', '-z', '--cached', '--others', '--exclude-standard'], {cwd:root}).split('\0').filter(Boolean);
  const implicit = listed.filter(p => /\.[a-z]*proj$/iu.test(p)).map(p => p + '.user');
  const inventory = [...new Set([...listed, ...implicit])].filter(p => !/^(?:docs|rules|\.tmp|\.cache)\//u.test(p) && !(p.endsWith('.md') && !p.includes('/'))).sort();
  if (JSON.stringify(inventory) !== JSON.stringify(source.entries.map(([p]) => p).sort())) throw Error('Current Main source inventory changed');
  if (sha(JSON.stringify(source.entries)) !== c.source) throw Error('Frozen source manifest identity mismatch');
  for (const [p, h] of source.entries) {
    const file = path.join(source.source, p);
    if (h === null ? fs.existsSync(file) : !fs.existsSync(file) || sha(fs.readFileSync(file)) !== h) throw Error('Frozen source changed: ' + p);
  }
  if (sha(fs.readFileSync(c.apiExecutable)) !== c.apiSha256) throw Error('Accepted API identity changed');
  const elf = fs.readFileSync(c.apiExecutable).subarray(0, 4);
  if (elf.toString('hex') !== '7f454c46') throw Error('Native API must be an ELF executable');
  for (const f of files) if (sha(fs.readFileSync(helperSource(f))) !== helperHashes[f]) throw Error('Harness changed during experiment');
  if (guardIdentity() !== guardsHash) throw Error('Safety guard tests changed during experiment');
}
async function memory() {
  if (process.platform !== 'win32') throw Error('This recipe requires explicit Windows host memory evidence');
  const ps = "$m=Get-CimInstance Win32_OperatingSystem; [Console]::OutputEncoding=[Text.Encoding]::UTF8; @{FreePhysicalBytes=[long]$m.FreePhysicalMemory*1024; TotalPhysicalBytes=[long]$m.TotalVisibleMemorySize*1024; FreeVirtualBytes=[long]$m.FreeVirtualMemory*1024; TotalVirtualBytes=[long]$m.TotalVirtualMemorySize*1024; LogicalProcessors=[Environment]::ProcessorCount}|ConvertTo-Json -Compress";
  const host = JSON.parse(command('powershell.exe', ['-NoProfile', '-NonInteractive', '-EncodedCommand', Buffer.from(ps, 'utf16le').toString('base64')]));
  try { host.Guest = await readEngineGuestMemory({docker, imageId: c.sdkImageId, owner: c.owner, container: state.sdkName,
    journal: row => fs.appendFileSync(path.join(output, 'guest-memory.jsonl'), JSON.stringify({...row, at:new Date().toISOString()}) + '\n')}); }
  catch (error) { if (error.retainResources) preserveUnknownBoundary(error, e => { state.cleanupWarning = e.stack; save(); }); throw error; }
  return host;
}
function launch(name, args, env) {
  const row = {name, args, startedAt: new Date().toISOString(), exitCode: null};
  state.processes.push(row);
  const fd = fs.openSync(path.join(output, name + '.log'), 'wx');
  const child = spawn('docker', args, {env, stdio: ['ignore', fd, fd], windowsHide: true}); row.pid = child.pid; save();
  const completion = new Promise(resolve => {
    child.once('error', error => { row.error = error.message; });
    child.once('close', (code, signal) => { fs.closeSync(fd); row.exitCode = code; row.signal = signal;
      row.finishedAt = new Date().toISOString(); row.observedViaLiveChildHandle = true;
      write(path.join(output, name + '-terminal.json'), row); save(); resolve(row); });
  });
  const handle = {child, row, completion}; handles.push(handle); return handle;
}
save();
const {withTestRun, testRunEnvironment} = await import(pathToFileURL(path.join(source.source, 'scripts/testing/test-run-context.mjs')));
const queueDeadline = Date.now() + (c.queueMinutes ?? 90) * 60_000;
try {
  inputs();
  // AI 的两个衔接窗口必须全部结束；不能争抢中间释放的短暂空档。
  if (c.resourceGate) {
    state.status = 'waiting-predecessor-full-window'; save();
    while (true) {
      let gate;
      try { gate = json(c.resourceGate); }
      catch (error) { if (error.code !== 'ENOENT' && !(error instanceof SyntaxError)) throw error; }
      if (gate?.resourceReleaseConfirmed === true && gate?.cleanupConfirmed === true) break;
      if (Date.now() >= queueDeadline) throw Error('Predecessor resource queue deadline elapsed');
      await delay(10_000);
    }
    state.resourcePredecessor = {file: c.resourceGate, sha256: sha(fs.readFileSync(c.resourceGate))};
  }
  state.status = 'waiting-formal-heavy'; save();
  await withTestRun({cwd: root, heavy: true, timeoutMs: Math.max(1, queueDeadline - Date.now())}, async () => {
    state.admittedAt = new Date().toISOString(); save();
    inputs();
    const guards = spawnSync(process.execPath, ['--test', ...guardPaths],
      {encoding: 'utf8', windowsHide: true, timeout: 60_000});
    fs.writeFileSync(path.join(output, 'guard-tests.log'), (guards.stdout ?? '') + (guards.stderr ?? ''));
    write(path.join(output, 'guard-tests-terminal.json'), {exitCode: guards.status, signal: guards.signal, error: guards.error?.message ?? null});
    if (guards.error || guards.status !== 0) throw Error('Capacity safety guards did not pass');
    const memoryDeadline = Date.now() + 180_000;
    let host;
    while (true) {
      host = await memory(); state.host = host;
      if (memoryWithinBudget(host, 'start')) break;
      state.status = 'waiting-memory-headroom'; save();
      if (Date.now() >= memoryDeadline) throw Error('Insufficient host/Engine guest memory headroom for bounded capacity batch');
      await delay(5000);
    }
    const baseline = new Set(docker(['ps', '-a', '--format', '{{.ID}}']).split(/\s+/).filter(Boolean));
    if (docker(['volume', 'inspect', c.volume, '--format', '{{index .Labels "' + volumeOwnerLabel + '"}}']) !== c.volumeOwner
      || docker(['ps', '-a', '--filter', 'volume=' + c.volume, '--format', '{{.ID}}'])) throw Error('Private build volume is foreign or occupied');
    state.images = {};
    for (const [role, image] of Object.entries(c.images)) state.images[role] = imageIdFromInspect(docker(['image', 'inspect', image]));
    if (state.images.sdk !== c.sdkImageId) throw Error('SDK image identity mismatch');
    if (reused) for (const role of ['sdk', 'k6', 'collector']) if (reused.images[role] !== state.images[role]) throw Error('Reused sample image differs: ' + role);
    const network = c.owner + '-net', collector = c.owner + '-otel', sdk = c.owner + '-sdk';
    const label = 'fullnet.cache-capacity.owner=' + c.owner;
    const collectorConfig = `receivers:\n  otlp:\n    protocols:\n      grpc:\n        endpoint: 0.0.0.0:4317\nexporters:\n  prometheus:\n    endpoint: 0.0.0.0:8889\n    resource_to_telemetry_conversion:\n      enabled: true\n  nop: {}\nservice:\n  pipelines:\n    metrics:\n      receivers: [otlp]\n      exporters: [prometheus]\n    traces:\n      receivers: [otlp]\n      exporters: [nop]\n`;
    fs.writeFileSync(path.join(output, 'collector.yaml'), collectorConfig);
    let driver;
    try {
      docker(['network', 'create', '--label', label, network]);
      docker(['run', '-d', '--name', collector, '--label', label, '--network', network, '--network-alias', collectorDnsAlias, '--cpus', '1', '--memory', '512m', '--memory-swap', '512m',
        '-p', '127.0.0.1::8889', '-v', slash(path.join(output, 'collector.yaml')) + ':/etc/otelcol-contrib/config.yaml:ro', state.images.collector]);
      let endpoint = collectorEndpoint(docker(['port', collector, '8889/tcp']));
      const telemetry = async () => {
        const response = await fetch(endpoint, {signal: AbortSignal.timeout(10_000)});
        if (!response.ok) throw Error('Collector scrape failed');
        return response.text();
      };
      driver = launch('native-driver', ['run', '--rm', '--name', sdk, '--label', label, '--network', network,
        '--cpus', '4', '--memory', '8g', '--memory-swap', '8g', '--add-host', 'host.docker.internal:host-gateway',
        '-v', c.volume + ':/work', '-v', slash(root) + ':/src:ro', '-v', 'E:/FullNet-validation-cache/r12e-nuget-packages:/retained:ro', '-v', 'C:/Users/Administrator/.nuget/packages:/windows-nuget:ro', '-v', slash(output) + ':/src/artifacts', '-v', slash(output) + ':/task:ro', '-v', slash(output) + ':/results',
        '-v', slash(path.dirname(c.apiExecutable)) + ':/published/api:ro', '-v', '/var/run/docker.sock:/var/run/docker.sock',
        '-e', 'FULLNET_NATIVE_AOT_PUBLISH_DIR=/published/api', '-e', 'FULLNET_CAPACITY_RESULTS=/results',
        '-e', 'FULLNET_CAPACITY_OWNER=' + c.owner, '-e', 'TESTCONTAINERS_RYUK_DISABLED=true', '-e', 'RestoreFallbackFolders=/retained;/windows-nuget', '-e', 'FULLNET_CAPACITY_OTLP=' + collectorOtlpEndpoint,
        state.images.sdk, 'timeout', '--signal=TERM', '--kill-after=30s', '105m', 'bash', '/task/harness/driver.sh'], testRunEnvironment());
      state.sdkName = sdk; save();
      const contexts = new Set(), deadline = Date.now() + 100 * 60_000;
      for (let count = 0; count < 4; count++) {
        let dir, ready;
        while (!ready) {
          for (const provider of ['SqlServer', 'MySql']) for (const mode of ['C0', 'S2']) {
            const id = provider + '-' + mode, file = path.join(output, id, 'private-ready.json');
            if (!contexts.has(id) && fs.existsSync(file)) { dir = path.dirname(file); ready = json(file); contexts.add(id); }
          }
          if (!ready) {
            if (driver.row.finishedAt || Date.now() > deadline) throw Error('Driver ended or timed out before all contexts');
            await delay(1000);
          }
        }
        const identities = json(path.join(dir, 'api-identities.json'));
        const metrics = raw => readMetrics(raw, identities, path.basename(dir));
        const started = Date.now(); let serial = 0;
        const actualResourceBudgetReadbacks = [];
        for (const [id, role, memory, cpu] of [[ready.databaseContainer, ready.provider === 'SqlServer' ? 'sqlserver' : 'mysql', (ready.provider === 'SqlServer' ? 3 : 2) * 1024 ** 3, 2e9], [ready.redisContainer, 'redis', 512 * 1024 ** 2, 1e9]]) {
          if (!/^[0-9a-f]{64}$/u.test(id)) throw Error('Exact capacity service identity required');
          const [actual] = JSON.parse(docker(['inspect', id]));
          if (actual.Id !== id || actual.Image !== state.images[role] || actual.Config.Labels['fullnet.cache-capacity.owner'] !== c.owner
            || !actual.State.Running || actual.State.OOMKilled || actual.HostConfig.Memory !== memory || actual.HostConfig.MemorySwap !== memory || actual.HostConfig.NanoCpus !== cpu)
            throw Error('Actual capacity service owner, image or quota mismatch: ' + role);
          if (role === 'sqlserver' && actual.Config.Env.filter(x => x.startsWith('MSSQL_MEMORY_LIMIT_MB=')).join() !== 'MSSQL_MEMORY_LIMIT_MB=2048') throw Error('Actual SQL internal memory budget missing');
          actualResourceBudgetReadbacks.push({id, role, image: actual.Image, memory, memorySwap: memory, cpu, running: true, oomKilled: false});
        }
        write(path.join(dir, 'resource-admission.json'), {at: new Date().toISOString(), actualResourceBudgetReadbacks});
        verifyExperimentDatabaseBudget(json(path.join(dir, 'experiment-config.json')), databaseBudget, helperHashes['database-budget.json']);
        if (reused?.map.size && fs.existsSync(path.join(reused.directory, path.basename(dir), 'experiment-config.json')))
          verifyContextPolicies(path.join(reused.directory, path.basename(dir)), dir);
        // 每种策略重启采集器清空旧进程的累计系列，避免相同实例标签造成跨进程计数器重置。
        fs.writeFileSync(path.join(dir, 'collector-before-reset.log'), docker(['logs', collector]));
        docker(['restart', collector]);
        // Docker 重启可能重新分配动态宿主端口，重读真实映射而不是继续访问旧端口。
        endpoint = collectorEndpoint(docker(['port', collector, '8889/tcp']));
        write(path.join(dir, 'collector-endpoint.json'), {at: new Date().toISOString(), endpoint});
        const telemetryUntil = Date.now() + 60_000;
        while (true) {
          try {
            const raw = await telemetry(); fs.writeFileSync(path.join(dir, 'telemetry-readiness.prom'), raw);
            const rows = metrics(raw);
            if (new Set(rows.filter(r => r.name === 'http_server_active_requests').map(apiRole).filter(Boolean)).size === 2) {
              verifyDatabaseAdmissionRoles(rows); break;
            }
          } catch (error) { fs.writeFileSync(path.join(dir, 'telemetry-readiness-error.txt'), error.message); }
          if (Date.now() > telemetryUntil) throw Error('Both API telemetry identities did not become available');
          await delay(1000);
        }
        const rpc = async (action, arguments_ = {}) => {
          const id = randomUUID(); await writeControl(path.join(dir, 'command.json'), {...arguments_, id, action});
          const until = Date.now() + 30_000;
          while (Date.now() < until) {
            const file = path.join(dir, 'reply.json');
            if (fs.existsSync(file)) { const r = json(file); if (r.id === id) return r.result; }
            if (driver.row.finishedAt) throw Error('Driver ended during control request');
            await delay(100);
          }
          throw Error('Capacity control timeout: ' + action);
        };
        const sample = async (name, model, level, duration = '60s', intervention = null) => {
          if (Date.now() - started > 40 * 60_000) throw Error('Context exceeded bounded experiment time budget');
          const folder = path.join(dir, String(++serial).padStart(2, '0') + '-' + name); fs.mkdirSync(folder);
          const row = {context: path.basename(dir), name, model, level, duration, folder: path.relative(output, folder),
            startedAt: new Date().toISOString(), interventions: []}; state.samples.push(row); state.status = 'load-' + row.context + '-' + name; save();
          const previous = reused?.map.get(row.context + '/' + name);
          if (previous && eligibleSample(previous.row, row)) {
            fs.cpSync(previous.folder, folder, {recursive: true});
            fs.copyFileSync(path.join(folder, 'result.json'), path.join(folder, 'original-producer-result.json'));
            fs.copyFileSync(previous.terminalFile, path.join(folder, 'reused-k6-terminal.json'));
            fs.copyFileSync(previous.parserFile, path.join(folder, 'producer-metrics.mjs'));
            const originalIdentity = copySampleIdentityEvidence(previous.identityEvidence, folder);
            Object.assign(row, {result: previous.row.result, summarySha256: previous.row.summarySha256,
              startedAt: previous.row.startedAt, finishedAt: previous.row.finishedAt, interventions: previous.row.interventions,
              reusedFrom: {directory: reused.directory, stateSha256: reused.stateSha256, originalFolder: previous.row.folder,
                parserSha256: sha(fs.readFileSync(previous.parserFile)), terminalSha256: sha(fs.readFileSync(previous.terminalFile)), ...originalIdentity,
                previousAcceptance: reused.previousAcceptance, acceptedParentClaim: false, ancestor: previous.row.reusedFrom ?? null}});
            write(path.join(folder, 'result.json'), row); save(); return row;
          }
          row.authentication = await rpc('refresh');
          ready = json(path.join(dir, 'private-ready.json'));
          if (Date.parse(ready.expiresAtUtc) - Date.now() < (parseInt(duration, 10) + 60) * 1000) throw Error('Fresh credential cannot cover complete sample');
          await delay(2500);
          if (intervention === 'mixed') {
            // 首次写入与后续更新拥有不同 SQL 系列，先分别预热并等待导出，禁止把缺失基线当成零。
            write(path.join(folder, 'write-warmup.json'), {observations: [await rpc('write'), await rpc('write')]});
            await delay(5000);
          }
          write(path.join(folder, 'database-before.json'), await rpc('snapshot'));
          const before = await telemetry(); fs.writeFileSync(path.join(folder, 'before.prom'), before);
          const k6 = c.owner + '-k6';
          const env = {...testRunEnvironment(), URL_0: ready.urls[0], URL_1: ready.urls[1], TOKEN_0: ready.tokens[0], TOKEN_1: ready.tokens[1]};
          const p = launch('k6-' + row.context + '-' + serial, ['run', '--rm', '--name', k6, '--label', label,
            '--network', 'container:' + sdk, '--cpus', '2', '--memory', '1g', '--memory-swap', '1g',
            '-v', slash(path.join(output, 'harness/scenario.js')) + ':/scenario.js:ro', '-v', slash(folder) + ':/evidence',
            ...['URL_0', 'URL_1', 'TOKEN_0', 'TOKEN_1'].flatMap(key => ['-e', key]), '-e', 'MODEL=' + model, '-e', 'LEVEL=' + level,
            '-e', 'DURATION=' + duration, state.images.k6, 'run', '--quiet', '/scenario.js'], env);
          const snapshots = []; let safety;
          while (!p.row.finishedAt) {
            const time = new Date().toISOString(), raw = await telemetry(); snapshots.push(metrics(raw));
            fs.appendFileSync(path.join(folder, 'metrics-samples.prom'), '# sampled_at ' + time + '\n' + raw + '\n');
            // 发生器可能在采样中自然退出；查询运行集合后按所有权过滤，避免 --rm 的正常竞态使整个批次失败。
            const stats = docker(['stats', '--no-stream', '--format', '{{json .}}']);
            const ownedRows = stats.split(/\r?\n/).filter(Boolean).map(s => JSON.parse(s)).filter(r =>
              [sdk, collector, k6].includes(r.Name) || [ready.databaseContainer, ready.redisContainer].some(id => id.startsWith(r.ID)));
            fs.appendFileSync(path.join(folder, 'container-stats.jsonl'), JSON.stringify({at: time, rows: ownedRows}) + '\n');
            if (snapshots.length % 5 === 0) {
              const m = await memory(); fs.appendFileSync(path.join(folder, 'host-memory.jsonl'), JSON.stringify({at: time, ...m}) + '\n');
              if (!memoryWithinBudget(m, 'running')) {
                safety = 'Host/Engine guest memory safety floor reached';
                row.safetyViolation = {at: new Date().toISOString(), memory: m}; save();
                // 发生器可能已自行因阈值退出；停止命令的正常竞态不能掩盖真正的内存停止原因。
                if (!p.row.finishedAt) {
                  const stop = spawnSync('docker', ['stop', '--time', '10', k6], {encoding: 'utf8', windowsHide: true, timeout: 30_000});
                  row.safetyStop = {exitCode: stop.status, error: stop.error?.message ?? stop.stderr?.trim() ?? null}; save();
                }
                break;
              }
            }
            if (intervention === 'mixed' && !p.row.finishedAt) row.interventions.push(await rpc('write'));
            if (intervention === 'pause' && !row.interventions.length && snapshots.length >= 3 && !p.row.finishedAt)
              row.interventions.push({at: new Date().toISOString(), command: 'CLIENT PAUSE 5000 ALL', result: await rpc('pause')});
            await delay(1000);
          }
          const terminal = await p.completion; await delay(2500);
          const after = await telemetry(); fs.writeFileSync(path.join(folder, 'after.prom'), after);
          write(path.join(folder, 'database-after.json'), await rpc('snapshot'));
          const summaryPath = path.join(folder, 'summary.json');
          if (safety) {
            row.finishedAt = new Date().toISOString(); row.exitCode = terminal.exitCode;
            if (fs.existsSync(summaryPath)) row.summarySha256 = sha(fs.readFileSync(summaryPath));
            write(path.join(folder, 'resource-stopped.json'), row); save(); throw Error(safety);
          }
          if (!fs.existsSync(summaryPath)) throw Error('k6 ended without real summary: ' + terminal.exitCode);
          row.summarySha256 = sha(fs.readFileSync(summaryPath)); row.result = summarizeSample(json(summaryPath), terminal.exitCode, snapshots, metrics(before), metrics(after), sqlCounterMetric);
          row.finishedAt = new Date().toISOString(); write(path.join(folder, 'result.json'), row); save();
          const usableWarmup = name === 'warmup' && [0, 99].includes(terminal.exitCode) && row.result.requests > 0
            && row.result.payloadErrorRate === 0 && row.result.authorizationErrors === 0;
          if (name === 'warmup' && row.result.errorRate >= .01) throw Error('Warmup business errors: ' + row.result.budgetFailureKinds.join(', '));
          if (row.result.classification === 'correctness-failure' || (row.result.classification === 'Incomplete' && !usableWarmup))
            throw Error('Unsafe or incomplete sample: ' + row.result.classification + ': ' + row.result.unknown.join(', '));
          return row;
        };
        // 预热与正式样本分开；C0 与 S2 的 16 VU 是同机同配置对照，不宣称历史版本优化。
        await sample('warmup', 'closed', 4, '30s');
        // 热键可能让另一节点一直只命中 L2；复用曲线会缩短实际经过时间，必须先观察双方真实回源系列。
        // 仅向缺失节点读取，等待自然 TTL 收敛，不写业务值、不伪造零基线，也不改变策略或正式负载。
        const policy = json(path.join(dir, 'runtime-policies.json')).find(p => p.entryName === 'settings.grid-preference');
        const primeUntil = Date.now() + (policy.l2DurationSeconds + 15) * 1000, observations = [];
        while (true) {
          const raw = await telemetry(), missing = missingGridSqlRoles(metrics(raw), sqlCounterMetric);
          fs.writeFileSync(path.join(dir, 'grid-baseline-readiness.prom'), raw);
          write(path.join(dir, 'grid-baseline-readiness.json'), {at: new Date().toISOString(), missing, observations});
          if (!missing.length) break;
          if (Date.now() > primeUntil) throw Error('Both Grid SQL baseline series did not become available');
          if (!memoryWithinBudget(await memory(), 'running')) throw Error('Memory safety floor during baseline priming');
          for (const role of missing) {
            // Native 端口只在 SDK 网络命名空间内可达，通过生命周期驱动读，不能从 Windows 访问容器 loopback。
            observations.push({at: new Date().toISOString(), ...await rpc('prime-grid', {role})});
          }
          await delay(1000);
        }
        if (ready.mode === 'C0') {
          await sample('control-16-1', 'closed', 16);
          await sample('control-16-2', 'closed', 16);
        } else {
          let stable = 1;
          for (const model of ['closed', 'open']) {
            const levels = profileLevels(ready.provider, model, c.selection, c.providerCeilings?.[ready.provider]?.[model]);
            let last;
            for (const level of levels) {
              const result = await sample(model + '-' + level, model, level);
              if (result.result.classification === 'budget-exceeded') break;
              last = level; if (model === 'closed') stable = level;
            }
            if (last) await sample(model + '-' + last + '-repeat', model, last);
          }
          await sample('mixed-writes', 'closed', Math.min(stable, 16), '45s', 'mixed');
          await sample('redis-delay', 'closed', Math.min(stable, 16), '45s', 'pause');
        }
        await writeControl(path.join(dir, 'command.json'), {id: randomUUID(), action: 'finish'});
        const drainUntil = Date.now() + 90_000;
        while (!fs.existsSync(path.join(dir, 'lifecycle-completed.json'))) {
          if (driver.row.finishedAt || Date.now() > drainUntil) throw Error('API graceful drain incomplete');
          await delay(500);
        }
      }
      const terminal = await driver.completion;
      if (terminal.exitCode !== 0) throw Error('Native lifecycle actual exit: ' + terminal.exitCode);
      for (const name of ['source-initial', c.consumerReuse ? 'reused-consumer' : 'driver-build', 'discovery', 'lifecycle', 'source-final'])
        if (fs.readFileSync(path.join(output, name + '.exit'), 'utf8').trim() !== '0') throw Error('Missing successful stage: ' + name);
      state.lifecycle = verifyLifecycle(fs.readFileSync(path.join(output, 'discovery.log'), 'utf8'),
        fs.readFileSync(path.join(output, 'lifecycle/lifecycle.trx'), 'utf8'));
      write(path.join(output, 'lifecycle-proof.json'), state.lifecycle);
      inputs(); state.workloadCompleted = true; state.status = 'local-experiment-completed'; save();
      // 必需证据不完整时正式 run、父退出码及 acceptance 继续失败，即使其他场景已经采完。
    } finally {
      const cleanupStart = Date.now(); let abortWritten = false;
      // 查询失败意味着状态未知，留在占锁回调中重试；不能从 finally 抛出后提前释放资源。
      while (true) {
        try {
          // 控制文件清理独立于 Docker 是否可查询，包括原子写入中断后的临时文件。
          for (const provider of ['SqlServer', 'MySql']) for (const mode of ['C0', 'S2']) for (const suffix of ['', '.tmp']) {
            const secret = path.join(output, provider + '-' + mode, 'private-ready.json' + suffix);
            if (fs.existsSync(secret)) fs.unlinkSync(secret);
          }
          if (driver && !driver.row.finishedAt && !abortWritten) {
            write(path.join(output, 'abort.json'), {at: new Date().toISOString(), reason: 'Controller requested natural driver cleanup'});
            abortWritten = true;
          }
          const own = docker(['ps', '-a', '--filter', 'label=' + label, '--format', '{{.ID}} {{.Names}}']).split(/\r?\n/).filter(Boolean).map(s => s.split(' '));
          if (own.some(([, name]) => name === collector)) {
            const log = spawnSync('docker', ['logs', collector], {encoding: 'utf8', windowsHide: true, timeout: 30_000});
            fs.writeFileSync(path.join(output, 'collector.log'), (log.stdout ?? '') + (log.stderr ?? ''));
          }
          const kill = own.filter(([, name]) => name.endsWith('-k6') || (!driver || driver.row.finishedAt) || Date.now() - cleanupStart > 180_000).map(([id]) => id);
          if (kill.length) { docker(['rm', '-f', ...kill]); state.explicitOwnedCleanup = [...new Set([...(state.explicitOwnedCleanup ?? []), ...kill])]; }
          if (handles.some(h => !h.row.finishedAt)) { await delay(1000); continue; }
          // close 事件必须真的触发，不能仅凭容器删除推断 Docker CLI 已经退出。
          await Promise.all(handles.map(h => h.completion));
          const networks = docker(['network', 'ls', '--filter', 'label=' + label, '--format', '{{.ID}}']).split(/\s+/).filter(Boolean);
          if (networks.length) docker(['network', 'rm', ...networks]);
          state.remainingNewContainers = docker(['ps', '-a', '--filter', 'label=' + label, '--format', '{{.ID}}']).split(/\s+/).filter(Boolean);
          state.volumeOwners = docker(['ps', '-a', '--filter', 'volume=' + c.volume, '--format', '{{.ID}}']); save();
          if (!state.remainingNewContainers.length && !state.volumeOwners) break;
        } catch (error) {
          state.cleanupWarning = error.message; state.acceptance = false; save();
        }
        if (Date.now() - cleanupStart > 300_000) { state.status = 'cleanup-blocked-lock-retained'; save(); }
        await delay(5000);
      }
      state.cleanupConfirmed = true; save();
    }
  });
  state.resourceReleaseConfirmed = true; state.acceptance = state.workloadCompleted === true && state.cleanupConfirmed === true;
  state.status = 'completed-locks-released'; state.finishedAt = new Date().toISOString(); save();
} catch (error) {
  state.acceptance = false; state.status = 'failed-or-blocked'; state.error = error.message; state.finishedAt = new Date().toISOString(); save(); throw error;
}
