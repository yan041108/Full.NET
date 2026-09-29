import assert from 'node:assert/strict';
import { spawn, spawnSync } from 'node:child_process';
import { appendFile, mkdtemp, mkdir, readFile, rm, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const image = 'cr.fluentbit.io/fluent/fluent-bit:4.1.1@sha256:2a5cb41f99b7c5f3386bb34d42ce3b19eb47fbef6d93b64c0bd4afe9e6ec389c';
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
    Match fullnet.priority.*
    Path /work/out
    File priority.jsonl
    Format plain

[OUTPUT]
    Name file
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

async function run(directory) {
  const pulled = spawnSync('docker', ['pull', image], { encoding: 'utf8' });
  assert.equal(pulled.status, 0, `${pulled.stdout ?? ''}\n${pulled.stderr ?? ''}`);
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
  let stopError;
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
        const stopped = spawnSync('docker', ['stop', '--time', '2', containerName], { encoding: 'utf8' });
        if (stopped.status !== 0) {
          stopError = stopped.stderr || stopped.stdout || 'docker stop failed';
          spawnSync('docker', ['rm', '-f', containerName], { encoding: 'utf8' });
        }
      }, 12000);
    }
    const exitCode = await closed;
    assert.equal(runError, undefined, runError?.message);
    assert.equal(stopError, undefined, stopError);
    assert.ok(exitCode === 0 || exitCode === 143, output);
  } finally {
    clearTimeout(timer);
    spawnSync('docker', ['rm', '-f', containerName], { encoding: 'utf8' });
  }
}

if (process.argv.includes('--help')) {
  process.stdout.write('Usage: node eng/testing/fluent-bit-collector-route-smoke.mjs [--prepare-only|--print-config]\n');
} else if (process.platform !== 'linux' && !process.argv.includes('--prepare-only') && !process.argv.includes('--print-config')) {
  throw new Error('Fluent Bit Collector route smoke runs only on Linux CI.');
} else {
  const directory = await mkdtemp(path.join(os.tmpdir(), 'fullnet-fluent-bit-route-'));
  try {
    await prepare(directory);
    if (process.argv.includes('--print-config')) {
      process.stdout.write(await readFile(path.join(directory, 'fluent-bit.conf'), 'utf8'));
    } else if (process.argv.includes('--prepare-only')) {
      process.stdout.write('Fluent Bit route fixture prepared.\n');
    } else {
      await run(directory);
      await verify(directory);
      await appendRestartEvents(directory);
      await run(directory);
      await verifyRestart(directory);
      process.stdout.write('Fluent Bit Collector route smoke passed.\n');
    }
  } finally {
    await rm(directory, { recursive: true, force: true });
  }
}
