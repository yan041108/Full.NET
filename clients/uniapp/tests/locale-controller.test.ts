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
});
