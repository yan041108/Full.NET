import {
  applyDocumentLocale,
  localeStorageKey,
  resolveLocale,
  translate
} from '@fullnet/admin-i18n';

const bindings = [
  ['data-i18n', 'textContent'],
  ['data-i18n-aria-label', 'aria-label'],
  ['data-i18n-placeholder', 'placeholder'],
  ['data-i18n-title', 'title']
];

/**
 * 创建原生语言控制器；只开放固定文本属性，避免翻译内容进入 HTML 或可执行位置。
 */
export function createAdminI18n(options = {}) {
  const targetDocument = options.document ?? document;
  const storage = options.storage ?? resolveBrowserStorage();
  let locale = resolveLocale(
    safeRead(storage),
    options.preferredLocales ?? globalThis.navigator?.languages ?? []
  );
  let disposed = false;
  const listeners = new Set();

  const t = (key, parameters) => translate(locale, key, parameters);
  const snapshot = () => ({ locale, t, setPageTitle });

  applyDocumentLocale(
    targetDocument,
    locale,
    targetDocument.title || 'Full.NET'
  );

  function applyBindings(root = targetDocument) {
    for (const [attribute, property] of bindings) {
      root.querySelectorAll(`[${attribute}]`).forEach((element) => {
        const key = element.getAttribute(attribute);
        const value = t(key);
        if (property === 'textContent') {
          element.textContent = value;
        } else {
          element.setAttribute(property, value);
        }
      });
    }

    root.querySelectorAll('[data-locale-select]').forEach((selector) => {
      selector.value = locale;
    });
  }

  function setLocale(value) {
    if (value !== 'zh-CN' && value !== 'en-US') {
      return;
    }

    locale = value;
    safeWrite(storage, value);
    applyDocumentLocale(targetDocument, locale, targetDocument.title);
    applyBindings(targetDocument);
    if (disposed) {
      return;
    }

    const valueSnapshot = snapshot();
    listeners.forEach(listener => listener(valueSnapshot));
  }

  function setPageTitle(key) {
    applyDocumentLocale(
      targetDocument,
      locale,
      `${t(key)} · Full.NET`
    );
  }

  function subscribe(listener) {
    if (disposed) {
      return () => {};
    }

    listeners.add(listener);
    listener(snapshot());
    return () => listeners.delete(listener);
  }

  function dispose() {
    disposed = true;
    listeners.clear();
  }

  return {
    applyBindings,
    setLocale,
    setPageTitle,
    snapshot,
    subscribe,
    dispose
  };
}

function resolveBrowserStorage() {
  try {
    return globalThis.localStorage;
  } catch {
    return undefined;
  }
}

function safeRead(storage) {
  try {
    return storage?.getItem(localeStorageKey) ?? null;
  } catch {
    return null;
  }
}

function safeWrite(storage, value) {
  try {
    storage?.setItem(localeStorageKey, value);
  } catch {
    // 浏览器禁用存储时保留当前内存语言，认证与路由仍可继续工作。
  }
}

export const adminI18n = createAdminI18n();
