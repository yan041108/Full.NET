import {
  isCanonicalLocale,
  toCanonicalLocale,
  toUniLocale,
  type CanonicalLocale,
  type UniLocale
} from './locale-adapter';

export interface AccountLocaleSnapshot {
  preferredLocale: CanonicalLocale;
  profileVersion: number;
}

export interface LocaleSnapshot extends AccountLocaleSnapshot {
  authenticated: boolean;
  saving: boolean;
}

export interface LocaleRuntime {
  getLocale(): unknown;
  setLocale(locale: UniLocale): void;
  onLocaleChange(listener: (locale: unknown) => void): () => void;
}

export interface LocaleStorage {
  get(): unknown;
  set(locale: CanonicalLocale): void;
}

export interface LocaleControllerDependencies {
  runtime: LocaleRuntime;
  storage: LocaleStorage;
}

export interface LocaleController {
  initialize(): LocaleSnapshot;
  subscribe(listener: (snapshot: LocaleSnapshot) => void): () => void;
  setAnonymousLocale(locale: CanonicalLocale): LocaleSnapshot;
  hydrateAccount(snapshot: AccountLocaleSnapshot): LocaleSnapshot;
  saveAuthenticatedLocale(
    locale: CanonicalLocale,
    persist: (request: AccountLocaleSnapshot) => Promise<AccountLocaleSnapshot>
  ): Promise<LocaleSnapshot>;
}

function isAccountLocaleSnapshot(value: unknown): value is AccountLocaleSnapshot {
  return typeof value === 'object'
    && value !== null
    && isCanonicalLocale((value as Record<string, unknown>).preferredLocale)
    && Number.isInteger((value as Record<string, unknown>).profileVersion)
    && (value as Record<string, unknown>).profileVersion as number > 0;
}

function copySnapshot(snapshot: LocaleSnapshot): LocaleSnapshot {
  return { ...snapshot };
}

export function createLocaleController(
  dependencies: LocaleControllerDependencies
): LocaleController {
  const listeners = new Set<(snapshot: LocaleSnapshot) => void>();
  let initialized = false;
  let stopRuntimeListener: (() => void) | undefined;
  let snapshot: LocaleSnapshot = {
    preferredLocale: 'zh-CN',
    profileVersion: 0,
    authenticated: false,
    saving: false
  };

  const notify = (): void => {
    for (const listener of listeners) {
      listener(copySnapshot(snapshot));
    }
  };

  const applyRuntimeLocale = (locale: CanonicalLocale): void => {
    dependencies.runtime.setLocale(toUniLocale(locale));
  };

  const initialize = (): LocaleSnapshot => {
    if (initialized) {
      return copySnapshot(snapshot);
    }

    initialized = true;
    const storedLocale = dependencies.storage.get();
    snapshot = {
      preferredLocale: isCanonicalLocale(storedLocale)
        ? storedLocale
        : toCanonicalLocale(dependencies.runtime.getLocale()),
      profileVersion: 0,
      authenticated: false,
      saving: false
    };
    applyRuntimeLocale(snapshot.preferredLocale);
    stopRuntimeListener = dependencies.runtime.onLocaleChange(platformLocale => {
      if (snapshot.authenticated) {
        return;
      }

      const preferredLocale = toCanonicalLocale(platformLocale);
      snapshot = { ...snapshot, preferredLocale };
      dependencies.storage.set(preferredLocale);
      notify();
    });
    return copySnapshot(snapshot);
  };

  const ensureInitialized = (): void => {
    initialize();
  };

  return {
    initialize,
    subscribe(listener) {
      listeners.add(listener);
      return () => listeners.delete(listener);
    },
    setAnonymousLocale(locale) {
      ensureInitialized();
      if (snapshot.authenticated) {
        throw new Error('Cannot change an authenticated locale anonymously.');
      }
      if (!isCanonicalLocale(locale)) {
        throw new TypeError('Anonymous locale must be canonical.');
      }

      snapshot = { ...snapshot, preferredLocale: locale };
      dependencies.storage.set(locale);
      applyRuntimeLocale(locale);
      notify();
      return copySnapshot(snapshot);
    },
    hydrateAccount(accountSnapshot) {
      ensureInitialized();
      if (!isAccountLocaleSnapshot(accountSnapshot)) {
        throw new TypeError('Account locale snapshot must be complete and supported.');
      }

      snapshot = { ...accountSnapshot, authenticated: true, saving: false };
      dependencies.storage.set(accountSnapshot.preferredLocale);
      applyRuntimeLocale(accountSnapshot.preferredLocale);
      notify();
      return copySnapshot(snapshot);
    },
    async saveAuthenticatedLocale(locale, persist) {
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

        snapshot = { ...response, authenticated: true, saving: false };
        dependencies.storage.set(response.preferredLocale);
        applyRuntimeLocale(response.preferredLocale);
        notify();
        return copySnapshot(snapshot);
      } catch (error) {
        snapshot = previousSnapshot;
        notify();
        throw error;
      }
    }
  };
}
