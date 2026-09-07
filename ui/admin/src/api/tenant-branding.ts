import {
  deleteCurrentTenantBrandingLogo,
  deleteHostTenantBrandingLogo,
  downloadCurrentTenantBrandingLogoContent,
  downloadHostTenantBrandingLogoContent,
  getCurrentTenantBranding,
  getHostTenantBranding,
  updateCurrentTenantBranding,
  updateHostTenantBranding,
  uploadCurrentTenantBrandingLogo,
  uploadHostTenantBrandingLogo,
  type TenantBrandingResponse,
  type UpdateTenantBrandingRequest
} from '@fullnet/client-contracts';
import { http } from './http';

/** Host 读取指定租户品牌（/api/v1/tenancy/tenants/{tenantId}/branding）。 */
export async function getTenantBranding(
  tenantId: string,
  signal?: AbortSignal
): Promise<TenantBrandingResponse> {
  return getHostTenantBranding(http, tenantId, signal);
}

export async function updateTenantBranding(
  tenantId: string,
  request: UpdateTenantBrandingRequest,
  signal?: AbortSignal
): Promise<TenantBrandingResponse> {
  return updateHostTenantBranding(http, tenantId, request, signal);
}

export async function uploadTenantBrandingLogo(
  tenantId: string,
  file: File,
  signal?: AbortSignal
): Promise<TenantBrandingResponse> {
  return uploadHostTenantBrandingLogo(http, tenantId, file, signal);
}

export async function removeTenantBrandingLogo(
  tenantId: string,
  signal?: AbortSignal
): Promise<TenantBrandingResponse> {
  return deleteHostTenantBrandingLogo(http, tenantId, signal);
}

export async function fetchTenantBrandingLogoBlob(
  tenantId: string,
  signal?: AbortSignal
): Promise<Blob> {
  return downloadHostTenantBrandingLogoContent(http, tenantId, signal);
}

/** 读取当前作用域租户品牌。 */
export async function getCurrentScopeTenantBranding(
  signal?: AbortSignal
): Promise<TenantBrandingResponse> {
  return getCurrentTenantBranding(http, signal);
}

/** 更新当前作用域租户品牌文本字段。 */
export async function updateCurrentScopeTenantBranding(
  request: UpdateTenantBrandingRequest,
  signal?: AbortSignal
): Promise<TenantBrandingResponse> {
  return updateCurrentTenantBranding(http, request, signal);
}

/** 上传当前作用域租户 Logo。 */
export async function uploadCurrentScopeTenantBrandingLogo(
  file: File,
  signal?: AbortSignal
): Promise<TenantBrandingResponse> {
  return uploadCurrentTenantBrandingLogo(http, file, signal);
}

/** 删除当前作用域租户 Logo。 */
export async function deleteCurrentScopeTenantBrandingLogo(
  signal?: AbortSignal
): Promise<TenantBrandingResponse> {
  return deleteCurrentTenantBrandingLogo(http, signal);
}

/** 下载当前作用域租户 Logo 二进制内容。 */
export async function fetchCurrentScopeTenantBrandingLogoBlob(
  signal?: AbortSignal
): Promise<Blob> {
  return downloadCurrentTenantBrandingLogoContent(http, signal);
}
