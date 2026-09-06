export interface PlatformBackupTask {
  id: string;
  taskKey: string;
  displayName: string;
  description: string | null;
  databaseProvider: string;
  isEnabled: boolean;
  sortOrder: number;
  createdAtUtc: string;
  updatedAtUtc: string | null;
}

export interface PlatformBackupRun {
  id: string;
  taskId: string;
  taskKey: string;
  taskDisplayName: string;
  status: string;
  startedAtUtc: string;
  completedAtUtc: string | null;
  artifactFileName: string | null;
  artifactSizeBytes: number | null;
  artifactContentType: string | null;
  summaryMessage: string | null;
  createdAtUtc: string;
  canDownload: boolean;
}

export interface PlatformBackupRunPage {
  items: PlatformBackupRun[];
  page: number;
  pageSize: number;
  total: number;
}

export interface PlatformBackupExecutorStatus {
  artifactRootPath: string;
  artifactRootExists: boolean;
  enabledTaskCount: number;
  deploymentNotice: string;
}

export interface PlatformBackupRunListQuery {
  page?: number;
  pageSize?: number;
  taskId?: string;
  status?: string;
  fromUtc?: string;
  toUtc?: string;
}

export function isPlatformBackupTask(value: unknown): value is PlatformBackupTask {
  return isRecord(value)
    && isNonEmptyString(value.id)
    && isNonEmptyString(value.taskKey)
    && isNonEmptyString(value.displayName)
    && (value.description === null || typeof value.description === 'string')
    && isNonEmptyString(value.databaseProvider)
    && typeof value.isEnabled === 'boolean'
    && typeof value.sortOrder === 'number'
    && isNonEmptyString(value.createdAtUtc)
    && (value.updatedAtUtc === null || typeof value.updatedAtUtc === 'string');
}

export function isPlatformBackupRun(value: unknown): value is PlatformBackupRun {
  return isRecord(value)
    && isNonEmptyString(value.id)
    && isNonEmptyString(value.taskId)
    && isNonEmptyString(value.taskKey)
    && isNonEmptyString(value.taskDisplayName)
    && isNonEmptyString(value.status)
    && isNonEmptyString(value.startedAtUtc)
    && (value.completedAtUtc === null || typeof value.completedAtUtc === 'string')
    && (value.artifactFileName === null || typeof value.artifactFileName === 'string')
    && (value.artifactSizeBytes === null || typeof value.artifactSizeBytes === 'number')
    && (value.artifactContentType === null || typeof value.artifactContentType === 'string')
    && (value.summaryMessage === null || typeof value.summaryMessage === 'string')
    && isNonEmptyString(value.createdAtUtc)
    && typeof value.canDownload === 'boolean';
}

export function isPlatformBackupRunPage(value: unknown): value is PlatformBackupRunPage {
  return isRecord(value)
    && Array.isArray(value.items)
    && value.items.every(isPlatformBackupRun)
    && typeof value.page === 'number'
    && typeof value.pageSize === 'number'
    && typeof value.total === 'number';
}

export function isPlatformBackupExecutorStatus(
  value: unknown
): value is PlatformBackupExecutorStatus {
  return isRecord(value)
    && typeof value.artifactRootPath === 'string'
    && typeof value.artifactRootExists === 'boolean'
    && typeof value.enabledTaskCount === 'number'
    && typeof value.deploymentNotice === 'string';
}

function isNonEmptyString(value: unknown): value is string {
  return typeof value === 'string' && value.trim().length > 0;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}
