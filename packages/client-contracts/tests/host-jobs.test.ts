import { describe, expect, it } from 'vitest';
import {
  isCreateHostJobDefinitionRequest,
  isDisableHostJobDefinitionRequest,
  isHostJobDefinition,
  isHostJobDefinitionPage,
  isHostJobExecution,
  isHostJobExecutionPage,
  isUpdateHostJobDefinitionRequest
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
});
