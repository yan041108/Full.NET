import {
  enterpriseRequestCreateEnterpriseRequest,
  enterpriseRequestDeleteEnterpriseRequest,
  enterpriseRequestGetApprovalProgress,
  enterpriseRequestGetEnterpriseRequest,
  enterpriseRequestListEnterpriseRequests,
  enterpriseRequestUpdateEnterpriseRequest,
  submitEnterpriseRequestForApproval,
  type CreateEnterpriseRequestRequest,
  type DeleteEnterpriseRequestRequest,
  type EnterpriseRequestResponse,
  type EnterpriseRequestApprovalProgressResponse,
  type HttpClient,
  type UpdateEnterpriseRequestRequest
} from '@fullnet/client-contracts';

export {
  type CreateEnterpriseRequestRequest,
  type DeleteEnterpriseRequestRequest,
  type EnterpriseRequestResponse,
  type EnterpriseRequestApprovalProgressResponse,
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
    get: async (id: string, signal?: AbortSignal) => {
      const response = await enterpriseRequestGetEnterpriseRequest(http, { enterpriseRequestId: id }, signal);
      if (response.id !== id) throw new Error('client.invalid_enterprise_request_response');
      return response;
    },
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
    approvalProgress: async (id: string, signal?: AbortSignal) => {
      const response = await enterpriseRequestGetApprovalProgress(http, { id }, signal);
      if (response.requestId !== id || !['not_submitted', 'queued', 'started', 'finalized', 'recovery_required'].includes(response.deliveryState))
        throw new Error('client.invalid_enterprise_request_approval_progress');
      if (!isConsistentApprovalProgress(response)) throw new Error('client.invalid_enterprise_request_approval_progress');
      return response;
    },
    submitForApproval: (id: string, signal?: AbortSignal) =>
      submitEnterpriseRequestForApproval(http, { id }, signal).then(response => {
        if (response.id !== id) {
          throw new Error('client.invalid_enterprise_request_response');
        }
        return response;
      })
  };
}

/** 生成守卫验证线格式；此处再绑定业务阶段，避免显示互相矛盾的身份、版本和时间。 */
function isConsistentApprovalProgress(value: EnterpriseRequestApprovalProgressResponse): boolean {
  const terminal = ['Approved', 'Rejected', 'Cancelled'].includes(value.requestStatus);
  if (value.requestVersion < 1 || [value.submittedAtUtc, value.startedAtUtc, value.completedAtUtc]
    .some(time => time !== null && !Number.isFinite(Date.parse(time)))) return false;
  if (value.deliveryState === 'not_submitted' || value.deliveryState === 'recovery_required')
    return (value.deliveryState === 'not_submitted' ? value.requestStatus === 'Draft' : value.requestStatus === 'Submitted' || terminal)
      && value.workflowInstanceId === null && value.workflowDefinitionVersionId === null && value.submittedVersion === null
      && value.submittedAtUtc === null && value.startedAtUtc === null && value.completedAtUtc === null;
  if (!value.workflowInstanceId || !value.workflowDefinitionVersionId || value.submittedVersion === null || value.submittedVersion < 1 || value.submittedAtUtc === null)
    return false;
  if (value.deliveryState === 'finalized')
    return terminal && value.requestVersion === value.submittedVersion + 1 && value.completedAtUtc !== null;
  return value.requestStatus === 'Submitted' && value.requestVersion === value.submittedVersion && value.completedAtUtc === null
    && (value.deliveryState === 'queued' ? value.startedAtUtc === null : value.startedAtUtc !== null);
}
