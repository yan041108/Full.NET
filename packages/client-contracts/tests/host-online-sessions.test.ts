import { describe, expect, it } from 'vitest';
import { isHostOnlineSession, isHostOnlineSessionPage } from '../src/host-online-sessions';

describe('Host 在线会话客户端契约', () => {
  it('校验分页列表与单条会话', () => {
    const session = {
      id: '019bc2b1-2a40-7cc3-8992-a80de51bf295',
      userId: '019bc2b1-2a40-7cc3-8992-a80de51bf296',
      username: 'operator',
      displayName: '运维账号',
      clientId: 'fullnet-admin',
      activeTenantId: null,
      createdAtUtc: '2026-07-26T00:00:00Z',
      expiresAtUtc: '2026-08-26T00:00:00Z'
    };
    expect(isHostOnlineSession(session)).toBe(true);
    expect(isHostOnlineSessionPage({
      items: [session],
      page: 1,
      pageSize: 20,
      total: 1
    })).toBe(true);
    expect(isHostOnlineSession({ id: 'invalid' })).toBe(false);
  });
});
