import { HubConnectionBuilder } from '@microsoft/signalr';
import type { IdentitySessionSnapshot } from './identity-session.js';

export const NOTIFICATIONS_REALTIME_CODES = {
  probeSelf: 'realtime.probe.self',
  announcementPublished: 'notifications.announcement.published',
  inboxMessageReceived: 'notifications.inbox.message.received',
  inboxUnreadCountChanged: 'notifications.inbox.unread.changed'
} as const;

export type NotificationsRealtimeCode =
  typeof NOTIFICATIONS_REALTIME_CODES[keyof typeof NOTIFICATIONS_REALTIME_CODES];

export interface RealtimeMessage {
  code: NotificationsRealtimeCode;
  data?: Record<string, unknown>;
}

export interface NotificationsHubConnection {
  start(): Promise<void>;
  stop(): Promise<void>;
  on(methodName: string, handler: (message: unknown) => void): void;
  off(methodName: string, handler: (message: unknown) => void): void;
}

export interface NotificationsHubConnectionOptions {
  hubPath: string;
  accessTokenFactory: () => string | undefined;
  reconnectDelays: readonly number[];
}

export interface NotificationsRealtimeSession {
  snapshot(): IdentitySessionSnapshot;
  subscribe(listener: (snapshot: IdentitySessionSnapshot) => void): () => void;
  readAccessToken(): string | undefined;
}

export interface NotificationsRealtimeOptions {
  session: NotificationsRealtimeSession;
  onMessage: (message: RealtimeMessage) => void;
  hubPath?: string;
  connectionFactory?: (
    options: NotificationsHubConnectionOptions
  ) => NotificationsHubConnection;
}

export interface NotificationsRealtimeController {
  whenSettled(): Promise<void>;
  dispose(): Promise<void>;
}

const clientMethodName = 'ReceiveMessageAsync';
const reconnectDelays = [0, 2_000, 10_000, 30_000] as const;
const supportedCodes = new Set<string>(Object.values(NOTIFICATIONS_REALTIME_CODES));

/** 验证 SignalR 下行信封，只允许已登记机器码和普通对象数据进入管理端状态。 */
export function isRealtimeMessage(value: unknown): value is RealtimeMessage {
  if (!isRecord(value)
    || typeof value.code !== 'string'
    || !supportedCodes.has(value.code)) {
    return false;
  }

  return value.data === undefined || isRecord(value.data);
}

/**
 * 将认证会话绑定到 Notifications Hub；连接故障保持降级，不阻断 HTTP 会话主流程。
 */
export function createNotificationsRealtimeController(
  options: NotificationsRealtimeOptions
): NotificationsRealtimeController {
  const connectionFactory = options.connectionFactory ?? createSignalRConnection;
  let activeKey: string | undefined;
  let activeConnection: NotificationsHubConnection | undefined;
  let activeHandler: ((message: unknown) => void) | undefined;
  let disposed = false;
  let transition = Promise.resolve();

  const unsubscribe = options.session.subscribe(snapshot => {
    const desiredKey = readConnectionKey(snapshot);
    transition = transition
      .then(() => synchronize(desiredKey))
      .catch(() => undefined);
  });

  async function synchronize(desiredKey: string | undefined): Promise<void> {
    if (disposed && desiredKey !== undefined) {
      return;
    }

    if (activeKey === desiredKey) {
      return;
    }

    await stopActiveConnection();
    if (desiredKey === undefined) {
      return;
    }

    const connection = connectionFactory({
      hubPath: options.hubPath ?? '/hubs/notifications',
      accessTokenFactory: () => options.session.readAccessToken(),
      reconnectDelays
    });
    const handler = (value: unknown) => {
      if (isRealtimeMessage(value)) {
        options.onMessage(value);
      }
    };
    connection.on(clientMethodName, handler);
    try {
      await connection.start();
      if (disposed) {
        connection.off(clientMethodName, handler);
        await connection.stop();
        return;
      }

      activeConnection = connection;
      activeHandler = handler;
      activeKey = desiredKey;
    } catch {
      connection.off(clientMethodName, handler);
      try {
        await connection.stop();
      } catch {
        // 初始连接可能尚未进入可停止状态；保持 HTTP 主流程可用即可。
      }
    }
  }

  async function stopActiveConnection(): Promise<void> {
    const connection = activeConnection;
    const handler = activeHandler;
    activeConnection = undefined;
    activeHandler = undefined;
    activeKey = undefined;
    if (connection === undefined) {
      return;
    }

    if (handler !== undefined) {
      connection.off(clientMethodName, handler);
    }
    try {
      await connection.stop();
    } catch {
      // 断开失败不应阻止本地会话清理或新上下文连接。
    }
  }

  return {
    whenSettled: () => transition,
    async dispose(): Promise<void> {
      if (disposed) {
        await transition;
        return;
      }

      disposed = true;
      unsubscribe();
      transition = transition
        .then(() => synchronize(undefined))
        .catch(() => undefined);
      await transition;
    }
  };
}

function readConnectionKey(snapshot: IdentitySessionSnapshot): string | undefined {
  if (snapshot.state !== 'authenticated' || snapshot.currentUser === undefined) {
    return undefined;
  }

  return `${snapshot.currentUser.sessionId}:${snapshot.currentUser.tenantId ?? 'host'}`;
}

function createSignalRConnection(
  options: NotificationsHubConnectionOptions
): NotificationsHubConnection {
  return new HubConnectionBuilder()
    .withUrl(options.hubPath, {
      accessTokenFactory: async () => options.accessTokenFactory() ?? ''
    })
    .withAutomaticReconnect([...options.reconnectDelays])
    .build();
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}
