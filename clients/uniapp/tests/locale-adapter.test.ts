import { describe, expect, it } from 'vitest';

import {
  isCanonicalLocale,
  toCanonicalLocale,
  toUniLocale
} from '../src/i18n/locale-adapter';

describe('locale adapter', () => {
  it.each([
    ['zh', 'zh-CN'],
    ['zh-CN', 'zh-CN'],
    ['zh-Hans', 'zh-CN'],
    ['zh_CN', 'zh-CN'],
    ['en', 'en-US'],
    ['en-US', 'en-US'],
    ['en_US', 'en-US']
  ])('normalizes %s to %s', (value, expected) => {
    expect(toCanonicalLocale(value)).toBe(expected);
  });

  it('falls back to Simplified Chinese for an unknown locale', () => {
    expect(toCanonicalLocale('fr-FR')).toBe('zh-CN');
    expect(toCanonicalLocale(undefined)).toBe('zh-CN');
  });

  it('recognizes only canonical locales', () => {
    expect(isCanonicalLocale('zh-CN')).toBe(true);
    expect(isCanonicalLocale('en-US')).toBe(true);
    expect(isCanonicalLocale('zh-Hans')).toBe(false);
    expect(isCanonicalLocale('en')).toBe(false);
  });

  it('maps canonical locales to the uni-app platform locales', () => {
    expect(toUniLocale('zh-CN')).toBe('zh-Hans');
    expect(toUniLocale('en-US')).toBe('en');
  });
});
