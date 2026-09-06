export interface AdministrativeRegion {
  id: string;
  parentId: string | null;
  code: string;
  name: string;
  shortName: string | null;
  mergerName: string | null;
  zipCode: string | null;
  cityCode: string | null;
  level: number;
  regionType: string | null;
  pinYin: string | null;
  longitude: number | null;
  latitude: number | null;
  displayOrder: number;
  remark: string | null;
  createdAtUtc: string;
  updatedAtUtc: string | null;
  version: number;
}

export interface AdministrativeRegionChild {
  id: string;
  parentId: string | null;
  code: string;
  name: string;
  level: number;
  displayOrder: number;
  hasChildren: boolean;
}

export interface AdministrativeRegionTreeNode {
  id: string;
  parentId: string | null;
  code: string;
  name: string;
  level: number;
  displayOrder: number;
  children: AdministrativeRegionTreeNode[];
}

export interface AdministrativeRegionPage {
  items: AdministrativeRegion[];
  page: number;
  pageSize: number;
  total: number;
}

export interface AdministrativeRegionDatasetManifest {
  id: string;
  datasetKey: string;
  datasetVersion: string;
  sourceDigest: string;
  recordCount: number;
  appliedAtUtc: string;
  appliedByUserId: string;
}

export interface ImportAdministrativeRegionItem {
  code: string;
  parentCode?: string | null;
  name: string;
  shortName?: string | null;
  mergerName?: string | null;
  zipCode?: string | null;
  cityCode?: string | null;
  level: number;
  regionType?: string | null;
  pinYin?: string | null;
  longitude?: number | null;
  latitude?: number | null;
  displayOrder?: number | null;
}

export interface ImportAdministrativeRegionsRequest {
  datasetKey: string;
  datasetVersion: string;
  sourceDigest: string;
  mergeMode: 'merge' | 'replace';
  items: ImportAdministrativeRegionItem[];
}

export interface ImportAdministrativeRegionsPreview {
  added: Array<{ code: string; name: string; level: number }>;
  updated: Array<{ code: string; name: string; changedFields: string[] }>;
  removed: Array<{ code: string; name: string }>;
  skippedCount: number;
}

export interface ImportAdministrativeRegionsApplyResult {
  addedCount: number;
  updatedCount: number;
  removedCount: number;
  skippedCount: number;
  manifest: AdministrativeRegionDatasetManifest;
}

export interface CreateAdministrativeRegionRequest {
  parentId?: string | null;
  code: string;
  name: string;
  shortName?: string | null;
  mergerName?: string | null;
  zipCode?: string | null;
  cityCode?: string | null;
  level: number;
  regionType?: string | null;
  pinYin?: string | null;
  longitude?: number | null;
  latitude?: number | null;
  displayOrder: number;
  remark?: string | null;
}

export interface UpdateAdministrativeRegionRequest {
  parentId?: string | null;
  name: string;
  shortName?: string | null;
  mergerName?: string | null;
  zipCode?: string | null;
  cityCode?: string | null;
  level: number;
  regionType?: string | null;
  pinYin?: string | null;
  longitude?: number | null;
  latitude?: number | null;
  displayOrder: number;
  remark?: string | null;
  version: number;
}

const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export function isAdministrativeRegion(value: unknown): value is AdministrativeRegion {
  return isRecord(value)
    && isGuid(value.id)
    && (value.parentId === null || isGuid(value.parentId))
    && isNonEmptyString(value.code)
    && isNonEmptyString(value.name)
    && Number.isInteger(value.level)
    && Number.isInteger(value.displayOrder)
    && Number.isInteger(value.version)
    && typeof value.createdAtUtc === 'string';
}

export function isAdministrativeRegionChild(value: unknown): value is AdministrativeRegionChild {
  return isRecord(value)
    && isGuid(value.id)
    && (value.parentId === null || isGuid(value.parentId))
    && isNonEmptyString(value.code)
    && isNonEmptyString(value.name)
    && Number.isInteger(value.level)
    && Number.isInteger(value.displayOrder)
    && typeof value.hasChildren === 'boolean';
}

export function isAdministrativeRegionTreeNode(value: unknown): value is AdministrativeRegionTreeNode {
  return isRecord(value)
    && isGuid(value.id)
    && (value.parentId === null || isGuid(value.parentId))
    && isNonEmptyString(value.code)
    && isNonEmptyString(value.name)
    && Number.isInteger(value.level)
    && Number.isInteger(value.displayOrder)
    && Array.isArray(value.children)
    && value.children.every(isAdministrativeRegionTreeNode);
}

export function isAdministrativeRegionPage(value: unknown): value is AdministrativeRegionPage {
  return isRecord(value)
    && Array.isArray(value.items)
    && value.items.every(isAdministrativeRegion)
    && Number.isInteger(value.page)
    && Number.isInteger(value.pageSize)
    && Number.isInteger(value.total);
}

export function isAdministrativeRegionDatasetManifest(
  value: unknown
): value is AdministrativeRegionDatasetManifest {
  return isRecord(value)
    && isGuid(value.id)
    && isNonEmptyString(value.datasetKey)
    && isNonEmptyString(value.datasetVersion)
    && isNonEmptyString(value.sourceDigest)
    && Number.isInteger(value.recordCount)
    && typeof value.appliedAtUtc === 'string'
    && isGuid(value.appliedByUserId);
}

export function isImportAdministrativeRegionsPreview(
  value: unknown
): value is ImportAdministrativeRegionsPreview {
  return isRecord(value)
    && Array.isArray(value.added)
    && Array.isArray(value.updated)
    && Array.isArray(value.removed)
    && Number.isInteger(value.skippedCount);
}

export function isImportAdministrativeRegionsApplyResult(
  value: unknown
): value is ImportAdministrativeRegionsApplyResult {
  return isRecord(value)
    && Number.isInteger(value.addedCount)
    && Number.isInteger(value.updatedCount)
    && Number.isInteger(value.removedCount)
    && Number.isInteger(value.skippedCount)
    && isAdministrativeRegionDatasetManifest(value.manifest);
}

function isGuid(value: unknown): value is string {
  return typeof value === 'string' && guidPattern.test(value);
}

function isNonEmptyString(value: unknown): value is string {
  return typeof value === 'string' && value.trim().length > 0;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}
