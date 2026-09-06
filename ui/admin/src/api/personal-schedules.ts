import {
  calendarCreateMyPersonalSchedule,
  calendarDeleteMyPersonalSchedule,
  calendarListMyPersonalSchedules,
  calendarSetMyPersonalScheduleStatus,
  calendarUpdateMyPersonalSchedule,
  isPersonalSchedule,
  isPersonalSchedulePage,
  type ChangePersonalScheduleRequest,
  type CreatePersonalScheduleRequest,
  type PersonalSchedule,
  type PersonalSchedulePage,
  type PersonalScheduleStatus,
  type SetPersonalScheduleStatusRequest,
  type UpdatePersonalScheduleRequest
} from '@fullnet/client-contracts';
import { http } from './http';

/** 个人日程列表查询参数；月份视图通过 fromUtc/toUtc 限定范围。 */
export interface PersonalScheduleListQuery {
  page?: number;
  pageSize?: number;
  status?: PersonalScheduleStatus | '';
  fromUtc?: string;
  toUtc?: string;
}

/** 分页查询当前用户个人日程，并对响应页做失败关闭校验。 */
export async function listPersonalSchedules(
  query: PersonalScheduleListQuery = {},
  signal?: AbortSignal
): Promise<PersonalSchedulePage> {
  const value = await calendarListMyPersonalSchedules(
    http,
    {
      page: query.page ?? 1,
      pageSize: query.pageSize ?? 200,
      status: query.status || undefined,
      fromUtc: query.fromUtc,
      toUtc: query.toUtc
    },
    signal
  );
  if (!isPersonalSchedulePage(value)) {
    throw new Error('client.invalid_personal_schedule_page');
  }

  return value;
}

/** 创建个人日程。 */
export async function createPersonalSchedule(
  body: CreatePersonalScheduleRequest,
  signal?: AbortSignal
): Promise<PersonalSchedule> {
  const value = await calendarCreateMyPersonalSchedule(http, { body }, signal);
  if (!isPersonalSchedule(value)) {
    throw new Error('client.invalid_personal_schedule');
  }

  return value;
}

/** 更新个人日程。 */
export async function updatePersonalSchedule(
  scheduleId: string,
  body: UpdatePersonalScheduleRequest,
  signal?: AbortSignal
): Promise<PersonalSchedule> {
  const value = await calendarUpdateMyPersonalSchedule(
    http,
    { scheduleId, body },
    signal
  );
  if (!isPersonalSchedule(value)) {
    throw new Error('client.invalid_personal_schedule');
  }

  return value;
}

/** 删除个人日程。 */
export async function deletePersonalSchedule(
  scheduleId: string,
  body: ChangePersonalScheduleRequest,
  signal?: AbortSignal
): Promise<void> {
  await calendarDeleteMyPersonalSchedule(http, { scheduleId, body }, signal);
}

/** 切换个人日程完成状态。 */
export async function setPersonalScheduleStatus(
  scheduleId: string,
  body: SetPersonalScheduleStatusRequest,
  signal?: AbortSignal
): Promise<PersonalSchedule> {
  const value = await calendarSetMyPersonalScheduleStatus(
    http,
    { scheduleId, body },
    signal
  );
  if (!isPersonalSchedule(value)) {
    throw new Error('client.invalid_personal_schedule');
  }

  return value;
}

export type {
  ChangePersonalScheduleRequest,
  CreatePersonalScheduleRequest,
  PersonalSchedule,
  PersonalSchedulePage,
  PersonalScheduleStatus,
  SetPersonalScheduleStatusRequest,
  UpdatePersonalScheduleRequest
};
