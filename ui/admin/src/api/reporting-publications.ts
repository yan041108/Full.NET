import { isReportingPublicationPage, isReportingPublishedDefinition, isReportingDefinitionTenantGrant,
  type ReportingPublishedDefinition, type ReportingDefinitionTenantGrant, type PagedResult } from '@fullnet/client-contracts';
import { reportingListPublishedDefinitions, reportingListDefinitionTenantGrants, reportingSetDefinitionTenantGrant } from '@fullnet/client-contracts';
import { http } from './http';

export async function listReportingPublishedDefinitions(nameContains = '', signal?: AbortSignal): Promise<PagedResult<ReportingPublishedDefinition>> {
  const value = await reportingListPublishedDefinitions(http, { nameContains, page: 1, pageSize: 20 }, signal);
  if (!isReportingPublicationPage(value, isReportingPublishedDefinition)) throw new Error('client.invalid_reporting_publication_page');
  return value;
}
export async function listReportingDefinitionTenantGrants(id: string, page = 1, signal?: AbortSignal): Promise<PagedResult<ReportingDefinitionTenantGrant>> {
  const value = await reportingListDefinitionTenantGrants(http, { definitionId: id, page, pageSize: 20 }, signal);
  if (!isReportingPublicationPage(value, isReportingDefinitionTenantGrant)) throw new Error('client.invalid_reporting_grant_page');
  return value;
}
export async function setReportingDefinitionTenantGrant(id: string, tenant: string, isEnabled: boolean, expectedVersion: number,
  signal?: AbortSignal): Promise<ReportingDefinitionTenantGrant> {
  const value = await reportingSetDefinitionTenantGrant(http, { definitionId: id, tenantId: tenant, body: { isEnabled, expectedVersion } }, signal);
  if (!isReportingDefinitionTenantGrant(value)) throw new Error('client.invalid_reporting_grant');
  return value;
}
