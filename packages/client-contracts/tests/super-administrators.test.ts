import { describe, expect, it } from 'vitest';
import {
  isSuperAdministratorArray,
  isSuperAdministratorAuditArray,
  isSuperAdministratorChangeResponse
} from '../src/super-administrators';

describe('超级管理员客户端契约', () => {
  it('校验列表、审计和变更响应', () => {
    expect(isSuperAdministratorArray([{
      userId: 'user-id', username: 'admin', displayName: '系统管理员', isActive: true
    }])).toBe(true);
    expect(isSuperAdministratorArray([{ userId: 'user-id' }])).toBe(false);
    expect(isSuperAdministratorAuditArray([{
      id: 'audit-id', targetUserId: 'target-id', actorUserId: 'actor-id',
      eventType: 'identity.super_administrator.granted', resultCode: 'ok',
      succeeded: true, occurredAtUtc: '2026-07-18T00:00:00Z'
    }])).toBe(true);
    expect(isSuperAdministratorChangeResponse({
      targetUserId: 'target-id', changed: true
    })).toBe(true);
  });
});
