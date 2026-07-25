import { describe, expect, it } from 'vitest';
import {
  isAuditingExceptionLog,
  isAuditingExceptionLogPage
} from '../src/auditing-exception-logs';
import { createAdminNavigationCatalog } from '../src/navigation-catalog';

const sampleLog = {
  id: '01912345-6789-7abc-8def-0123456789ab',
  occurredAtUtc: '2026-07-25T08:00:00.000Z',
  exceptionType: 'System.InvalidOperationException',
  message: 'auditing.exception_probe',
  stackTrace: 'at Probe()',
  httpMethod: 'POST',
  requestPath: '/api/v1/auditing/exception-probes',
  userId: '01912345-6789-7abc-8def-0123456789ac',
  tenantId: null,
  traceId: 'trace',
  clientIpFingerprint: 'abc'
};

describe('Auditing 异常日志契约', () => {
  it('接受合法异常日志分页', () => {
    expect(isAuditingExceptionLog(sampleLog)).toBe(true);
    expect(isAuditingExceptionLogPage({
      items: [sampleLog],
      page: 1,
      pageSize: 20,
      total: 1
    })).toBe(true);
  });

  it('拒绝畸形异常日志', () => {
    expect(isAuditingExceptionLog({ ...sampleLog, message: 1 })).toBe(false);
  });

  it('导航白名单发布 exception-logs', () => {
    const catalog = createAdminNavigationCatalog();
    expect(catalog.localNavigationFor('exception-logs')).toEqual({
      componentKey: 'exception-logs',
      routeName: 'exception-logs',
      path: '/auditing/exception-logs'
    });
  });
});
