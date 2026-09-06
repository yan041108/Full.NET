import {
  isReportingDefinition,
  isReportingDefinitionList,
  isReportingDefinitionVersion,
  isReportingDefinitionVersionList,
  isReportingGroup,
  isReportingGroupList,
  isReportingQueryPortDefinition,
  isReportingQueryPortList,
  type CreateReportingDefinitionRequest,
  type CreateReportingGroupRequest,
  type PublishReportingDefinitionRequest,
  type ReportingDefinition,
  type ReportingDefinitionListQuery,
  type ReportingDefinitionVersion,
  type ReportingGroup,
  type ReportingQueryPortDefinition,
  type UpdateReportingDefinitionRequest,
  type UpdateReportingGroupRequest
} from '@fullnet/client-contracts';
import { request } from './http';

function buildDefinitionListQuery(query: ReportingDefinitionListQuery): string {
  const params = new URLSearchParams();
  if (query.groupId?.trim()) {
    params.set('groupId', query.groupId.trim());
  }
  if (query.nameContains?.trim()) {
    params.set('nameContains', query.nameContains.trim());
  }
  const serialized = params.toString();
  return serialized ? `?${serialized}` : '';
}

export async function listReportingGroups(signal?: AbortSignal): Promise<ReportingGroup[]> {
  const value = await request<unknown>('/api/v1/reporting/groups', { method: 'GET' }, signal);
  if (!isReportingGroupList(value)) {
    throw new Error('client.invalid_reporting_group_list');
  }
  return value;
}

export async function createReportingGroup(
  body: CreateReportingGroupRequest,
  signal?: AbortSignal
): Promise<ReportingGroup> {
  const value = await request<unknown>('/api/v1/reporting/groups', { method: 'POST', body }, signal);
  if (!isReportingGroup(value)) {
    throw new Error('client.invalid_reporting_group');
  }
  return value;
}

export async function updateReportingGroup(
  id: string,
  body: UpdateReportingGroupRequest,
  signal?: AbortSignal
): Promise<ReportingGroup> {
  const value = await request<unknown>(
    `/api/v1/reporting/groups/${encodeURIComponent(id)}`,
    { method: 'PUT', body },
    signal
  );
  if (!isReportingGroup(value)) {
    throw new Error('client.invalid_reporting_group');
  }
  return value;
}

export async function deleteReportingGroup(id: string, signal?: AbortSignal): Promise<boolean> {
  const value = await request<unknown>(
    `/api/v1/reporting/groups/${encodeURIComponent(id)}`,
    { method: 'DELETE' },
    signal
  );
  return value === true;
}

export async function listReportingQueryPorts(signal?: AbortSignal): Promise<ReportingQueryPortDefinition[]> {
  const value = await request<unknown>('/api/v1/reporting/query-ports', { method: 'GET' }, signal);
  if (!isReportingQueryPortList(value)) {
    throw new Error('client.invalid_reporting_query_port_list');
  }
  return value;
}

export async function getReportingQueryPort(
  queryPortKey: string,
  signal?: AbortSignal
): Promise<ReportingQueryPortDefinition> {
  const value = await request<unknown>(
    `/api/v1/reporting/query-ports/${encodeURIComponent(queryPortKey)}`,
    { method: 'GET' },
    signal
  );
  if (!isReportingQueryPortDefinition(value)) {
    throw new Error('client.invalid_reporting_query_port');
  }
  return value;
}

export async function listReportingDefinitions(
  query: ReportingDefinitionListQuery = {},
  signal?: AbortSignal
): Promise<ReportingDefinition[]> {
  const value = await request<unknown>(
    `/api/v1/reporting/definitions${buildDefinitionListQuery(query)}`,
    { method: 'GET' },
    signal
  );
  if (!isReportingDefinitionList(value)) {
    throw new Error('client.invalid_reporting_definition_list');
  }
  return value;
}

export async function getReportingDefinition(
  id: string,
  signal?: AbortSignal
): Promise<ReportingDefinition> {
  const value = await request<unknown>(
    `/api/v1/reporting/definitions/${encodeURIComponent(id)}`,
    { method: 'GET' },
    signal
  );
  if (!isReportingDefinition(value)) {
    throw new Error('client.invalid_reporting_definition');
  }
  return value;
}

export async function createReportingDefinition(
  body: CreateReportingDefinitionRequest,
  signal?: AbortSignal
): Promise<ReportingDefinition> {
  const value = await request<unknown>('/api/v1/reporting/definitions', { method: 'POST', body }, signal);
  if (!isReportingDefinition(value)) {
    throw new Error('client.invalid_reporting_definition');
  }
  return value;
}

export async function updateReportingDefinition(
  id: string,
  body: UpdateReportingDefinitionRequest,
  signal?: AbortSignal
): Promise<ReportingDefinition> {
  const value = await request<unknown>(
    `/api/v1/reporting/definitions/${encodeURIComponent(id)}`,
    { method: 'PUT', body },
    signal
  );
  if (!isReportingDefinition(value)) {
    throw new Error('client.invalid_reporting_definition');
  }
  return value;
}

export async function deleteReportingDefinition(id: string, signal?: AbortSignal): Promise<boolean> {
  const value = await request<unknown>(
    `/api/v1/reporting/definitions/${encodeURIComponent(id)}`,
    { method: 'DELETE' },
    signal
  );
  return value === true;
}

export async function publishReportingDefinition(
  id: string,
  body: PublishReportingDefinitionRequest,
  signal?: AbortSignal
): Promise<ReportingDefinitionVersion> {
  const value = await request<unknown>(
    `/api/v1/reporting/definitions/${encodeURIComponent(id)}/publish`,
    { method: 'POST', body },
    signal
  );
  if (!isReportingDefinitionVersion(value)) {
    throw new Error('client.invalid_reporting_definition_version');
  }
  return value;
}

export async function listReportingDefinitionVersions(
  id: string,
  signal?: AbortSignal
): Promise<ReportingDefinitionVersion[]> {
  const value = await request<unknown>(
    `/api/v1/reporting/definitions/${encodeURIComponent(id)}/versions`,
    { method: 'GET' },
    signal
  );
  if (!isReportingDefinitionVersionList(value)) {
    throw new Error('client.invalid_reporting_definition_version_list');
  }
  return value;
}
