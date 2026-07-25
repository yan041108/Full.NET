import { request } from './http';
import {
  isHostJobDefinition,
  isHostJobDefinitionPage,
  isHostJobExecution,
  isHostJobExecutionPage,
  type HostJobDefinition,
  type HostJobDefinitionPage,
  type HostJobExecution,
  type HostJobExecutionPage
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

export async function createHostJobDefinition(
  jobKey: string,
  displayName: string,
  description?: string
): Promise<HostJobDefinition> {
  const value = await request<unknown>('/api/v1/jobs/host-definitions', {
    method: 'POST',
    body: { jobKey, displayName, description: description ?? null }
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
  version: number
): Promise<HostJobDefinition> {
  const value = await request<unknown>(
    `/api/v1/jobs/host-definitions/${encodeURIComponent(id)}`,
    {
      method: 'PUT',
      body: { displayName, description, version }
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
      body: { version }
    }
  );
  if (!isHostJobDefinition(value)) {
    throw new Error('Invalid host job definition payload.');
  }
  return value;
}

export async function triggerHostJobDefinition(
  id: string
): Promise<HostJobExecution> {
  const value = await request<unknown>(
    `/api/v1/jobs/host-definitions/${encodeURIComponent(id)}/trigger`,
    { method: 'POST', body: {} }
  );
  if (!isHostJobExecution(value)) {
    throw new Error('Invalid host job execution payload.');
  }
  return value;
}

export async function listHostJobExecutions(
  jobDefinitionId?: string,
  page = 1,
  pageSize = 20
): Promise<HostJobExecutionPage> {
  const query = jobDefinitionId
    ? `&jobDefinitionId=${encodeURIComponent(jobDefinitionId)}`
    : '';
  const value = await request<unknown>(
    `/api/v1/jobs/host-executions?page=${page}&pageSize=${pageSize}${query}`
  );
  if (!isHostJobExecutionPage(value)) {
    throw new Error('Invalid host job execution page response');
  }
  return value;
}
