import { afterEach, describe, expect, it, vi } from 'vitest';

import { createHttpClient } from '../src/api/http';

type LocaleEvent = { readonly locale?: unknown };
type LocaleListener = (event: LocaleEvent) => void;

class FakeUniRuntime {
  currentLocale: string;
  storedLocale: unknown;
  readonly localeListeners: LocaleListener[] = [];
  readonly setLocaleCalls: string[] = [];
  readonly navigationTitles: string[] = [];
  readonly requests: UniNamespace.RequestOptions[] = [];
  readonly queuedLocaleEvents: LocaleEvent[] = [];
  onLocaleChangeCalls = 0;
  queueLocaleEvents = false;
  responseStatus = 200;
  responseData: UniNamespace.RequestSuccessCallbackResult['data'] = {
    preferredLocale: 'zh-CN',
    profileVersion: 8
  };

  constructor(locale: string, storedLocale?: unknown) {
    this.currentLocale = locale;
    this.storedLocale = storedLocale;
  }

  readonly getLocale = (): string => this.currentLocale;

  readonly setLocale = (locale: string): void => {
    this.currentLocale = locale;
    this.setLocaleCalls.push(locale);
    const event = { locale };
    if (this.queueLocaleEvents) {
      this.queuedLocaleEvents.push(event);
      return;
    }

    this.emitLocale(event);
  };

  readonly onLocaleChange = (listener: LocaleListener): void => {
    this.onLocaleChangeCalls += 1;
    this.localeListeners.push(listener);
  };

  readonly getStorageSync = (): unknown => this.storedLocale;

  readonly setStorageSync = (_key: string, locale: unknown): void => {
    this.storedLocale = locale;
  };

  readonly setNavigationBarTitle = (options: { readonly title: string }): void => {
    this.navigationTitles.push(options.title);
  };

  readonly request = (options: UniNamespace.RequestOptions): void => {
    this.requests.push(options);
    options.success?.({
      statusCode: this.responseStatus,
      data: this.responseData,
      header: {},
      cookies: [],
      errMsg: 'request:ok'
    });
  };

  emitLocale(event: LocaleEvent): void {
    for (const listener of this.localeListeners) {
      listener(event);
    }
  }

  deliverQueuedLocale(locale: string): void {
    const index = this.queuedLocaleEvents.findIndex(event => event.locale === locale);
    if (index < 0) {
      throw new Error(`Queued locale event not found: ${locale}`);
    }

    const [event] = this.queuedLocaleEvents.splice(index, 1);
    this.emitLocale(event);
  }
}

function installRuntime(fake: FakeUniRuntime): void {
  Object.defineProperty(globalThis, 'uni', {
    configurable: true,
    value: fake as unknown as Uni
  });
}

async function loadSubject(fake: FakeUniRuntime) {
  vi.resetModules();
  installRuntime(fake);
  const subject = await import('../src/i18n');
  const document = { documentElement: { lang: 'unset' } };
  Object.defineProperty(globalThis, 'document', {
    configurable: true,
    value: document
  });
  return { subject, document };
}

afterEach(() => {
  Reflect.deleteProperty(globalThis, 'uni');
  Reflect.deleteProperty(globalThis, 'document');
  vi.resetModules();
});

describe('Vue I18n application integration', () => {
  it('initializes once and synchronizes committed canonical locales to every presentation boundary', async () => {
    const fake = new FakeUniRuntime('zh-Hans');
    const { subject, document } = await loadSubject(fake);

    expect(subject.initializeLocale()).toMatchObject({ preferredLocale: 'zh-CN' });
    expect(subject.initializeLocale()).toMatchObject({ preferredLocale: 'zh-CN' });
    expect(fake.onLocaleChangeCalls).toBe(1);
    expect(subject.i18n.global.locale.value).toBe('zh-CN');
    expect(document.documentElement.lang).toBe('zh-CN');

    await subject.setActiveLocale('en-US');

    expect(fake.setLocaleCalls).toEqual(['en']);
    expect(fake.storedLocale).toBe('en-US');
    expect(subject.i18n.global.locale.value).toBe('en-US');
    expect(fake.navigationTitles.at(-1)).toBe('Language settings');
    expect(document.documentElement.lang).toBe('en-US');
  });

  it('does not recommit the locale event mirrored by an application setLocale call', async () => {
    const fake = new FakeUniRuntime('zh-Hans');
    const { subject } = await loadSubject(fake);
    subject.initializeLocale();
    const snapshots: string[] = [];
    subject.localeController.subscribe(snapshot => snapshots.push(snapshot.preferredLocale));

    await subject.setActiveLocale('en-US');

    expect(snapshots).toEqual(['en-US']);
  });

  it('ignores locale-change payloads without a locale instead of falling back to Chinese', async () => {
    const fake = new FakeUniRuntime('en', 'en-US');
    const { subject } = await loadSubject(fake);
    subject.initializeLocale();
    const snapshots: string[] = [];
    subject.localeController.subscribe(snapshot => snapshots.push(snapshot.preferredLocale));

    fake.emitLocale({});

    expect(subject.localeController.initialize()).toMatchObject({ preferredLocale: 'en-US' });
    expect(fake.storedLocale).toBe('en-US');
    expect(snapshots).toEqual([]);
  });

  it('hydrates only from an injected account snapshot and saves through the Task 3 wire mapping', async () => {
    const fake = new FakeUniRuntime('en', 'en-US');
    const { subject } = await loadSubject(fake);
    const http = createHttpClient({
      request: fake.request as Uni['request'],
      getLocale: () => subject.localeController.initialize().preferredLocale
    });

    subject.hydrateAuthenticatedLocale({ preferredLocale: 'en-US', profileVersion: 7 }, http);
    expect(fake.requests).toEqual([]);

    await expect(subject.setActiveLocale('zh-CN')).resolves.toMatchObject({
      preferredLocale: 'zh-CN',
      profileVersion: 8,
      authenticated: true,
      saving: false
    });
    expect(fake.requests).toHaveLength(1);
    expect(fake.requests[0]).toMatchObject({
      url: '/api/v1/me/locale',
      method: 'PUT',
      data: { locale: 'zh-CN', profileVersion: 7 },
      header: { 'Accept-Language': 'en-US' }
    });
  });

  it('keeps the previous authenticated language and version when preference persistence fails', async () => {
    const fake = new FakeUniRuntime('zh-Hans', 'zh-CN');
    fake.responseStatus = 409;
    fake.responseData = {
      code: 'identity.profile_version_conflict',
      title: 'Profile changed.',
      traceId: 'trace-save-failure'
    };
    const { subject } = await loadSubject(fake);
    const http = createHttpClient({
      request: fake.request as Uni['request'],
      getLocale: () => subject.localeController.initialize().preferredLocale
    });
    subject.hydrateAuthenticatedLocale({ preferredLocale: 'zh-CN', profileVersion: 5 }, http);

    await expect(subject.setActiveLocale('en-US')).rejects.toMatchObject({ status: 409 });

    expect(subject.localeController.initialize()).toEqual({
      preferredLocale: 'zh-CN',
      profileVersion: 5,
      authenticated: true,
      saving: false
    });
    expect(subject.i18n.global.locale.value).toBe('zh-CN');
    expect(fake.storedLocale).toBe('zh-CN');
  });

  it('ignores stale application locale events delivered after a newer committed locale', async () => {
    const fake = new FakeUniRuntime('zh-Hans');
    fake.queueLocaleEvents = true;
    const { subject } = await loadSubject(fake);
    subject.initializeLocale();
    const snapshots: string[] = [];
    subject.localeController.subscribe(snapshot => snapshots.push(snapshot.preferredLocale));

    await subject.setActiveLocale('en-US');
    await subject.setActiveLocale('zh-CN');
    expect(fake.queuedLocaleEvents).toEqual([{ locale: 'en' }, { locale: 'zh-Hans' }]);

    fake.deliverQueuedLocale('zh-Hans');
    fake.deliverQueuedLocale('en');

    expect(subject.localeController.initialize()).toMatchObject({ preferredLocale: 'zh-CN' });
    expect(snapshots).toEqual(['en-US', 'zh-CN']);
  });
});
