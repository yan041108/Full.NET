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

export interface VueNotificationsRealtimeState {
  unreadCount: Ref<number>;
  inboxRevision: Ref<number>;
  announcementRevision: Ref<number>;
  whenSettled(): Promise<void>;
  dispose(): Promise<void>;
}

export interface VueNotificationsRealtimeOptions {
  session: NotificationsRealtimeSession;
  enabled?: boolean;
  hubPath?: string;
  loadUnreadCount?: () => Promise<InboxUnreadCount>;
  realtimeFactory?: (
    options: NotificationsRealtimeOptions
  ) => NotificationsRealtimeController;
}

export const notificationsRealtimeKey:
  InjectionKey<VueNotificationsRealtimeState> = Symbol('notifications-realtime');

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

  const onMessage = (message: RealtimeMessage): void => {
    if (message.code === NOTIFICATIONS_REALTIME_CODES.inboxMessageReceived) {
      inboxRevision.value++;
      return;
    }

    if (message.code === NOTIFICATIONS_REALTIME_CODES.inboxUnreadCountChanged) {
      const value = message.data?.unreadCount;
      if (typeof value === 'number'
        && Number.isSafeInteger(value)
        && value >= 0) {
        unreadCount.value = value;
        inboxRevision.value++;
      }
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

    void queueUnreadCountLoad(generation, false);
  });

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

export function useNotificationsRealtime(): VueNotificationsRealtimeState {
  return inject(notificationsRealtimeKey, fallbackState);
}
