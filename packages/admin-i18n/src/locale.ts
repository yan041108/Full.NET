export const supportedLocales = ['zh-CN', 'en-US'] as const;

export type SupportedLocale = typeof supportedLocales[number];

export const localeStorageKey = 'fullnet.admin.locale';

/**
 * 解析受支持语言；保存值优先于浏览器偏好，所有未知值统一回退到中文。
 */
export function resolveLocale(
  savedLocale: unknown,
  preferredLocales: readonly string[] = []
): SupportedLocale {
  if (savedLocale === 'zh-CN' || savedLocale === 'en-US') {
    return savedLocale;
  }

  for (const value of preferredLocales) {
    const language = value.toLowerCase();
    if (language.startsWith('en')) {
      return 'en-US';
    }

    if (language.startsWith('zh')) {
      return 'zh-CN';
    }
  }

  return 'zh-CN';
}

/**
 * 同步文档语言和标题，供两套管理端保持辅助技术语义一致。
 */
export function applyDocumentLocale(
  target: Document,
  locale: SupportedLocale,
  title: string
): void {
  target.documentElement.lang = locale;
  target.title = title;
}
