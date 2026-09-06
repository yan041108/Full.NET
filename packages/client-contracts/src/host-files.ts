export interface HostFile {
  id: string;
  originalFileName: string;
  contentType: string;
  sizeBytes: number;
  contentHash: string | null;
  createdAtUtc: string;
  createdByUserId: string;
  folderId: string | null;
  revision: number;
  updatedAtUtc: string | null;
  updatedByUserId: string | null;
}

export interface HostFilePage {
  items: HostFile[];
  page: number;
  pageSize: number;
  total: number;
}

export interface HostFolderTreeNode {
  id: string;
  parentId: string | null;
  name: string;
  displayOrder: number;
  revision: number;
  children: HostFolderTreeNode[];
}

export interface HostFolder {
  id: string;
  parentId: string | null;
  name: string;
  displayOrder: number;
  revision: number;
  createdAtUtc: string;
  createdByUserId: string;
  updatedAtUtc: string | null;
  updatedByUserId: string | null;
}

export interface HostFileReferenceClaim {
  id: string;
  idempotencyKey: string;
  consumerModule: string;
  consumerReferenceId: string;
  state: string;
  createdAtUtc: string;
  updatedAtUtc: string;
  confirmedAtUtc: string | null;
  releasedAtUtc: string | null;
}

export interface HostFileReferenceClaimPage {
  items: HostFileReferenceClaim[];
  page: number;
  pageSize: number;
  total: number;
}

export interface BatchUploadHostFileItem {
  originalFileName: string;
  succeeded: boolean;
  file: HostFile | null;
  errorCode: string | null;
  message: string | null;
}

export interface BatchUploadHostFilesResponse {
  succeededCount: number;
  results: BatchUploadHostFileItem[];
}

export interface BatchDeleteHostFileItem {
  fileId: string;
  succeeded: boolean;
  errorCode: string | null;
  message: string | null;
}

export interface BatchDeleteHostFilesResponse {
  succeededCount: number;
  results: BatchDeleteHostFileItem[];
}

export function isPreviewableHostFile(contentType: string): boolean {
  const normalized = contentType.split(';', 2)[0].trim().toLowerCase();
  if (normalized === 'text/html' || normalized === 'image/svg+xml') {
    return false;
  }

  return normalized.startsWith('text/')
    || normalized.startsWith('image/')
    || normalized === 'application/pdf';
}

const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export function isHostFile(value: unknown): value is HostFile {
  return isRecord(value)
    && isGuid(value.id)
    && typeof value.originalFileName === 'string'
    && typeof value.contentType === 'string'
    && Number.isInteger(value.sizeBytes)
    && (value.contentHash === null || typeof value.contentHash === 'string')
    && typeof value.createdAtUtc === 'string'
    && isGuid(value.createdByUserId)
    && (value.folderId === null || isGuid(value.folderId))
    && Number.isInteger(value.revision)
    && (value.updatedAtUtc === null || typeof value.updatedAtUtc === 'string')
    && (value.updatedByUserId === null || isGuid(value.updatedByUserId));
}

export function isHostFilePage(value: unknown): value is HostFilePage {
  return isRecord(value)
    && Array.isArray(value.items)
    && value.items.every(isHostFile)
    && Number.isInteger(value.page)
    && Number.isInteger(value.pageSize)
    && Number.isInteger(value.total);
}

function isGuid(value: unknown): value is string {
  return typeof value === 'string' && guidPattern.test(value);
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}
