import { describe, expect, it } from 'vitest';
import { isMyTenantInvitation } from '../src/my-tenant-invitations.js';

const invitation = { id: '01900000-0000-7000-8000-000000000001', tenantId: '01900000-0000-7000-8000-000000000002',
  tenantName: 'Acme', targetEmail: 'user@example.test', memberRole: 'Member', expiresAtUtc: '2030-01-01T00:00:00Z', createdAtUtc: '2026-11-04T00:00:00Z' };
describe('我的邀请状态', () => {
  it.each(['Pending', 'Accepted'])('允许 %s 供显式接受或激活重试', status => expect(isMyTenantInvitation({ ...invitation, status })).toBe(true));
  it.each([undefined, 'Revoked', 'unknown'])('拒绝未知或不可操作状态 %s', status => expect(isMyTenantInvitation({ ...invitation, status })).toBe(false));
});
