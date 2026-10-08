import {
  enterpriseRequestCreateEnterpriseRequest,
  enterpriseRequestDeleteEnterpriseRequest,
  enterpriseRequestListEnterpriseRequests,
  enterpriseRequestUpdateEnterpriseRequest,
  readEnterpriseRequestResponse,
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
/** 创建时选择机构上下文；服务端仍校验当前用户的机构权限。 */
export type CreateEnterpriseRequestInput = CreateEnterpriseRequestRequest & { organizationUnitId: string };
export { http as enterpriseRequestsHttp } from './http';

export const enterpriseRequestPermissions = {
  read: 'enterprise_request.enterprise_requests.read',
  create: 'enterprise_request.enterprise_requests.create',
  update: 'enterprise_request.enterprise_requests.update',
  submit: 'enterprise_request.enterprise_requests.submit',
  disable: 'enterprise_request.enterprise_requests.disable',
  write: 'enterprise_request.enterprise_requests.update'
} as const;

export function createEnterpriseRequestsApi(
  http: GeneratedRequest
) {
  return {
    list: (page = 1, pageSize = 20, signal?: AbortSignal) =>
      enterpriseRequestListEnterpriseRequests(http, { page, pageSize }, signal),
    create: (input: CreateEnterpriseRequestInput, signal?: AbortSignal) =>
      enterpriseRequestCreateEnterpriseRequest(http, { body: {
        requestNumber: input.requestNumber,
        title: input.title,
        status: input.status,
        totalAmount: input.totalAmount,
        applicantUserId: input.applicantUserId
      } }, signal, { headers: { 'X-FullNet-Organization-Unit-Id': input.organizationUnitId } }),
    update: (id: string, input: UpdateEnterpriseRequestRequest, signal?: AbortSignal) =>
      enterpriseRequestUpdateEnterpriseRequest(
        http,
        { enterpriseRequestId: id, body: input }, signal
      ),
    delete: (id: string, input: DeleteEnterpriseRequestRequest, signal?: AbortSignal) =>
      enterpriseRequestDeleteEnterpriseRequest(
        http,
        { enterpriseRequestId: id, body: input }, signal
      ),
    submitForApproval: (id: string, signal?: AbortSignal) =>
      http.request<unknown>(
        `/api/v1/enterprise_request/enterprise-requests/${encodeURIComponent(id)}/submit-for-approval`,
        { method: 'POST', signal }
      ).then(value => {
        const response = readEnterpriseRequestResponse(value);
        if (response.id !== id) {
          throw new Error('client.invalid_enterprise_request_response');
        }
        return response;
      })
  };
}
