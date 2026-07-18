import { createIdentitySession as createSharedIdentitySession } from '@fullnet/client-contracts';
import { http } from './http.js';
import { adminI18n } from './i18n.js';
import { isSupportedNavigationTree } from './navigation.js';

/**
 * 创建独立管理端会话状态机；Access Token 只保存在闭包内，不写入浏览器持久化存储。
 */
export function createIdentitySession(options = {}) {
  const i18n = options.i18n ?? adminI18n;
  return createSharedIdentitySession({
    http,
    i18n: {
      getLocale: () => i18n.snapshot().locale,
      setLocale: locale => i18n.setLocale(locale)
    },
    isSupportedNavigationTree
  });
}

export const identitySession = createIdentitySession();
