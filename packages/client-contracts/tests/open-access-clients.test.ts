import { describe, expect, it } from 'vitest';
import { isOpenAccessClient, isOpenAccessClientPage } from '../src/open-access-clients';

describe('OpenAccess 客户端契约', () => {
  it('接受省略可空字段的接入方应用响应', () => {
    expect(isOpenAccessClient({
      id: '019bc2b1-2a40-7cc3-8992-a80de51bf294',
      apiKeyId: '019bc2b1-2a40-7cc3-8992-a80de51bf295',
      userId: '019bc2b1-2a40-7cc3-8992-a80de51bf296',
      username: 'admin',
      name: '演示接入方',
      accessKeyId: 'fnoa_demo123456',
      permissions: ['identity.users.read'],
      isActive: true,
      createdAtUtc: '2026-09-21T00:00:00Z',
      version: 1
    })).toBe(true);
  });

  it('接受省略可空字段的分页列表', () => {
    expect(isOpenAccessClientPage({
      items: [{
        id: '019bc2b1-2a40-7cc3-8992-a80de51bf294',
        apiKeyId: '019bc2b1-2a40-7cc3-8992-a80de51bf295',
        userId: '019bc2b1-2a40-7cc3-8992-a80de51bf296',
        username: 'admin',
        name: '演示接入方',
        accessKeyId: 'fnoa_demo123456',
        permissions: [],
        isActive: true,
        createdAtUtc: '2026-09-21T00:00:00Z',
        version: 1
      }],
      page: 1,
      pageSize: 20,
      total: 1
    })).toBe(true);
  });
});
