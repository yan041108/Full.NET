import { describe, expect, it } from 'vitest';
import { readCachePolicySummary } from '../src/generated/guards.generated.js';

const current = {
  entryName: 'settings.grid-preference', ownerModule: 'settings', consistencyClass: 's2',
  accessKind: 'use_cache', l1DurationSeconds: 60, l2DurationSeconds: 900,
  jitterSeconds: 0, negativeDurationSeconds: null, failSafeEnabled: false, failSafeMaxDurationSeconds: null,
  requiresVersionRecheck: false, maxSerializedBytes: 65536, runtimeStatus: 'unknown',
  snapshotAgeSeconds: null, snapshotMaxAgeSeconds: null,
  requiresDirectInvalidation: false, canInvalidate: true, invalidationOperations: []
};
const durations = ['jitterSeconds', 'negativeDurationSeconds', 'failSafeMaxDurationSeconds', 'snapshotAgeSeconds', 'snapshotMaxAgeSeconds'] as const;

describe('缓存策略时长的实际数字字符串合同', () => {
  it('缺失当前必需策略字段时拒绝不完整目录', () => {
    const { runtimeStatus, ...incomplete } = current;
    expect(() => readCachePolicySummary(incomplete)).toThrow();
  });
  it.each(durations)('%s 接受数字、null 和合法数字字符串', field => {
    for (const value of [0, 0.5, null, '0', '0.5', '1e3'])
      expect(readCachePolicySummary({ ...current, [field]: value })[field]).toBe(value);
  });
  it.each(durations)('%s 拒绝与服务器 schema 不符的字符串', field => {
    for (const value of ['', 'bad', '0.5s', 'NaN', 'Infinity'])
      expect(() => readCachePolicySummary({ ...current, [field]: value })).toThrow();
  });
});
