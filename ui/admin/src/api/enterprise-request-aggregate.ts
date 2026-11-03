import {
  enterpriseRequestCreateEnterpriseRequestAggregate,
  enterpriseRequestGetEnterpriseRequestAggregate,
  enterpriseRequestUpdateEnterpriseRequestAggregate,
  type CreateEnterpriseRequestAggregateRequest,
  type UpdateEnterpriseRequestAggregateRequest
} from './enterprise-request-client/index.generated';
import { http } from './http';

/** 应用自有聚合客户端复用统一 HTTP 边界，不把样例 DTO 继续扩散到框架契约。 */
export const getEnterpriseRequestAggregate = (id: string, signal?: AbortSignal) =>
  enterpriseRequestGetEnterpriseRequestAggregate(http, { enterpriseRequestId: id }, signal);
export const createEnterpriseRequestAggregate = (organizationUnitId: string, body: CreateEnterpriseRequestAggregateRequest, signal?: AbortSignal) =>
  enterpriseRequestCreateEnterpriseRequestAggregate(http, { 'X-FullNet-Organization-Unit-Id': organizationUnitId, body }, signal);
export const updateEnterpriseRequestAggregate = (id: string, body: UpdateEnterpriseRequestAggregateRequest, signal?: AbortSignal) =>
  enterpriseRequestUpdateEnterpriseRequestAggregate(http, { enterpriseRequestId: id, body }, signal);
