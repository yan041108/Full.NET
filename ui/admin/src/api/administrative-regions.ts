import {
  isAdministrativeRegion,
  isAdministrativeRegionChild,
  isAdministrativeRegionDatasetManifest,
  isAdministrativeRegionPage,
  isAdministrativeRegionTreeNode,
  isImportAdministrativeRegionsApplyResult,
  isImportAdministrativeRegionsPreview,
  type AdministrativeRegion,
  type AdministrativeRegionChild,
  type AdministrativeRegionDatasetManifest,
  type AdministrativeRegionPage,
  type AdministrativeRegionTreeNode,
  type CreateAdministrativeRegionRequest,
  type ImportAdministrativeRegionsApplyResult,
  type ImportAdministrativeRegionsPreview,
  type ImportAdministrativeRegionsRequest,
  type UpdateAdministrativeRegionRequest
} from '@fullnet/client-contracts';
import { request } from './http';

export interface AdministrativeRegionListQuery {
  page?: number;
  pageSize?: number;
  parentId?: string;
  name?: string;
  code?: string;
  level?: number;
}

function buildListQuery(query: AdministrativeRegionListQuery): string {
  const params = new URLSearchParams();
  params.set('page', String(query.page ?? 1));
  params.set('pageSize', String(query.pageSize ?? 20));
  if (query.parentId) {
    params.set('parentId', query.parentId);
  }
  if (query.name?.trim()) {
    params.set('name', query.name.trim());
  }
  if (query.code?.trim()) {
    params.set('code', query.code.trim());
  }
  if (query.level !== undefined) {
    params.set('level', String(query.level));
  }
  return params.toString();
}

export async function listAdministrativeRegionChildren(
  parentId?: string,
  signal?: AbortSignal
): Promise<AdministrativeRegionChild[]> {
  const params = parentId ? `?parentId=${encodeURIComponent(parentId)}` : '';
  const value = await request<unknown>(
    `/api/v1/regions/administrative-regions/children${params}`,
    { method: 'GET' },
    signal
  );
  if (!Array.isArray(value) || !value.every(isAdministrativeRegionChild)) {
    throw new Error('client.invalid_administrative_region_children');
  }
  return value;
}

export async function getAdministrativeRegionTree(
  parentId?: string,
  maxDepth = 5,
  signal?: AbortSignal
): Promise<AdministrativeRegionTreeNode[]> {
  const params = new URLSearchParams({ maxDepth: String(maxDepth) });
  if (parentId) {
    params.set('parentId', parentId);
  }
  const value = await request<unknown>(
    `/api/v1/regions/administrative-regions/tree?${params.toString()}`,
    { method: 'GET' },
    signal
  );
  if (!Array.isArray(value) || !value.every(isAdministrativeRegionTreeNode)) {
    throw new Error('client.invalid_administrative_region_tree');
  }
  return value;
}

export async function listAdministrativeRegions(
  query: AdministrativeRegionListQuery = {},
  signal?: AbortSignal
): Promise<AdministrativeRegionPage> {
  const value = await request<unknown>(
    `/api/v1/regions/administrative-regions?${buildListQuery(query)}`,
    { method: 'GET' },
    signal
  );
  if (!isAdministrativeRegionPage(value)) {
    throw new Error('client.invalid_administrative_region_page');
  }
  return value;
}

export async function getAdministrativeRegion(
  id: string,
  signal?: AbortSignal
): Promise<AdministrativeRegion> {
  const value = await request<unknown>(
    `/api/v1/regions/administrative-regions/${id}`,
    { method: 'GET' },
    signal
  );
  if (!isAdministrativeRegion(value)) {
    throw new Error('client.invalid_administrative_region');
  }
  return value;
}

export async function getLatestAdministrativeRegionDatasetManifest(
  datasetKey = 'china.administrative',
  signal?: AbortSignal
): Promise<AdministrativeRegionDatasetManifest | null> {
  try {
    const value = await request<unknown>(
      `/api/v1/regions/administrative-regions/dataset-manifest/latest?datasetKey=${encodeURIComponent(datasetKey)}`,
      { method: 'GET' },
      signal
    );
    if (!isAdministrativeRegionDatasetManifest(value)) {
      throw new Error('client.invalid_administrative_region_manifest');
    }
    return value;
  } catch (error: unknown) {
    if (isNotFoundProblem(error)) {
      return null;
    }
    throw error;
  }
}

export async function createAdministrativeRegion(
  body: CreateAdministrativeRegionRequest,
  signal?: AbortSignal
): Promise<AdministrativeRegion> {
  const value = await request<unknown>(
    '/api/v1/regions/administrative-regions',
    {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify(body)
    },
    signal
  );
  if (!isAdministrativeRegion(value)) {
    throw new Error('client.invalid_administrative_region');
  }
  return value;
}

export async function updateAdministrativeRegion(
  id: string,
  body: UpdateAdministrativeRegionRequest,
  signal?: AbortSignal
): Promise<AdministrativeRegion> {
  const value = await request<unknown>(
    `/api/v1/regions/administrative-regions/${id}`,
    {
      method: 'PUT',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify(body)
    },
    signal
  );
  if (!isAdministrativeRegion(value)) {
    throw new Error('client.invalid_administrative_region');
  }
  return value;
}

export async function deleteAdministrativeRegion(
  id: string,
  version: number,
  signal?: AbortSignal
): Promise<void> {
  await request<unknown>(
    `/api/v1/regions/administrative-regions/${id}/delete`,
    {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ version })
    },
    signal
  );
}

export async function previewAdministrativeRegionImport(
  body: ImportAdministrativeRegionsRequest,
  signal?: AbortSignal
): Promise<ImportAdministrativeRegionsPreview> {
  const value = await request<unknown>(
    '/api/v1/regions/administrative-regions/import/preview',
    {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify(body)
    },
    signal
  );
  if (!isImportAdministrativeRegionsPreview(value)) {
    throw new Error('client.invalid_administrative_region_import_preview');
  }
  return value;
}

export async function applyAdministrativeRegionImport(
  body: ImportAdministrativeRegionsRequest,
  signal?: AbortSignal
): Promise<ImportAdministrativeRegionsApplyResult> {
  const value = await request<unknown>(
    '/api/v1/regions/administrative-regions/import/apply',
    {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify(body)
    },
    signal
  );
  if (!isImportAdministrativeRegionsApplyResult(value)) {
    throw new Error('client.invalid_administrative_region_import_apply');
  }
  return value;
}

function isNotFoundProblem(error: unknown): boolean {
  return typeof error === 'object'
    && error !== null
    && 'status' in error
    && (error as { status?: number }).status === 404;
}

export type {
  AdministrativeRegion,
  AdministrativeRegionChild,
  AdministrativeRegionDatasetManifest,
  AdministrativeRegionPage,
  AdministrativeRegionTreeNode,
  ImportAdministrativeRegionsApplyResult,
  ImportAdministrativeRegionsPreview,
  ImportAdministrativeRegionsRequest
};
