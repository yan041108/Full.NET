import {
  isHostJobDefinition,
  isHostJobDefinitionPage,
  isHostJobExecution,
  isHostJobExecutionPage,
  isHostJobGroupList,
  JOB_HANDLER_KINDS,
  jobsClearHostJobExecutions,
  jobsCreateHostJobDefinition,
  jobsDeleteHostJobDefinition,
  jobsDisableHostJobDefinition,
  jobsGetHostJobExecution,
  jobsListHostJobDefinitions,
  jobsListHostJobExecutions,
  jobsListHostJobGroups,
  jobsTriggerHostJobDefinition,
  jobsUpdateHostJobDefinition,
  type HostJobDefinition,
  type HostJobDefinitionPage,
  type HostJobExecution,
  type HostJobExecutionListQuery,
  type HostJobExecutionPage,
  type HostJobGroup,
  type HttpJobArgs
} from '@fullnet/client-contracts';
import { http } from './http';

export async function listHostJobDefinitions(
  page = 1,
  pageSize = 20,
  signal?: AbortSignal
): Promise<HostJobDefinitionPage> {
  const value = await jobsListHostJobDefinitions(
    http,
    { page, pageSize },
    signal
  );
  if (!isHostJobDefinitionPage(value)) {
    throw new Error('client.invalid_host_job_definition_page');
  }

  return value;
}

export async function listHostJobGroups(
  signal?: AbortSignal
): Promise<HostJobGroup[]> {
  const value = await jobsListHostJobGroups(http, {}, signal);
  if (!isHostJobGroupList(value)) {
    throw new Error('client.invalid_host_job_group_list');
  }

  return value;
}

export async function createHostJobDefinition(
  jobKey: string,
  handlerKind: string,
  displayName: string,
  args?: HttpJobArgs | null,
  description?: string | null,
  groupName?: string | null,
  allowConcurrentExecutions = false,
  signal?: AbortSignal
): Promise<HostJobDefinition> {
  const value = await jobsCreateHostJobDefinition(
    http,
    {
      body: {
        jobKey,
        handlerKind,
        args: args ?? null,
        displayName,
        description: description ?? null,
        groupName: groupName ?? null,
        allowConcurrentExecutions
      }
    },
    signal
  );
  if (!isHostJobDefinition(value)) {
    throw new Error('client.invalid_host_job_definition');
  }

  return value;
}

export async function updateHostJobDefinition(
  id: string,
  displayName: string,
  description: string | null,
  handlerKind: string,
  args: HttpJobArgs | null,
  version: number,
  groupName?: string | null,
  allowConcurrentExecutions = false,
  signal?: AbortSignal
): Promise<HostJobDefinition> {
  const value = await jobsUpdateHostJobDefinition(
    http,
    {
      definitionId: id,
      body: {
        displayName,
        description,
        handlerKind,
        args,
        groupName: groupName ?? null,
        allowConcurrentExecutions,
        version
      }
    },
    signal
  );
  if (!isHostJobDefinition(value)) {
    throw new Error('client.invalid_host_job_definition');
  }

  return value;
}

export async function disableHostJobDefinition(
  id: string,
  version: number,
  signal?: AbortSignal
): Promise<HostJobDefinition> {
  const value = await jobsDisableHostJobDefinition(
    http,
    {
      definitionId: id,
      body: { version }
    },
    signal
  );
  if (!isHostJobDefinition(value)) {
    throw new Error('client.invalid_host_job_definition');
  }

  return value;
}

export async function deleteHostJobDefinition(
  id: string,
  version: number,
  signal?: AbortSignal
): Promise<void> {
  await jobsDeleteHostJobDefinition(
    http,
    {
      definitionId: id,
      body: { version }
    },
    signal
  );
}

export async function triggerHostJobDefinition(
  id: string,
  signal?: AbortSignal
): Promise<HostJobExecution> {
  const value = await jobsTriggerHostJobDefinition(
    http,
    { definitionId: id },
    signal
  );
  if (!isHostJobExecution(value)) {
    throw new Error('client.invalid_host_job_execution');
  }

  return value;
}

export async function listHostJobExecutions(
  query: HostJobExecutionListQuery = {},
  signal?: AbortSignal
): Promise<HostJobExecutionPage> {
  const value = await jobsListHostJobExecutions(
    http,
    {
      page: query.page ?? 1,
      pageSize: query.pageSize ?? 20,
      jobDefinitionId: query.jobDefinitionId,
      jobScheduleId: query.jobScheduleId,
      status: query.status,
      fromUtc: query.fromUtc,
      toUtc: query.toUtc
    },
    signal
  );
  if (!isHostJobExecutionPage(value)) {
    throw new Error('client.invalid_host_job_execution_page');
  }

  return value;
}

export async function getHostJobExecution(
  id: string,
  signal?: AbortSignal
): Promise<HostJobExecution> {
  const value = await jobsGetHostJobExecution(
    http,
    { executionId: id },
    signal
  );
  if (!isHostJobExecution(value)) {
    throw new Error('client.invalid_host_job_execution');
  }

  return value;
}

export async function clearHostJobExecutions(
  jobDefinitionId: string,
  signal?: AbortSignal
): Promise<void> {
  await jobsClearHostJobExecutions(
    http,
    { jobDefinitionId },
    signal
  );
}

export { JOB_HANDLER_KINDS };
export type {
  HostJobDefinition,
  HostJobDefinitionPage,
  HostJobExecution,
  HostJobExecutionListQuery,
  HostJobExecutionPage,
  HostJobGroup,
  HttpJobArgs
};
