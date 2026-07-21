import { describe, expect, it } from 'vitest';
import { isHostUser, isHostUserPage } from '../src/host-users';

describe('Host 用户客户端契约', () => {
  it('校验分页列表与单条用户', () => {
    const user = {
      id: 'user-id',
      username: 'operator',
      displayName: '运维账号',
      isActive: true,
      createdAtUtc: '2026-07-21T00:00:00Z',
      updatedAtUtc: null,
      version: 1
    };
    expect(isHostUser(user)).toBe(true);
    expect(isHostUserPage({
      items: [user],
      page: 1,
      pageSize: 20,
      total: 1
    })).toBe(true);
    expect(isHostUser({ id: 'user-id' })).toBe(false);
  });
});
