import assert from 'node:assert/strict';
import test from 'node:test';
import { verifySustainedProof } from '../../eng/testing/log-consumer-sustained-probe.mjs';

const points = [
  { milliseconds: 10000, phase: 'healthy', committed: 403, sourceEnd: 403, lag: 0 },
  { milliseconds: 25000, phase: 'paused', committed: 903, sourceEnd: 1203, lag: 300 },
  { milliseconds: 42000, phase: 'recovering', committed: 2003, sourceEnd: 2003, lag: 0 },
  { milliseconds: 63000, phase: 'final', committed: 3003, sourceEnd: 3003, lag: 0 },
];
const proof = { sendMilliseconds: 61000, resumedMilliseconds: 31000, finalOffset: 3003 };

test('持续输入必须真实产生积压，恢复后在输入未结束时追平', () => {
  assert.deepEqual(verifySustainedProof(points, proof), { maxLagRecords: 300, observedCatchUpMilliseconds: 11000 });
  assert.throws(() => verifySustainedProof(points.map(p => ({ ...p, phase: p.phase === 'paused' ? 'healthy' : p.phase })), proof));
  assert.throws(() => verifySustainedProof(points.filter(p => p.phase !== 'recovering'), proof));
});
test('拒绝超预算、位点跳跃和未持续足够时间的假通过', () => {
  for (const sendMilliseconds of [1000, 90001, NaN]) assert.throws(() => verifySustainedProof(points, { ...proof, sendMilliseconds }));
  assert.throws(() => verifySustainedProof(points.map(p => p.phase === 'paused' ? { ...p, sourceEnd: 2503, lag: 1600 } : p), proof));
  assert.throws(() => verifySustainedProof(points.map(p => p.phase === 'paused' ? { ...p, committed: 3004 } : p), proof));
  assert.throws(() => verifySustainedProof(points.map(p => p.phase === 'final' ? { ...p, committed: 3002, lag: 1 } : p), proof));
  assert.throws(() => verifySustainedProof(points.map(p => p.phase === 'final' ? { ...p, milliseconds: 91001 } : p), proof));
});
