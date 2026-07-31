import {
  NOTIFICATIONS_REALTIME_CODES,
  createNotificationsRealtimeController
} from '@fullnet/client-contracts';

/**
 * 将共享 SignalR 客户端映射到 Layui 壳层和当前通知页面，不复制协议状态机。
 */
export function createLayuiNotificationsRealtime(options) {
  if (options.enabled === false) {
    return {
      whenSettled: async () => undefined,
      dispose: async () => undefined
    };
  }

  let sessionGeneration = 0;
  let loadTransition = Promise.resolve();
  let disposed = false;

  const onMessage = message => {
    if (message.code === NOTIFICATIONS_REALTIME_CODES.inboxMessageReceived) {
      options.onInboxChanged();
      return;
    }

    if (message.code === NOTIFICATIONS_REALTIME_CODES.inboxUnreadCountChanged) {
      const count = message.data?.unreadCount;
      if (Number.isSafeInteger(count) && count >= 0) {
        options.onUnreadCount(count);
        options.onInboxChanged();
      }
      return;
    }

    if (message.code === NOTIFICATIONS_REALTIME_CODES.announcementPublished) {
      options.onAnnouncementChanged();
    }
  };

  const realtime = (options.realtimeFactory
    ?? createNotificationsRealtimeController)({
    session: options.session,
    onMessage,
    onReconnected: () => queueUnreadCountLoad(sessionGeneration, true),
    hubPath: options.hubPath
  });
  const unsubscribeSession = options.session.subscribe(snapshot => {
    const generation = ++sessionGeneration;
    if (snapshot.state !== 'authenticated') {
      options.onUnreadCount(0);
      return;
    }

    void queueUnreadCountLoad(generation, false);
  });

  function queueUnreadCountLoad(generation, refreshInbox) {
    loadTransition = loadTransition.then(async () => {
      try {
        const response = await options.request(
          '/api/v1/notifications/my-inbox-messages/unread-count'
        );
        if (!disposed
          && generation === sessionGeneration
          && Number.isSafeInteger(response?.unreadCount)
          && response.unreadCount >= 0) {
          options.onUnreadCount(response.unreadCount);
          if (refreshInbox) {
            options.onInboxChanged();
          }
        }
      } catch {
        // 未读数查询失败时保持壳层可用，后续 SignalR 消息或页面请求仍可恢复。
      }
    });
    return loadTransition;
  }

  return {
    async whenSettled() {
      await Promise.all([loadTransition, realtime.whenSettled()]);
    },
    async dispose() {
      if (disposed) return;
      disposed = true;
      sessionGeneration++;
      unsubscribeSession();
      await realtime.dispose();
      await loadTransition;
    }
  };
}
