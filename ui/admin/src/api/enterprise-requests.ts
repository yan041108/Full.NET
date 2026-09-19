import {
  enterpriseRequestCreateEnterpriseRequest,
  enterpriseRequestDeleteEnterpriseRequest,
  enterpriseRequestListEnterpriseRequests,
  enterpriseRequestUpdateEnterpriseRequest,
  type CreateEnterpriseRequestRequest,
  type DeleteEnterpriseRequestRequest,
  type EnterpriseRequestResponse,
  type HttpClient,
  type UpdateEnterpriseRequestRequest
} from '@fullnet/client-contracts';

export {
  type CreateEnterpriseRequestRequest,
  type DeleteEnterpriseRequestRequest,
  type EnterpriseRequestResponse,
  type UpdateEnterpriseRequestRequest
} from '@fullnet/client-contracts';

export type GeneratedRequest = HttpClient;
export { http as enterpriseRequestsHttp } from './http';

export const enterpriseRequestPermissions = {
  read: 'enterprise_request.enterprise_requests.read',
  create: 'enterprise_request.enterprise_requests.create',
  update: 'enterprise_request.enterprise_requests.update',
  disable: 'enterprise_request.enterprise_requests.disable',
  write: 'enterprise_request.enterprise_requests.update'
} as const;

export function createEnterpriseRequestsApi(
  http: GeneratedRequest
) {
  return {
    list: (page = 1, pageSize = 20) =>
      enterpriseRequestListEnterpriseRequests(http, { page, pageSize }),
    create: (input: CreateEnterpriseRequestRequest) =>
      enterpriseRequestCreateEnterpriseRequest(http, { body: input }),
    update: (id: string, input: UpdateEnterpriseRequestRequest) =>
      enterpriseRequestUpdateEnterpriseRequest(
        http,
        { enterpriseRequestId: id, body: input }
      ),
    delete: (id: string, input: DeleteEnterpriseRequestRequest) =>
      enterpriseRequestDeleteEnterpriseRequest(
        http,
        { enterpriseRequestId: id, body: input }
      ),
    submitForApproval: (id: string) =>
      http.request<unknown>(
        `/api/v1/enterprise_request/enterprise-requests/${encodeURIComponent(id)}/submit-for-approval`,
        { method: 'POST' }
      ).then(value => {
        if (!value || typeof value !== 'object' || typeof (value as { id?: unknown }).id !== 'string') {
          throw new Error('client.invalid_enterprise_request_response');
        }
        return value as EnterpriseRequestResponse;
      })
  };
}
