import { describe, expect, it, vi } from 'vitest';
import {
  NOTIFICATIONS_REALTIME_CODES,
  type IdentitySessionSnapshot,
  type NotificationsRealtimeOptions,
  type RealtimeMessage
} from '@fullnet/client-contracts';
import { createVueNotificationsRealtime } from './realtime';

describe('Vue Notifications 实时状态', () => {
  it('显式禁用时不连接 Hub 也不查询未读数', async () => {
    const session = createSession();
    const loadUnreadCount = vi.fn();
    const realtimeFactory = vi.fn();

    const state = createVueNotificationsRealtime({
      session,
      enabled: false,
      loadUnreadCount,
      realtimeFactory
    });

    await state.whenSettled();
    expect(realtimeFactory).not.toHaveBeenCalled();
    expect(session.subscribe).not.toHaveBeenCalled();
    expect(loadUnreadCount).not.toHaveBeenCalled();
    await state.dispose();
  });

  it('同步初始未读数并按稳定消息推进页面修订号', async () => {
    const session = createSession();
    const loadUnreadCount = vi.fn().mockResolvedValue({ unreadCount: 4 });
    let onMessage: ((message: RealtimeMessage) => void) | undefined;
    const disposeRealtime = vi.fn().mockResolvedValue(undefined);
    const state = createVueNotificationsRealtime({
      session,
      loadUnreadCount,
      realtimeFactory: (options: NotificationsRealtimeOptions) => {
        onMessage = options.onMessage;
        return {
          whenSettled: async () => undefined,
          dispose: disposeRealtime
        };
      }
    });

    session.publish(authenticatedSnapshot());
    await state.whenSettled();
    expect(loadUnreadCount).toHaveBeenCalledOnce();
    expect(state.unreadCount.value).toBe(4);

    onMessage?.({
      code: NOTIFICATIONS_REALTIME_CODES.inboxMessageReceived,
      data: { messageId: 'message-id' }
    });
    expect(state.inboxRevision.value).toBe(1);

    onMessage?.({
      code: NOTIFICATIONS_REALTIME_CODES.inboxUnreadCountChanged,
      data: { unreadCount: 2 }
    });
    expect(state.unreadCount.value).toBe(2);
    expect(state.inboxRevision.value).toBe(2);

    onMessage?.({
      code: NOTIFICATIONS_REALTIME_CODES.announcementPublished,
      data: { announcementId: 'announcement-id' }
    });
    expect(state.announcementRevision.value).toBe(1);

    session.publish(anonymousSnapshot());
    await state.whenSettled();
    expect(state.unreadCount.value).toBe(0);

    await state.dispose();
    expect(session.unsubscribe).toHaveBeenCalledOnce();
    expect(disposeRealtime).toHaveBeenCalledOnce();
  });

  it('忽略无效未读数且旧会话查询不得覆盖新会话', async () => {
    const session = createSession();
    let resolveFirst: ((value: { unreadCount: number }) => void) | undefined;
    const loadUnreadCount = vi.fn()
      .mockReturnValueOnce(new Promise(resolve => {
        resolveFirst = resolve;
      }))
      .mockResolvedValueOnce({ unreadCount: 7 });
    let onMessage: ((message: RealtimeMessage) => void) | undefined;
    const state = createVueNotificationsRealtime({
      session,
      loadUnreadCount,
      realtimeFactory: options => {
        onMessage = options.onMessage;
        return {
          whenSettled: async () => undefined,
          dispose: async () => undefined
        };
      }
    });

    session.publish(authenticatedSnapshot('session-a'));
    session.publish(authenticatedSnapshot('session-b'));
    resolveFirst?.({ unreadCount: 99 });
    await state.whenSettled();
    expect(state.unreadCount.value).toBe(7);

    onMessage?.({
      code: NOTIFICATIONS_REALTIME_CODES.inboxUnreadCountChanged,
      data: { unreadCount: -1 }
    });
    expect(state.unreadCount.value).toBe(7);
    await state.dispose();
  });
});

function createSession() {
  let listener: ((snapshot: IdentitySessionSnapshot) => void) | undefined;
  const session = {
    unsubscribe: vi.fn(),
    snapshot: () => anonymousSnapshot(),
    readAccessToken: () => 'access-token',
    subscribe: vi.fn((value: (snapshot: IdentitySessionSnapshot) => void) => {
      listener = value;
      value(anonymousSnapshot());
      return session.unsubscribe;
    }),
    publish(snapshot: IdentitySessionSnapshot) {
      listener?.(snapshot);
    }
  };
  return session;
}

function authenticatedSnapshot(sessionId = 'session-id'): IdentitySessionSnapshot {
  return {
    state: 'authenticated',
    currentUser: {
      id: 'user-id',
      username: 'admin',
      displayName: '系统管理员',
      tenantId: null,
      actorScope: 'host',
      scope: 'host',
      isSuperAdministrator: true,
      permissions: [],
      sessionId,
      preferredLocale: 'zh-CN',
      profileVersion: 1
    },
    navigation: [],
    availableTenants: [],
    switching: false,
    savingLocale: false,
    currentContextName: 'Full.NET Host'
  };
}

function anonymousSnapshot(): IdentitySessionSnapshot {
  return {
    state: 'anonymous',
    navigation: [],
    availableTenants: [],
    switching: false,
    savingLocale: false,
    currentContextName: 'Full.NET Host'
  };
}
