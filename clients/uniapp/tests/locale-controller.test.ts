import { describe, expect, it } from 'vitest';

import {
  createLocaleController,
  type LocaleRuntime,
  type LocaleStorage
} from '../src/i18n/locale-controller';

class MemoryStorage implements LocaleStorage {
  public readonly writes: string[] = [];

  public constructor(private value: unknown = undefined) {}

  public get(): unknown {
    return this.value;
  }

  public set(locale: 'zh-CN' | 'en-US'): void {
    this.value = locale;
    this.writes.push(locale);
  }
}

class FakeRuntime implements LocaleRuntime {
  public readonly setCalls: string[] = [];
  private readonly listeners = new Set<(locale: unknown) => void>();

  public constructor(private locale: unknown) {}

  public getLocale(): unknown {
    return this.locale;
  }

  public setLocale(locale: 'zh-Hans' | 'en'): void {
    this.locale = locale;
    this.setCalls.push(locale);
  }

  public onLocaleChange(listener: (locale: unknown) => void): () => void {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  }

  public emit(locale: unknown): void {
    this.locale = locale;
    for (const listener of this.listeners) {
      listener(locale);
    }
  }

  public get currentLocale(): unknown {
    return this.locale;
  }

  public get listenerCount(): number {
    return this.listeners.size;
  }
}

class MutatingFaultStorage extends MemoryStorage {
  public failNextSet = false;

  public override set(locale: 'zh-CN' | 'en-US'): void {
    super.set(locale);
    if (this.failNextSet) {
      this.failNextSet = false;
      throw new Error('storage set failed after mutation');
    }
  }

  public get currentLocale(): unknown {
    return this.get();
  }
}

class MutatingFaultRuntime extends FakeRuntime {
  public failNextSet = false;

  public override setLocale(locale: 'zh-Hans' | 'en'): void {
    super.setLocale(locale);
    if (this.failNextSet) {
      this.failNextSet = false;
      throw new Error('runtime set failed after mutation');
    }
  }
}

function createFaultSubject() {
  const storage = new MutatingFaultStorage('zh-CN');
  const runtime = new MutatingFaultRuntime('zh-Hans');
  const controller = createLocaleController({ runtime, storage });
  controller.initialize();

  return { controller, runtime, storage };
}

function createSubject(options: { stored?: unknown; device?: unknown } = {}) {
  const storage = new MemoryStorage(options.stored);
  const runtime = new FakeRuntime(options.device ?? 'zh-Hans');
  const controller = createLocaleController({ runtime, storage });

  return { controller, runtime, storage };
}

describe('locale controller', () => {
  it('prefers an explicit locally stored canonical locale over the device locale', () => {
    const { controller, runtime } = createSubject({ stored: 'en-US', device: 'zh-Hans' });

    expect(controller.initialize()).toEqual({
      preferredLocale: 'en-US',
      profileVersion: 0,
      authenticated: false,
      saving: false
    });
    expect(runtime.setCalls).toEqual(['en']);
  });

  it('uses the normalized device locale when local storage has no canonical choice', () => {
    const { controller } = createSubject({ stored: 'en', device: 'en_US' });

    expect(controller.initialize().preferredLocale).toBe('en-US');
  });

  it('persists an anonymous locale selection immediately', () => {
    const { controller, runtime, storage } = createSubject();
    controller.initialize();

    expect(controller.setAnonymousLocale('en-US')).toMatchObject({
      preferredLocale: 'en-US',
      authenticated: false
    });
    expect(storage.writes).toEqual(['en-US']);
    expect(runtime.setCalls.at(-1)).toBe('en');
  });

  it('normalizes and persists platform locale events only while anonymous', () => {
    const { controller, runtime, storage } = createSubject();
    controller.initialize();

    runtime.emit('en_US');

    expect(controller.initialize().preferredLocale).toBe('en-US');
    expect(storage.writes).toEqual(['en-US']);
  });

  it('accepts only a complete supported account snapshot during hydration', () => {
    const { controller } = createSubject();
    controller.initialize();

    expect(() => controller.hydrateAccount({ preferredLocale: 'en-US', profileVersion: 3 })).not.toThrow();
    expect(() =>
      controller.hydrateAccount({ preferredLocale: 'en' as 'en-US', profileVersion: 4 })
    ).toThrow(TypeError);
    expect(() =>
      controller.hydrateAccount({ preferredLocale: 'zh-CN', profileVersion: 0 })
    ).toThrow(TypeError);
    expect(controller.initialize()).toMatchObject({
      preferredLocale: 'en-US',
      profileVersion: 3,
      authenticated: true
    });
  });

  it('does not let a platform event replace the authenticated account locale', () => {
    const { controller, runtime, storage } = createSubject();
    controller.initialize();
    controller.hydrateAccount({ preferredLocale: 'en-US', profileVersion: 2 });

    runtime.emit('zh-Hans');

    expect(controller.initialize()).toMatchObject({
      preferredLocale: 'en-US',
      profileVersion: 2,
      authenticated: true
    });
    expect(storage.writes).toEqual(['en-US']);
  });

  it('saves an authenticated locale only when the response matches the request and advances its version', async () => {
    const { controller } = createSubject();
    controller.initialize();
    controller.hydrateAccount({ preferredLocale: 'zh-CN', profileVersion: 5 });
    let request: unknown;

    const saved = await controller.saveAuthenticatedLocale('en-US', async value => {
      request = value;
      return { preferredLocale: 'en-US', profileVersion: 6 };
    });

    expect(request).toEqual({ preferredLocale: 'en-US', profileVersion: 5 });
    expect(saved).toEqual({
      preferredLocale: 'en-US',
      profileVersion: 6,
      authenticated: true,
      saving: false
    });
  });

  it('rejects a concurrent authenticated save without starting a second persistence request', async () => {
    const { controller } = createSubject();
    controller.initialize();
    controller.hydrateAccount({ preferredLocale: 'zh-CN', profileVersion: 5 });
    let resolvePersist: ((value: { preferredLocale: 'en-US'; profileVersion: number }) => void) | undefined;
    const persist = () =>
      new Promise<{ preferredLocale: 'en-US'; profileVersion: number }>(resolve => {
        resolvePersist = resolve;
      });

    const first = controller.saveAuthenticatedLocale('en-US', persist);
    await expect(controller.saveAuthenticatedLocale('en-US', persist)).rejects.toThrow('already in progress');
    resolvePersist?.({ preferredLocale: 'en-US', profileVersion: 6 });
    await expect(first).resolves.toMatchObject({ profileVersion: 6, saving: false });
  });

  it.each([
    ['a conflicting locale', { preferredLocale: 'zh-CN', profileVersion: 6 }],
    ['a stale version', { preferredLocale: 'en-US', profileVersion: 5 }],
    ['a malformed response', { preferredLocale: 'en-US', profileVersion: 0 }]
  ])('restores both locale and version after %s', async (_scenario, response) => {
    const { controller } = createSubject();
    controller.initialize();
    controller.hydrateAccount({ preferredLocale: 'zh-CN', profileVersion: 5 });

    await expect(
      controller.saveAuthenticatedLocale('en-US', async () => response as never)
    ).rejects.toThrow();
    expect(controller.initialize()).toEqual({
      preferredLocale: 'zh-CN',
      profileVersion: 5,
      authenticated: true,
      saving: false
    });
  });

  it('restores both locale and version after a persistence error', async () => {
    const { controller } = createSubject();
    controller.initialize();
    controller.hydrateAccount({ preferredLocale: 'zh-CN', profileVersion: 5 });

    await expect(
      controller.saveAuthenticatedLocale('en-US', async () => {
        throw new Error('network unavailable');
      })
    ).rejects.toThrow('network unavailable');
    expect(controller.initialize()).toMatchObject({
      preferredLocale: 'zh-CN',
      profileVersion: 5,
      saving: false
    });
  });

  it('isolates listener failures, reports them, and completes an authenticated save', async () => {
    const errors: unknown[] = [];
    const storage = new MemoryStorage('zh-CN');
    const runtime = new FakeRuntime('zh-Hans');
    const controller = createLocaleController({
      runtime,
      storage,
      onListenerError: error => errors.push(error)
    });
    controller.initialize();
    controller.hydrateAccount({ preferredLocale: 'zh-CN', profileVersion: 5 });
    const received: number[] = [];
    controller.subscribe(() => {
      throw new Error('listener failed');
    });
    controller.subscribe(snapshot => received.push(snapshot.profileVersion));

    await expect(
      controller.saveAuthenticatedLocale('en-US', async () => ({
        preferredLocale: 'en-US',
        profileVersion: 6
      }))
    ).resolves.toMatchObject({ preferredLocale: 'en-US', profileVersion: 6, saving: false });

    expect(received).toEqual([5, 6]);
    expect(errors).toHaveLength(2);
    expect(errors.every(error => error instanceof Error && error.message === 'listener failed')).toBe(true);
  });

  it.each(['storage.get', 'runtime.getLocale', 'runtime.setLocale', 'runtime.onLocaleChange'] as const)(
    'allows initialize to retry after %s fails',
    step => {
      let failed = false;
      const storage: LocaleStorage = {
        get: () => {
          if (step === 'storage.get' && !failed) {
            failed = true;
            throw new Error('storage get failed');
          }
          return undefined;
        },
        set: () => undefined
      };
      const runtime: LocaleRuntime & { setCalls: number; subscribeCalls: number } = {
        setCalls: 0,
        subscribeCalls: 0,
        getLocale: () => {
          if (step === 'runtime.getLocale' && !failed) {
            failed = true;
            throw new Error('runtime get failed');
          }
          return 'en';
        },
        setLocale: () => {
          runtime.setCalls += 1;
          if (step === 'runtime.setLocale' && !failed) {
            failed = true;
            throw new Error('runtime set failed');
          }
        },
        onLocaleChange: () => {
          runtime.subscribeCalls += 1;
          if (step === 'runtime.onLocaleChange' && !failed) {
            failed = true;
            throw new Error('runtime subscribe failed');
          }
          return () => undefined;
        }
      };
      const controller = createLocaleController({ runtime, storage });

      expect(() => controller.initialize()).toThrow();
      expect(controller.initialize()).toMatchObject({ preferredLocale: 'en-US', saving: false });
      expect(runtime.subscribeCalls).toBe(step === 'runtime.onLocaleChange' ? 2 : 1);
      expect(runtime.setCalls).toBe(
        step === 'runtime.setLocale' || step === 'runtime.onLocaleChange' ? 3 : 1
      );
    }
  );

  it('compensates a runtime mutation that throws during initialization and permits a fresh retry', () => {
    const storage = new MemoryStorage('zh-CN');
    const runtime = new MutatingFaultRuntime('en');
    runtime.failNextSet = true;
    const controller = createLocaleController({ runtime, storage });

    expect(() => controller.initialize()).toThrow('runtime set failed after mutation');
    expect(runtime.currentLocale).toBe('en');
    expect(runtime.listenerCount).toBe(0);

    storage.set('en-US');
    expect(controller.initialize()).toEqual({
      preferredLocale: 'en-US',
      profileVersion: 0,
      authenticated: false,
      saving: false
    });
  });

  it('compensates storage mutation failures without changing an anonymous snapshot', () => {
    const { controller, runtime, storage } = createFaultSubject();
    storage.failNextSet = true;

    expect(() => controller.setAnonymousLocale('en-US')).toThrow('storage set failed after mutation');
    expect(controller.initialize()).toMatchObject({ preferredLocale: 'zh-CN', authenticated: false });
    expect(storage.currentLocale).toBe('zh-CN');
    expect(runtime.currentLocale).toBe('zh-Hans');
  });

  it('compensates runtime mutation failures without changing a hydrated account snapshot', () => {
    const { controller, runtime, storage } = createFaultSubject();
    runtime.failNextSet = true;

    expect(() =>
      controller.hydrateAccount({ preferredLocale: 'en-US', profileVersion: 3 })
    ).toThrow('runtime set failed after mutation');
    expect(controller.initialize()).toEqual({
      preferredLocale: 'zh-CN',
      profileVersion: 0,
      authenticated: false,
      saving: false
    });
    expect(storage.currentLocale).toBe('zh-CN');
    expect(runtime.currentLocale).toBe('zh-Hans');
  });

  it('compensates authenticated save collaborator failures and restores the prior snapshot', async () => {
    const { controller, runtime, storage } = createFaultSubject();
    controller.hydrateAccount({ preferredLocale: 'zh-CN', profileVersion: 5 });
    runtime.failNextSet = true;

    await expect(
      controller.saveAuthenticatedLocale('en-US', async () => ({
        preferredLocale: 'en-US',
        profileVersion: 6
      }))
    ).rejects.toThrow('runtime set failed after mutation');
    expect(controller.initialize()).toEqual({
      preferredLocale: 'zh-CN',
      profileVersion: 5,
      authenticated: true,
      saving: false
    });
    expect(storage.currentLocale).toBe('zh-CN');
    expect(runtime.currentLocale).toBe('zh-Hans');
  });

  it('rejects account hydration while an authenticated save is in flight', async () => {
    const { controller } = createSubject();
    controller.initialize();
    controller.hydrateAccount({ preferredLocale: 'zh-CN', profileVersion: 5 });
    let resolvePersist: ((value: { preferredLocale: 'en-US'; profileVersion: number }) => void) | undefined;
    const persist = () =>
      new Promise<{ preferredLocale: 'en-US'; profileVersion: number }>(resolve => {
        resolvePersist = resolve;
      });

    const first = controller.saveAuthenticatedLocale('en-US', persist);
    expect(() =>
      controller.hydrateAccount({ preferredLocale: 'zh-CN', profileVersion: 9 })
    ).toThrow('save is in progress');
    await expect(controller.saveAuthenticatedLocale('en-US', persist)).rejects.toThrow('already in progress');
    expect(controller.initialize()).toMatchObject({ preferredLocale: 'zh-CN', profileVersion: 5, saving: true });
    resolvePersist?.({ preferredLocale: 'en-US', profileVersion: 6 });
    await expect(first).resolves.toMatchObject({ preferredLocale: 'en-US', profileVersion: 6, saving: false });
  });

  it('disposes runtime subscriptions and rejects later controller operations', () => {
    const { controller, runtime } = createSubject();
    controller.initialize();
    const unsubscribe = controller.subscribe(() => undefined);

    controller.dispose();
    controller.dispose();
    unsubscribe();

    expect(runtime.listenerCount).toBe(0);
    expect(() => controller.initialize()).toThrow('disposed');
    expect(() => controller.subscribe(() => undefined)).toThrow('disposed');
    expect(() => controller.setAnonymousLocale('en-US')).toThrow('disposed');
  });

  it('invalidates a pending save when disposed before the persistence response arrives', async () => {
    const { controller, runtime, storage } = createSubject({ stored: 'zh-CN' });
    controller.initialize();
    controller.hydrateAccount({ preferredLocale: 'zh-CN', profileVersion: 5 });
    let resolvePersist: ((value: { preferredLocale: 'en-US'; profileVersion: number }) => void) | undefined;
    let persistCalls = 0;
    const persist = () => {
      persistCalls += 1;
      return new Promise<{ preferredLocale: 'en-US'; profileVersion: number }>(resolve => {
        resolvePersist = resolve;
      });
    };

    const save = controller.saveAuthenticatedLocale('en-US', persist);
    controller.dispose();
    resolvePersist?.({ preferredLocale: 'en-US', profileVersion: 6 });

    await expect(save).rejects.toThrow('disposed');
    expect(persistCalls).toBe(1);
    expect(storage.writes).not.toContain('en-US');
    expect(runtime.setCalls).not.toContain('en');
  });
});
