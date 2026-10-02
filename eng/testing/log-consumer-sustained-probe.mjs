import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { setTimeout as sleep } from 'node:timers/promises';
import { createWorkload, verifyDocuments } from './log-consumer-throughput-probe.mjs';

// 验收判定必须独立于实际工具调用，未知结果不能被写成通过。
export function verifySustainedProof(samples, { sendMilliseconds, resumedMilliseconds, finalOffset }) {
  assert.ok(Number.isFinite(sendMilliseconds) && sendMilliseconds >= 59000 && sendMilliseconds <= 90000);
  assert.equal(finalOffset, 3003);
  assert.ok(Number.isFinite(resumedMilliseconds) && resumedMilliseconds > 0 && resumedMilliseconds < sendMilliseconds);
  assert.ok(Array.isArray(samples) && samples.length >= 3 && samples.length <= 40);
  let previousTime = -1;
  let previousOffset = 3;
  let previousEnd = 3;
  for (const point of samples) {
    assert.ok(Number.isFinite(point.milliseconds) && point.milliseconds > previousTime);
    assert.ok(['healthy', 'paused', 'recovering', 'final'].includes(point.phase));
    assert.ok(Number.isInteger(point.committed) && point.committed >= previousOffset);
    assert.ok(Number.isInteger(point.sourceEnd) && point.sourceEnd >= previousEnd && point.sourceEnd <= finalOffset);
    assert.equal(point.lag, point.sourceEnd - point.committed);
    assert.ok(point.lag >= 0 && point.lag <= 1000);
    previousTime = point.milliseconds;
    previousOffset = point.committed;
    previousEnd = point.sourceEnd;
  }
  assert.ok(samples.some(point => point.phase === 'paused' && point.lag > 0), 'Missing real backlog during ES pause.');
  const caughtUp = samples.find(point => point.phase === 'recovering' && point.milliseconds > resumedMilliseconds
    && point.milliseconds < sendMilliseconds && point.sourceEnd < finalOffset && point.lag === 0);
  assert.ok(caughtUp, 'Consumer must catch up while input continues.');
  const final = samples.at(-1);
  assert.equal(final.phase, 'final');
  assert.equal(final.committed, finalOffset);
  assert.equal(final.sourceEnd, finalOffset);
  assert.ok(final.milliseconds - sendMilliseconds >= 0 && final.milliseconds - sendMilliseconds <= 30000,
    'Final confirmation exceeded the absolute post-send drain deadline.');
  return { maxLagRecords: Math.max(...samples.map(point => point.lag)),
    observedCatchUpMilliseconds: caughtUp.milliseconds - resumedMilliseconds };
}

// 固定本地验收场景；生产链路不引用测试 Producer、故障注入或这些定时器。
export async function runSustainedProbe({ kube, docker, namespace, kafka, es, source, group, dlq,
  event, eventId, index, esPort, esRequest, basic, waitFor }) {
  const events = Array.from({ length: 3 }, () => createWorkload({
    base: { ...event, '@mt': 'local-sustained' }, eventId, count: 1000, eventBytes: 2048,
  }).events).flat();
  assert.equal(new Set(events.map(item => item.LogEventId)).size, 3000);
  const readPod = async () => {
    const pods = JSON.parse(await kube(['-n', namespace, 'get', 'pods', '-o', 'json'])).items;
    assert.equal(pods.length, 1);
    return pods[0];
  };
  const before = await readPod();
  const cgroup = file => kube(['-n', namespace, 'exec', before.metadata.name, '--', 'cat', `/sys/fs/cgroup/${file}`]);
  const cpuBefore = await cgroup('cpu.stat');
  const samples = [];
  let phase = 'healthy';
  let paused = false;
  let resumedMilliseconds;
  let observationError;
  const started = performance.now();
  const abort = new AbortController();
  const child = spawn('docker', ['exec', '-i', kafka, '/opt/kafka/bin/kafka-console-producer.sh',
    '--bootstrap-server', 'localhost:9092', '--topic', source, '--producer-property', 'acks=all',
    '--property', 'parse.key=true', '--property', 'key.separator=|'],
  { stdio: ['pipe', 'ignore', 'ignore'], windowsHide: true });
  const closed = new Promise(resolve => {
    child.once('error', () => resolve(-1));
    child.once('close', code => resolve(code));
  });
  child.stdin.on('error', () => {});
  // 包含 CLI 的最终退出；失败后外层会移除本任务容器，不能遗留远端 exec。
  const deadline = setTimeout(() => { abort.abort(); child.kill(); }, 120000);
  const sample = async (samplePhase = phase, timeoutMilliseconds = 10000) => {
    const output = await docker(['exec', kafka, '/opt/kafka/bin/kafka-consumer-groups.sh',
      '--bootstrap-server', 'localhost:9092', '--group', group, '--describe'], undefined, timeoutMilliseconds);
    const row = output.split('\n').map(line => line.trim().split(/\s+/)).find(parts => parts[1] === source && parts[2] === '0');
    assert.ok(row, 'Missing source partition sample.');
    assert.ok(samples.length < 40);
    const point = { milliseconds: performance.now() - started, phase: samplePhase,
      committed: Number(row[3]), sourceEnd: Number(row[4]), lag: Number(row[5]) };
    samples.push(point);
    return point;
  };
  const observer = (async () => {
    while (!abort.signal.aborted) {
      await sleep(5000, undefined, { signal: abort.signal });
      await sample();
    }
  })().catch(error => { if (!abort.signal.aborted) { observationError = error; abort.abort(); child.kill(); } });
  const fault = (async () => {
    await sleep(20000, undefined, { signal: abort.signal });
    await docker(['pause', es]);
    paused = true;
    phase = 'paused';
    const pauseStarted = performance.now();
    await sleep(10000, undefined, { signal: abort.signal });
    await docker(['unpause', es]);
    paused = false;
    phase = 'recovering';
    resumedMilliseconds = performance.now() - started;
    return performance.now() - pauseStarted;
  })();
  const sender = (async () => {
    for (let offset = 0; offset < events.length; offset += 10) {
      const tick = performance.now();
      assert.ok(tick - started < 90000, 'Paced input exceeded time budget.');
      const frame = events.slice(offset, offset + 10).map(item => `${item.LogEventId}|${JSON.stringify(item)}\n`).join('');
      await new Promise((resolve, reject) => child.stdin.write(frame, error => error ? reject(new Error('Producer input failed.')) : resolve()));
      await sleep(Math.max(0, 200 - (performance.now() - tick)), undefined, { signal: abort.signal });
    }
    const sendMilliseconds = performance.now() - started;
    child.stdin.end();
    assert.equal(await closed, 0, 'Paced Producer must finish successfully.');
    return sendMilliseconds;
  })();
  let sendMilliseconds;
  let pauseMilliseconds;
  let proof;
  let drainedMilliseconds;
  try {
    [sendMilliseconds, pauseMilliseconds] = await Promise.all([sender, fault]);
    assert.ok(pauseMilliseconds >= 10000 && pauseMilliseconds <= 20000, 'ES pause duration exceeded the experiment budget.');
    abort.abort();
    await observer;
    if (observationError) throw observationError;
    // 发送完成时即起算，Producer 关闭与最后一次观察查询同样消耗排空预算。
    const drainStarted = started + sendMilliseconds;
    const drainDeadline = drainStarted + 30000;
    const remaining = () => {
      const value = Math.floor(drainDeadline - performance.now());
      assert.ok(value > 0, 'Absolute drain deadline expired.');
      return value;
    };
    await waitFor('Sustained input fully committed', async () => {
      const point = await sample('final', Math.min(10000, remaining()));
      remaining();
      return point.committed === 3003;
    }, remaining());
    drainedMilliseconds = performance.now() - drainStarted;
    proof = verifySustainedProof(samples, { sendMilliseconds, resumedMilliseconds, finalOffset: 3003 });
  } finally {
    abort.abort();
    child.stdin.destroy();
    if (child.exitCode === null && child.signalCode === null) child.kill();
    await Promise.allSettled([sender, fault, observer, closed]);
    clearTimeout(deadline);
    if (paused) await docker(['unpause', es]);
  }
  const after = await readPod();
  assert.equal(after.metadata.uid, before.metadata.uid);
  assert.equal(after.status.containerStatuses[0].restartCount, before.status.containerStatuses[0].restartCount);
  assert.equal(after.status.containerStatuses[0].ready, true);
  let verified = 0;
  for (let offset = 0; offset < events.length; offset += 100) {
    const batch = events.slice(offset, offset + 100);
    const response = await esRequest(esPort, 'POST', `/${index}/_mget`, basic, { ids: batch.map(item => item.LogEventId) });
    assert.equal(response.status, 200);
    verified += verifyDocuments(batch, response.body.docs);
  }
  assert.equal(await docker(['exec', kafka, '/opt/kafka/bin/kafka-get-offsets.sh',
    '--bootstrap-server', 'localhost:9092', '--topic', dlq, '--time', 'latest']), `${dlq}:0:1`);
  const cpuAfter = await cgroup('cpu.stat');
  const counter = (text, key) => {
    const match = text.match(new RegExp(`^${key} (\\d+)$`, 'm'));
    assert.ok(match);
    return Number(match[1]);
  };
  const memoryPeakBytes = Number(await cgroup('memory.peak'));
  assert.ok(memoryPeakBytes > 0 && memoryPeakBytes <= 256 * 1024 * 1024);
  return { events: verified, eventJsonBytes: 2048, jsonBytes: 3000 * 2048,
    targetEventsPerSecond: 50, observedSentEventsPerSecond: verified * 1000 / sendMilliseconds,
    sendMilliseconds, pauseMilliseconds, resumedMilliseconds, drainedMilliseconds, ...proof, samples,
    cpuUsageMicroseconds: counter(cpuAfter, 'usage_usec') - counter(cpuBefore, 'usage_usec'),
    throttledMicroseconds: counter(cpuAfter, 'throttled_usec') - counter(cpuBefore, 'throttled_usec'),
    memoryPeakBytes, resources: before.spec.containers[0].resources,
    committedOffset: 3003, lag: 0, addedDlqRecords: 0, podUidUnchanged: true, noAdditionalRestarts: true,
    consumerImageId: before.status.containerStatuses[0].imageID,
    scope: '60-second local paced input with 10-second ES pause; sampled catch-up includes CLI overhead; no API P99, maximum throughput or long-duration capacity claim' };
}
