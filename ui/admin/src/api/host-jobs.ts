import { request } from './http';
import {
  isHostJobDefinition,
  isHostJobDefinitionPage,
  isHostJobExecution,
  isHostJobExecutionPage,
  isHostJobGroupList,
  type HostJobDefinition,
  type HostJobDefinitionPage,
  type HostJobExecution,
  type HostJobExecutionListQuery,
  type HostJobExecutionPage,
  type HostJobGroup
} from '@fullnet/client-contracts';

export async function listHostJobDefinitions(
  page = 1,
  pageSize = 20
): Promise<HostJobDefinitionPage> {
  const value = await request<unknown>(
    `/api/v1/jobs/host-definitions?page=${page}&pageSize=${pageSize}`
  );
  if (!isHostJobDefinitionPage(value)) {
    throw new Error('Invalid host job definition page response');
  }
  return value;
}

export async function listHostJobGroups(): Promise<HostJobGroup[]> {
  const value = await request<unknown>('/api/v1/jobs/host-definitions/groups');
  if (!isHostJobGroupList(value)) {
    throw new Error('Invalid host job group list response');
  }
  return value;
}

export async function createHostJobDefinition(
  jobKey: string,
  displayName: string,
  description?: string | null,
  groupName?: string | null,
  allowConcurrentExecutions = false
): Promise<HostJobDefinition> {
  const value = await request<unknown>('/api/v1/jobs/host-definitions', {
    method: 'POST',
    body: JSON.stringify({
      jobKey,
      displayName,
      description: description ?? null,
      groupName: groupName ?? null,
      allowConcurrentExecutions
    })
  });
  if (!isHostJobDefinition(value)) {
    throw new Error('Invalid host job definition payload.');
  }
  return value;
}

export async function updateHostJobDefinition(
  id: string,
  displayName: string,
  description: string | null,
  version: number,
  groupName?: string | null,
  allowConcurrentExecutions = false
): Promise<HostJobDefinition> {
  const value = await request<unknown>(
    `/api/v1/jobs/host-definitions/${encodeURIComponent(id)}`,
    {
      method: 'PUT',
      body: JSON.stringify({
        displayName,
        description,
        groupName: groupName ?? null,
        allowConcurrentExecutions,
        version
      })
    }
  );
  if (!isHostJobDefinition(value)) {
    throw new Error('Invalid host job definition payload.');
  }
  return value;
}

export async function disableHostJobDefinition(
  id: string,
  version: number
): Promise<HostJobDefinition> {
  const value = await request<unknown>(
    `/api/v1/jobs/host-definitions/${encodeURIComponent(id)}/disable`,
    {
      method: 'POST',
      body: JSON.stringify({ version })
    }
  );
  if (!isHostJobDefinition(value)) {
    throw new Error('Invalid host job definition payload.');
  }
  return value;
}

export async function deleteHostJobDefinition(
  id: string,
  version: number
): Promise<void> {
  await request<unknown>(
    `/api/v1/jobs/host-definitions/${encodeURIComponent(id)}/delete`,
    {
      method: 'POST',
      body: JSON.stringify({ version })
    }
  );
}

export async function triggerHostJobDefinition(
  id: string
): Promise<HostJobExecution> {
  const value = await request<unknown>(
    `/api/v1/jobs/host-definitions/${encodeURIComponent(id)}/trigger`,
    { method: 'POST' }
  );
  if (!isHostJobExecution(value)) {
    throw new Error('Invalid host job execution payload.');
  }
  return value;
}

export async function listHostJobExecutions(
  query: HostJobExecutionListQuery = {}
): Promise<HostJobExecutionPage> {
  const params = new URLSearchParams();
  params.set('page', String(query.page ?? 1));
  params.set('pageSize', String(query.pageSize ?? 20));
  if (query.jobDefinitionId) {
    params.set('jobDefinitionId', query.jobDefinitionId);
  }
  if (query.jobScheduleId) {
    params.set('jobScheduleId', query.jobScheduleId);
  }
  if (query.status) {
    params.set('status', query.status);
  }
  if (query.fromUtc) {
    params.set('fromUtc', query.fromUtc);
  }
  if (query.toUtc) {
    params.set('toUtc', query.toUtc);
  }
  const value = await request<unknown>(
    `/api/v1/jobs/host-executions?${params.toString()}`
  );
  if (!isHostJobExecutionPage(value)) {
    throw new Error('Invalid host job execution page response');
  }
  return value;
}

export async function getHostJobExecution(id: string): Promise<HostJobExecution> {
  const value = await request<unknown>(
    `/api/v1/jobs/host-executions/${encodeURIComponent(id)}`
  );
  if (!isHostJobExecution(value)) {
    throw new Error('Invalid host job execution payload.');
  }
  return value;
}

export async function clearHostJobExecutions(
  jobDefinitionId: string
): Promise<void> {
  await request<unknown>(
    `/api/v1/jobs/host-executions/clear?jobDefinitionId=${encodeURIComponent(jobDefinitionId)}`,
    { method: 'POST' }
  );
}
