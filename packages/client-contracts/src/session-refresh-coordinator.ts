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
const storageLockKey = 'fullnet.session.refresh.lock';
const storageLockTtlMs = 30_000;

function sleep(ms: number): Promise<void> {
  return new Promise(resolve => {
    setTimeout(resolve, ms);
  });
}

interface StorageLockRecord {
  owner: string;
  expiresAt: number;
}

function isStorageLockRecord(value: unknown): value is StorageLockRecord {
  if (typeof value !== 'object' || value === null) {
    return false;
  }

  const record = value as Partial<StorageLockRecord>;
  return typeof record.owner === 'string'
    && record.owner.length > 0
    && typeof record.expiresAt === 'number'
    && Number.isFinite(record.expiresAt);
}

function readSharedStorage(): Storage | undefined {
  try {
    return typeof localStorage === 'undefined' ? undefined : localStorage;
  } catch {
    return undefined;
  }
}

function readStorageLock(): StorageLockRecord | undefined {
  const storage = readSharedStorage();
  if (storage === undefined) {
    return undefined;
  }

  try {
    const raw = storage.getItem(storageLockKey);
    if (!raw) {
      return undefined;
    }

    const parsed = JSON.parse(raw) as unknown;
    if (isStorageLockRecord(parsed)) {
      return parsed;
    }

    storage.removeItem(storageLockKey);
    return undefined;
  } catch {
    try {
      storage.removeItem(storageLockKey);
    } catch {
      // 浏览器拒绝存储访问等同于回退能力不可用，不得阻断会话恢复。
    }

    return undefined;
  }
}

function writeStorageLock(owner: string): boolean {
  const storage = readSharedStorage();
  if (storage === undefined) {
    return false;
  }

  try {
    storage.setItem(storageLockKey, JSON.stringify({
      owner,
      expiresAt: Date.now() + storageLockTtlMs
    } satisfies StorageLockRecord));
    return true;
  } catch {
    return false;
  }
}

function clearStorageLock(owner: string): void {
  const storage = readSharedStorage();
  if (storage === undefined) {
    return;
  }

  const current = readStorageLock();
  if (current?.owner === owner) {
    try {
      storage.removeItem(storageLockKey);
    } catch {
      // 租约自带过期时间，清理被拒绝时由后续调用安全接管。
    }
  }
}

async function withStorageLock<T>(
  owner: string,
  operation: () => Promise<T>
): Promise<T> {
  const deadline = Date.now() + storageLockTtlMs;
  while (Date.now() < deadline) {
    const current = readStorageLock();
    if (!current || current.expiresAt <= Date.now()) {
      if (!writeStorageLock(owner)) {
        return operation();
      }

      if (readStorageLock()?.owner === owner) {
        try {
          return await operation();
        } finally {
          clearStorageLock(owner);
        }
      }
    }

    await sleep(25);
  }

  throw new Error('session refresh storage lock timeout');
}

function createTabId(): string {
  if (typeof crypto !== 'undefined' && crypto.randomUUID !== undefined) {
    return crypto.randomUUID();
  }

  return `tab-${Date.now()}-${Math.random().toString(16).slice(2)}`;
}

/**
 * 浏览器跨 Tab 会话刷新协调器：优先使用 Web Locks；不支持时用 localStorage
 * 共享短租约，BroadcastChannel 只负责传播退出与完成信号。
 */
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

    if (readSharedStorage() !== undefined) {
      return withStorageLock(tabId, execute);
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
