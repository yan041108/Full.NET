import { afterEach, describe, expect, it, vi } from 'vitest';
import {
  createSessionRefreshCoordinator,
  type SessionRefreshCoordinatorMessage
} from '../src/session-refresh-coordinator';

const sleep = (ms: number) => new Promise<void>(resolve => {
  setTimeout(resolve, ms);
});

class MockBroadcastChannel {
  static channels = new Map<string, Set<MockBroadcastChannel>>();

  readonly name: string;
  onmessage: ((event: MessageEvent) => void) | null = null;

  constructor(name: string) {
    this.name = name;
    if (!MockBroadcastChannel.channels.has(name)) {
      MockBroadcastChannel.channels.set(name, new Set());
    }

    MockBroadcastChannel.channels.get(name)!.add(this);
  }

  postMessage(data: unknown): void {
    const peers = MockBroadcastChannel.channels.get(this.name) ?? new Set();
    peers.forEach(peer => {
      if (peer === this) {
        return;
      }

      peer.onmessage?.({ data } as MessageEvent);
    });
  }

  close(): void {
    MockBroadcastChannel.channels.get(this.name)?.delete(this);
  }
}

afterEach(() => {
  MockBroadcastChannel.channels.clear();
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe('session refresh coordinator', () => {
  it('Web Locks 下串行执行刷新并广播完成事件', async () => {
    const request = vi.fn(async () => {
      await Promise.resolve();
      return true;
    });
    const lockRequest = vi.fn(
      async (_name: string, callback: () => Promise<boolean>) => callback()
    );
    vi.stubGlobal('navigator', { locks: { request: lockRequest } });
    vi.stubGlobal('BroadcastChannel', MockBroadcastChannel);

    const leader = createSessionRefreshCoordinator({ tabId: 'leader' });
    const followerMessages: SessionRefreshCoordinatorMessage[] = [];
    const follower = createSessionRefreshCoordinator({ tabId: 'follower' });
    follower.subscribe(message => followerMessages.push(message));

    await leader.runExclusive(request);

    expect(request).toHaveBeenCalledTimes(1);
    expect(lockRequest).toHaveBeenCalledWith(
      'fullnet.session.refresh',
      expect.any(Function)
    );
    expect(followerMessages).toEqual([
      { type: 'refresh-complete', success: true, sourceId: 'leader' }
    ]);
  });

  it('其他标签页收到退出广播后由订阅方处理', () => {
    vi.stubGlobal('BroadcastChannel', MockBroadcastChannel);
    const leader = createSessionRefreshCoordinator({ tabId: 'leader' });
    const follower = createSessionRefreshCoordinator({ tabId: 'follower' });
    const cleared: SessionRefreshCoordinatorMessage[] = [];
    follower.subscribe(message => cleared.push(message));

    leader.notifySessionCleared();

    expect(cleared).toEqual([
      { type: 'session-cleared', sourceId: 'leader' }
    ]);
  });

  it('无 Web Locks 时通过跨 Tab 共享存储互斥执行', async () => {
    vi.stubGlobal('navigator', {});
    vi.stubGlobal('BroadcastChannel', MockBroadcastChannel);
    const storage = new Map<string, string>();
    vi.stubGlobal('localStorage', {
      getItem: (key: string) => storage.get(key) ?? null,
      setItem: (key: string, value: string) => {
        storage.set(key, value);
      },
      removeItem: (key: string) => {
        storage.delete(key);
      }
    });
    vi.stubGlobal('sessionStorage', {
      getItem: () => {
        throw new Error('sessionStorage is isolated per tab');
      }
    });

    let active = 0;
    let maxActive = 0;
    const leader = createSessionRefreshCoordinator({ tabId: 'leader' });
    const follower = createSessionRefreshCoordinator({ tabId: 'follower' });
    await Promise.all([
      leader.runExclusive(async () => {
        active += 1;
        maxActive = Math.max(maxActive, active);
        await sleep(30);
        active -= 1;
        return true;
      }),
      follower.runExclusive(async () => {
        active += 1;
        maxActive = Math.max(maxActive, active);
        await sleep(30);
        active -= 1;
        return true;
      })
    ]);

    expect(maxActive).toBe(1);
  });

  it('共享存储被浏览器策略拒绝时降级执行刷新', async () => {
    vi.stubGlobal('navigator', {});
    vi.stubGlobal('BroadcastChannel', MockBroadcastChannel);
    vi.stubGlobal('localStorage', {
      getItem: () => {
        throw new DOMException('Storage access denied', 'SecurityError');
      }
    });
    const operation = vi.fn().mockResolvedValue(true);
    const coordinator = createSessionRefreshCoordinator({ tabId: 'denied-tab' });

    await expect(coordinator.runExclusive(operation)).resolves.toBe(true);

    expect(operation).toHaveBeenCalledOnce();
  });
});
