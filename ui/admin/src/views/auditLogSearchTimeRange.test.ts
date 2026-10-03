import { describe, expect, it } from 'vitest';
import { resolveAuditLogSearchTimeRange } from './auditLogSearchTimeRange';

describe('审计日志 contains 时间窗', () => {
  it('没有显式时间时补最近 24 小时并返回可回显的 UTC 值', () => {
    expect(resolveAuditLogSearchTimeRange({}, true, new Date('2026-09-29T12:00:00Z'))).toEqual({
      valid: true,
      fromUtc: '2026-09-28T12:00:00.000Z',
      toUtc: '2026-09-29T12:00:00.000Z',
      defaulted: true
    });
  });

  it('contains 只有一个时间或时间无效时拒绝查询', () => {
    expect(resolveAuditLogSearchTimeRange({ fromUtc: '2026-09-29T00:00:00Z' }, true).valid).toBe(false);
    expect(resolveAuditLogSearchTimeRange({ fromUtc: 'invalid', toUtc: '2026-09-29T00:00:00Z' }, true).valid).toBe(false);
  });

  it('普通筛选可只给单侧时间，倒置范围仍拒绝', () => {
    expect(resolveAuditLogSearchTimeRange({ fromUtc: '2026-09-29T00:00:00Z' }, false)).toEqual({
      valid: true,
      fromUtc: '2026-09-29T00:00:00.000Z',
      toUtc: undefined,
      defaulted: false
    });
    expect(resolveAuditLogSearchTimeRange({
      fromUtc: '2026-09-30T00:00:00Z', toUtc: '2026-09-29T00:00:00Z'
    }, false).valid).toBe(false);
  });
});
