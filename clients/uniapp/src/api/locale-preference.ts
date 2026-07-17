import { isCanonicalLocale, type CanonicalLocale } from '../i18n/locale-adapter';
import type { HttpClient } from './http';

export interface CurrentProfileLocale {
  readonly preferredLocale: CanonicalLocale;
  readonly profileVersion: number;
}

/** 读取完整当前用户响应中的语言偏好快照。 */
export async function getCurrentProfile(http: HttpClient): Promise<CurrentProfileLocale> {
  return guardCurrentProfileLocale(await http.request<unknown>({
    path: '/api/v1/me',
    method: 'GET'
  }));
}

/** 将端口模型显式映射到既有更新偏好线路字段。 */
export async function saveLocalePreference(
  http: HttpClient,
  request: CurrentProfileLocale
): Promise<CurrentProfileLocale> {
  const snapshot = guardCurrentProfileLocale(request);
  return guardCurrentProfileLocale(await http.request<unknown>({
    path: '/api/v1/me/locale',
    method: 'PUT',
    data: {
      locale: snapshot.preferredLocale,
      profileVersion: snapshot.profileVersion
    }
  }));
}

function guardCurrentProfileLocale(value: unknown): CurrentProfileLocale {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) {
    throw new TypeError('Current profile locale response is invalid.');
  }

  const candidate = value as Record<string, unknown>;
  if (
    hasSensitiveTokenKey(candidate)
    || !isCanonicalLocale(candidate.preferredLocale)
    || typeof candidate.profileVersion !== 'number'
    || !Number.isSafeInteger(candidate.profileVersion)
    || candidate.profileVersion <= 0
  ) {
    throw new TypeError('Current profile locale response is invalid.');
  }

  return {
    preferredLocale: candidate.preferredLocale,
    profileVersion: candidate.profileVersion
  };
}

function hasSensitiveTokenKey(candidate: Record<string, unknown>): boolean {
  return Object.getOwnPropertyNames(candidate).some(key => {
    const normalizedKey = key.toLowerCase().replace(/[^a-z0-9]/g, '');
    return normalizedKey === 'accesstoken' || normalizedKey === 'refreshtoken';
  });
}
