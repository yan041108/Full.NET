import assert from 'node:assert/strict';
import { spawn, spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { appendFile, mkdtemp, mkdir, readFile, readdir, rm, stat, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { verifyCollectorRequestReplay } from './collector-request-replay-proof.mjs';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const image = 'cr.fluentbit.io/fluent/fluent-bit:4.1.1@sha256:2a5cb41f99b7c5f3386bb34d42ce3b19eb47fbef6d93b64c0bd4afe9e6ec389c';
const metricsProbeImage = 'node:24.21.0-alpine@sha256:ebfe2f90462722a7a4de65e91990e97fe0d401c70e0e762c5b53302f905ec1c1';
const containerName = `fullnet-log-route-${process.pid}`;
const ids = Object.freeze({
  info: '0199aabb-ccdd-7000-8000-000000000001',
  priority: '0199aabb-ccdd-7000-8000-000000000002',
  error: '0199aabb-ccdd-7000-8000-000000000003',
  priorityError: '0199aabb-ccdd-7000-8000-000000000009',
  docker: '0199aabb-ccdd-7000-8000-000000000004',
  legacy: '0199aabb-ccdd-7000-8000-000000000005',
  missing: '0199aabb-ccdd-7000-8000-000000000006',
  local: '0199aabb-ccdd-7000-8000-00000000000a',
  missingInstance: '0199aabb-ccdd-7000-8000-00000000000b',
  missingClass: '0199aabb-ccdd-7000-8000-00000000000c',
  missingMessage: '0199aabb-ccdd-7000-8000-00000000000d',
  missingTimestamp: '0199aabb-ccdd-7000-8000-00000000000e',
  restartInfo: '0199aabb-ccdd-7000-8000-000000000007',
  restartError: '0199aabb-ccdd-7000-8000-000000000008',
  crashRestartInfo: '0199aabb-ccdd-7000-8000-00000000000f',
  crashRestartError: '0199aabb-ccdd-7000-8000-000000000010',
});

function containerFileName(podName) {
  return `${podName}_default_api-${'a'.repeat(64)}.log`;
}

function literalBlock(values, name) {
  const expression = new RegExp(`^  ${name}: \\|\\r?\\n([\\s\\S]*?)(?=^  [A-Za-z]+: \\||^extraVolumes:|(?![\\s\\S]))`, 'm');
  const match = values.match(expression);
  assert.ok(match, `Missing Fluent Bit ${name} block`);
  return match[1].replace(/^    /gm, '');
}

function collectorConfig(values) {
  let service = literalBlock(values, 'service')
    .replace(/Parsers_File\s+\/fluent-bit\/etc\/conf\/custom_parsers\.conf/, 'Parsers_File /work/custom_parsers.conf')
    .replace(/storage\.path\s+\/var\/fluent-bit\/buffer/, 'storage.path /work/storage');
  let inputs = literalBlock(values, 'inputs')
    .replace(/DB\s+\/var\/fluent-bit\/tail\.db/, 'DB /work/storage/tail.db\n    Read_From_Head On')
    .replace(/Refresh_Interval\s+5/, 'Refresh_Interval 1');
  let filters = literalBlock(values, 'filters')
    .replace(/Kube_Tag_Prefix\s+kube\.var\.log\.containers\./,
      'Kube_Tag_Prefix kube.var.log.containers.\n    Kube_Meta_Preload_Cache_Dir /work/meta\n    Kube_URL http://127.0.0.1:1\n    Kube_Token_File /work/token');
  assert.match(filters, /Kube_Meta_Preload_Cache_Dir/);
  assert.match(inputs, /Read_From_Head On/);
  const outputs = `[OUTPUT]
    Name file
    Alias fullnet_priority_forward
    Match fullnet.priority.*
    Path /work/out
    File priority.jsonl
    Format plain

[OUTPUT]
    Name file
    Alias fullnet_b2_forward
    Match fullnet.b2.*
    Path /work/out
    File b2.jsonl
    Format plain
`;
  service = service.trimEnd();
  inputs = inputs.trimEnd();
  filters = filters.trimEnd();
  return `${service}\n\n${inputs}\n\n${filters}\n\n${outputs}`;
}

function envelope(event) {
  return { Instance: 'smoke-api-1', 'log.class': 'Diagnostic', ...event };
}

function cri(event) {
  return `2026-09-29T00:00:00.000000000Z stdout F ${JSON.stringify(envelope(event))}\n`;
}

function docker(event) {
  return `${JSON.stringify({ log: `${JSON.stringify(envelope(event))}\n`, stream: 'stdout', time: '2026-09-29T00:00:00.000000000Z' })}\n`;
}

async function prepare(directory) {
  const values = await readFile(path.join(root, 'deploy/observability/fluent-bit-values.yaml'), 'utf8');
  await Promise.all(['containers', 'meta', 'storage', 'out'].map((name) => mkdir(path.join(directory, name))));
  await writeFile(path.join(directory, 'fluent-bit.conf'), collectorConfig(values));
  await writeFile(path.join(directory, 'custom_parsers.conf'), literalBlock(values, 'customParsers'));
  await writeFile(path.join(directory, 'token'), 'route-smoke-only\n');

  const pods = [
    { name: 'fullnet-old', ingress: 'collector', format: cri, events: [
      { '@t': '2026-09-29T00:00:00.0000000Z', '@mt': 'ordinary', LogEventId: ids.info, DiagnosticGroup: 'do-not-export', kubernetes: { labels: { 'fullnet.io/log-ingress': 'applicationkafka' }, secret: 'do-not-export' } },
      { '@t': '2026-09-29T00:00:01.0000000Z', '@mt': 'http', LogEventId: ids.priority, 'reliability.class': 'Priority', 'log.stream': 'http-operation' },
      { '@t': '2026-09-29T00:00:02.0000000Z', '@l': 'Error', '@mt': 'failure', LogEventId: ids.error },
      { '@t': '2026-09-29T00:00:02.5000000Z', '@l': 'Error', '@mt': 'priority failure', LogEventId: ids.priorityError, 'reliability.class': 'Priority' },
      { '@t': '2026-09-29T00:00:02.0000000Z', '@mt': 'unstructured', log: 'unsafe raw' },
      { '@t': '2026-09-29T00:00:02.0000000Z', '@mt': 'missing instance', LogEventId: ids.missingInstance, Instance: undefined },
      { '@t': '2026-09-29T00:00:02.0000000Z', '@mt': 'missing class', LogEventId: ids.missingClass, 'log.class': undefined },
      { '@t': '2026-09-29T00:00:02.0000000Z', '@mt': undefined, LogEventId: ids.missingMessage },
      { '@t': undefined, '@mt': 'missing timestamp', LogEventId: ids.missingTimestamp },
    ] },
    { name: 'fullnet-docker', ingress: 'collector', format: docker, events: [
      { '@t': '2026-09-29T00:00:03.0000000Z', '@mt': 'docker', LogEventId: ids.docker, 'reliability.class': 'BestEffort' },
    ] },
    { name: 'fullnet-direct', ingress: 'applicationkafka', format: cri, events: [
      { '@t': '2026-09-29T00:00:04.0000000Z', '@mt': 'mirror', LogEventId: ids.priority, 'reliability.class': 'Priority', kubernetes: { labels: { 'fullnet.io/log-ingress': 'collector' } } },
    ] },
    { name: 'fullnet-legacy', ingress: 'legacy', format: cri, events: [
      { '@t': '2026-09-29T00:00:05.0000000Z', '@mt': 'legacy', LogEventId: ids.legacy },
    ] },
    { name: 'fullnet-local', ingress: 'local', format: cri, events: [
      { '@t': '2026-09-29T00:00:05.5000000Z', '@mt': 'local', LogEventId: ids.local },
    ] },
    { name: 'fullnet-missing', format: cri, events: [
      { '@t': '2026-09-29T00:00:06.0000000Z', '@mt': 'missing', LogEventId: ids.missing, kubernetes: { labels: { 'fullnet.io/log-ingress': 'collector' } } },
    ] },
  ];
  for (const pod of pods) {
    const fileName = containerFileName(pod.name);
    await writeFile(path.join(directory, 'containers', fileName), pod.events.map(pod.format).join(''));
    if (pod.ingress) {
      await writeFile(path.join(directory, 'meta', `default_${pod.name}.meta`), JSON.stringify({
        apiVersion: 'v1', kind: 'Pod',
        metadata: { name: pod.name, namespace: 'default', uid: `smoke-${pod.name}`, labels: { 'fullnet.io/log-ingress': pod.ingress } },
        spec: { nodeName: 'smoke-node' },
      }));
    }
  }
}

async function records(file) {
  const content = await readFile(file, 'utf8');
  return content.trim().split(/\r?\n/).map((line) => JSON.parse(line));
}

async function verify(directory) {
  const [priority, b2] = await Promise.all([
    records(path.join(directory, 'out', 'priority.jsonl')),
    records(path.join(directory, 'out', 'b2.jsonl')),
  ]);
  assert.deepEqual(priority.map((entry) => entry.LogEventId).sort(), [ids.error, ids.priority, ids.priorityError].sort());
  assert.deepEqual(b2.map((entry) => entry.LogEventId).sort(), [ids.docker, ids.info].sort());
  for (const entry of [...priority, ...b2]) {
    assert.ok(entry['@t']);
    assert.ok(entry['@mt']);
    assert.equal(entry.Instance, 'smoke-api-1');
    assert.equal(entry['log.class'], 'Diagnostic');
    assert.equal(Object.hasOwn(entry, 'kubernetes'), false);
    assert.equal(Object.hasOwn(entry, 'DiagnosticGroup'), false);
    assert.equal(Object.hasOwn(entry, 'log'), false);
    assert.equal(Object.keys(entry).some((key) => key.startsWith('_fullnet_')), false);
  }
  assert.equal(Object.hasOwn(b2.find((entry) => entry.LogEventId === ids.info), '@l'), false);
  assert.equal(priority.find((entry) => entry.LogEventId === ids.priority)['reliability.class'], 'Priority');
}

async function verifyRestart(directory) {
  const [priority, b2] = await Promise.all([
    records(path.join(directory, 'out', 'priority.jsonl')),
    records(path.join(directory, 'out', 'b2.jsonl')),
  ]);
  assert.deepEqual(priority.map((entry) => entry.LogEventId), [ids.restartError]);
  assert.deepEqual(b2.map((entry) => entry.LogEventId), [ids.restartInfo]);
  for (const entry of [...priority, ...b2]) {
    assert.equal(Object.keys(entry).some((key) => key.startsWith('_fullnet_')), false);
  }
}

async function appendRestartEvents(directory) {
  const oldPodFile = path.join(directory, 'containers', containerFileName('fullnet-old'));
  const nextEvents = [
    { '@t': '2026-09-29T00:00:07.0000000Z', '@mt': 'after restart', LogEventId: ids.restartInfo },
    { '@t': '2026-09-29T00:00:08.0000000Z', '@l': 'Error', '@mt': 'after restart failure', LogEventId: ids.restartError },
  ];
  await appendFile(oldPodFile, nextEvents.map(cri).join(''));
  await Promise.all(['priority.jsonl', 'b2.jsonl'].map((name) => rm(path.join(directory, 'out', name))));
}

async function verifyRequestReplay(directory, sourcePath) {
  const sourceFile = path.resolve(sourcePath);
  assert.ok((await stat(sourceFile)).size <= 8 * 1024 * 1024, 'Request replay input exceeds 8 MiB.');
  // 解析与摘要绑定同一个已读取的快照，避免回放期间文件重写导致证据错配。
  const sourceBytes = await readFile(sourceFile);
  assert.ok(sourceBytes.length <= 8 * 1024 * 1024, 'Request replay input exceeds 8 MiB.');
  const sourceSha256 = createHash('sha256').update(sourceBytes).digest('hex');
  const source = sourceBytes.toString('utf8').trim().split(/\r?\n/).map(line => JSON.parse(line))
    .filter(event => event['@mt'] === 'HttpOperationCompleted');
  assert.equal(source.length, 5200, 'Run the sustained Projected fixture first.');
  const isPriority = event => event['reliability.class'] === 'Priority' || ['Error', 'Fatal'].includes(event['@l']);
  const expectedOutput = event => Object.fromEntries(Object.entries(event)
    .filter(([key]) => !['DiagnosticGroup', 'kubernetes', 'tenant_id', 'user_id'].includes(key)));
  const baseline = verifyCollectorRequestReplay(source, source.filter(isPriority).map(expectedOutput), source.filter(event => !isPriority(event)).map(expectedOutput));
  assert.equal(baseline.priority, 520);
  assert.equal(baseline.projections, 5200);
  // 只清空本次随机临时夹具；保留可信 Pod 元数据，不由日志正文决定入口。
  for (const file of await readdir(path.join(directory, 'containers')))
    await writeFile(path.join(directory, 'containers', file), '');
  const input = source.map(event => `${event['@t']} stdout F ${JSON.stringify(event)}\n`).join('');
  await writeFile(path.join(directory, 'containers', containerFileName('fullnet-old')), input);
  // 不同 ID 的 ApplicationKafka 镜像能独立识别误采集，不能被正确来源覆盖。
  const mirrors = source.map(event => ({ ...event,
    LogEventId: `${event.LogEventId.startsWith('0') ? 'f' : '0'}${event.LogEventId.slice(1)}`,
    kubernetes: { labels: { 'fullnet.io/log-ingress': 'collector' } },
  }));
  const sourceIds = new Set(source.map(event => event.LogEventId));
  assert.ok(mirrors.every(event => !sourceIds.has(event.LogEventId)));
  await writeFile(path.join(directory, 'containers', containerFileName('fullnet-direct')),
    mirrors.map(event => `${event['@t']} stdout F ${JSON.stringify(event)}\n`).join(''));
  const started = performance.now();
  await run(directory, 'graceful', 15000);
  const remaining = spawnSync('docker', ['ps', '-aq', '--filter', `name=^/${containerName}$`], { encoding: 'utf8', timeout: 15000 });
  assert.equal(remaining.status, 0, 'Cannot verify request replay container cleanup.');
  assert.equal(remaining.stdout.trim(), '', 'Request replay container remains after cleanup.');
  const [priority, bestEffort] = await Promise.all([
    records(path.join(directory, 'out', 'priority.jsonl')),
    records(path.join(directory, 'out', 'b2.jsonl')),
  ]);
  const evidenceDirectory = path.join(root, 'artifacts/collector-request-replay');
  await mkdir(evidenceDirectory, { recursive: true });
  await Promise.all(['priority.jsonl', 'b2.jsonl'].map(async name =>
    writeFile(path.join(evidenceDirectory, name), await readFile(path.join(directory, 'out', name)))));
  const proof = verifyCollectorRequestReplay(source, priority, bestEffort);
  return { ...proof, sourceJsonBytes: Buffer.byteLength(source.map(event => JSON.stringify(event)).join('\n')),
    sourceFile, sourceSha256,
    elapsedMilliseconds: performance.now() - started,
    applicationKafkaMirrorsRejected: mirrors.length,
    scope: 'Real HTTP envelopes through pinned Fluent Bit CRI tail, Kubernetes metadata and production routing filters; file outputs replace Forward; finite preloaded replay, no Kafka/ES/ACK or sustained throughput claim' };
}

async function recordsOrEmpty(file) {
  try {
    return await records(file);
  } catch (error) {
    if (error.code === 'ENOENT') return [];
    throw error;
  }
}

async function appendCrashRestartEvents(directory) {
  const oldPodFile = path.join(directory, 'containers', containerFileName('fullnet-old'));
  const nextEvents = [
    { '@t': '2026-09-29T00:00:09.0000000Z', '@mt': 'after crash restart', LogEventId: ids.crashRestartInfo },
    { '@t': '2026-09-29T00:00:10.0000000Z', '@l': 'Error', '@mt': 'after crash restart failure', LogEventId: ids.crashRestartError },
  ];
  await appendFile(oldPodFile, nextEvents.map(cri).join(''));
  await Promise.all(['priority.jsonl', 'b2.jsonl'].map((name) => rm(path.join(directory, 'out', name))));
}

async function verifyCrashRestart(directory) {
  const [priority, b2] = await Promise.all([
    records(path.join(directory, 'out', 'priority.jsonl')),
    records(path.join(directory, 'out', 'b2.jsonl')),
  ]);
  assert.deepEqual(priority.map((entry) => entry.LogEventId), [ids.crashRestartError]);
  assert.deepEqual(b2.map((entry) => entry.LogEventId), [ids.crashRestartInfo]);
}

async function verifyOutputFaultRecovery(directory, finiteRetryProbe = false) {
  if (finiteRetryProbe) {
    const helper = spawnSync('docker', ['image', 'inspect', metricsProbeImage], { stdio: 'ignore' });
    if (helper.status !== 0) {
      const pulled = spawnSync('docker', ['pull', metricsProbeImage], { encoding: 'utf8' });
      assert.equal(pulled.status, 0, `${pulled.stdout ?? ''}\n${pulled.stderr ?? ''}`);
    }
  }
  const original = await readFile(path.join(directory, 'fluent-bit.conf'), 'utf8');
  // 故障探针保持候选配置的优先级无限重试和 B2 有限重试策略。
  const priorityRetries = 'False';
  const b2Retries = finiteRetryProbe ? '3' : 'False';
  const unavailableOutputs = `[OUTPUT]
    Name forward
    Alias fullnet_priority_forward
    Match fullnet.priority.*
    Host 127.0.0.1
    Port 24224
    Require_ack_response On
    Retry_Limit ${priorityRetries}
    storage.total_limit_size 256MB

[OUTPUT]
    Name forward
    Alias fullnet_b2_forward
    Match fullnet.b2.*
    Host 127.0.0.1
    Port 24224
    Require_ack_response On
    Retry_Limit ${b2Retries}
    storage.total_limit_size 256MB
`;
  await writeFile(path.join(directory, 'fluent-bit.conf'), original.replace(/\[OUTPUT\][\s\S]*$/, unavailableOutputs));
  const faultRun = await run(directory, 'kill', finiteRetryProbe ? 130000 : 12000, finiteRetryProbe);
  assert.match(faultRun.output, /no upstream connections available/);
  assert.deepEqual(await readdir(path.join(directory, 'out')), []);
  const sourceDirectory = path.join(directory, 'containers');
  // 删除源日志以证明恢复来自本地缓冲，而非 Tail 从头重读。
  await Promise.all((await readdir(sourceDirectory)).map((name) => rm(path.join(sourceDirectory, name))));
  await writeFile(path.join(directory, 'fluent-bit.conf'), original);
  await run(directory);
  if (!finiteRetryProbe) {
    await verify(directory);
    return;
  }
  const [priority, b2] = await Promise.all([
    recordsOrEmpty(path.join(directory, 'out', 'priority.jsonl')),
    recordsOrEmpty(path.join(directory, 'out', 'b2.jsonl')),
  ]);
  const expected = [ids.info, ids.priority, ids.error, ids.priorityError, ids.docker];
  const recovered = [...priority, ...b2].map((entry) => entry.LogEventId);
  const outputMetrics = Object.fromEntries(
    faultRun.metricsText.split(/\r?\n/)
      .map((line) => line.match(/^fluentbit_output_(dropped_records_total|retries_failed_total)\{name="(fullnet_(?:priority|b2)_forward)"\}\s+(\d+(?:\.\d+)?)/))
      .filter(Boolean)
      .map((match) => [`${match[2]}.${match[1]}`, Number(match[3])]),
  );
  assert.ok(Object.hasOwn(outputMetrics, 'fullnet_priority_forward.dropped_records_total'));
  assert.ok(Object.hasOwn(outputMetrics, 'fullnet_b2_forward.dropped_records_total'));
  assert.ok(Object.hasOwn(outputMetrics, 'fullnet_priority_forward.retries_failed_total'));
  assert.ok(Object.hasOwn(outputMetrics, 'fullnet_b2_forward.retries_failed_total'));
  assert.equal(outputMetrics['fullnet_priority_forward.dropped_records_total'], 0);
  assert.equal(priority.length, 3);
  assert.equal(outputMetrics['fullnet_priority_forward.dropped_records_total'] + priority.length, 3);
  assert.equal(outputMetrics['fullnet_b2_forward.dropped_records_total'] + b2.length, 2);
  process.stdout.write(`${JSON.stringify({
    retryLimit: { priority: 'False', bestEffort: 3 },
    faultSeconds: 130,
    recovered,
    missing: expected.filter((id) => !recovered.includes(id)),
    outputMetrics,
  })}\n`);
}

function readCollectorMetrics() {
  const helper = spawnSync('docker', [
    'run', '--rm', '--network', `container:${containerName}`, '--entrypoint', 'node',
    metricsProbeImage, '-e',
    "fetch('http://127.0.0.1:2020/api/v1/metrics/prometheus').then(async response => { if (!response.ok) throw new Error(`HTTP ${response.status}`); process.stdout.write(await response.text()); }).catch(error => { console.error(error); process.exitCode = 1; });",
  ], { encoding: 'utf8', timeout: 15000 });
  assert.equal(helper.status, 0, helper.stderr || helper.error?.message);
  return helper.stdout;
}

async function run(directory, shutdown = 'graceful', durationMs = 12000, captureMetrics = false) {
  const localImage = spawnSync('docker', ['image', 'inspect', image], { stdio: 'ignore' });
  if (localImage.status !== 0) {
    const pulled = spawnSync('docker', ['pull', image], { encoding: 'utf8' });
    assert.equal(pulled.status, 0, `${pulled.stdout ?? ''}\n${pulled.stderr ?? ''}`);
  }
  const args = [
    'run', '--rm', '--network', 'none', '--name', containerName,
    '--mount', `type=bind,source=${directory},target=/work`,
    '--mount', `type=bind,source=${path.join(directory, 'containers')},target=/var/log/containers,readonly`,
    image, '-c', '/work/fluent-bit.conf',
  ];
  const child = spawn('docker', args, { stdio: ['ignore', 'pipe', 'pipe'] });
  let output = '';
  child.stdout.on('data', (chunk) => { output += chunk.toString(); });
  child.stderr.on('data', (chunk) => { output += chunk.toString(); });
  let exited = false;
  let runError;
  const closed = new Promise((resolve) => {
    child.on('error', (error) => { runError = error; exited = true; resolve(null); });
    child.on('close', (code) => { exited = true; resolve(code); });
  });
  let timer;
  let shutdownError;
  let metricsText = '';
  try {
    let created = false;
    for (let attempt = 0; attempt < 100 && !exited; attempt++) {
      const inspected = spawnSync('docker', ['inspect', containerName], { stdio: 'ignore' });
      if (inspected.status === 0) {
        created = true;
        break;
      }
      await new Promise((resolve) => setTimeout(resolve, 100));
    }
    assert.ok(created || exited, `Docker did not create ${containerName}: ${output}`);
    if (created) {
      timer = setTimeout(() => {
        if (captureMetrics) {
          try {
            metricsText = readCollectorMetrics();
          } catch (error) {
            shutdownError = error.message;
          }
        }
        const command = shutdown === 'kill'
          ? ['kill', '--signal=KILL', containerName]
          : ['stop', '--time', '2', containerName];
        const stopped = spawnSync('docker', command, { encoding: 'utf8' });
        if (stopped.status !== 0) {
          shutdownError = stopped.stderr || stopped.stdout || `docker ${command[0]} failed`;
          spawnSync('docker', ['rm', '-f', containerName], { encoding: 'utf8' });
        }
      }, durationMs);
    }
    const exitCode = await closed;
    assert.equal(runError, undefined, runError?.message);
    assert.equal(shutdownError, undefined, shutdownError);
    assert.ok(shutdown === 'kill' ? exitCode === 137 : exitCode === 0 || exitCode === 143, output);
    return { output, metricsText };
  } finally {
    clearTimeout(timer);
    spawnSync('docker', ['rm', '-f', containerName], { encoding: 'utf8' });
  }
}

if (process.argv.includes('--help')) {
  process.stdout.write('Usage: node eng/testing/fluent-bit-collector-route-smoke.mjs [--prepare-only|--print-config|--docker-desktop|--finite-retry-probe|--request-replay <Projected JSONL>]\n');
} else if (process.platform !== 'linux' && !process.argv.includes('--docker-desktop')
    && !process.argv.includes('--prepare-only') && !process.argv.includes('--print-config')) {
  throw new Error('Fluent Bit Collector route smoke requires Linux CI or explicit --docker-desktop.');
} else {
  const replayIndex = process.argv.indexOf('--request-replay');
  const reportPath = path.join(root, 'artifacts/collector-request-replay/result.json');
  if (replayIndex >= 0) {
    await rm(reportPath, { force: true });
    assert.ok(process.argv[replayIndex + 1] && !process.argv[replayIndex + 1].startsWith('--'), 'Missing request replay input.');
    assert.ok(!['--prepare-only', '--print-config', '--finite-retry-probe'].some(flag => process.argv.includes(flag)), 'Request replay cannot combine other modes.');
  }
  const directory = await mkdtemp(path.join(os.tmpdir(), 'fullnet-fluent-bit-route-'));
  let replayProof;
  try {
    await prepare(directory);
    if (process.argv.includes('--print-config')) {
      process.stdout.write(await readFile(path.join(directory, 'fluent-bit.conf'), 'utf8'));
    } else if (process.argv.includes('--prepare-only')) {
      process.stdout.write('Fluent Bit route fixture prepared.\n');
    } else if (replayIndex >= 0) {
      replayProof = await verifyRequestReplay(directory, process.argv[replayIndex + 1]);
    } else if (process.argv.includes('--finite-retry-probe')) {
      await verifyOutputFaultRecovery(directory, true);
    } else {
      await run(directory);
      await verify(directory);
      await appendRestartEvents(directory);
      await run(directory);
      await verifyRestart(directory);
      await run(directory, 'kill');
      await appendCrashRestartEvents(directory);
      await run(directory);
      await verifyCrashRestart(directory);
      const faultDirectory = await mkdtemp(path.join(os.tmpdir(), 'fullnet-fluent-bit-output-fault-'));
      try {
        await prepare(faultDirectory);
        await verifyOutputFaultRecovery(faultDirectory);
      } finally {
        await rm(faultDirectory, { recursive: true, force: true });
      }
      process.stdout.write('Fluent Bit Collector route smoke passed.\n');
    }
  } finally {
    await rm(directory, { recursive: true, force: true });
  }
  if (replayProof) {
    await mkdir(path.dirname(reportPath), { recursive: true });
    await writeFile(reportPath, JSON.stringify({ passed: true, cleanupVerified: true, image, ...replayProof }, null, 2));
    process.stdout.write(`Collector request replay verified: ${replayProof.total} events, ${replayProof.applicationKafkaMirrorsRejected} mirrors rejected.\n`);
  }
}
