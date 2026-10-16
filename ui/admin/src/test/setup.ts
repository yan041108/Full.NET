import { beforeEach } from 'vitest';
import { useAdminI18n } from '../i18n/adminI18n';

if (typeof window.matchMedia !== 'function') {
  Object.defineProperty(window, 'matchMedia', {
    writable: true,
    // 用稳定的浏览器替身承接全局 API；各文件的 resetAllMocks 不应清掉它的实现。
    value: (query: string) => ({
      matches: false,
      media: query,
      addEventListener() {},
      removeEventListener() {},
      addListener() {},
      removeListener() {},
      dispatchEvent() { return false; }
    })
  });
}

beforeEach(() => {
  localStorage.clear();
  sessionStorage.clear();
  useAdminI18n().setLocale('zh-CN');
});
