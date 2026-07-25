import { describe, expect, it } from 'vitest';
import {
  isAuditingOperationLog,
  isAuditingOperationLogPage
} from '../src/auditing-operation-logs';
import { createAdminNavigationCatalog } from '../src/navigation-catalog';

const sampleLog = {
  id: '01912345-6789-7abc-8def-0123456789ab',
  occurredAtUtc: '2026-07-25T08:00:00.000Z',
  actionKey: 'POST /api/v1/settings/config-entries',
  httpMethod: 'POST',
  requestPath: '/api/v1/settings/config-entries',
  statusCode: 201,
  durationMs: 20,
  succeeded: true,
  userId: '01912345-6789-7abc-8def-0123456789ac',
  tenantId: null,
  traceId: 'trace',
  clientIpFingerprint: 'abc',
  permissionCode: 'settings.config.write'
};

describe('Auditing 操作日志契约', () => {
  it('接受合法操作日志分页', () => {
    expect(isAuditingOperationLog(sampleLog)).toBe(true);
    expect(isAuditingOperationLogPage({
      items: [sampleLog],
      page: 1,
      pageSize: 20,
      total: 1
    })).toBe(true);
  });

  it('拒绝畸形操作日志', () => {
    expect(isAuditingOperationLog({ ...sampleLog, succeeded: 'yes' })).toBe(false);
  });

  it('导航白名单发布 operation-logs', () => {
    const catalog = createAdminNavigationCatalog();
    expect(catalog.localNavigationFor('operation-logs')).toEqual({
      componentKey: 'operation-logs',
      routeName: 'operation-logs',
      path: '/auditing/operation-logs'
    });
  });
});
