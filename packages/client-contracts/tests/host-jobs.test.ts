import { describe, expect, it } from 'vitest';
import {
  isChangeHostJobScheduleStateRequest,
  isCreateHostJobDefinitionRequest,
  isCreateHostJobScheduleRequest,
  isDisableHostJobDefinitionRequest,
  isHostJobDefinition,
  isHostJobDefinitionPage,
  isHostJobExecution,
  isHostJobExecutionPage,
  isHostJobSchedule,
  isHostJobScheduleCronPreview,
  isHostJobScheduleDefinitionOption,
  isHostJobScheduleDefinitionOptionList,
  isHostJobSchedulePage,
  isUpdateHostJobDefinitionRequest,
  isUpdateHostJobScheduleRequest
} from '../src/host-jobs';

describe('host-jobs contracts', () => {
  // 中文注释：HostJobDefinition.groupName 对应 C# 端 JobDefinition.GroupName，默认 null 或 'System' 等分组字符串
  const definition = {
    id: '01912345-6789-7abc-8def-0123456789ab',
    jobKey: 'jobs.ping',
    handlerKind: 'ping',
    args: null,
    displayName: '探针任务',
    description: '烟囱验证',
    groupName: 'System',
    isEnabled: true,
    allowConcurrentExecutions: false,
    createdAtUtc: '2026-07-26T00:00:00Z',
    updatedAtUtc: null,
    version: 1
  };

  const execution = {
    id: '01912345-6789-7abc-8def-0123456789ac',
    jobDefinitionId: definition.id,
    jobScheduleId: null,
    status: 'succeeded',
    triggerKind: 'manual',
    scheduledForUtc: null,
    errorMessage: null,
    startedAtUtc: '2026-07-26T00:00:01Z',
    finishedAtUtc: '2026-07-26T00:00:02Z',
    nextAttemptAtUtc: null,
    attemptCount: 1,
    createdAtUtc: '2026-07-26T00:00:00Z'
  };

  it('accepts valid host job payloads', () => {
    expect(isHostJobDefinition(definition)).toBe(true);
    expect(isHostJobDefinitionPage({
      items: [definition],
      page: 1,
      pageSize: 20,
      total: 1
    })).toBe(true);
    expect(isHostJobExecution(execution)).toBe(true);
    expect(isHostJobExecutionPage({
      items: [execution],
      page: 1,
      pageSize: 20,
      total: 1
    })).toBe(true);
    expect(isCreateHostJobDefinitionRequest({
      jobKey: 'jobs.ping',
      handlerKind: 'ping',
      displayName: '探针'
    })).toBe(true);
    expect(isUpdateHostJobDefinitionRequest({
      displayName: '探针',
      handlerKind: 'ping',
      allowConcurrentExecutions: false,
      version: 1
    })).toBe(true);
    expect(isDisableHostJobDefinitionRequest({ version: 1 })).toBe(true);
  });

  it('rejects invalid job keys', () => {
    expect(isHostJobDefinition({ ...definition, id: 'bad' })).toBe(false);
    expect(isHostJobExecution({ ...execution, status: 'unknown' })).toBe(false);
  });

  it('accepts valid host job schedule payloads', () => {
    // 中文注释：HostJobSchedule 新增 5 个字段与 C# JobSchedule 公共契约对齐：
    // numberOfRuns / numberOfErrors 为累计整数统计；startTime / endTime 为调度窗口边界；args 为传入作业的 JSON 字符串
    const schedule = {
      id: '01912345-6789-7abc-8def-0123456789ad',
      jobDefinitionId: definition.id,
      jobDefinitionJobKey: definition.jobKey,
      jobDefinitionDisplayName: definition.displayName,
      triggerKind: 'cron',
      cronExpression: '0 9 * * *',
      timeZoneId: 'UTC',
      oneTimeAtUtc: null,
      misfirePolicy: 'skip',
      isEnabled: true,
      nextExecutionAtUtc: '2026-08-03T09:00:00Z',
      lastExecutionAtUtc: null,
      completedAtUtc: null,
      numberOfRuns: 0,
      numberOfErrors: 0,
      startTime: null,
      endTime: null,
      args: null,
      createdAtUtc: '2026-07-26T00:00:00Z',
      updatedAtUtc: null,
      version: 1
    };

    expect(isHostJobSchedule(schedule)).toBe(true);
    expect(isHostJobSchedulePage({
      items: [schedule],
      page: 1,
      pageSize: 20,
      total: 1
    })).toBe(true);
    expect(isCreateHostJobScheduleRequest({
      jobDefinitionId: definition.id,
      triggerKind: 'cron',
      timeZoneId: 'UTC',
      misfirePolicy: 'skip',
      cronExpression: '0 9 * * *'
    })).toBe(true);
    expect(isUpdateHostJobScheduleRequest({
      triggerKind: 'cron',
      timeZoneId: 'UTC',
      misfirePolicy: 'skip',
      version: 1
    })).toBe(true);
    expect(isChangeHostJobScheduleStateRequest({ version: 1 })).toBe(true);
    expect(isHostJobScheduleDefinitionOption({
      id: definition.id,
      jobKey: definition.jobKey,
      handlerKind: definition.handlerKind,
      displayName: definition.displayName
    })).toBe(true);
    expect(isHostJobScheduleDefinitionOptionList([
      {
        id: definition.id,
        jobKey: definition.jobKey,
        handlerKind: definition.handlerKind,
        displayName: definition.displayName
      }
    ])).toBe(true);
    expect(isHostJobScheduleCronPreview({
      humanDescription: 'jobs.cron.macro.daily',
      nextExecutionAtUtc: '2026-08-03T09:00:00Z',
      nextOccurrencesUtc: ['2026-08-03T09:00:00Z', '2026-08-04T09:00:00Z']
    })).toBe(true);
  });

  it('负向测试：缺少 HostJobDefinition / HostJobSchedule 必填字段时返回 false', () => {
    // 中文注释：缺少 groupName（应为 string|null）时 isHostJobDefinition 返回 false
    expect(isHostJobDefinition({
      id: definition.id,
      jobKey: definition.jobKey,
      displayName: definition.displayName,
      description: definition.description,
      // 注意：未提供 groupName
      isEnabled: definition.isEnabled,
      createdAtUtc: definition.createdAtUtc,
      updatedAtUtc: definition.updatedAtUtc,
      version: definition.version
    })).toBe(false);

    // 中文注释：缺少 numberOfRuns（应为整数）时 isHostJobSchedule 返回 false
    expect(isHostJobSchedule({
      id: '01912345-6789-7abc-8def-0123456789ad',
      jobDefinitionId: definition.id,
      jobDefinitionJobKey: definition.jobKey,
      jobDefinitionDisplayName: definition.displayName,
      triggerKind: 'cron',
      cronExpression: '0 9 * * *',
      timeZoneId: 'UTC',
      oneTimeAtUtc: null,
      misfirePolicy: 'skip',
      isEnabled: true,
      nextExecutionAtUtc: '2026-08-03T09:00:00Z',
      lastExecutionAtUtc: null,
      completedAtUtc: null,
      // 注意：未提供 numberOfRuns
      numberOfErrors: 0,
      startTime: null,
      endTime: null,
      args: null,
      createdAtUtc: '2026-07-26T00:00:00Z',
      updatedAtUtc: null,
      version: 1
    })).toBe(false);

    // 中文注释：缺少 numberOfErrors / startTime / endTime / args 时 isHostJobSchedule 返回 false
    expect(isHostJobSchedule({
      id: '01912345-6789-7abc-8def-0123456789ad',
      jobDefinitionId: definition.id,
      jobDefinitionJobKey: definition.jobKey,
      jobDefinitionDisplayName: definition.displayName,
      triggerKind: 'cron',
      cronExpression: '0 9 * * *',
      timeZoneId: 'UTC',
      oneTimeAtUtc: null,
      misfirePolicy: 'skip',
      isEnabled: true,
      nextExecutionAtUtc: '2026-08-03T09:00:00Z',
      lastExecutionAtUtc: null,
      completedAtUtc: null,
      numberOfRuns: 0
      // 注意：未提供 numberOfErrors / startTime / endTime / args
    } as unknown as Parameters<typeof isHostJobSchedule>[0])).toBe(false);
  });
});
