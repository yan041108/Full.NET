import { createHttpClient } from '../../api/http';
import { localeController } from '../../i18n';
import { createMpWeixinIdentitySession } from './mp-weixin-identity-session';

export const mpWeixinHttpClient = createHttpClient({
  request: uni.request,
  getLocale: () => localeController.initialize().preferredLocale,
  apiBaseUrl: import.meta.env.VITE_API_BASE_URL ?? ''
});

export const mpWeixinIdentitySession = createMpWeixinIdentitySession({
  http: mpWeixinHttpClient
});

/** 微信小程序不支持 HttpOnly Refresh Cookie，启动时不尝试恢复会话。 */
export async function restoreMpWeixinIdentitySession(): Promise<boolean> {
  return await mpWeixinIdentitySession.restore();
}
