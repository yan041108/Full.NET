import assert from 'node:assert/strict';
import test from 'node:test';
import { verifyDocuments, createWorkload } from '../../eng/testing/log-consumer-throughput-probe.mjs';

test('逐 ID 对账拒绝缺失、重复与内容错误，不能用总数替代', () => {
  const events = [{ LogEventId: 'a', '@mt': 'one' }, { LogEventId: 'b', '@mt': 'two' }];
  const docs = events.map(event => ({ _id: event.LogEventId, found: true, _source: event }));
  assert.equal(verifyDocuments(events, docs), 2);
  assert.throws(() => verifyDocuments(events, docs.slice(0, 1)));
  assert.throws(() => verifyDocuments(events, [docs[0], docs[0]]));
  assert.throws(() => verifyDocuments(events, [docs[0], { ...docs[1], found: false }]));
  assert.throws(() => verifyDocuments(events, [docs[0], { ...docs[1], _source: { ...events[1], '@mt': 'wrong' } }]));
});

test('工作负载严格限制条数和 UTF-8 单事件字节，拒绝重复 ID', () => {
  let id = 0;
  const workload = createWorkload({ base: { '@mt': '中文' }, eventId: () => String(++id), count: 3, eventBytes: 2048 });
  assert.equal(workload.events.length, 3);
  assert.equal(workload.jsonBytes, 6144);
  for (const event of workload.events) assert.equal(Buffer.byteLength(JSON.stringify(event)), 2048);
  assert.throws(() => createWorkload({ base: {}, eventId: () => 'same', count: 2, eventBytes: 2048 }));
  for (const count of [0, 1001, 1.5]) assert.throws(() => createWorkload({ base: {}, eventId: () => 'a', count, eventBytes: 2048 }));
});
