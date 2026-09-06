import type { HttpClient, RequestOptions } from './http.js';

/** 租户品牌读取响应。 */
export interface TenantBrandingResponse {
  readonly tenantId: string;
  readonly systemTitle: string | null;
  readonly logoFileId: string | null;
  readonly contactPhone: string | null;
  readonly contactEmail: string | null;
  readonly contactAddress: string | null;
  readonly copyright: string | null;
  readonly version: number;
}

/** 运行时租户品牌摘要。 */
export interface TenantRuntimeBrandingResponse {
  readonly systemTitle: string | null;
  readonly hasLogo: boolean;
  readonly contactPhone: string | null;
  readonly contactEmail: string | null;
  readonly contactAddress: string | null;
  readonly copyright: string | null;
}

/** 更新租户品牌文本字段请求。 */
export interface UpdateTenantBrandingRequest {
  readonly systemTitle?: string | null;
  readonly contactPhone?: string | null;
  readonly contactEmail?: string | null;
  readonly contactAddress?: string | null;
  readonly copyright?: string | null;
  readonly version: number;
}

const guidPattern =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function isOptionalString(value: unknown): boolean {
  return value === null || typeof value === 'string';
}

function isOptionalGuid(value: unknown): boolean {
  return value === null || (typeof value === 'string' && guidPattern.test(value));
}

/** 校验租户品牌读取响应。 */
export function isTenantBrandingResponse(value: unknown): value is TenantBrandingResponse {
  return isRecord(value)
    && typeof value.tenantId === 'string'
    && guidPattern.test(value.tenantId)
    && isOptionalString(value.systemTitle)
    && isOptionalGuid(value.logoFileId)
    && isOptionalString(value.contactPhone)
    && isOptionalString(value.contactEmail)
    && isOptionalString(value.contactAddress)
    && isOptionalString(value.copyright)
    && typeof value.version === 'number'
    && Number.isInteger(value.version);
}

/** 校验运行时租户品牌摘要。 */
export function isTenantRuntimeBrandingResponse(
  value: unknown
): value is TenantRuntimeBrandingResponse {
  return isRecord(value)
    && isOptionalString(value.systemTitle)
    && typeof value.hasLogo === 'boolean'
    && isOptionalString(value.contactPhone)
    && isOptionalString(value.contactEmail)
    && isOptionalString(value.contactAddress)
    && isOptionalString(value.copyright);
}

/** 读取当前租户品牌信息。 */
export async function getCurrentTenantBranding(
  http: HttpClient,
  signal?: AbortSignal
): Promise<TenantBrandingResponse> {
  const value = await http.request<unknown>(
    '/api/v1/tenancy/branding',
    { method: 'GET' },
    signal
  );
  if (!isTenantBrandingResponse(value)) {
    throw new TypeError('租户品牌响应不符合 TenantBrandingResponse 契约。');
  }

  return value;
}

/** Host 读取指定租户品牌信息。 */
export async function getHostTenantBranding(
  http: HttpClient,
  tenantId: string,
  signal?: AbortSignal
): Promise<TenantBrandingResponse> {
  const value = await http.request<unknown>(
    `/api/v1/tenancy/tenants/${tenantId}/branding`,
    { method: 'GET' },
    signal
  );
  if (!isTenantBrandingResponse(value)) {
    throw new TypeError('租户品牌响应不符合 TenantBrandingResponse 契约。');
  }

  return value;
}

/** 读取运行时租户品牌摘要（登录壳层）。 */
export async function getRuntimeTenantBranding(
  http: HttpClient,
  signal?: AbortSignal
): Promise<TenantRuntimeBrandingResponse> {
  const value = await http.request<unknown>(
    '/api/v1/tenancy/branding/current',
    { method: 'GET' },
    signal
  );
  if (!isTenantRuntimeBrandingResponse(value)) {
    throw new TypeError('运行时品牌响应不符合 TenantRuntimeBrandingResponse 契约。');
  }

  return value;
}

/** 更新当前租户品牌文本字段。 */
export async function updateCurrentTenantBranding(
  http: HttpClient,
  request: UpdateTenantBrandingRequest,
  signal?: AbortSignal
): Promise<TenantBrandingResponse> {
  const value = await http.request<unknown>(
    '/api/v1/tenancy/branding',
    {
      method: 'PUT',
      body: JSON.stringify(request)
    },
    signal,
    { retryUnauthorized: false }
  );
  if (!isTenantBrandingResponse(value)) {
    throw new TypeError('租户品牌更新响应不符合 TenantBrandingResponse 契约。');
  }

  return value;
}

/** Host 更新指定租户品牌文本字段。 */
export async function updateHostTenantBranding(
  http: HttpClient,
  tenantId: string,
  request: UpdateTenantBrandingRequest,
  signal?: AbortSignal
): Promise<TenantBrandingResponse> {
  const value = await http.request<unknown>(
    `/api/v1/tenancy/tenants/${tenantId}/branding`,
    {
      method: 'PUT',
      body: JSON.stringify(request)
    },
    signal,
    { retryUnauthorized: false }
  );
  if (!isTenantBrandingResponse(value)) {
    throw new TypeError('租户品牌更新响应不符合 TenantBrandingResponse 契约。');
  }

  return value;
}

async function uploadLogo(
  http: HttpClient,
  path: string,
  file: File,
  signal?: AbortSignal
): Promise<TenantBrandingResponse> {
  const formData = new FormData();
  formData.append('file', file);
  const value = await http.request<unknown>(
    path,
    {
      method: 'POST',
      body: formData
    } as RequestOptions,
    signal,
    { retryUnauthorized: false }
  );
  if (!isTenantBrandingResponse(value)) {
    throw new TypeError('租户 Logo 上传响应不符合 TenantBrandingResponse 契约。');
  }

  return value;
}

/** 上传当前租户 Logo。 */
export async function uploadCurrentTenantBrandingLogo(
  http: HttpClient,
  file: File,
  signal?: AbortSignal
): Promise<TenantBrandingResponse> {
  return uploadLogo(http, '/api/v1/tenancy/branding/logo', file, signal);
}

/** Host 上传指定租户 Logo。 */
export async function uploadHostTenantBrandingLogo(
  http: HttpClient,
  tenantId: string,
  file: File,
  signal?: AbortSignal
): Promise<TenantBrandingResponse> {
  return uploadLogo(http, `/api/v1/tenancy/tenants/${tenantId}/branding/logo`, file, signal);
}

/** 删除当前租户 Logo。 */
export async function deleteCurrentTenantBrandingLogo(
  http: HttpClient,
  signal?: AbortSignal
): Promise<TenantBrandingResponse> {
  const value = await http.request<unknown>(
    '/api/v1/tenancy/branding/logo',
    { method: 'DELETE' },
    signal,
    { retryUnauthorized: false }
  );
  if (!isTenantBrandingResponse(value)) {
    throw new TypeError('租户 Logo 删除响应不符合 TenantBrandingResponse 契约。');
  }

  return value;
}

/** Host 删除指定租户 Logo。 */
export async function deleteHostTenantBrandingLogo(
  http: HttpClient,
  tenantId: string,
  signal?: AbortSignal
): Promise<TenantBrandingResponse> {
  const value = await http.request<unknown>(
    `/api/v1/tenancy/tenants/${tenantId}/branding/logo`,
    { method: 'DELETE' },
    signal,
    { retryUnauthorized: false }
  );
  if (!isTenantBrandingResponse(value)) {
    throw new TypeError('租户 Logo 删除响应不符合 TenantBrandingResponse 契约。');
  }

  return value;
}

/** 下载当前租户 Logo 内容。 */
export async function downloadCurrentTenantBrandingLogoContent(
  http: HttpClient,
  signal?: AbortSignal
): Promise<Blob> {
  return http.requestBlob('/api/v1/tenancy/branding/logo/content', { method: 'GET' }, signal);
}

/** Host 下载指定租户 Logo 内容。 */
export async function downloadHostTenantBrandingLogoContent(
  http: HttpClient,
  tenantId: string,
  signal?: AbortSignal
): Promise<Blob> {
  return http.requestBlob(
    `/api/v1/tenancy/tenants/${tenantId}/branding/logo/content`,
    { method: 'GET' },
    signal
  );
}
