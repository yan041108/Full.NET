import { describe, expect, it, vi } from 'vitest';
import { localeStorageKey } from '@fullnet/admin-i18n';
import { createAdminI18n } from '../js/core/i18n.js';

describe('Layui 管理端语言状态', () => {
  it('切换语言后只更新受控文本和属性绑定', () => {
    document.body.innerHTML = `
      <label data-i18n="locale.label"></label>
      <button data-i18n="auth.submit"></button>
      <input data-i18n-placeholder="auth.usernamePlaceholder">`;
    const storage = createMemoryStorage();
    const i18n = createAdminI18n({
      storage,
      document,
      preferredLocales: ['zh-CN']
    });

    i18n.applyBindings(document);
    i18n.setLocale('en-US');

    expect(document.querySelector('label').textContent).toBe('Language');
    expect(document.querySelector('button').textContent).toBe('Open console');
    expect(document.querySelector('input').placeholder)
      .toBe('Enter the host administrator username…');
    expect(document.documentElement.lang).toBe('en-US');
    expect(storage.getItem(localeStorageKey)).toBe('en-US');
    i18n.dispose();
  });

  it('销毁后不再通知语言订阅者', () => {
    const i18n = createAdminI18n({ document, preferredLocales: ['zh-CN'] });
    const listener = vi.fn();
    i18n.subscribe(listener);

    i18n.dispose();
    i18n.setLocale('en-US');

    expect(listener).toHaveBeenCalledTimes(1);
  });
});

function createMemoryStorage() {
  const values = new Map();
  return {
    getItem: key => values.get(key) ?? null,
    setItem: (key, value) => values.set(key, value)
  };
}
