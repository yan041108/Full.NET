import { afterEach, describe, expect, it, vi } from 'vitest';
import {
  NOTIFICATIONS_REALTIME_CODES,
  createNotificationsRealtimeController,
  isRealtimeMessage,
  type IdentitySessionSnapshot,
  type NotificationsHubConnection,
  type NotificationsRealtimeOptions
} from '../src/index';

describe('Notifications 实时客户端', () => {
  afterEach(() => {
    vi.useRealTimers();
  });

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

  it('首次连接失败后按退避重新创建连接并恢复', async () => {
    vi.useFakeTimers();
    const session = createSession();
    const first = createConnection();
    const second = createConnection();
    first.start.mockRejectedValueOnce(new Error('offline'));
    const connections = [first, second];
    const connectionFactory = vi.fn(options => {
      const connection = connections.shift()!;
      connection.configure(options.accessTokenFactory);
      return connection;
    });
    const controller = createNotificationsRealtimeController({
      session,
      onMessage: vi.fn(),
      connectionFactory
    });

    session.publish(authenticatedSnapshot(null));
    await controller.whenSettled();
    expect(first.start).toHaveBeenCalledOnce();
    expect(connectionFactory).toHaveBeenCalledOnce();

    await vi.advanceTimersByTimeAsync(0);
    await controller.whenSettled();

    expect(second.start).toHaveBeenCalledOnce();
    expect(connectionFactory).toHaveBeenCalledTimes(2);
    await controller.dispose();
  });

  it('自动重连成功后通知上层修复可能遗漏的业务状态', async () => {
    const session = createSession();
    const connection = createConnection();
    const onReconnected = vi.fn().mockResolvedValue(undefined);
    const options: NotificationsRealtimeOptions & {
      onReconnected: () => Promise<void>;
    } = {
      session,
      onMessage: vi.fn(),
      onReconnected,
      connectionFactory: factoryOptions => {
        connection.configure(factoryOptions.accessTokenFactory);
        return connection;
      }
    };
    const controller = createNotificationsRealtimeController(options);

    session.publish(authenticatedSnapshot(null));
    await controller.whenSettled();
    await connection.reconnect();

    expect(onReconnected).toHaveBeenCalledOnce();
    await controller.dispose();
  });

  it('自动重连耗尽并关闭后重新创建连接，显式销毁不再重试', async () => {
    vi.useFakeTimers();
    const session = createSession();
    const first = createConnection();
    const second = createConnection();
    const connections = [first, second];
    const connectionFactory = vi.fn(options => {
      const connection = connections.shift()!;
      connection.configure(options.accessTokenFactory);
      return connection;
    });
    const controller = createNotificationsRealtimeController({
      session,
      onMessage: vi.fn(),
      connectionFactory
    });

    session.publish(authenticatedSnapshot(null));
    await controller.whenSettled();
    await first.close();
    await vi.advanceTimersByTimeAsync(0);
    await controller.whenSettled();

    expect(second.start).toHaveBeenCalledOnce();
    expect(connectionFactory).toHaveBeenCalledTimes(2);

    await controller.dispose();
    await second.close();
    await vi.advanceTimersByTimeAsync(60_000);
    expect(connectionFactory).toHaveBeenCalledTimes(2);
  });

  it('切换上下文会取消旧连接重试并立即连接新上下文', async () => {
    vi.useFakeTimers();
    const session = createSession();
    const failed = createConnection();
    const tenant = createConnection();
    failed.start.mockRejectedValueOnce(new Error('offline'));
    const connections = [failed, tenant];
    const connectionFactory = vi.fn(options => {
      const connection = connections.shift()!;
      connection.configure(options.accessTokenFactory);
      return connection;
    });
    const controller = createNotificationsRealtimeController({
      session,
      onMessage: vi.fn(),
      connectionFactory
    });

    session.publish(authenticatedSnapshot(null));
    await controller.whenSettled();
    expect(vi.getTimerCount()).toBe(1);
    session.token = 'tenant-token';
    session.publish(authenticatedSnapshot('tenant-id'));
    await controller.whenSettled();

    expect(tenant.start).toHaveBeenCalledOnce();
    expect(tenant.readAccessToken()).toBe('tenant-token');
    await vi.advanceTimersByTimeAsync(60_000);
    expect(connectionFactory).toHaveBeenCalledTimes(2);
    await controller.dispose();
  });

  it('匿名化或销毁后不再执行待处理的首次连接重试', async () => {
    vi.useFakeTimers();
    const session = createSession();
    const connection = createConnection();
    connection.start.mockRejectedValueOnce(new Error('offline'));
    const connectionFactory = vi.fn(() => connection);
    const controller = createNotificationsRealtimeController({
      session,
      onMessage: vi.fn(),
      connectionFactory
    });

    session.publish(authenticatedSnapshot(null));
    await controller.whenSettled();
    expect(vi.getTimerCount()).toBe(1);
    session.publish(anonymousSnapshot());
    await controller.whenSettled();
    await vi.advanceTimersByTimeAsync(60_000);
    expect(connectionFactory).toHaveBeenCalledOnce();

    await controller.dispose();
    await vi.advanceTimersByTimeAsync(60_000);
    expect(connectionFactory).toHaveBeenCalledOnce();
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
  let reconnectedHandler: (() => void | Promise<void>) | undefined;
  let closedHandler: (() => void | Promise<void>) | undefined;
  const connection: NotificationsHubConnection & {
    start: ReturnType<typeof vi.fn>;
    stop: ReturnType<typeof vi.fn>;
    receive(message: unknown): void;
    reconnect(): Promise<void>;
    close(): Promise<void>;
    readAccessToken(): string | undefined;
    configure(tokenFactory: () => string | undefined): void;
    onreconnected: ReturnType<typeof vi.fn>;
    onclose: ReturnType<typeof vi.fn>;
  } = {
    start: vi.fn().mockResolvedValue(undefined),
    stop: vi.fn().mockResolvedValue(undefined),
    on: vi.fn((_methodName, value) => {
      handler = value;
    }),
    off: vi.fn(() => {
      handler = undefined;
    }),
    onreconnected: vi.fn(value => {
      reconnectedHandler = value;
    }),
    onclose: vi.fn(value => {
      closedHandler = value;
    }),
    receive(message) {
      handler?.(message);
    },
    async reconnect() {
      await reconnectedHandler?.();
    },
    async close() {
      await closedHandler?.();
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
