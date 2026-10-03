import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { randomUUID } from 'node:crypto';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const kafkaImage = 'apache/kafka:4.1.2@sha256:5cc2a2fd93fa2687b44015eee04fb2c3edd9e526bd64bf8bec5ff1e268772e0e';
const vectorImage = 'timberio/vector:0.58.0-debian@sha256:1c1ea358c617ea0b23003d5af87f7a678b30f8f7096437e680380c47fc13d2d9';
const nodeImage = 'node:24.21.0-alpine@sha256:ebfe2f90462722a7a4de65e91990e97fe0d401c70e0e762c5b53302f905ec1c1';
const suffix = `${process.pid}-${randomUUID().slice(0, 8)}`;
const network = `fullnet-vector-probe-${suffix}`;
const broker = `fullnet-vector-kafka-${suffix}`;
const fakeEs = `fullnet-vector-es-${suffix}`;
const consumer = `fullnet-vector-consumer-${suffix}`;
const topic = 'fullnet-logs-probe';
const group = 'fullnet-vector-es-probe';

function runDocker(args, options = {}) {
  const result = spawnSync('docker', args, { encoding: 'utf8', maxBuffer: 2 * 1024 * 1024, ...options });
  assert.equal(result.status, 0, `${args.join(' ')}\n${result.stdout ?? ''}\n${result.stderr ?? ''}\n${result.error?.message ?? ''}`);
  return result.stdout.trim();
}

function tryDocker(args) {
  return spawnSync('docker', args, { encoding: 'utf8', maxBuffer: 2 * 1024 * 1024 });
}

async function waitFor(label, probe, timeoutMs = 90000) {
  const deadline = Date.now() + timeoutMs;
  let lastError;
  while (Date.now() < deadline) {
    try {
      const result = probe();
      if (result) return result;
    } catch (error) {
      lastError = error;
    }
    await new Promise((resolve) => setTimeout(resolve, 1000));
  }
  throw new Error(`${label} timed out${lastError ? `: ${lastError.message}` : ''}`);
}

function probeEs() {
  return JSON.parse(runDocker(['exec', fakeEs, 'node', '-e',
    'fetch("http://127.0.0.1:9200/probe").then(r=>r.text()).then(console.log)']));
}

function setEsStatus(status) {
  runDocker(['exec', fakeEs, 'node', '-e',
    `fetch("http://127.0.0.1:9200/status/${status}").then(r=>{if(r.status!==204)process.exit(1)})`]);
  assert.equal(probeEs().itemStatus, status);
}

function committedOffset() {
  const output = runDocker(['exec', broker, '/opt/kafka/bin/kafka-consumer-groups.sh',
    '--bootstrap-server', 'kafka:9092', '--describe', '--group', group]);
  const row = output.split(/\r?\n/).map((line) => line.trim().split(/\s+/))
    .find((fields) => fields[0] === group && fields[1] === topic && fields[2] === '0');
  return row?.[3] === '-' || !row ? null : Number(row[3]);
}

function produce(eventId) {
  const event = { LogEventId: eventId, '@t': '2026-09-30T00:00:00.000Z', '@mt': 'vector-probe' };
  runDocker(['exec', '-i', broker, '/opt/kafka/bin/kafka-console-producer.sh',
    '--bootstrap-server', 'kafka:9092', '--topic', topic], { input: `${JSON.stringify(event)}\n` });
}

async function main() {
  for (const image of [kafkaImage, vectorImage, nodeImage]) {
    if (tryDocker(['image', 'inspect', image]).status !== 0) runDocker(['pull', image]);
  }
  try {
    runDocker(['network', 'create', network]);
    runDocker(['run', '-d', '--name', broker, '--network', network, '--network-alias', 'kafka',
      '-e', 'KAFKA_NODE_ID=1', '-e', 'KAFKA_PROCESS_ROLES=broker,controller',
      '-e', 'KAFKA_LISTENERS=PLAINTEXT://0.0.0.0:9092,CONTROLLER://0.0.0.0:9093',
      '-e', 'KAFKA_ADVERTISED_LISTENERS=PLAINTEXT://kafka:9092',
      '-e', 'KAFKA_LISTENER_SECURITY_PROTOCOL_MAP=PLAINTEXT:PLAINTEXT,CONTROLLER:PLAINTEXT',
      '-e', 'KAFKA_INTER_BROKER_LISTENER_NAME=PLAINTEXT',
      '-e', 'KAFKA_CONTROLLER_LISTENER_NAMES=CONTROLLER',
      '-e', 'KAFKA_CONTROLLER_QUORUM_VOTERS=1@kafka:9093',
      '-e', 'KAFKA_OFFSETS_TOPIC_REPLICATION_FACTOR=1',
      '-e', 'KAFKA_TRANSACTION_STATE_LOG_REPLICATION_FACTOR=1',
      '-e', 'KAFKA_TRANSACTION_STATE_LOG_MIN_ISR=1',
      '-e', 'KAFKA_GROUP_INITIAL_REBALANCE_DELAY_MS=0',
      '-e', 'KAFKA_AUTO_CREATE_TOPICS_ENABLE=false',
      '-e', 'CLUSTER_ID=fullnet-vector-es-probe', kafkaImage]);
    await waitFor('Kafka startup', () => tryDocker(['exec', broker, '/opt/kafka/bin/kafka-topics.sh',
      '--bootstrap-server', 'kafka:9092', '--list']).status === 0);
    runDocker(['exec', broker, '/opt/kafka/bin/kafka-topics.sh', '--bootstrap-server', 'kafka:9092',
      '--create', '--topic', topic, '--partitions', '1', '--replication-factor', '1']);
    runDocker(['run', '-d', '--name', fakeEs, '--network', network, '--network-alias', 'fake-es',
      '--mount', `type=bind,source=${root},target=/work,readonly`, nodeImage,
      'node', '/work/tests/deployment/vector-fake-es.mjs']);
    await waitFor('fake Elasticsearch startup', () => tryDocker(['exec', fakeEs, 'node', '-e',
      'fetch("http://127.0.0.1:9200/probe").then(r=>process.exit(r.ok?0:1)).catch(()=>process.exit(1))']).status === 0);
    runDocker(['run', '-d', '--name', consumer, '--network', network,
      '--mount', `type=bind,source=${root},target=/work,readonly`,
      vectorImage, '--config', '/work/tests/deployment/vector-kafka-es-probe.yaml']);
    await waitFor('Vector Kafka subscription', () => {
      const result = tryDocker(['logs', consumer]);
      return result.status === 0 && `${result.stdout}${result.stderr}`.includes('Vector has started.');
    });

    produce('0199aabb-ccdd-7000-8000-000000000081');
    await waitFor('429 partial retries', () => probeEs().bulkRequests >= 2);
    await new Promise((resolve) => setTimeout(resolve, 6500));
    assert.equal(committedOffset(), null, 'Kafka Offset advanced on a partially rejected 429 Bulk response');

    setEsStatus(201);
    await waitFor('Offset after successful Bulk item', () => committedOffset() === 1);
    setEsStatus(400);
    const priorRequests = probeEs().bulkRequests;
    produce('0199aabb-ccdd-7000-8000-000000000082');
    await waitFor('400 partial response', () => probeEs().bulkRequests > priorRequests);
    await new Promise((resolve) => setTimeout(resolve, 6500));
    const afterPermanentFailure = committedOffset();
    assert.equal(afterPermanentFailure, 1, 'Kafka Offset advanced past a non-isolated permanent Bulk item failure');
    const beforeLaterSuccess = probeEs().bulkRequests;
    produce('0199aabb-ccdd-7000-8000-000000000083');
    await waitFor('later successful Bulk item', () =>
      probeEs().bulkRequests > beforeLaterSuccess && probeEs().responses.some((item) =>
        item.eventId.endsWith('083') && item.status === 201));
    await new Promise((resolve) => setTimeout(resolve, 6500));
    const responses = probeEs().responses;
    assert.ok(responses.some((item) => item.eventId.endsWith('082') && item.status === 400),
      'The permanent failure was not observed for the earlier event');
    assert.ok(responses.some((item) => item.eventId.endsWith('083') && item.status === 201),
      'The later event did not succeed');
    const afterLaterSuccess = committedOffset();
    assert.equal(afterLaterSuccess, 3, 'Expected the observed unsafe Offset advance to remain reproducible');
    process.stdout.write(`Vector Kafka/ES candidate disqualified: 429 held Offset, first success committed 1, permanent 400 was dropped, later success advanced Offset to ${afterLaterSuccess}.\n`);
  } catch (error) {
    for (const name of [consumer, fakeEs, broker]) {
      const logs = tryDocker(['logs', '--tail', '45', name]);
      if (logs.status === 0) process.stderr.write(`${name}:\n${logs.stdout}${logs.stderr}`);
    }
    throw error;
  } finally {
    for (const name of [consumer, fakeEs, broker]) tryDocker(['rm', '-f', name]);
    tryDocker(['network', 'rm', network]);
  }
}

if (process.argv.includes('--help')) {
  process.stdout.write('Usage: node eng/testing/vector-kafka-es-boundary-smoke.mjs [--docker-desktop]\n');
} else if (process.platform !== 'linux' && !process.argv.includes('--docker-desktop')) {
  throw new Error('Vector Kafka/ES boundary smoke requires Linux CI or explicit --docker-desktop.');
} else {
  await main();
}
