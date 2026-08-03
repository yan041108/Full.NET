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
  const definition = {
    id: '01912345-6789-7abc-8def-0123456789ab',
    jobKey: 'jobs.ping',
    displayName: '探针任务',
    description: '烟囱验证',
    isEnabled: true,
    createdAtUtc: '2026-07-26T00:00:00Z',
    updatedAtUtc: null,
    version: 1
  };

  const execution = {
    id: '01912345-6789-7abc-8def-0123456789ac',
    jobDefinitionId: definition.id,
    status: 'succeeded',
    triggerKind: 'manual',
    errorMessage: null,
    startedAtUtc: '2026-07-26T00:00:01Z',
    finishedAtUtc: '2026-07-26T00:00:02Z',
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
      displayName: '探针'
    })).toBe(true);
    expect(isUpdateHostJobDefinitionRequest({
      displayName: '探针',
      version: 1
    })).toBe(true);
    expect(isDisableHostJobDefinitionRequest({ version: 1 })).toBe(true);
  });

  it('rejects invalid job keys', () => {
    expect(isHostJobDefinition({ ...definition, id: 'bad' })).toBe(false);
    expect(isHostJobExecution({ ...execution, status: 'unknown' })).toBe(false);
  });

  it('accepts valid host job schedule payloads', () => {
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
      displayName: definition.displayName
    })).toBe(true);
    expect(isHostJobScheduleDefinitionOptionList([
      {
        id: definition.id,
        jobKey: definition.jobKey,
        displayName: definition.displayName
      }
    ])).toBe(true);
    expect(isHostJobScheduleCronPreview({
      nextExecutionAtUtc: '2026-08-03T09:00:00Z'
    })).toBe(true);
  });
});
