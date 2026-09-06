import {
  deleteHostTenantBrandingLogo,
  downloadHostTenantBrandingLogoContent,
  getHostTenantBranding,
  updateHostTenantBranding,
  uploadHostTenantBrandingLogo,
  type TenantBrandingResponse,
  type UpdateTenantBrandingRequest
} from '@fullnet/client-contracts';
import { http } from './http';

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
