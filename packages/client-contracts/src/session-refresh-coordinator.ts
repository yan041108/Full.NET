export type SessionRefreshCoordinatorMessage =
  | { type: 'refresh-complete'; success: boolean; sourceId: string }
  | { type: 'session-cleared'; sourceId: string };

export interface SessionRefreshCoordinator {
  readonly tabId: string;
  runExclusive<T>(operation: () => Promise<T>): Promise<T>;
  notifySessionCleared(): void;
  subscribe(
    listener: (message: SessionRefreshCoordinatorMessage) => void
  ): () => void;
  dispose(): void;
}

export interface SessionRefreshCoordinatorOptions {
  channelName?: string;
  lockName?: string;
  tabId?: string;
}

const defaultChannelName = 'fullnet.session.refresh';
const defaultLockName = 'fullnet.session.refresh';

function createTabId(): string {
  if (typeof crypto !== 'undefined' && crypto.randomUUID !== undefined) {
    return crypto.randomUUID();
  }

  return `tab-${Date.now()}-${Math.random().toString(16).slice(2)}`;
}

/** 浏览器跨 Tab 会话刷新协调器：优先 Web Locks，BroadcastChannel 传播退出与完成信号。 */
export function createSessionRefreshCoordinator(
  options: SessionRefreshCoordinatorOptions = {}
): SessionRefreshCoordinator {
  const channelName = options.channelName ?? defaultChannelName;
  const lockName = options.lockName ?? defaultLockName;
  const tabId = options.tabId ?? createTabId();
  const listeners = new Set<
    (message: SessionRefreshCoordinatorMessage) => void
  >();
  let channel: BroadcastChannel | undefined;

  function ensureChannel(): BroadcastChannel | undefined {
    if (typeof BroadcastChannel === 'undefined') {
      return undefined;
    }

    if (channel === undefined) {
      channel = new BroadcastChannel(channelName);
      channel.onmessage = event => {
        const message = event.data as SessionRefreshCoordinatorMessage | undefined;
        if (message?.type === 'refresh-complete'
          || message?.type === 'session-cleared') {
          listeners.forEach(listener => listener(message));
        }
      };
    }

    return channel;
  }

  function supportsWebLocks(): boolean {
    return typeof navigator !== 'undefined'
      && navigator.locks?.request !== undefined;
  }

  function publish(
    message: SessionRefreshCoordinatorMessage
  ): void {
    ensureChannel()?.postMessage(message);
    listeners.forEach(listener => listener(message));
  }

  async function runExclusive<T>(operation: () => Promise<T>): Promise<T> {
    const execute = async () => {
      const result = await operation();
      publish({
        type: 'refresh-complete',
        success: result === true,
        sourceId: tabId
      });
      return result;
    };

    if (supportsWebLocks()) {
      return navigator.locks.request(lockName, execute);
    }

    return execute();
  }

  function notifySessionCleared(): void {
    publish({ type: 'session-cleared', sourceId: tabId });
  }

  function subscribe(
    listener: (message: SessionRefreshCoordinatorMessage) => void
  ): () => void {
    listeners.add(listener);
    return () => listeners.delete(listener);
  }

  function dispose(): void {
    listeners.clear();
    channel?.close();
    channel = undefined;
  }

  ensureChannel();

  return {
    tabId,
    runExclusive,
    notifySessionCleared,
    subscribe,
    dispose
  };
}
