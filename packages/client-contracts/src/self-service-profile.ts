import type {
  HostUserProfileResponse,
  HostUserProfileWriteRequest
} from './generated/models.generated.js';
import type { HttpClient, RequestOptions } from './http.js';

/** 当前用户自助档案读取响应。 */
export interface SelfServiceProfileResponse {
  readonly userId: string;
  readonly username: string;
  readonly displayName: string;
  readonly accountType: string;
  readonly userVersion: number;
  readonly readableFieldKeys: readonly string[];
  readonly writableFieldKeys: readonly string[];
  readonly avatarFileId: string | null;
  readonly signatureFileId: string | null;
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

function isOptionalGuid(value: unknown): boolean {
  return value === null || (typeof value === 'string' && guidPattern.test(value));
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
    && isOptionalGuid(value.avatarFileId)
    && isOptionalGuid(value.signatureFileId)
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

async function uploadProfileMedia(
  http: HttpClient,
  path: string,
  file: File,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<SelfServiceProfileResponse> {
  const body = new FormData();
  body.append('file', file);
  const value = options === undefined
    ? await http.request<unknown>(path, { method: 'POST', body }, signal)
    : await http.request<unknown>(path, { method: 'POST', body }, signal, options);
  if (!isSelfServiceProfileResponse(value)) {
    throw new TypeError('自助档案媒体上传响应不符合 SelfServiceProfileResponse 契约。');
  }

  return value;
}

/** 上传并绑定当前用户头像。 */
export async function uploadSelfServiceAvatar(
  http: HttpClient,
  file: File,
  signal?: AbortSignal
): Promise<SelfServiceProfileResponse> {
  return uploadProfileMedia(http, '/api/v1/me/profile/avatar', file, signal, {
    retryUnauthorized: false
  });
}

/** 上传并绑定当前用户签名图。 */
export async function uploadSelfServiceSignature(
  http: HttpClient,
  file: File,
  signal?: AbortSignal
): Promise<SelfServiceProfileResponse> {
  return uploadProfileMedia(http, '/api/v1/me/profile/signature', file, signal, {
    retryUnauthorized: false
  });
}

/** 删除当前用户头像绑定。 */
export async function deleteSelfServiceAvatar(
  http: HttpClient,
  signal?: AbortSignal
): Promise<SelfServiceProfileResponse> {
  const value = await http.request<unknown>(
    '/api/v1/me/profile/avatar',
    { method: 'DELETE' },
    signal,
    { retryUnauthorized: false }
  );
  if (!isSelfServiceProfileResponse(value)) {
    throw new TypeError('自助头像删除响应不符合 SelfServiceProfileResponse 契约。');
  }

  return value;
}

/** 删除当前用户签名绑定。 */
export async function deleteSelfServiceSignature(
  http: HttpClient,
  signal?: AbortSignal
): Promise<SelfServiceProfileResponse> {
  const value = await http.request<unknown>(
    '/api/v1/me/profile/signature',
    { method: 'DELETE' },
    signal,
    { retryUnauthorized: false }
  );
  if (!isSelfServiceProfileResponse(value)) {
    throw new TypeError('自助签名删除响应不符合 SelfServiceProfileResponse 契约。');
  }

  return value;
}

/** 下载当前用户头像内容。 */
export async function downloadSelfServiceAvatarContent(
  http: HttpClient,
  signal?: AbortSignal
): Promise<Blob> {
  return http.request<Blob>(
    '/api/v1/me/profile/avatar/content',
    { method: 'GET' },
    signal
  );
}

/** 下载当前用户签名内容。 */
export async function downloadSelfServiceSignatureContent(
  http: HttpClient,
  signal?: AbortSignal
): Promise<Blob> {
  return http.request<Blob>(
    '/api/v1/me/profile/signature/content',
    { method: 'GET' },
    signal
  );
}
