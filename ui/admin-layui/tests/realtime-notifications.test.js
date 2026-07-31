import { describe, expect, it, vi } from 'vitest';
import { NOTIFICATIONS_REALTIME_CODES } from '@fullnet/client-contracts';
import { createLayuiNotificationsRealtime } from '../js/core/realtime-notifications.js';

describe('Layui Notifications 实时状态', () => {
  it('显式禁用时不连接 Hub 也不查询未读数', async () => {
    const session = createSession();
    const request = vi.fn();
    const realtimeFactory = vi.fn();

    const state = createLayuiNotificationsRealtime({
      session,
      enabled: false,
      request,
      onUnreadCount: vi.fn(),
      onInboxChanged: vi.fn(),
      onAnnouncementChanged: vi.fn(),
      realtimeFactory
    });

    await state.whenSettled();
    expect(realtimeFactory).not.toHaveBeenCalled();
    expect(session.subscribe).not.toHaveBeenCalled();
    expect(request).not.toHaveBeenCalled();
    await state.dispose();
  });

  it('将管理端解析后的 API Hub 地址传给共享 SignalR 客户端', async () => {
    const session = createSession();
    const realtimeFactory = vi.fn(() => ({
      whenSettled: async () => undefined,
      dispose: async () => undefined
    }));
    const hubPath = 'http://localhost:5149/hubs/notifications';

    const state = createLayuiNotificationsRealtime({
      session,
      hubPath,
      request: vi.fn(),
      onUnreadCount: vi.fn(),
      onInboxChanged: vi.fn(),
      onAnnouncementChanged: vi.fn(),
      realtimeFactory
    });

    expect(realtimeFactory).toHaveBeenCalledWith(expect.objectContaining({
      hubPath
    }));
    await state.dispose();
  });

  it('同步未读数并分发站内信与公告刷新', async () => {
    const session = createSession();
    const request = vi.fn().mockResolvedValue({ unreadCount: 5 });
    const onUnreadCount = vi.fn();
    const onInboxChanged = vi.fn();
    const onAnnouncementChanged = vi.fn();
    let onMessage;
    const disposeRealtime = vi.fn().mockResolvedValue(undefined);
    const state = createLayuiNotificationsRealtime({
      session,
      request,
      onUnreadCount,
      onInboxChanged,
      onAnnouncementChanged,
      realtimeFactory: options => {
        onMessage = options.onMessage;
        return {
          whenSettled: async () => undefined,
          dispose: disposeRealtime
        };
      }
    });

    session.publish(authenticatedSnapshot());
    await state.whenSettled();
    expect(request).toHaveBeenCalledWith(
      '/api/v1/notifications/my-inbox-messages/unread-count'
    );
    expect(onUnreadCount).toHaveBeenLastCalledWith(5);

    onMessage({
      code: NOTIFICATIONS_REALTIME_CODES.inboxMessageReceived,
      data: { messageId: 'message-id' }
    });
    expect(onInboxChanged).toHaveBeenCalledOnce();

    onMessage({
      code: NOTIFICATIONS_REALTIME_CODES.inboxUnreadCountChanged,
      data: { unreadCount: 2 }
    });
    expect(onUnreadCount).toHaveBeenLastCalledWith(2);
    expect(onInboxChanged).toHaveBeenCalledTimes(2);

    onMessage({
      code: NOTIFICATIONS_REALTIME_CODES.announcementPublished,
      data: { announcementId: 'announcement-id' }
    });
    expect(onAnnouncementChanged).toHaveBeenCalledOnce();

    session.publish(anonymousSnapshot());
    await state.whenSettled();
    expect(onUnreadCount).toHaveBeenLastCalledWith(0);

    await state.dispose();
    expect(session.unsubscribe).toHaveBeenCalledOnce();
    expect(disposeRealtime).toHaveBeenCalledOnce();
  });

  it('忽略负数和非整数未读值', () => {
    const session = createSession();
    const onUnreadCount = vi.fn();
    let onMessage;
    const state = createLayuiNotificationsRealtime({
      session,
      request: vi.fn(),
      onUnreadCount,
      onInboxChanged: vi.fn(),
      onAnnouncementChanged: vi.fn(),
      realtimeFactory: options => {
        onMessage = options.onMessage;
        return {
          whenSettled: async () => undefined,
          dispose: async () => undefined
        };
      }
    });
    onUnreadCount.mockClear();

    onMessage({
      code: NOTIFICATIONS_REALTIME_CODES.inboxUnreadCountChanged,
      data: { unreadCount: -1 }
    });
    onMessage({
      code: NOTIFICATIONS_REALTIME_CODES.inboxUnreadCountChanged,
      data: { unreadCount: 1.5 }
    });

    expect(onUnreadCount).not.toHaveBeenCalled();
    void state.dispose();
  });

  it('SignalR 重连后补拉未读数并刷新当前收件箱', async () => {
    const session = createSession();
    const request = vi.fn()
      .mockResolvedValueOnce({ unreadCount: 2 })
      .mockResolvedValueOnce({ unreadCount: 6 });
    const onUnreadCount = vi.fn();
    const onInboxChanged = vi.fn();
    let onReconnected;
    const state = createLayuiNotificationsRealtime({
      session,
      request,
      onUnreadCount,
      onInboxChanged,
      onAnnouncementChanged: vi.fn(),
      realtimeFactory: options => {
        onReconnected = options.onReconnected;
        return {
          whenSettled: async () => undefined,
          dispose: async () => undefined
        };
      }
    });

    session.publish(authenticatedSnapshot());
    await state.whenSettled();
    await onReconnected();
    await state.whenSettled();

    expect(request).toHaveBeenCalledTimes(2);
    expect(onUnreadCount).toHaveBeenLastCalledWith(6);
    expect(onInboxChanged).toHaveBeenCalledOnce();
    await state.dispose();
  });

  it('SignalR 重连后的补拉失败保持现有状态且不传播异常', async () => {
    const session = createSession();
    const request = vi.fn()
      .mockResolvedValueOnce({ unreadCount: 3 })
      .mockRejectedValueOnce(new Error('offline'));
    const onUnreadCount = vi.fn();
    const onInboxChanged = vi.fn();
    let onReconnected;
    const state = createLayuiNotificationsRealtime({
      session,
      request,
      onUnreadCount,
      onInboxChanged,
      onAnnouncementChanged: vi.fn(),
      realtimeFactory: options => {
        onReconnected = options.onReconnected;
        return {
          whenSettled: async () => undefined,
          dispose: async () => undefined
        };
      }
    });

    session.publish(authenticatedSnapshot());
    await state.whenSettled();
    await expect(onReconnected()).resolves.toBeUndefined();
    await state.whenSettled();

    expect(onUnreadCount).toHaveBeenLastCalledWith(3);
    expect(onInboxChanged).not.toHaveBeenCalled();
    await state.dispose();
  });
});

function createSession() {
  let listener;
  const session = {
    unsubscribe: vi.fn(),
    snapshot: () => anonymousSnapshot(),
    readAccessToken: () => 'access-token',
    subscribe: vi.fn(value => {
      listener = value;
      value(anonymousSnapshot());
      return session.unsubscribe;
    }),
    publish(snapshot) {
      listener?.(snapshot);
    }
  };
  return session;
}

function authenticatedSnapshot() {
  return {
    state: 'authenticated',
    currentUser: {
      sessionId: 'session-id',
      tenantId: null
    },
    navigation: [],
    availableTenants: [],
    switching: false,
    savingLocale: false,
    currentContextName: 'Full.NET Host'
  };
}

function anonymousSnapshot() {
  return {
    state: 'anonymous',
    navigation: [],
    availableTenants: [],
    switching: false,
    savingLocale: false,
    currentContextName: 'Full.NET Host'
  };
}
