import type {
  HostUserProfileResponse,
  HostUserProfileWriteRequest
} from './generated/models.generated.js';
import type { HttpClient } from './http.js';

/** 当前用户自助档案读取响应。 */
export interface SelfServiceProfileResponse {
  readonly userId: string;
  readonly username: string;
  readonly displayName: string;
  readonly accountType: string;
  readonly userVersion: number;
  readonly readableFieldKeys: readonly string[];
  readonly writableFieldKeys: readonly string[];
  readonly profile: HostUserProfileResponse | null;
}

/** 当前用户自助档案更新请求。 */
export interface UpdateSelfServiceProfileRequest {
  readonly displayName?: string | null;
  readonly userVersion?: number | null;
  readonly profile?: HostUserProfileWriteRequest | null;
}

const guidPattern =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function isHostUserProfileResponse(value: unknown): value is HostUserProfileResponse {
  return isRecord(value)
    && typeof value.version === 'number'
    && Number.isInteger(value.version);
}

/** 校验自助档案读取响应。 */
export function isSelfServiceProfileResponse(
  value: unknown
): value is SelfServiceProfileResponse {
  return isRecord(value)
    && typeof value.userId === 'string'
    && guidPattern.test(value.userId)
    && typeof value.username === 'string'
    && typeof value.displayName === 'string'
    && typeof value.accountType === 'string'
    && typeof value.userVersion === 'number'
    && Number.isInteger(value.userVersion)
    && Array.isArray(value.readableFieldKeys)
    && value.readableFieldKeys.every(fieldKey => typeof fieldKey === 'string')
    && Array.isArray(value.writableFieldKeys)
    && value.writableFieldKeys.every(fieldKey => typeof fieldKey === 'string')
    && (value.profile === null || isHostUserProfileResponse(value.profile));
}

/** 读取当前用户自助档案。 */
export async function getSelfServiceProfile(
  http: HttpClient,
  signal?: AbortSignal
): Promise<SelfServiceProfileResponse> {
  const value = await http.request<unknown>(
    '/api/v1/me/profile',
    { method: 'GET' },
    signal
  );
  if (!isSelfServiceProfileResponse(value)) {
    throw new TypeError('自助档案响应不符合 SelfServiceProfileResponse 契约。');
  }

  return value;
}

/** 更新当前用户自助档案。 */
export async function updateSelfServiceProfile(
  http: HttpClient,
  request: UpdateSelfServiceProfileRequest,
  signal?: AbortSignal
): Promise<SelfServiceProfileResponse> {
  const value = await http.request<unknown>(
    '/api/v1/me/profile',
    {
      method: 'PUT',
      body: JSON.stringify(request)
    },
    signal,
    { retryUnauthorized: false }
  );
  if (!isSelfServiceProfileResponse(value)) {
    throw new TypeError('自助档案更新响应不符合 SelfServiceProfileResponse 契约。');
  }

  return value;
}
