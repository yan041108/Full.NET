export interface HostJobDefinition {
  id: string;
  jobKey: string;
  displayName: string;
  description: string | null;
  isEnabled: boolean;
  createdAtUtc: string;
  updatedAtUtc: string | null;
  version: number;
}

export interface HostJobDefinitionPage {
  items: HostJobDefinition[];
  page: number;
  pageSize: number;
  total: number;
}

export interface HostJobExecution {
  id: string;
  jobDefinitionId: string;
  status: 'pending' | 'running' | 'succeeded' | 'failed';
  triggerKind: string;
  errorMessage: string | null;
  startedAtUtc: string | null;
  finishedAtUtc: string | null;
  attemptCount: number;
  createdAtUtc: string;
}

export interface HostJobExecutionPage {
  items: HostJobExecution[];
  page: number;
  pageSize: number;
  total: number;
}

export interface CreateHostJobDefinitionRequest {
  jobKey: string;
  displayName: string;
  description?: string | null;
}

export interface UpdateHostJobDefinitionRequest {
  displayName: string;
  description?: string | null;
  version: number;
}

export interface DisableHostJobDefinitionRequest {
  version: number;
}

export const JOBS_WELL_KNOWN_KEYS = {
  ping: 'jobs.ping'
} as const;

const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export function isHostJobDefinition(value: unknown): value is HostJobDefinition {
  return isRecord(value)
    && isGuid(value.id)
    && isNonEmptyString(value.jobKey)
    && isNonEmptyString(value.displayName)
    && (value.description === null || typeof value.description === 'string')
    && typeof value.isEnabled === 'boolean'
    && typeof value.createdAtUtc === 'string'
    && (value.updatedAtUtc === null || typeof value.updatedAtUtc === 'string')
    && Number.isInteger(value.version);
}

export function isHostJobDefinitionPage(
  value: unknown
): value is HostJobDefinitionPage {
  return isRecord(value)
    && Array.isArray(value.items)
    && value.items.every(isHostJobDefinition)
    && Number.isInteger(value.page)
    && Number.isInteger(value.pageSize)
    && Number.isInteger(value.total);
}

export function isHostJobExecution(value: unknown): value is HostJobExecution {
  return isRecord(value)
    && isGuid(value.id)
    && isGuid(value.jobDefinitionId)
    && (value.status === 'pending'
      || value.status === 'running'
      || value.status === 'succeeded'
      || value.status === 'failed')
    && isNonEmptyString(value.triggerKind)
    && (value.errorMessage === null || typeof value.errorMessage === 'string')
    && (value.startedAtUtc === null || typeof value.startedAtUtc === 'string')
    && (value.finishedAtUtc === null || typeof value.finishedAtUtc === 'string')
    && Number.isInteger(value.attemptCount)
    && typeof value.createdAtUtc === 'string';
}

export function isHostJobExecutionPage(
  value: unknown
): value is HostJobExecutionPage {
  return isRecord(value)
    && Array.isArray(value.items)
    && value.items.every(isHostJobExecution)
    && Number.isInteger(value.page)
    && Number.isInteger(value.pageSize)
    && Number.isInteger(value.total);
}

export function isCreateHostJobDefinitionRequest(
  value: unknown
): value is CreateHostJobDefinitionRequest {
  return isRecord(value)
    && isNonEmptyString(value.jobKey)
    && isNonEmptyString(value.displayName)
    && (value.description === undefined
      || value.description === null
      || typeof value.description === 'string');
}

export function isUpdateHostJobDefinitionRequest(
  value: unknown
): value is UpdateHostJobDefinitionRequest {
  return isRecord(value)
    && isNonEmptyString(value.displayName)
    && (value.description === undefined
      || value.description === null
      || typeof value.description === 'string')
    && Number.isInteger(value.version);
}

export function isDisableHostJobDefinitionRequest(
  value: unknown
): value is DisableHostJobDefinitionRequest {
  return isRecord(value) && Number.isInteger(value.version);
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
