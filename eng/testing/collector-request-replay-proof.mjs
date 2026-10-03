import assert from 'node:assert/strict';

// 真实请求快照必须逐 ID、路由和内容对账，不能只用总数掩盖重复或漏投。
export function verifyCollectorRequestReplay(source, priority, bestEffort) {
  assert.ok(Array.isArray(source) && source.length > 0 && source.length <= 5200);
  assert.ok(Array.isArray(priority) && Array.isArray(bestEffort));
  const expected = new Map();
  const requestIds = new Set();
  let projections = 0;
  for (const event of source) {
    assert.equal(event['@mt'], 'HttpOperationCompleted');
    assert.match(event.LogEventId, /^[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i);
    assert.ok(!expected.has(event.LogEventId) && typeof event.RequestId === 'string' && event.RequestId.length > 0 && !requestIds.has(event.RequestId));
    assert.ok([200, 500].includes(event['http.status_code']));
    const hasRequest = typeof event.RequestPayload === 'string';
    const hasResponse = typeof event.ResponsePayload === 'string';
    assert.equal(hasRequest, hasResponse);
    if (hasRequest) {
      JSON.parse(event.RequestPayload);
      JSON.parse(event.ResponsePayload);
      projections++;
    }
    requestIds.add(event.RequestId);
    expected.set(event.LogEventId, event);
  }
  assert.equal(priority.length + bestEffort.length, source.length);
  const seen = new Set();
  const removed = new Set(['DiagnosticGroup', 'kubernetes', 'tenant_id', 'user_id']);
  for (const [lane, events] of [['priority', priority], ['bestEffort', bestEffort]]) {
    for (const event of events) {
      const original = expected.get(event.LogEventId);
      assert.ok(original && !seen.has(event.LogEventId));
      seen.add(event.LogEventId);
      const isPriority = original['reliability.class'] === 'Priority' || ['Error', 'Fatal'].includes(original['@l']);
      assert.equal(lane, isPriority ? 'priority' : 'bestEffort');
      for (const [key, value] of Object.entries(original))
        if (!removed.has(key)) assert.deepEqual(event[key], value);
      for (const key of Object.keys(event)) {
        assert.ok(!removed.has(key) && !key.startsWith('_fullnet_') && key !== 'log');
        assert.ok(Object.hasOwn(original, key) || ['time', 'stream', '_p'].includes(key), `Unexpected collector output key: ${key}`);
        if (key === '_p') assert.equal(event[key], 'F');
      }
    }
  }
  return { total: source.length, priority: priority.length, bestEffort: bestEffort.length, projections };
}
