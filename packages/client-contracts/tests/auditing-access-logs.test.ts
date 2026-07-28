import { describe, expect, it } from 'vitest';
import {
  applyAuditingAccessLogContainsDefaults,
  isAuditingAccessLog,
  isAuditingAccessLogCursorPage,
  isAuditingAccessLogPage
} from '../src/auditing-access-logs';
import { createAdminNavigationCatalog } from '../src/navigation-catalog';

const sampleLog = {
  id: '01912345-6789-7abc-8def-0123456789ab',
  occurredAtUtc: '2026-07-25T08:00:00.000Z',
  httpMethod: 'GET',
  requestPath: '/api/v1/settings/enum-catalogs',
  statusCode: 200,
  durationMs: 12,
  userId: '01912345-6789-7abc-8def-0123456789ac',
  tenantId: null,
  traceId: '00-trace',
  clientIpFingerprint: 'abc',
  isAuthenticated: true
};

describe('Auditing 访问日志契约', () => {
  it('接受合法访问日志分页', () => {
    expect(isAuditingAccessLog(sampleLog)).toBe(true);
    expect(isAuditingAccessLogPage({
      items: [sampleLog],
      page: 1,
      pageSize: 20,
      total: 1
    })).toBe(true);
  });

  it('拒绝畸形访问日志', () => {
    expect(isAuditingAccessLog({ ...sampleLog, id: 'bad' })).toBe(false);
    expect(isAuditingAccessLogPage({
      items: [{ ...sampleLog, statusCode: '200' }],
      page: 1,
      pageSize: 20,
      total: 1
    })).toBe(false);
  });

  it('接受游标页并拒绝不一致的后续状态', () => {
    expect(isAuditingAccessLogCursorPage({
      items: [sampleLog],
      nextCursor: 'AQID',
      hasMore: true
    })).toBe(true);
    expect(isAuditingAccessLogCursorPage({
      items: [sampleLog],
      nextCursor: null,
      hasMore: true
    })).toBe(false);
    expect(isAuditingAccessLogCursorPage({
      items: [sampleLog],
      nextCursor: 'AQID',
      hasMore: false
    })).toBe(false);
  });

  it('导航白名单发布 access-logs', () => {
    const catalog = createAdminNavigationCatalog();
    expect(catalog.localNavigationFor('access-logs')).toEqual({
      componentKey: 'access-logs',
      routeName: 'access-logs',
      path: '/auditing/access-logs'
    });
  });

  it('contains 首次启用时补充可见的 24 小时 UTC 范围', () => {
    expect(applyAuditingAccessLogContainsDefaults(
      { pathContains: ' /api/v1 ' },
      new Date('2026-07-28T08:30:00.000Z')
    )).toEqual({
      pathContains: '/api/v1',
      fromUtc: '2026-07-27T08:30:00.000Z',
      toUtc: '2026-07-28T08:30:00.000Z'
    });
  });

  it('无 contains 或半填时间范围时不静默补齐', () => {
    expect(applyAuditingAccessLogContainsDefaults(
      {},
      new Date('2026-07-28T08:30:00.000Z')
    )).toEqual({});
    expect(applyAuditingAccessLogContainsDefaults(
      {
        pathContains: '/api',
        fromUtc: '2026-07-27T08:30:00.000Z'
      },
      new Date('2026-07-28T08:30:00.000Z')
    )).toEqual({
      pathContains: '/api',
      fromUtc: '2026-07-27T08:30:00.000Z'
    });
  });
});
