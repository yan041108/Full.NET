import { readonly, ref, type DeepReadonly, type Ref } from 'vue';
import {
  applyDocumentLocale,
  localeStorageKey,
  resolveLocale,
  translate,
  type MessageKey,
  type MessageParameters,
  type SupportedLocale
} from '@fullnet/admin-i18n';

export interface AdminI18nOptions {
  storage?: Pick<Storage, 'getItem' | 'setItem'>;
  preferredLocales?: readonly string[];
  document?: Document;
}

export interface AdminI18n {
  locale: DeepReadonly<Ref<SupportedLocale>>;
  t: (key: MessageKey, parameters?: MessageParameters) => string;
  setLocale: (value: SupportedLocale) => void;
  setPageTitle: (key: MessageKey) => void;
}

/**
 * 创建 Vue 语言状态；偏好存储失败只影响持久化，不能阻断认证和页面导航。
 */
export function createAdminI18n(options: AdminI18nOptions = {}): AdminI18n {
  const targetDocument = options.document ?? document;
  const storage = options.storage ?? resolveBrowserStorage();
  const locale = ref(resolveLocale(
    safeRead(storage),
    options.preferredLocales ?? globalThis.navigator?.languages ?? []
  ));
  const t = (key: MessageKey, parameters?: MessageParameters): string =>
    translate(locale.value, key, parameters);

  applyDocumentLocale(
    targetDocument,
    locale.value,
    targetDocument.title || 'Full.NET'
  );

  function setLocale(value: SupportedLocale): void {
    locale.value = value;
    safeWrite(storage, value);
    applyDocumentLocale(targetDocument, value, targetDocument.title);
  }

  function setPageTitle(key: MessageKey): void {
    applyDocumentLocale(
      targetDocument,
      locale.value,
      `${t(key)} · Full.NET`
    );
  }

  return {
    locale: readonly(locale),
    t,
    setLocale,
    setPageTitle
  };
}

function resolveBrowserStorage(): Pick<Storage, 'getItem' | 'setItem'> | undefined {
  try {
    return globalThis.localStorage;
  } catch {
    return undefined;
  }
}

function safeRead(
  storage: Pick<Storage, 'getItem' | 'setItem'> | undefined
): string | null {
  try {
    return storage?.getItem(localeStorageKey) ?? null;
  } catch {
    return null;
  }
}

function safeWrite(
  storage: Pick<Storage, 'getItem' | 'setItem'> | undefined,
  value: SupportedLocale
): void {
  try {
    storage?.setItem(localeStorageKey, value);
  } catch {
    // 受限浏览器可能禁止 localStorage；当前内存语言仍然有效。
  }
}

const adminI18n = createAdminI18n();

/** 返回当前管理端共享语言状态。 */
export function useAdminI18n(): AdminI18n {
  return adminI18n;
}
