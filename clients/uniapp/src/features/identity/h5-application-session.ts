import { createHttpClient } from '../../api/http';
import { localeController } from '../../i18n';
import { readH5CsrfHeaders } from './h5-csrf';
import { createH5IdentitySession } from './h5-identity-session';

export const h5HttpClient = createHttpClient({
  request: uni.request,
  getLocale: () => localeController.initialize().preferredLocale
});

export const h5IdentitySession = createH5IdentitySession({
  http: h5HttpClient,
  readCsrfHeaders: () => readH5CsrfHeaders()
});

/** H5 启动时尝试用 HttpOnly Refresh Cookie 恢复会话；失败保持匿名。 */
export async function restoreH5IdentitySession(): Promise<boolean> {
  return await h5IdentitySession.restore();
}
