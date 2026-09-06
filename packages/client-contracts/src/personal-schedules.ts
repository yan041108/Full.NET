export interface PersonalSchedule {
  id: string;
  content: string;
  startAtUtc: string;
  endAtUtc: string;
  status: PersonalScheduleStatus;
  completedAtUtc: string | null;
  createdAtUtc: string;
  updatedAtUtc: string | null;
  version: number;
}

export interface PersonalSchedulePage {
  items: PersonalSchedule[];
  page: number;
  pageSize: number;
  total: number;
}

export interface CreatePersonalScheduleRequest {
  content: string;
  startAtUtc: string;
  endAtUtc: string;
}

export interface UpdatePersonalScheduleRequest {
  content: string;
  startAtUtc: string;
  endAtUtc: string;
  version: number;
}

export interface ChangePersonalScheduleRequest {
  version: number;
}

export interface SetPersonalScheduleStatusRequest {
  status: PersonalScheduleStatus;
  version: number;
}

/** 个人日程完成状态稳定机器码，与服务端 PersonalScheduleStatuses 对齐。 */
export const PERSONAL_SCHEDULE_STATUSES = {
  pending: 'pending',
  completed: 'completed'
} as const;

export type PersonalScheduleStatus =
  (typeof PERSONAL_SCHEDULE_STATUSES)[keyof typeof PERSONAL_SCHEDULE_STATUSES];

const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export function isPersonalScheduleStatus(
  value: unknown
): value is PersonalScheduleStatus {
  return value === PERSONAL_SCHEDULE_STATUSES.pending
    || value === PERSONAL_SCHEDULE_STATUSES.completed;
}

export function isPersonalSchedule(value: unknown): value is PersonalSchedule {
  return isRecord(value)
    && isGuid(value.id)
    && isNonEmptyString(value.content)
    && typeof value.startAtUtc === 'string'
    && typeof value.endAtUtc === 'string'
    && isPersonalScheduleStatus(value.status)
    && (value.completedAtUtc === null || typeof value.completedAtUtc === 'string')
    && typeof value.createdAtUtc === 'string'
    && (value.updatedAtUtc === null || typeof value.updatedAtUtc === 'string')
    && Number.isInteger(value.version);
}

export function isPersonalSchedulePage(value: unknown): value is PersonalSchedulePage {
  return isRecord(value)
    && Array.isArray(value.items)
    && value.items.every(isPersonalSchedule)
    && Number.isInteger(value.page)
    && Number.isInteger(value.pageSize)
    && Number.isInteger(value.total);
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
