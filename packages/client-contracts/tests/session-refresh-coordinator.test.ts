import { afterEach, describe, expect, it, vi } from 'vitest';
import {
  createSessionRefreshCoordinator,
  type SessionRefreshCoordinatorMessage
} from '../src/session-refresh-coordinator';

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
});
