export interface HostJobDefinition {
  id: string;
  jobKey: string;
  handlerKind: string;
  args: HttpJobArgs | null;
  displayName: string;
  description: string | null;
  groupName: string | null;
  isEnabled: boolean;
  allowConcurrentExecutions: boolean;
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
  jobScheduleId: string | null;
  status: 'pending' | 'running' | 'succeeded' | 'failed';
  triggerKind: string;
  scheduledForUtc: string | null;
  errorMessage: string | null;
  startedAtUtc: string | null;
  finishedAtUtc: string | null;
  nextAttemptAtUtc: string | null;
  attemptCount: number;
  createdAtUtc: string;
}

export interface HostJobExecutionListQuery {
  page?: number;
  pageSize?: number;
  jobDefinitionId?: string;
  jobScheduleId?: string;
  status?: string;
  fromUtc?: string;
  toUtc?: string;
}

export interface HostJobExecutionPage {
  items: HostJobExecution[];
  page: number;
  pageSize: number;
  total: number;
}

export interface CreateHostJobDefinitionRequest {
  jobKey: string;
  handlerKind: string;
  args?: HttpJobArgs | null;
  displayName: string;
  description?: string | null;
  groupName?: string | null;
  allowConcurrentExecutions?: boolean;
}

export interface UpdateHostJobDefinitionRequest {
  displayName: string;
  description?: string | null;
  groupName?: string | null;
  handlerKind: string;
  args?: HttpJobArgs | null;
  allowConcurrentExecutions: boolean;
  version: number;
}

export interface DisableHostJobDefinitionRequest {
  version: number;
}

export interface DeleteHostJobDefinitionRequest {
  version: number;
}

/** 作业分组去重选项，对应 Admin.NET ListJobGroup。 */
export interface HostJobGroup {
  groupName: string;
}

export const JOBS_WELL_KNOWN_KEYS = {
  ping: 'jobs.ping'
} as const;

export const JOB_HANDLER_KINDS = {
  ping: 'ping',
  http: 'http'
} as const;

export type JobHandlerKind =
  (typeof JOB_HANDLER_KINDS)[keyof typeof JOB_HANDLER_KINDS];

export interface HttpJobSecretHeaderRef {
  configKey: string;
}

export interface HttpJobArgs {
  url: string;
  method: string;
  headers?: Record<string, string> | null;
  secretHeaders?: Record<string, HttpJobSecretHeaderRef> | null;
  timeoutSeconds?: number | null;
  successStatusCodes?: number[] | null;
}

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
  jobDefinitionJobKey: string;
  jobDefinitionDisplayName: string;
  triggerKind: string;
  cronExpression: string | null;
  timeZoneId: string;
  oneTimeAtUtc: string | null;
  misfirePolicy: string;
  isEnabled: boolean;
  nextExecutionAtUtc: string | null;
  lastExecutionAtUtc: string | null;
  completedAtUtc: string | null;
  numberOfRuns: number;
  numberOfErrors: number;
  startTime: string | null;
  endTime: string | null;
  args: string | null;
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
  startTime?: string | null;
  endTime?: string | null;
  args?: string | null;
}

export interface UpdateHostJobScheduleRequest {
  triggerKind: string;
  cronExpression?: string | null;
  timeZoneId: string;
  oneTimeAtUtc?: string | null;
  misfirePolicy: string;
  startTime?: string | null;
  endTime?: string | null;
  args?: string | null;
  version: number;
}

export interface ChangeHostJobScheduleStateRequest {
  version: number;
}

export interface HostJobScheduleDefinitionOption {
  id: string;
  jobKey: string;
  handlerKind: string;
  displayName: string;
}

export interface HostJobScheduleCronPreview {
  humanDescription: string;
  nextExecutionAtUtc: string;
  nextOccurrencesUtc: string[];
}

export interface HostJobHealthBacklog {
  pendingCount: number;
  oldestClaimableCreatedAtUtc: string | null;
  dueRetryCount: number;
  oldestDueRetryAtUtc: string | null;
}

export interface HostJobWorkerInstance {
  instanceId: string;
  hostProfile: string;
  startedAtUtc: string;
  lastHeartbeatAtUtc: string;
  workerVersion: string | null;
  isStale: boolean;
}

export interface HostJobHealth {
  registeredHandlers: string[];
  backlog: HostJobHealthBacklog;
  workers: HostJobWorkerInstance[];
}

export function isHostJobSchedule(value: unknown): value is HostJobSchedule {
  return isRecord(value)
    && isGuid(value.id)
    && isGuid(value.jobDefinitionId)
    && isNonEmptyString(value.jobDefinitionJobKey)
    && isNonEmptyString(value.jobDefinitionDisplayName)
    && isNonEmptyString(value.triggerKind)
    && (value.cronExpression === null || typeof value.cronExpression === 'string')
    && isNonEmptyString(value.timeZoneId)
    && (value.oneTimeAtUtc === null || typeof value.oneTimeAtUtc === 'string')
    && isNonEmptyString(value.misfirePolicy)
    && typeof value.isEnabled === 'boolean'
    && (value.nextExecutionAtUtc === null || typeof value.nextExecutionAtUtc === 'string')
    && (value.lastExecutionAtUtc === null || typeof value.lastExecutionAtUtc === 'string')
    && (value.completedAtUtc === null || typeof value.completedAtUtc === 'string')
    && Number.isInteger(value.numberOfRuns)
    && Number.isInteger(value.numberOfErrors)
    && (value.startTime === null || typeof value.startTime === 'string')
    && (value.endTime === null || typeof value.endTime === 'string')
    && (value.args === null || typeof value.args === 'string')
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
      || typeof value.oneTimeAtUtc === 'string')
    && (value.startTime === undefined
      || value.startTime === null
      || typeof value.startTime === 'string')
    && (value.endTime === undefined
      || value.endTime === null
      || typeof value.endTime === 'string')
    && (value.args === undefined
      || value.args === null
      || typeof value.args === 'string');
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
      || typeof value.oneTimeAtUtc === 'string')
    && (value.startTime === undefined
      || value.startTime === null
      || typeof value.startTime === 'string')
    && (value.endTime === undefined
      || value.endTime === null
      || typeof value.endTime === 'string')
    && (value.args === undefined
      || value.args === null
      || typeof value.args === 'string');
}

export function isChangeHostJobScheduleStateRequest(
  value: unknown
): value is ChangeHostJobScheduleStateRequest {
  return isRecord(value) && Number.isInteger(value.version);
}

export function isHostJobScheduleDefinitionOption(
  value: unknown
): value is HostJobScheduleDefinitionOption {
  return isRecord(value)
    && isGuid(value.id)
    && isNonEmptyString(value.jobKey)
    && isNonEmptyString(value.handlerKind)
    && isNonEmptyString(value.displayName);
}

function isHttpJobArgs(value: unknown): value is HttpJobArgs {
  return isRecord(value)
    && isNonEmptyString(value.url)
    && isNonEmptyString(value.method)
    && (value.headers === undefined
      || value.headers === null
      || (isRecord(value.headers)
        && Object.values(value.headers).every(item => typeof item === 'string')))
    && (value.secretHeaders === undefined
      || value.secretHeaders === null
      || (isRecord(value.secretHeaders)
        && Object.values(value.secretHeaders).every(
          item => isRecord(item) && typeof item.configKey === 'string'
        )))
    && (value.timeoutSeconds === undefined
      || value.timeoutSeconds === null
      || typeof value.timeoutSeconds === 'number')
    && (value.successStatusCodes === undefined
      || value.successStatusCodes === null
      || (Array.isArray(value.successStatusCodes)
        && value.successStatusCodes.every(code => Number.isInteger(code))));
}

export function isHostJobScheduleDefinitionOptionList(
  value: unknown
): value is HostJobScheduleDefinitionOption[] {
  return Array.isArray(value) && value.every(isHostJobScheduleDefinitionOption);
}

export function isHostJobScheduleCronPreview(
  value: unknown
): value is HostJobScheduleCronPreview {
  return isRecord(value)
    && typeof value.humanDescription === 'string'
    && typeof value.nextExecutionAtUtc === 'string'
    && Array.isArray(value.nextOccurrencesUtc)
    && value.nextOccurrencesUtc.every(item => typeof item === 'string');
}

export function isHostJobHealth(value: unknown): value is HostJobHealth {
  return isRecord(value)
    && Array.isArray(value.registeredHandlers)
    && value.registeredHandlers.every(item => typeof item === 'string')
    && isRecord(value.backlog)
    && Number.isInteger(value.backlog.pendingCount)
    && (value.backlog.oldestClaimableCreatedAtUtc === null
      || typeof value.backlog.oldestClaimableCreatedAtUtc === 'string')
    && Number.isInteger(value.backlog.dueRetryCount)
    && (value.backlog.oldestDueRetryAtUtc === null
      || typeof value.backlog.oldestDueRetryAtUtc === 'string')
    && Array.isArray(value.workers)
    && value.workers.every(isHostJobWorkerInstance);
}

function isHostJobWorkerInstance(value: unknown): value is HostJobWorkerInstance {
  return isRecord(value)
    && isGuid(value.instanceId)
    && typeof value.hostProfile === 'string'
    && typeof value.startedAtUtc === 'string'
    && typeof value.lastHeartbeatAtUtc === 'string'
    && (value.workerVersion === null || typeof value.workerVersion === 'string')
    && typeof value.isStale === 'boolean';
}

const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export function isHostJobDefinition(value: unknown): value is HostJobDefinition {
  return isRecord(value)
    && isGuid(value.id)
    && isNonEmptyString(value.jobKey)
    && isNonEmptyString(value.handlerKind)
    && (value.args === null || isHttpJobArgs(value.args))
    && isNonEmptyString(value.displayName)
    && (value.description === null || typeof value.description === 'string')
    && (value.groupName === null || typeof value.groupName === 'string')
    && typeof value.isEnabled === 'boolean'
    && typeof value.allowConcurrentExecutions === 'boolean'
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
    && (value.jobScheduleId === null || isGuid(value.jobScheduleId))
    && (value.status === 'pending'
      || value.status === 'running'
      || value.status === 'succeeded'
      || value.status === 'failed')
    && isNonEmptyString(value.triggerKind)
    && (value.scheduledForUtc === null || typeof value.scheduledForUtc === 'string')
    && (value.errorMessage === null || typeof value.errorMessage === 'string')
    && (value.startedAtUtc === null || typeof value.startedAtUtc === 'string')
    && (value.finishedAtUtc === null || typeof value.finishedAtUtc === 'string')
    && (value.nextAttemptAtUtc === null || typeof value.nextAttemptAtUtc === 'string')
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
    && isNonEmptyString(value.handlerKind)
    && (value.args === undefined || value.args === null || isHttpJobArgs(value.args))
    && isNonEmptyString(value.displayName)
    && (value.description === undefined
      || value.description === null
      || typeof value.description === 'string')
    && (value.groupName === undefined
      || value.groupName === null
      || typeof value.groupName === 'string')
    && (value.allowConcurrentExecutions === undefined
      || typeof value.allowConcurrentExecutions === 'boolean');
}

export function isUpdateHostJobDefinitionRequest(
  value: unknown
): value is UpdateHostJobDefinitionRequest {
  return isRecord(value)
    && isNonEmptyString(value.displayName)
    && isNonEmptyString(value.handlerKind)
    && (value.args === undefined || value.args === null || isHttpJobArgs(value.args))
    && (value.description === undefined
      || value.description === null
      || typeof value.description === 'string')
    && (value.groupName === undefined
      || value.groupName === null
      || typeof value.groupName === 'string')
    && typeof value.allowConcurrentExecutions === 'boolean'
    && Number.isInteger(value.version);
}

export function isDisableHostJobDefinitionRequest(
  value: unknown
): value is DisableHostJobDefinitionRequest {
  return isRecord(value) && Number.isInteger(value.version);
}

export function isDeleteHostJobDefinitionRequest(
  value: unknown
): value is DeleteHostJobDefinitionRequest {
  return isRecord(value) && Number.isInteger(value.version);
}

export function isHostJobGroup(value: unknown): value is HostJobGroup {
  return isRecord(value) && typeof value.groupName === 'string';
}

export function isHostJobGroupList(value: unknown): value is HostJobGroup[] {
  return Array.isArray(value) && value.every(isHostJobGroup);
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
