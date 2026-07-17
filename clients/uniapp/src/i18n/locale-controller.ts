import {
  isCanonicalLocale,
  toCanonicalLocale,
  toUniLocale,
  type CanonicalLocale,
  type UniLocale
} from './locale-adapter';

/** 账号端口返回的规范语言偏好与乐观并发版本。 */
export interface AccountLocaleSnapshot {
  preferredLocale: CanonicalLocale;
  profileVersion: number;
}

/** 控制器在任意时刻对调用方公开的不可变语言状态副本。 */
export interface LocaleSnapshot extends AccountLocaleSnapshot {
  authenticated: boolean;
  saving: boolean;
}

/** 平台语言运行时的最小适配边界，避免页面直接处理别名。 */
export interface LocaleRuntime {
  getLocale(): unknown;
  setLocale(locale: UniLocale): void;
  onLocaleChange(listener: (locale: unknown) => void): () => void;
}

/** 本地规范语言选择的持久化边界。 */
export interface LocaleStorage {
  get(): unknown;
  set(locale: CanonicalLocale): void;
}

/** 构造语言状态机所需的可替换协作方。 */
export interface LocaleControllerDependencies {
  runtime: LocaleRuntime;
  storage: LocaleStorage;
  /** 订阅者或补偿失败时的错误报告端口，报告失败不会改变原始状态转换结果。 */
  onListenerError?: (error: unknown) => void;
}

/** 管理匿名与认证语言状态转换的可释放控制器。 */
export interface LocaleController {
  initialize(): LocaleSnapshot;
  subscribe(listener: (snapshot: LocaleSnapshot) => void): () => void;
  setAnonymousLocale(locale: CanonicalLocale): LocaleSnapshot;
  hydrateAccount(snapshot: AccountLocaleSnapshot): LocaleSnapshot;
  saveAuthenticatedLocale(
    locale: CanonicalLocale,
    persist: (request: AccountLocaleSnapshot) => Promise<AccountLocaleSnapshot>
  ): Promise<LocaleSnapshot>;
  /** 停止平台监听并释放订阅者；释放后所有新操作都会被拒绝。 */
  dispose(): void;
}

function isAccountLocaleSnapshot(value: unknown): value is AccountLocaleSnapshot {
  if (typeof value !== 'object' || value === null) {
    return false;
  }

  const candidate = value as Record<string, unknown>;
  return isCanonicalLocale(candidate.preferredLocale)
    && Number.isInteger(candidate.profileVersion)
    && typeof candidate.profileVersion === 'number'
    && candidate.profileVersion > 0;
}

function copySnapshot(snapshot: LocaleSnapshot): LocaleSnapshot {
  return { ...snapshot };
}

/** 创建独立的语言状态机，供应用生命周期或测试按需重建。 */
export function createLocaleController(
  dependencies: LocaleControllerDependencies
): LocaleController {
  const listeners = new Set<(snapshot: LocaleSnapshot) => void>();
  let initialized = false;
  let disposed = false;
  let stopRuntimeListener: (() => void) | undefined;
  let snapshot: LocaleSnapshot = {
    preferredLocale: 'zh-CN',
    profileVersion: 0,
    authenticated: false,
    saving: false
  };

  const reportError = (error: unknown): void => {
    if (dependencies.onListenerError) {
      try {
        dependencies.onListenerError(error);
        return;
      } catch (reportingError) {
        console.error(reportingError);
      }
    }

    console.error(error);
  };

  const compensate = (operation: () => void): void => {
    try {
      operation();
    } catch (error) {
      reportError(error);
    }
  };

  const notify = (): void => {
    for (const listener of listeners) {
      try {
        listener(copySnapshot(snapshot));
      } catch (error) {
        reportError(error);
      }
    }
  };

  const assertActive = (): void => {
    if (disposed) {
      throw new Error('Locale controller has been disposed.');
    }
  };

  const setRuntimeLocale = (locale: CanonicalLocale): void => {
    dependencies.runtime.setLocale(toUniLocale(locale));
  };

  const persistAndApplyRuntime = (
    previousLocale: CanonicalLocale,
    nextLocale: CanonicalLocale
  ): void => {
    try {
      dependencies.storage.set(nextLocale);
      setRuntimeLocale(nextLocale);
    } catch (error) {
      // 外部协作方可能在修改状态后抛出，因此两个边界都回滚到已提交快照的语言。
      compensate(() => setRuntimeLocale(previousLocale));
      compensate(() => dependencies.storage.set(previousLocale));
      throw error;
    }
  };

  const initialize = (): LocaleSnapshot => {
    assertActive();
    if (initialized) {
      return copySnapshot(snapshot);
    }

    const previousSnapshot = copySnapshot(snapshot);
    let previousRuntimeLocale: CanonicalLocale | undefined;
    let runtimeChanged = false;
    try {
      const storedLocale = dependencies.storage.get();
      previousRuntimeLocale = toCanonicalLocale(dependencies.runtime.getLocale());
      const preferredLocale = isCanonicalLocale(storedLocale)
        ? storedLocale
        : previousRuntimeLocale;
      setRuntimeLocale(preferredLocale);
      runtimeChanged = true;
      const stopListener = dependencies.runtime.onLocaleChange(platformLocale => {
        if (snapshot.authenticated) {
          return;
        }

        const nextLocale = toCanonicalLocale(platformLocale);
        const previousLocale = snapshot.preferredLocale;
        try {
          dependencies.storage.set(nextLocale);
          snapshot = { ...snapshot, preferredLocale: nextLocale };
          notify();
        } catch (error) {
          compensate(() => dependencies.storage.set(previousLocale));
          compensate(() => setRuntimeLocale(previousLocale));
          reportError(error);
        }
      });

      snapshot = {
        preferredLocale,
        profileVersion: 0,
        authenticated: false,
        saving: false
      };
      stopRuntimeListener = stopListener;
      initialized = true;
      return copySnapshot(snapshot);
    } catch (error) {
      if (runtimeChanged && previousRuntimeLocale) {
        const runtimeLocaleToRestore = previousRuntimeLocale;
        compensate(() => setRuntimeLocale(runtimeLocaleToRestore));
      }
      snapshot = previousSnapshot;
      initialized = false;
      throw error;
    }
  };

  const ensureInitialized = (): void => {
    initialize();
  };

  return {
    initialize,
    subscribe(listener) {
      assertActive();
      listeners.add(listener);
      return () => listeners.delete(listener);
    },
    setAnonymousLocale(locale) {
      assertActive();
      ensureInitialized();
      if (snapshot.authenticated) {
        throw new Error('Cannot change an authenticated locale anonymously.');
      }
      if (!isCanonicalLocale(locale)) {
        throw new TypeError('Anonymous locale must be canonical.');
      }

      const previousSnapshot = copySnapshot(snapshot);
      persistAndApplyRuntime(previousSnapshot.preferredLocale, locale);
      snapshot = { ...previousSnapshot, preferredLocale: locale };
      notify();
      return copySnapshot(snapshot);
    },
    hydrateAccount(accountSnapshot) {
      assertActive();
      ensureInitialized();
      if (snapshot.saving) {
        throw new Error('Cannot hydrate an account while an authenticated locale save is in progress.');
      }
      if (!isAccountLocaleSnapshot(accountSnapshot)) {
        throw new TypeError('Account locale snapshot must be complete and supported.');
      }

      const previousSnapshot = copySnapshot(snapshot);
      persistAndApplyRuntime(previousSnapshot.preferredLocale, accountSnapshot.preferredLocale);
      snapshot = { ...accountSnapshot, authenticated: true, saving: false };
      notify();
      return copySnapshot(snapshot);
    },
    async saveAuthenticatedLocale(locale, persist) {
      assertActive();
      ensureInitialized();
      if (!snapshot.authenticated) {
        throw new Error('Cannot save a locale without authentication.');
      }
      if (snapshot.saving) {
        throw new Error('Authenticated locale save is already in progress.');
      }
      if (!isCanonicalLocale(locale)) {
        throw new TypeError('Authenticated locale must be canonical.');
      }

      const previousSnapshot = copySnapshot(snapshot);
      const request: AccountLocaleSnapshot = {
        preferredLocale: locale,
        profileVersion: previousSnapshot.profileVersion
      };
      snapshot = { ...previousSnapshot, saving: true };
      notify();

      try {
        const response = await persist(request);
        if (
          !isAccountLocaleSnapshot(response)
          || response.preferredLocale !== request.preferredLocale
          || response.profileVersion <= request.profileVersion
        ) {
          throw new TypeError('Authenticated locale save returned an invalid snapshot.');
        }

        persistAndApplyRuntime(previousSnapshot.preferredLocale, response.preferredLocale);
        snapshot = { ...response, authenticated: true, saving: false };
        notify();
        return copySnapshot(snapshot);
      } catch (error) {
        snapshot = previousSnapshot;
        notify();
        throw error;
      }
    },
    dispose() {
      if (disposed) {
        return;
      }

      disposed = true;
      const stopListener = stopRuntimeListener;
      stopRuntimeListener = undefined;
      listeners.clear();
      if (stopListener) {
        compensate(stopListener);
      }
    }
  };
}
