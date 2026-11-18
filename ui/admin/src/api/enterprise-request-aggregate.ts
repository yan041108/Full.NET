import {
  enterpriseRequestCreateEnterpriseRequestAggregate,
  enterpriseRequestGetEnterpriseRequestAggregate,
  enterpriseRequestUpdateEnterpriseRequestAggregate,
  type CreateEnterpriseRequestAggregateRequest,
  type UpdateEnterpriseRequestAggregateRequest
} from './enterprise-request-client/index.generated';
import { http } from './http';
import type { SavedQueryConfiguration } from '@fullnet/client-contracts';

/** 应用自有聚合客户端复用统一 HTTP 边界，不把样例 DTO 继续扩散到框架契约。 */
export const getEnterpriseRequestAggregate = (id: string, signal?: AbortSignal) =>
  enterpriseRequestGetEnterpriseRequestAggregate(http, { enterpriseRequestId: id }, signal);
export const createEnterpriseRequestAggregate = (organizationUnitId: string, body: CreateEnterpriseRequestAggregateRequest, signal?: AbortSignal) =>
  enterpriseRequestCreateEnterpriseRequestAggregate(http, { 'X-FullNet-Organization-Unit-Id': organizationUnitId, body }, signal);
export const updateEnterpriseRequestAggregate = (id: string, body: UpdateEnterpriseRequestAggregateRequest, signal?: AbortSignal) =>
  enterpriseRequestUpdateEnterpriseRequestAggregate(http, { enterpriseRequestId: id, body }, signal);

/** 仅为本业务列表编码受控查询，不能让视图拼接业务接口地址或扩展允许的筛选字段。 */
export function enterpriseRequestQueryPath(path: string, method: string, current: () => SavedQueryConfiguration): string {
  const listPath = '/api/v1/enterprise_request/enterprise-requests';
  if (method.toUpperCase() !== 'GET' || path.split('?')[0]!.replace(/\/$/, '') !== listPath) return path;
  const configuration = current(); const parameters = new URLSearchParams(path.split('?')[1] ?? '');
  if (!['id', 'requestNumber', 'title', 'status'].includes(configuration.sortBy)) throw new TypeError('client.invalid_query_scheme');
  parameters.set('sortBy', configuration.sortBy); parameters.set('descending', String(configuration.descending));
  const fields = new Map([['requestNumber', 'filterRequestNumber'], ['title', 'filterTitle'], ['status', 'filterStatus']]);
  for (const filter of configuration.filters) {
    const parameter = fields.get(filter.fieldKey); if (!parameter || filter.operatorKey !== 'contains') throw new TypeError('client.invalid_query_scheme');
    parameters.set(parameter, filter.value);
  }
  return listPath + '?' + parameters;
}
