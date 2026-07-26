import { describe, expect, it, vi } from 'vitest';
import {
  NOTIFICATIONS_REALTIME_CODES,
  createNotificationsRealtimeController,
  isRealtimeMessage,
  type IdentitySessionSnapshot,
  type NotificationsHubConnection
} from '../src/index';

describe('Notifications 实时客户端', () => {
  it('只接受稳定机器码和结构化数据', () => {
    expect(isRealtimeMessage({
      code: NOTIFICATIONS_REALTIME_CODES.inboxUnreadCountChanged,
      data: { unreadCount: 2 }
    })).toBe(true);
    expect(isRealtimeMessage({ code: 'notifications.unknown' })).toBe(false);
    expect(isRealtimeMessage({ code: 42 })).toBe(false);
    expect(isRealtimeMessage({
      code: NOTIFICATIONS_REALTIME_CODES.inboxMessageReceived,
      data: []
    })).toBe(false);
  });

  it('认证后连接，切换上下文时重连，匿名后停止', async () => {
    const session = createSession();
    const first = createConnection();
    const second = createConnection();
    const connections = [first, second];
    const onMessage = vi.fn();
    const controller = createNotificationsRealtimeController({
      session,
      onMessage,
      connectionFactory: vi.fn(options => {
        const connection = connections.shift()!;
        connection.configure(options.accessTokenFactory);
        return connection;
      })
    });

    session.publish(authenticatedSnapshot(null));
    await controller.whenSettled();
    expect(first.start).toHaveBeenCalledOnce();
    expect(first.readAccessToken()).toBe('host-token');

    first.receive({
      code: NOTIFICATIONS_REALTIME_CODES.inboxUnreadCountChanged,
      data: { unreadCount: 3 }
    });
    first.receive({ code: 'notifications.unknown' });
    expect(onMessage).toHaveBeenCalledOnce();

    session.token = 'tenant-token';
    session.publish(authenticatedSnapshot('tenant-id'));
    await controller.whenSettled();
    expect(first.stop).toHaveBeenCalledOnce();
    expect(second.start).toHaveBeenCalledOnce();
    expect(second.readAccessToken()).toBe('tenant-token');

    session.publish(anonymousSnapshot());
    await controller.whenSettled();
    expect(second.stop).toHaveBeenCalledOnce();

    await controller.dispose();
    expect(session.unsubscribe).toHaveBeenCalledOnce();
  });

  it('连接失败不向身份会话传播未处理异常', async () => {
    const session = createSession();
    const connection = createConnection();
    connection.start.mockRejectedValueOnce(new Error('offline'));
    const controller = createNotificationsRealtimeController({
      session,
      onMessage: vi.fn(),
      connectionFactory: options => {
        connection.configure(options.accessTokenFactory);
        return connection;
      }
    });

    session.publish(authenticatedSnapshot(null));

    await expect(controller.whenSettled()).resolves.toBeUndefined();
    await controller.dispose();
  });
});

function createSession() {
  let listener: ((snapshot: IdentitySessionSnapshot) => void) | undefined;
  const session = {
    token: 'host-token',
    unsubscribe: vi.fn(),
    snapshot: () => anonymousSnapshot(),
    readAccessToken: () => session.token,
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

function createConnection() {
  let handler: ((message: unknown) => void) | undefined;
  let accessTokenFactory: (() => string | undefined) | undefined;
  const connection: NotificationsHubConnection & {
    start: ReturnType<typeof vi.fn>;
    stop: ReturnType<typeof vi.fn>;
    receive(message: unknown): void;
    readAccessToken(): string | undefined;
    configure(tokenFactory: () => string | undefined): void;
  } = {
    start: vi.fn().mockResolvedValue(undefined),
    stop: vi.fn().mockResolvedValue(undefined),
    on: vi.fn((_methodName, value) => {
      handler = value;
    }),
    off: vi.fn(() => {
      handler = undefined;
    }),
    receive(message) {
      handler?.(message);
    },
    readAccessToken() {
      return accessTokenFactory?.();
    }
  };
  return Object.assign(connection, {
    configure(tokenFactory: () => string | undefined): void {
      accessTokenFactory = tokenFactory;
    }
  });
}

function authenticatedSnapshot(tenantId: string | null): IdentitySessionSnapshot {
  return {
    state: 'authenticated',
    currentUser: {
      id: 'user-id',
      username: 'admin',
      displayName: '系统管理员',
      tenantId,
      actorScope: 'host',
      scope: 'host',
      isSuperAdministrator: true,
      permissions: [],
      sessionId: 'session-id',
      preferredLocale: 'zh-CN',
      profileVersion: 1
    },
    navigation: [],
    availableTenants: [],
    switching: false,
    savingLocale: false,
    currentContextName: tenantId ?? 'Full.NET Host'
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
