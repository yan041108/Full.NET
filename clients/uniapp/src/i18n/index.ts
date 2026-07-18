import { createI18n } from 'vue-i18n';

import type { HttpClient } from '../api/http';
import { saveLocalePreference } from '../api/locale-preference';
import { toCanonicalLocale, toUniLocale, type CanonicalLocale, type UniLocale } from './locale-adapter';
import {
  createLocaleController,
  type AccountLocaleSnapshot,
  type LocaleRuntime,
  type LocaleSnapshot
} from './locale-controller';
import enUS from './messages.en-US.json';
import zhCN from './messages.zh-CN.json';

const localeStorageKey = 'fullnet.preferred-locale';

export const i18n = createI18n({
  legacy: false,
  locale: 'zh-CN',
  fallbackLocale: 'zh-CN',
  messages: {
    'zh-CN': zhCN,
    'en-US': enUS
  }
});

let expectedPlatformLocale: UniLocale | undefined;
let authenticatedHttp: HttpClient | undefined;
let initialized = false;

const runtime: LocaleRuntime = {
  getLocale() {
    return toCanonicalLocale(uni.getLocale());
  },
  setLocale(locale) {
    const nextLocale = toUniLocale(toCanonicalLocale(locale));
    const currentLocale = toUniLocale(toCanonicalLocale(uni.getLocale()));
    if (currentLocale === nextLocale) {
      return;
    }

    // 平台可能为 setLocale 再派发 locale change；该标记只吞掉本应用触发的镜像事件。
    expectedPlatformLocale = nextLocale;
    uni.setLocale(nextLocale);
  },
  onLocaleChange(listener) {
    let active = true;
    uni.onLocaleChange(result => {
      if (!active) {
        return;
      }

      if (typeof result?.locale !== 'string') {
        return;
      }

      const canonicalLocale = toCanonicalLocale(result.locale);
      const platformLocale = toUniLocale(canonicalLocale);
      const currentPlatformLocale = toUniLocale(toCanonicalLocale(uni.getLocale()));
      if (platformLocale !== currentPlatformLocale) {
        return;
      }

      if (expectedPlatformLocale === platformLocale) {
        expectedPlatformLocale = undefined;
        return;
      }

      expectedPlatformLocale = undefined;
      listener(canonicalLocale);
    });

    // uni-app 没有公开 offLocaleChange；释放时让闭包失效，避免已销毁控制器继续接收事件。
    return () => {
      active = false;
    };
  }
};

export const localeController = createLocaleController({
  runtime,
  storage: {
    get: () => uni.getStorageSync(localeStorageKey),
    set: locale => uni.setStorageSync(localeStorageKey, locale)
  }
});

/** 启动语言控制器，并把首个已提交快照同步到各展示边界。 */
export function initializeLocale(): LocaleSnapshot {
  const snapshot = localeController.initialize();
  if (!initialized) {
    localeController.subscribe(nextSnapshot => {
      synchronizeCommittedLocale(nextSnapshot.preferredLocale);
    });
    initialized = true;
  }

  synchronizeCommittedLocale(snapshot.preferredLocale);
  return snapshot;
}

/** 由真实认证壳层注入完整账号快照与已有 HTTP 客户端，不会主动请求 /me。 */
export function hydrateAuthenticatedLocale(
  snapshot: AccountLocaleSnapshot,
  http: HttpClient
): LocaleSnapshot {
  initializeLocale();
  const hydrated = localeController.hydrateAccount(snapshot);
  authenticatedHttp = http;
  return hydrated;
}

/** 提交匿名设备语言，或通过已注入账号端口原子保存认证语言。 */
export async function setActiveLocale(locale: CanonicalLocale): Promise<LocaleSnapshot> {
  const snapshot = initializeLocale();
  if (!snapshot.authenticated) {
    return localeController.setAnonymousLocale(locale);
  }

  if (!authenticatedHttp) {
    throw new Error('Authenticated locale persistence is not configured.');
  }

  const http = authenticatedHttp;
  return localeController.saveAuthenticatedLocale(locale, request =>
    saveLocalePreference(http, request)
  );
}

/** 使用当前 Vue I18n 资源刷新公开导航栏标题，供页面重新显示时调用。 */
export function synchronizeNavigationTitle(): void {
  try {
    uni.setNavigationBarTitle({
      title: String(i18n.global.t('settings.title')),
      fail: () => undefined
    });
  } catch {
    // 应用启动阶段页面可能尚未创建；页面 onShow 会用同一公开入口再次同步标题。
  }
}

function synchronizeCommittedLocale(locale: CanonicalLocale): void {
  i18n.global.locale.value = locale;
  runtime.setLocale(toUniLocale(locale));
  synchronizeNavigationTitle();

  // H5 才存在 document；运行时检测避免小程序构建或启动访问浏览器全局。
  if (typeof document !== 'undefined' && document.documentElement) {
    document.documentElement.lang = locale;
  }
}
