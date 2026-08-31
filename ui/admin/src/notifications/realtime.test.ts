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

  it('将管理端解析后的 API Hub 地址传给共享 SignalR 客户端', async () => {
    const session = createSession();
    const realtimeFactory = vi.fn(() => ({
      whenSettled: async () => undefined,
      dispose: async () => undefined
    }));
    const hubPath = 'http://localhost:5149/hubs/notifications';

    const state = createVueNotificationsRealtime({
      session,
      hubPath,
      realtimeFactory
    });

    expect(realtimeFactory).toHaveBeenCalledWith(expect.objectContaining({
      hubPath
    }));
    await state.dispose();
  });

  it('同步初始未读数并按稳定消息推进页面修订号', async () => {
    const session = createSession();
    const loadUnreadCount = vi.fn()
      .mockResolvedValueOnce({ unreadCount: 4 })
      .mockResolvedValueOnce({ unreadCount: 2 });
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
    await state.whenSettled();
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
      .mockResolvedValueOnce({ unreadCount: 7 })
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
    await state.whenSettled();
    expect(state.unreadCount.value).toBe(7);
    await state.dispose();
  });

  it('SignalR 重连后补拉未读数并刷新当前收件箱', async () => {
    const session = createSession();
    const loadUnreadCount = vi.fn()
      .mockResolvedValueOnce({ unreadCount: 2 })
      .mockResolvedValueOnce({ unreadCount: 6 });
    let onReconnected: (() => void | Promise<void>) | undefined;
    const state = createVueNotificationsRealtime({
      session,
      loadUnreadCount,
      realtimeFactory: options => {
        onReconnected = (options as NotificationsRealtimeOptions & {
          onReconnected?: () => void | Promise<void>;
        }).onReconnected;
        return {
          whenSettled: async () => undefined,
          dispose: async () => undefined
        };
      }
    });

    session.publish(authenticatedSnapshot());
    await state.whenSettled();
    await onReconnected?.();
    await state.whenSettled();

    expect(loadUnreadCount).toHaveBeenCalledTimes(2);
    expect(state.unreadCount.value).toBe(6);
    expect(state.inboxRevision.value).toBe(1);
    await state.dispose();
  });

  it('SignalR 重连后的补拉失败保持现有状态且不传播异常', async () => {
    const session = createSession();
    const loadUnreadCount = vi.fn()
      .mockResolvedValueOnce({ unreadCount: 3 })
      .mockRejectedValueOnce(new Error('offline'));
    let onReconnected: (() => void | Promise<void>) | undefined;
    const state = createVueNotificationsRealtime({
      session,
      loadUnreadCount,
      realtimeFactory: options => {
        onReconnected = (options as NotificationsRealtimeOptions & {
          onReconnected?: () => void | Promise<void>;
        }).onReconnected;
        return {
          whenSettled: async () => undefined,
          dispose: async () => undefined
        };
      }
    });

    session.publish(authenticatedSnapshot());
    await state.whenSettled();
    await expect(onReconnected?.()).resolves.toBeUndefined();
    await state.whenSettled();

    expect(state.unreadCount.value).toBe(3);
    expect(state.inboxRevision.value).toBe(0);
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
