import type { IdentitySessionSnapshot } from './identity-session.js';

export const NOTIFICATIONS_REALTIME_CODES = {
  probeSelf: 'realtime.probe.self',
  announcementPublished: 'notifications.announcement.published',
  inboxMessageReceived: 'notifications.inbox.message.received',
  inboxUnreadCountChanged: 'notifications.inbox.unread.changed',
  sessionRevoked: 'identity.session.revoked'
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
  onreconnected(handler: (connectionId?: string) => void): void;
  onclose(handler: (error?: Error) => void): void;
}

export interface NotificationsHubConnectionOptions {
  hubPath: string;
  accessTokenFactory: () => string | undefined;
  reconnectDelays: readonly number[];
  headers?: Record<string, string>;
}

export interface NotificationsRealtimeSession {
  snapshot(): IdentitySessionSnapshot;
  subscribe(listener: (snapshot: IdentitySessionSnapshot) => void): () => void;
  readAccessToken(): string | undefined;
  readCsrfToken?(): string | undefined;
}

export interface NotificationsRealtimeOptions {
  session: NotificationsRealtimeSession;
  onMessage: (message: RealtimeMessage) => void;
  onReconnected?: () => void | Promise<void>;
  hubPath?: string;
  connectionFactory?: (
    options: NotificationsHubConnectionOptions
  ) => NotificationsHubConnection | Promise<NotificationsHubConnection>;
}

export interface NotificationsRealtimeController {
  whenSettled(): Promise<void>;
  dispose(): Promise<void>;
}

const clientMethodName = 'ReceiveMessageAsync';
const reconnectDelays = [0, 2_000, 10_000, 30_000] as const;
const supportedCodes = new Set<string>(Object.values(NOTIFICATIONS_REALTIME_CODES));

/** 传输层返回已知协商状态，控制器可在不读取响应正文的情况下决定是否重试。 */
export class NotificationsHubHttpFailure extends Error {
  constructor(readonly status: number, readonly retryAfterSeconds?: number) {
    super(`Notifications Hub HTTP ${status}`);
  }
}

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
  let activeGeneration = 0;
  let activeConnection: NotificationsHubConnection | undefined;
  let activeHandler: ((message: unknown) => void) | undefined;
  let desiredKey: string | undefined;
  let generation = 0;
  let retryTimer: ReturnType<typeof setTimeout> | undefined;
  let retryAttempt = 0;
  let recovering = false;
  let disposed = false;
  let transition = Promise.resolve();

  const unsubscribe = options.session.subscribe(snapshot => {
    const nextKey = readConnectionKey(snapshot);
    if (desiredKey === nextKey) {
      return;
    }

    desiredKey = nextKey;
    const ticket = ++generation;
    cancelRetry();
    transition = transition
      .then(() => synchronize(ticket))
      .catch(() => undefined);
  });

  async function synchronize(ticket: number): Promise<void> {
    if (ticket !== generation || (activeConnection && activeGeneration === ticket)) return;

    await stopActiveConnection();
    if (disposed || ticket !== generation || desiredKey === undefined) return;

    let connection: NotificationsHubConnection;
    try {
      connection = await connectionFactory({
        hubPath: options.hubPath ?? '/hubs/notifications',
        accessTokenFactory: () => !disposed && ticket === generation ? options.session.readAccessToken() : undefined,
        headers: options.session.readCsrfToken?.() ? { 'X-CSRF-Token': options.session.readCsrfToken!()! } : undefined,
        reconnectDelays
      });
    } catch {
      // 延迟传输包加载失败沿用首次连接退避，不阻断 HTTP 会话，也不遗留未处理拒绝。
      scheduleRetry(ticket);
      return;
    }
    // 加载期间可能退出、撤销会话或切换租户，旧上下文不得越过外部连接边界。
    if (disposed || ticket !== generation) {
      return;
    }
    const handler = (value: unknown) => {
      if (!disposed && ticket === generation && isRealtimeMessage(value)) {
        options.onMessage(value);
      }
    };
    connection.on(clientMethodName, handler);
    connection.onreconnected(() => {
      if (!disposed && ticket === generation && activeConnection === connection) notifyReconnected(ticket);
    });
    connection.onclose(error => {
      if (activeConnection !== connection) {
        return;
      }

      activeConnection = undefined;
      activeHandler = undefined;
      connection.off(clientMethodName, handler);
      scheduleRetry(ticket, error, true);
    });
    try {
      await connection.start();
      if (disposed || ticket !== generation) {
        connection.off(clientMethodName, handler);
        await connection.stop();
        return;
      }

      activeConnection = connection;
      activeHandler = handler;
      activeGeneration = ticket;
      if (recovering || retryAttempt > 0) notifyReconnected(ticket);
      retryAttempt = 0;
      recovering = false;
    } catch (error) {
      connection.off(clientMethodName, handler);
      try {
        await connection.stop();
      } catch {
        // 初始连接可能尚未进入可停止状态；保持 HTTP 主流程可用即可。
      }
      scheduleRetry(ticket, error);
    }
  }

  function notifyReconnected(ticket: number): void {
    void Promise.resolve().then(() => {
      if (!disposed && ticket === generation) return options.onReconnected?.();
    }).catch(() => undefined);
  }

  function scheduleRetry(ticket: number, error?: unknown, afterClose = false): void {
    if (disposed || ticket !== generation || retryTimer !== undefined) {
      return;
    }

    // 同一认证上下文收到拒绝后停止协商，HTTP 会话仍由现有身份流程维护。
    const failure = error instanceof NotificationsHubHttpFailure ? error : undefined;
    if (failure?.status === 401 || failure?.status === 403) return;
    recovering ||= afterClose;
    const delayIndex = Math.min(retryAttempt, reconnectDelays.length - 1);
    const retryDelay = reconnectDelays[delayIndex];
    const delay = failure?.status === 429 ? Math.max(retryDelay, (failure.retryAfterSeconds ?? 30) * 1000) : retryDelay;
    retryAttempt += 1;
    retryTimer = setTimeout(() => {
      retryTimer = undefined;
      if (disposed || ticket !== generation) {
        return;
      }

      transition = transition
        .then(() => synchronize(ticket))
        .catch(() => undefined);
    }, delay);
  }

  function cancelRetry(): void {
    if (retryTimer !== undefined) {
      clearTimeout(retryTimer);
      retryTimer = undefined;
    }
    retryAttempt = 0;
    recovering = false;
  }

  async function stopActiveConnection(): Promise<void> {
    const connection = activeConnection;
    const handler = activeHandler;
    activeConnection = undefined;
    activeHandler = undefined;
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
      desiredKey = undefined;
      const ticket = ++generation;
      cancelRetry();
      transition = transition
        .then(() => synchronize(ticket))
        .catch(() => undefined);
      await transition;
    }
  };
}

function readConnectionKey(snapshot: IdentitySessionSnapshot): string | undefined {
  if (snapshot.state !== 'authenticated' || snapshot.switching || snapshot.currentUser === undefined) {
    return undefined;
  }

  if (snapshot.currentUser.passwordChangeRequired) {
    return undefined;
  }

  return `${snapshot.currentUser.sessionId}:${snapshot.currentUser.tenantId ?? 'host'}`;
}

async function createSignalRConnection(
  options: NotificationsHubConnectionOptions
): Promise<NotificationsHubConnection> {
  // 匿名首屏不加载实时传输；仅由已认证会话的连接状态机触发。
  const { createNotificationsSignalRConnection } = await import('./notifications-signalr.js');
  return createNotificationsSignalRConnection(options);
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}
