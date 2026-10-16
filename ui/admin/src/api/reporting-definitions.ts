import {
  reportingListPublishedDefinitions,
  reportingListTenantVersionGrants,
  isReportingTenantVersionGrantPage,
  reportingGrantTenantVersion,
  reportingRevokeTenantVersion,
  isReportingDefinition,
  isReportingPublishedDefinitionList,
  type ReportingPublishedDefinition,
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
import { http, request } from './http';
import { matchesReportingDraft, sameReportingId, validReportingDefinition, validReportingGroup, validReportingPort, validReportingVersion } from './reporting-management-guards';

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
  if (!isReportingGroupList(value) || !value.every(validReportingGroup) || new Set(value.map(row => row.id.toLowerCase())).size !== value.length) {
    throw new Error('client.invalid_reporting_group_list');
  }
  return value;
}

export async function createReportingGroup(
  body: CreateReportingGroupRequest,
  signal?: AbortSignal
): Promise<ReportingGroup> {
  const input = { ...body };
  const value = await request<unknown>('/api/v1/reporting/groups', { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify(input) }, signal);
  if (!isReportingGroup(value) || !validReportingGroup(value) || value.version !== 1 || !sameReportingId(value.parentId, input.parentId)
    || value.name !== input.name.trim() || value.sortOrder !== input.sortOrder || value.isEnabled !== input.isEnabled) {
    throw new Error('client.invalid_reporting_group');
  }
  return value;
}

export async function updateReportingGroup(
  id: string,
  body: UpdateReportingGroupRequest,
  signal?: AbortSignal
): Promise<ReportingGroup> {
  const input = { ...body };
  const value = await request<unknown>(
    `/api/v1/reporting/groups/${encodeURIComponent(id)}`,
    { method: 'PUT', headers: { 'content-type': 'application/json' }, body: JSON.stringify(input) },
    signal
  );
  if (!isReportingGroup(value) || !validReportingGroup(value) || !sameReportingId(value.id, id) || value.version !== input.version + 1
    || !sameReportingId(value.parentId, input.parentId) || value.name !== input.name.trim() || value.sortOrder !== input.sortOrder || value.isEnabled !== input.isEnabled) {
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
  if (typeof value !== 'boolean') throw new Error('client.invalid_reporting_group_delete_result');
  return value;
}

export async function listReportingQueryPorts(signal?: AbortSignal): Promise<ReportingQueryPortDefinition[]> {
  const value = await request<unknown>('/api/v1/reporting/query-ports', { method: 'GET' }, signal);
  if (!isReportingQueryPortList(value) || !value.every(validReportingPort) || new Set(value.map(row => row.queryPortKey)).size !== value.length) {
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
  if (!isReportingQueryPortDefinition(value) || !validReportingPort(value) || value.queryPortKey !== queryPortKey) {
    throw new Error('client.invalid_reporting_query_port');
  }
  return value;
}

export async function listReportingDefinitions(
  query: ReportingDefinitionListQuery = {},
  signal?: AbortSignal
): Promise<ReportingDefinition[]> {
  const input = { ...query };
  const value = await request<unknown>(
    `/api/v1/reporting/definitions${buildDefinitionListQuery(input)}`,
    { method: 'GET' },
    signal
  );
  if (!isReportingDefinitionList(value) || !value.every(validReportingDefinition) || new Set(value.map(row => row.id.toLowerCase())).size !== value.length
    || input.groupId?.trim() && value.some(row => !sameReportingId(row.groupId, input.groupId!.trim()))) {
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
  if (!isReportingDefinition(value) || !validReportingDefinition(value) || !sameReportingId(value.id, id)) {
    throw new Error('client.invalid_reporting_definition');
  }
  return value;
}

export async function createReportingDefinition(
  body: CreateReportingDefinitionRequest,
  signal?: AbortSignal
): Promise<ReportingDefinition> {
  const input = { ...body, parameterSchema: body.parameterSchema.map(row => ({ ...row })) };
  const value = await request<unknown>('/api/v1/reporting/definitions', { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify(input) }, signal);
  if (!isReportingDefinition(value) || !validReportingDefinition(value) || value.version !== 1 || value.latestPublishedVersionNumber !== 0
    || value.definitionKey !== input.definitionKey.trim() || !matchesReportingDraft(value, input)) {
    throw new Error('client.invalid_reporting_definition');
  }
  return value;
}

export async function updateReportingDefinition(
  id: string,
  body: UpdateReportingDefinitionRequest,
  signal?: AbortSignal
): Promise<ReportingDefinition> {
  const input = { ...body, parameterSchema: body.parameterSchema.map(row => ({ ...row })) };
  const value = await request<unknown>(
    `/api/v1/reporting/definitions/${encodeURIComponent(id)}`,
    { method: 'PUT', headers: { 'content-type': 'application/json' }, body: JSON.stringify(input) },
    signal
  );
  if (!isReportingDefinition(value) || !validReportingDefinition(value) || !sameReportingId(value.id, id) || value.version !== input.version + 1 || !matchesReportingDraft(value, input)) {
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
  if (typeof value !== 'boolean') throw new Error('client.invalid_reporting_definition_delete_result');
  return value;
}

export async function publishReportingDefinition(
  id: string,
  body: PublishReportingDefinitionRequest,
  signal?: AbortSignal
): Promise<ReportingDefinitionVersion> {
  const input = { ...body };
  const value = await request<unknown>(
    `/api/v1/reporting/definitions/${encodeURIComponent(id)}/publish`,
    { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify(input) },
    signal
  );
  if (!isReportingDefinitionVersion(value) || !validReportingVersion(value) || !sameReportingId(value.definitionId, id) || value.changeNote !== (input.changeNote?.trim() || null)) {
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
  if (!isReportingDefinitionVersionList(value) || !value.every(validReportingVersion)) {
    throw new Error('client.invalid_reporting_definition_version_list');
  }
  if (value.some(row => row.definitionId.toLowerCase() !== id.toLowerCase() || !Number.isSafeInteger(row.versionNumber) || row.versionNumber < 1)
    || new Set(value.map(row => row.versionNumber)).size !== value.length || new Set(value.map(row => row.id.toLowerCase())).size !== value.length) {
    throw new Error('client.invalid_reporting_definition_version');
  }
  return value;
}

/** 只读取当前作用域可以执行的不可变版本目录。 */
export async function listReportingPublishedDefinitions(signal?: AbortSignal): Promise<ReportingPublishedDefinition[]> {
  const value = await reportingListPublishedDefinitions(http, {}, signal);
  if (!isReportingPublishedDefinitionList(value)) throw new Error('client.invalid_reporting_definition_list');
  return value;
}

/** Host 按租户与版本进行显式授权，参数不会改变当前请求租户。 */
export async function setReportingTenantVersionGrant(definitionId: string, versionNumber: number,
  tenantId: string, grant: boolean, signal?: AbortSignal): Promise<boolean> {
  const parameters = { definitionId, versionNumber, tenantId };
  const value = grant
    ? await reportingGrantTenantVersion(http, parameters, signal)
    : await reportingRevokeTenantVersion(http, parameters, signal);
  if (value !== true) throw new Error('client.invalid_reporting_definition');
  return value;
}

/** 仅供 Host 按精确发布版本分页读取已授权租户。 */
export async function listReportingTenantVersionGrants(definitionId: string, versionNumber: number,
  page = 1, pageSize = 20, signal?: AbortSignal): Promise<import('@fullnet/client-contracts').ReportingTenantVersionGrantPage> {
  const value = await reportingListTenantVersionGrants(http, {definitionId, versionNumber, page, pageSize}, signal);
  if (!isReportingTenantVersionGrantPage(value) || value.page !== page || value.pageSize !== pageSize
    || new Set(value.items.map(id => id.toLowerCase())).size !== value.items.length || value.items.includes('00000000-0000-0000-0000-000000000000')) throw new Error('client.invalid_reporting_tenant_grants');
  return value;
}
