import assert from 'node:assert/strict';
import test from 'node:test';
import { verifyCollectorRequestReplay } from '../../eng/testing/collector-request-replay-proof.mjs';

const ordinary = { '@t': '2026-10-01T00:00:00Z', '@mt': 'HttpOperationCompleted', LogEventId: '0199aabb-ccdd-7000-8000-000000000001', Instance: 'fixture', RequestId: 'one', 'reliability.class': 'BestEffort', 'http.status_code': 200, RequestPayload: '{"pageSize":20}', ResponsePayload: '{"totalCount":100}' };
const error = { ...ordinary, LogEventId: '0199aabb-ccdd-7000-8000-000000000002', RequestId: 'two', 'reliability.class': 'Priority', 'http.status_code': 500 };
const source = [ordinary, error];

test('完整请求快照必须在正确路由中保留内容', () => {
  assert.deepEqual(verifyCollectorRequestReplay(source, [error], [ordinary]), { total: 2, priority: 1, bestEffort: 1, projections: 2 });
  assert.deepEqual(verifyCollectorRequestReplay(source, [error], [{ ...ordinary, _p: 'F' }]), { total: 2, priority: 1, bestEffort: 1, projections: 2 });
});
test('拒绝漏投、重复镜像、错误路由和投影篡改', () => {
  for (const [priority, bestEffort] of [[[], [ordinary]], [[error, error], []], [[ordinary], [error]], [[error], [{ ...ordinary, ResponsePayload: '{}' }]], [[error], [{ ...ordinary, kubernetes: {} }]]])
    assert.throws(() => verifyCollectorRequestReplay(source, priority, bestEffort));
  assert.throws(() => verifyCollectorRequestReplay(source, [error], [{ ...ordinary, _p: 'P' }]));
  assert.throws(() => verifyCollectorRequestReplay(source, [error], [{ ...ordinary, arbitrary: 'unexpected' }]));
});
test('拒绝非法或重复的源ID，保留受控剔除字段语义', () => {
  assert.throws(() => verifyCollectorRequestReplay([], [], []));
  assert.throws(() => verifyCollectorRequestReplay([ordinary, ordinary], [error], [ordinary]));
  assert.throws(() => verifyCollectorRequestReplay([{ ...ordinary, LogEventId: 'invalid' }], [], [ordinary]));
  assert.deepEqual(verifyCollectorRequestReplay([{ ...ordinary, DiagnosticGroup: 'private' }], [], [ordinary]), { total: 1, priority: 0, bestEffort: 1, projections: 1 });
});
