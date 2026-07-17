import { describe, expect, it } from 'vitest';
import { localeStorageKey } from '@fullnet/admin-i18n';
import { createAdminI18n } from './adminI18n';

describe('Vue 管理端语言状态', () => {
  it('保存语言并同步更新文档语义和当前页面标题', () => {
    const storage = createMemoryStorage();
    const i18n = createAdminI18n({
      storage,
      preferredLocales: ['zh-CN'],
      document
    });

    i18n.setLocale('en-US');
    i18n.setPageTitle('navigation.overview.title');

    expect(i18n.locale.value).toBe('en-US');
    expect(storage.getItem(localeStorageKey)).toBe('en-US');
    expect(document.documentElement.lang).toBe('en-US');
    expect(document.title).toBe('Overview · Full.NET');
  });

  it('存储不可用时仍保留内存语言并允许继续认证流程', () => {
    const storage = {
      getItem: () => { throw new DOMException('blocked'); },
      setItem: () => { throw new DOMException('blocked'); }
    };

    const i18n = createAdminI18n({
      storage,
      preferredLocales: ['en-GB'],
      document
    });

    expect(() => i18n.setLocale('zh-CN')).not.toThrow();
    expect(i18n.locale.value).toBe('zh-CN');
  });
});

function createMemoryStorage(): Pick<Storage, 'getItem' | 'setItem'> {
  const values = new Map<string, string>();
  return {
    getItem: key => values.get(key) ?? null,
    setItem: (key, value) => values.set(key, value)
  };
}
