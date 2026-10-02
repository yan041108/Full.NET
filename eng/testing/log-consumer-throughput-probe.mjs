import assert from 'node:assert/strict';
import { performance } from 'node:perf_hooks';

// 对账必须核对身份和完整内容；数量相同不能证明没有缺失或错误替换。
export function verifyDocuments(events, docs) {
  assert.equal(docs.length, events.length);
  const expected = new Map(events.map(event => [event.LogEventId, event]));
  assert.equal(expected.size, events.length);
  const seen = new Set();
  for (const doc of docs) {
    assert.equal(doc.found, true);
    assert.ok(expected.has(doc._id) && !seen.has(doc._id));
    assert.deepEqual(doc._source, expected.get(doc._id));
    seen.add(doc._id);
  }
  return seen.size;
}

export function createWorkload({ base, eventId, count, eventBytes }) {
  assert.ok(Number.isInteger(count) && count >= 1 && count <= 1000);
  assert.ok(Number.isInteger(eventBytes) && eventBytes >= 512 && eventBytes <= 4096);
  const events = [];
  const ids = new Set();
  for (let index = 0; index < count; index++) {
    const event = { ...base, LogEventId: eventId(), Sequence: index, Padding: '' };
    assert.ok(typeof event.LogEventId === 'string' && !ids.has(event.LogEventId));
    ids.add(event.LogEventId);
    const remaining = eventBytes - Buffer.byteLength(JSON.stringify(event));
    assert.ok(remaining >= 0);
    event.Padding = 'x'.repeat(remaining);
    assert.equal(Buffer.byteLength(JSON.stringify(event)), eventBytes);
    events.push(event);
  }
  return { events, jsonBytes: count * eventBytes };
}

// 单次有界突发仅建立串行消费者基线；计时包含 CLI 启动和位点观察开销，不作为最大吞吐。
export async function runThroughputProbe({ kube, docker, namespace, kafka, es, source, group, dlq,
  event, eventId, index, esPort, esRequest, basic, waitFor }) {
  const workload = createWorkload({ base: { ...event, '@mt': 'local-throughput' }, eventId, count: 1000, eventBytes: 2048 });
  const readPod = async () => {
    const items = JSON.parse(await kube(['-n', namespace, 'get', 'pods', '-o', 'json'])).items;
    assert.equal(items.length, 1);
    return items[0];
  };
  const before = await readPod();
  const name = before.metadata.name;
  const readFile = file => kube(['-n', namespace, 'exec', name, '--', 'cat', `/sys/fs/cgroup/${file}`]);
  const cpuBefore = await readFile('cpu.stat');
  const value = (text, key) => {
    const match = text.match(new RegExp(`^${key} (\\d+)$`, 'm'));
    assert.ok(match, `Missing cgroup field ${key}`);
    return Number(match[1]);
  };
  const input = workload.events.map(item => `${item.LogEventId}|${JSON.stringify(item)}\n`).join('');
  const started = performance.now();
  await docker(['exec', '-i', kafka, '/opt/kafka/bin/kafka-console-producer.sh', '--bootstrap-server', 'localhost:9092',
    '--topic', source, '--producer-property', 'acks=all', '--property', 'parse.key=true', '--property', 'key.separator=|'], input);
  await waitFor('1000-record burst completely committed', async () => {
    const output = await docker(['exec', kafka, '/opt/kafka/bin/kafka-consumer-groups.sh', '--bootstrap-server', 'localhost:9092',
      '--group', group, '--describe']);
    const row = output.split('\n').map(line => line.trim().split(/\s+/)).find(parts => parts[1] === source && parts[2] === '0');
    return row?.[3] === '1003' && row[4] === '1003' && row[5] === '0';
  }, 120000);
  const elapsedMilliseconds = performance.now() - started;
  assert.ok(elapsedMilliseconds <= 120000, 'Total send-to-commit observation exceeded the 120-second budget.');
  const cpuAfter = await readFile('cpu.stat');
  const memoryPeakBytes = Number(await readFile('memory.peak'));
  assert.ok(Number.isFinite(memoryPeakBytes) && memoryPeakBytes > 0);
  const after = await readPod();
  assert.equal(after.metadata.uid, before.metadata.uid);
  assert.equal(after.status.containerStatuses[0].restartCount, before.status.containerStatuses[0].restartCount);
  assert.equal(after.status.containerStatuses[0].ready, true);
  let verified = 0;
  for (let offset = 0; offset < workload.events.length; offset += 100) {
    const batch = workload.events.slice(offset, offset + 100);
    const response = await esRequest(esPort, 'POST', `/${index}/_mget`, basic, { ids: batch.map(item => item.LogEventId) });
    assert.equal(response.status, 200);
    verified += verifyDocuments(batch, response.body.docs);
  }
  const deadLetters = await docker(['exec', kafka, '/opt/kafka/bin/kafka-get-offsets.sh',
    '--bootstrap-server', 'localhost:9092', '--topic', dlq, '--time', 'latest']);
  assert.equal(deadLetters, `${dlq}:0:1`);
  const cpuUsageMicroseconds = value(cpuAfter, 'usage_usec') - value(cpuBefore, 'usage_usec');
  const throttledMicroseconds = value(cpuAfter, 'throttled_usec') - value(cpuBefore, 'throttled_usec');
  assert.ok(cpuUsageMicroseconds >= 0 && throttledMicroseconds >= 0);
  const downstreamResourceSamples = await docker(['stats', '--no-stream', '--format', '{{json .}}', kafka, es]);
  return { events: verified, eventJsonBytes: 2048, jsonBytes: workload.jsonBytes,
    elapsedMilliseconds, observedEventsPerSecond: verified * 1000 / elapsedMilliseconds,
    observedJsonBytesPerSecond: workload.jsonBytes * 1000 / elapsedMilliseconds,
    cpuUsageMicroseconds, throttledMicroseconds, memoryPeakBytes,
    resources: before.spec.containers[0].resources, committedOffset: 1003, lag: 0, addedDlqRecords: 0,
    podUidUnchanged: true, noAdditionalRestarts: true,
    downstreamResourceSamples: downstreamResourceSamples.split('\n').map(line => JSON.parse(line)),
    scope: '1000-record burst; includes CLI and commit observation overhead; memory peak covers Pod lifetime; no API latency or sustained-capacity claim' };
}
