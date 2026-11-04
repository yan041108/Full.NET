import { beforeEach, expect, it } from 'vitest';
import { ownerPendingKey, readPendingOwner, rememberPendingOwner, retainOwnerSession } from './tenant-owner-pending';
const key = ownerPendingKey('user', 'session', 'tenant', 'target');
const evidence = { operationId: 'operation', targetMemberId: 'target', targetUserId: 'receiver', targetVersion: 1, tenantId: 'tenant', actorUserId: 'user' };
beforeEach(() => { retainOwnerSession('reset', 'reset'); });
it('只保留非敏感操作证明，读取副本不改变原证明', () => {
  expect(rememberPendingOwner(key, { ...evidence, currentPassword: 'private-password', totpCode: '593712' } as typeof evidence)).toBe(true);
  const found = readPendingOwner(key)!; expect(found).not.toHaveProperty('currentPassword'); expect(found).not.toHaveProperty('totpCode');
  found.operationId = 'changed'; expect(readPendingOwner(key)!.operationId).toBe('operation');
});
it('同一账号会话跨企业保留未知，切换会话清除旧范围', () => {
  rememberPendingOwner(key, evidence); retainOwnerSession('USER', 'SESSION'); expect(readPendingOwner(key)).toBeDefined();
  retainOwnerSession('user', 'new-session'); expect(readPendingOwner(key)).toBeUndefined();
});
it('达到固定上限时拒绝新操作，不驱逐未核对事实', () => {
  for (let index = 0; index < 64; index++) expect(rememberPendingOwner(ownerPendingKey('user', 'session', 'tenant', String(index)), evidence)).toBe(true);
  expect(rememberPendingOwner(key, evidence)).toBe(false);
  expect(readPendingOwner(ownerPendingKey('user', 'session', 'tenant', '0'))).toBeDefined();
});
