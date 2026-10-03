import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { randomUUID } from 'node:crypto';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const kafkaImage = 'apache/kafka:4.1.2@sha256:5cc2a2fd93fa2687b44015eee04fb2c3edd9e526bd64bf8bec5ff1e268772e0e';
const logstashImage = 'docker.elastic.co/logstash/logstash:9.5.4@sha256:75a495fa0ce94f44da25b7458bf6bf68bb3e4ecde8cdd00cc4376d78fa64e1e7';
const suffix = `${process.pid}-${randomUUID().slice(0, 8)}`;
const network = `fullnet-log-pq-${suffix}`;
const volume = `fullnet-log-pq-data-${suffix}`;
const broker = `fullnet-log-pq-kafka-${suffix}`;
const blocked = `fullnet-log-pq-blocked-${suffix}`;
const replay = `fullnet-log-pq-replay-${suffix}`;
const topic = 'fullnet-logs-probe';
const group = 'fullnet-log-pq-probe';
const eventId = '0199aabb-ccdd-7000-8000-000000000071';

function docker(args, options = {}) {
  const result = spawnSync('docker', args, { encoding: 'utf8', maxBuffer: 2 * 1024 * 1024, ...options });
  assert.equal(result.status, 0, `${args.join(' ')}\n${result.stdout ?? ''}\n${result.stderr ?? ''}\n${result.error?.message ?? ''}`);
  return result.stdout.trim();
}

function tryDocker(args) {
  return spawnSync('docker', args, { encoding: 'utf8', maxBuffer: 2 * 1024 * 1024 });
}

async function waitFor(description, probe, timeoutMs = 120000) {
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
  throw new Error(`${description} timed out${lastError ? `: ${lastError.message}` : ''}`);
}

function logstashArgs(name, fixture) {
  return ['run', '-d', '--name', name, '--network', network,
    '--mount', `type=bind,source=${root},target=/work,readonly`,
    '--mount', `type=bind,source=${path.join(root, 'tests/deployment/logstash-pq-probe.yml')},target=/usr/share/logstash/config/logstash.yml,readonly`,
    '--mount', `type=volume,source=${volume},target=/usr/share/logstash/data`,
    '-e', 'LS_JAVA_OPTS=-Xms256m -Xmx256m',
    '--entrypoint', '/usr/share/logstash/bin/logstash', logstashImage,
    '-f', `/work/tests/deployment/${fixture}`];
}

function queueStats(name) {
  const output = docker(['exec', name, 'curl', '-fsS', 'http://127.0.0.1:9600/_node/stats/pipelines']);
  return JSON.parse(output).pipelines.main.queue;
}

function committedOffset() {
  const output = docker(['exec', broker, '/opt/kafka/bin/kafka-consumer-groups.sh',
    '--bootstrap-server', 'kafka:9092', '--describe', '--group', group]);
  const row = output.split(/\r?\n/).map((line) => line.trim().split(/\s+/))
    .find((fields) => fields[0] === group && fields[1] === topic && fields[2] === '0');
  return row ? Number(row[3]) : null;
}

function replayedEvents() {
  const logs = docker(['logs', replay]);
  return logs.split(/\r?\n/).filter((line) => line.startsWith('{')).flatMap((line) => {
    try { return [JSON.parse(line)]; } catch { return []; }
  }).filter((event) => event.LogEventId === eventId);
}

async function run() {
  for (const image of [kafkaImage, logstashImage]) {
    if (tryDocker(['image', 'inspect', image]).status !== 0) docker(['pull', image]);
  }
  try {
    docker(['network', 'create', network]);
    docker(['volume', 'create', volume]);
    docker(['run', '-d', '--name', broker, '--network', network, '--network-alias', 'kafka',
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
      '-e', 'CLUSTER_ID=fullnet-log-pq-baseline', kafkaImage]);
    await waitFor('Kafka startup', () => tryDocker(['exec', broker, '/opt/kafka/bin/kafka-topics.sh',
      '--bootstrap-server', 'kafka:9092', '--list']).status === 0);
    docker(['exec', broker, '/opt/kafka/bin/kafka-topics.sh', '--bootstrap-server', 'kafka:9092',
      '--create', '--topic', topic, '--partitions', '1', '--replication-factor', '1']);
    docker(logstashArgs(blocked, 'logstash-pq-blocked.conf'));
    await waitFor('Logstash persisted queue startup', () => queueStats(blocked).type === 'persisted');
    const event = { LogEventId: eventId, '@t': '2026-09-30T00:00:00.000Z', '@mt': 'pq-probe' };
    docker(['exec', '-i', broker, '/opt/kafka/bin/kafka-console-producer.sh',
      '--bootstrap-server', 'kafka:9092', '--topic', topic], { input: `${JSON.stringify(event)}\n` });
    await waitFor('persisted event and committed offset', () =>
      queueStats(blocked).events_count === 1 && committedOffset() === 1);
    docker(['kill', blocked]);
    docker(['rm', blocked]);
    // 恢复输出时先断开 Broker，排除同一事件由 Kafka 重读而非 PQ 重放。
    docker(['stop', '-t', '1', broker]);
    assert.equal(docker(['inspect', broker, '--format', '{{.State.Status}}']), 'exited');
    docker(logstashArgs(replay, 'logstash-pq-replay.conf'));
    await waitFor('PQ replay after SIGKILL', () =>
      replayedEvents().length === 1 && queueStats(replay).events_count === 0);
    assert.equal(replayedEvents().length, 1, 'replayed event count changed');
    docker(['start', broker]);
    await waitFor('Kafka committed offset after restart', () => committedOffset() === 1);
    assert.equal(replayedEvents().length, 1, 'event was duplicated after Broker restart');
    process.stdout.write(`Logstash PQ restart characterized: committedOffset=1, recovered LogEventId=${eventId}, queueEvents=0; final Elasticsearch delivery remains unverified.\n`);
  } catch (error) {
    for (const name of [broker, blocked, replay]) {
      const logs = tryDocker(['logs', '--tail', '50', name]);
      if (logs.status === 0) process.stderr.write(`${name}:\n${logs.stdout}${logs.stderr}`);
    }
    throw error;
  } finally {
    for (const name of [replay, blocked, broker]) tryDocker(['rm', '-f', name]);
    tryDocker(['volume', 'rm', volume]);
    tryDocker(['network', 'rm', network]);
  }
}

if (process.argv.includes('--help')) {
  process.stdout.write('Usage: node eng/testing/logstash-pq-restart-smoke.mjs [--docker-desktop]\n');
} else if (process.platform !== 'linux' && !process.argv.includes('--docker-desktop')) {
  throw new Error('Logstash PQ restart smoke requires Linux CI or explicit --docker-desktop.');
} else {
  await run();
}
