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

export const JOB_TRIGGER_KINDS = {
  cron: 'cron',
  oneTime: 'one_time'
} as const;

export const JOB_MISFIRE_POLICIES = {
  skip: 'skip',
  fireOnce: 'fire_once'
} as const;

export interface HostJobSchedule {
  id: string;
  jobDefinitionId: string;
  triggerKind: string;
  cronExpression: string | null;
  timeZoneId: string;
  oneTimeAtUtc: string | null;
  misfirePolicy: string;
  isEnabled: boolean;
  nextExecutionAtUtc: string | null;
  lastExecutionAtUtc: string | null;
  completedAtUtc: string | null;
  createdAtUtc: string;
  updatedAtUtc: string | null;
  version: number;
}

export interface HostJobSchedulePage {
  items: HostJobSchedule[];
  page: number;
  pageSize: number;
  total: number;
}

export interface CreateHostJobScheduleRequest {
  jobDefinitionId: string;
  triggerKind: string;
  cronExpression?: string | null;
  timeZoneId: string;
  oneTimeAtUtc?: string | null;
  misfirePolicy: string;
}

export interface UpdateHostJobScheduleRequest {
  triggerKind: string;
  cronExpression?: string | null;
  timeZoneId: string;
  oneTimeAtUtc?: string | null;
  misfirePolicy: string;
  version: number;
}

export interface ChangeHostJobScheduleStateRequest {
  version: number;
}

export function isHostJobSchedule(value: unknown): value is HostJobSchedule {
  return isRecord(value)
    && isGuid(value.id)
    && isGuid(value.jobDefinitionId)
    && isNonEmptyString(value.triggerKind)
    && (value.cronExpression === null || typeof value.cronExpression === 'string')
    && isNonEmptyString(value.timeZoneId)
    && (value.oneTimeAtUtc === null || typeof value.oneTimeAtUtc === 'string')
    && isNonEmptyString(value.misfirePolicy)
    && typeof value.isEnabled === 'boolean'
    && (value.nextExecutionAtUtc === null || typeof value.nextExecutionAtUtc === 'string')
    && (value.lastExecutionAtUtc === null || typeof value.lastExecutionAtUtc === 'string')
    && (value.completedAtUtc === null || typeof value.completedAtUtc === 'string')
    && typeof value.createdAtUtc === 'string'
    && (value.updatedAtUtc === null || typeof value.updatedAtUtc === 'string')
    && Number.isInteger(value.version);
}

export function isHostJobSchedulePage(
  value: unknown
): value is HostJobSchedulePage {
  return isRecord(value)
    && Array.isArray(value.items)
    && value.items.every(isHostJobSchedule)
    && Number.isInteger(value.page)
    && Number.isInteger(value.pageSize)
    && Number.isInteger(value.total);
}

export function isCreateHostJobScheduleRequest(
  value: unknown
): value is CreateHostJobScheduleRequest {
  return isRecord(value)
    && isGuid(value.jobDefinitionId)
    && isNonEmptyString(value.triggerKind)
    && isNonEmptyString(value.timeZoneId)
    && isNonEmptyString(value.misfirePolicy)
    && (value.cronExpression === undefined
      || value.cronExpression === null
      || typeof value.cronExpression === 'string')
    && (value.oneTimeAtUtc === undefined
      || value.oneTimeAtUtc === null
      || typeof value.oneTimeAtUtc === 'string');
}

export function isUpdateHostJobScheduleRequest(
  value: unknown
): value is UpdateHostJobScheduleRequest {
  return isRecord(value)
    && isNonEmptyString(value.triggerKind)
    && isNonEmptyString(value.timeZoneId)
    && isNonEmptyString(value.misfirePolicy)
    && Number.isInteger(value.version)
    && (value.cronExpression === undefined
      || value.cronExpression === null
      || typeof value.cronExpression === 'string')
    && (value.oneTimeAtUtc === undefined
      || value.oneTimeAtUtc === null
      || typeof value.oneTimeAtUtc === 'string');
}

export function isChangeHostJobScheduleStateRequest(
  value: unknown
): value is ChangeHostJobScheduleStateRequest {
  return isRecord(value) && Number.isInteger(value.version);
}

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
