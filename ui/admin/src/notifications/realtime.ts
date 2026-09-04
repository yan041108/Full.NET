import {
  inject,
  ref,
  type InjectionKey,
  type Ref
} from 'vue';
import {
  NOTIFICATIONS_REALTIME_CODES,
  createNotificationsRealtimeController,
  type NotificationsRealtimeController,
  type NotificationsRealtimeOptions,
  type NotificationsRealtimeSession,
  type RealtimeMessage
} from '@fullnet/client-contracts';
import type { InboxUnreadCount } from '@fullnet/client-contracts';
import { getInboxUnreadCount } from '../api/inbox-messages';

/** Vue 端通知实时状态，只暴露 UI 真正需要的未读数与修订号。 */
export interface VueNotificationsRealtimeState {
  unreadCount: Ref<number>;
  inboxRevision: Ref<number>;
  announcementRevision: Ref<number>;
  whenSettled(): Promise<void>;
  dispose(): Promise<void>;
}

/** 创建实时通知状态所需依赖，可在测试中替换 HTTP 与实时控制器。 */
export interface VueNotificationsRealtimeOptions {
  session: NotificationsRealtimeSession;
  enabled?: boolean;
  hubPath?: string;
  loadUnreadCount?: () => Promise<InboxUnreadCount>;
  realtimeFactory?: (
    options: NotificationsRealtimeOptions
  ) => NotificationsRealtimeController;
}

/** 注入键，供壳层和通知面板共享同一份实时状态。 */
export const notificationsRealtimeKey:
  InjectionKey<VueNotificationsRealtimeState> = Symbol('notifications-realtime');

/** 未提供 Provider 时的失败关闭回退状态。 */
const fallbackState: VueNotificationsRealtimeState = {
  unreadCount: ref(0),
  inboxRevision: ref(0),
  announcementRevision: ref(0),
  whenSettled: async () => undefined,
  dispose: async () => undefined
};

/** 创建 Vue 端实时通知状态，UI 只消费修订号和未读数，不持有传输令牌。 */
export function createVueNotificationsRealtime(
  options: VueNotificationsRealtimeOptions
): VueNotificationsRealtimeState {
  if (options.enabled === false) {
    return {
      unreadCount: ref(0),
      inboxRevision: ref(0),
      announcementRevision: ref(0),
      whenSettled: async () => undefined,
      dispose: async () => undefined
    };
  }

  const unreadCount = ref(0);
  const inboxRevision = ref(0);
  const announcementRevision = ref(0);
  let sessionGeneration = 0;
  let loadTransition = Promise.resolve();
  let disposed = false;

  /** 仅把实时消息转换为本地修订号或刷新提示，真正未读数仍以 HTTP 权威值为准。 */
  const onMessage = (message: RealtimeMessage): void => {
    if (message.code === NOTIFICATIONS_REALTIME_CODES.inboxMessageReceived) {
      inboxRevision.value++;
      return;
    }

    if (message.code === NOTIFICATIONS_REALTIME_CODES.inboxUnreadCountChanged) {
      // 徽标必须以当前会话作用域的 HTTP 未读数为准；SignalR 载荷只提示刷新。
      inboxRevision.value++;
      void queueUnreadCountLoad(sessionGeneration, false);
      return;
    }

    if (message.code === NOTIFICATIONS_REALTIME_CODES.announcementPublished) {
      announcementRevision.value++;
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
      unreadCount.value = 0;
      return;
    }

    if (snapshot.switching) {
      // 上下文切换会先让旧 Access Token 失效；此窗口不得补拉旧作用域数据，避免其 401 触发 Refresh 覆盖新令牌。
      return;
    }

    void queueUnreadCountLoad(generation, false);
  });

  /** 串行化未读数刷新，并通过 sessionGeneration 丢弃过期会话返回值。 */
  function queueUnreadCountLoad(
    generation: number,
    refreshInbox: boolean
  ): Promise<void> {
    loadTransition = loadTransition.then(async () => {
      try {
        const loadUnreadCount = options.loadUnreadCount ?? getInboxUnreadCount;
        const response = await loadUnreadCount();
        if (!disposed && generation === sessionGeneration) {
          unreadCount.value = response.unreadCount;
          if (refreshInbox) {
            inboxRevision.value++;
          }
        }
      } catch {
        // 初始未读数失败保持零值，实时连接和站内信页面仍可独立恢复。
      }
    });
    return loadTransition;
  }

  return {
    unreadCount,
    inboxRevision,
    announcementRevision,
    async whenSettled(): Promise<void> {
      await Promise.all([loadTransition, realtime.whenSettled()]);
    },
    async dispose(): Promise<void> {
      if (disposed) {
        return;
      }

      disposed = true;
      sessionGeneration++;
      unsubscribeSession();
      await realtime.dispose();
      await loadTransition;
    }
  };
}

/** 读取当前注入的通知实时状态；缺失 Provider 时返回零值回退实现。 */
export function useNotificationsRealtime(): VueNotificationsRealtimeState {
  return inject(notificationsRealtimeKey, fallbackState);
}
