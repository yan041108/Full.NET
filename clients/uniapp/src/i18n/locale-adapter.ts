export type CanonicalLocale = 'zh-CN' | 'en-US';
export type UniLocale = 'zh-Hans' | 'en';

const canonicalAliases: Readonly<Record<string, CanonicalLocale>> = {
  zh: 'zh-CN',
  'zh-cn': 'zh-CN',
  'zh-hans': 'zh-CN',
  zh_cn: 'zh-CN',
  en: 'en-US',
  'en-us': 'en-US',
  en_us: 'en-US'
};

export function isCanonicalLocale(value: unknown): value is CanonicalLocale {
  return value === 'zh-CN' || value === 'en-US';
}

export function toCanonicalLocale(value: unknown): CanonicalLocale {
  if (typeof value !== 'string') {
    return 'zh-CN';
  }

  return canonicalAliases[value.trim().toLowerCase()] ?? 'zh-CN';
}

export function toUniLocale(locale: CanonicalLocale): UniLocale {
  return locale === 'zh-CN' ? 'zh-Hans' : 'en';
}
