import {
  enterpriseRequestCreateEnterpriseRequest,
  enterpriseRequestGetApprovalProgress,
  enterpriseRequestRepairApproval,
  enterpriseRequestDeleteEnterpriseRequest,
  enterpriseRequestListEnterpriseRequests,
  enterpriseRequestGetEnterpriseRequest,
  enterpriseRequestUpdateEnterpriseRequest,
  readEnterpriseRequestResponse,
  type EnterpriseRequestApprovalProgressResponse,
  type RepairEnterpriseRequestApprovalRequest,
  type CreateEnterpriseRequestRequest,
  type DeleteEnterpriseRequestRequest,
  type EnterpriseRequestResponse,
  type HttpClient,
  type UpdateEnterpriseRequestRequest
} from '@fullnet/client-contracts';

export {
  type EnterpriseRequestApprovalProgressResponse,
  type RepairEnterpriseRequestApprovalRequest,
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
  disable: 'enterprise_request.enterprise_requests.disable',
  submit: 'enterprise_request.enterprise_requests.submit',
  repairApproval: 'enterprise_request.enterprise_requests.repair_approval',
  write: 'enterprise_request.enterprise_requests.update'
} as const;

export function createEnterpriseRequestsApi(
  http: GeneratedRequest,
  getSignal?: () => AbortSignal
) {
  return {
    list: (page = 1, pageSize = 20, signal?: AbortSignal) =>
      enterpriseRequestListEnterpriseRequests(http, { page, pageSize }, signal ?? getSignal?.()),
    get: (id: string, signal?: AbortSignal) => enterpriseRequestGetEnterpriseRequest(http, { enterpriseRequestId: id }, signal ?? getSignal?.()),
    create: (input: CreateEnterpriseRequestInput, signal?: AbortSignal) =>
      enterpriseRequestCreateEnterpriseRequest(http, { body: {
        requestNumber: input.requestNumber,
        title: input.title,
        status: input.status,
        totalAmount: input.totalAmount,
        applicantUserId: input.applicantUserId
      } }, signal ?? getSignal?.(), { headers: { 'X-FullNet-Organization-Unit-Id': input.organizationUnitId } }),
    update: (id: string, input: UpdateEnterpriseRequestRequest, signal?: AbortSignal) =>
      enterpriseRequestUpdateEnterpriseRequest(
        http,
        { enterpriseRequestId: id, body: input }, signal ?? getSignal?.()
      ),
    delete: (id: string, input: DeleteEnterpriseRequestRequest, signal?: AbortSignal) =>
      enterpriseRequestDeleteEnterpriseRequest(
        http,
        { enterpriseRequestId: id, body: input }, signal ?? getSignal?.()
      ),
    approvalProgress: async (id: string, signal?: AbortSignal) => {
      const response = await enterpriseRequestGetApprovalProgress(http, { id }, signal);
      if (response.requestId !== id || !['not_submitted', 'queued', 'started', 'finalized', 'recovery_required'].includes(response.deliveryState))
        throw new Error('client.invalid_enterprise_request_approval_progress');
      if (!isConsistentApprovalProgress(response)) throw new Error('client.invalid_enterprise_request_approval_progress');
      return response;
    },
    repairApproval: (id: string, body: RepairEnterpriseRequestApprovalRequest, signal?: AbortSignal) =>
      enterpriseRequestRepairApproval(http, { id, body }, signal),
    submitForApproval: (id: string, signal?: AbortSignal) =>
      http.request<unknown>(
        `/api/v1/enterprise_request/enterprise-requests/${encodeURIComponent(id)}/submit-for-approval`,
        { method: 'POST' }, signal ?? getSignal?.()
      ).then(readEnterpriseRequestResponse)
  };
}

/** 生成守卫验证线格式；此处再绑定业务阶段，避免显示互相矛盾的身份、版本和时间。 */
function isConsistentApprovalProgress(value: EnterpriseRequestApprovalProgressResponse): boolean {
  if (!isConsistentFinalNotification(value)) return false;
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

/** 通知受理与外发状态只附着于已回写终态；统计不能把未知类别或矛盾计数视作发送成功。 */
function isConsistentFinalNotification(value: EnterpriseRequestApprovalProgressResponse): boolean {
  const notification = value.finalNotification;
  if (notification === undefined || notification === null) return true;
  if (value.deliveryState !== 'finalized' || notification.intentId === '00000000-0000-0000-0000-000000000000'
    || !Number.isFinite(Date.parse(notification.acceptedAtUtc))
    || (notification.nextAttemptAtUtc !== null && !Number.isFinite(Date.parse(notification.nextAttemptAtUtc)))) return false;
  const counts = [notification.totalDeliveryCount, notification.pendingDeliveryCount, notification.sentDeliveryCount,
    notification.failedDeliveryCount, notification.deadLetteredDeliveryCount, notification.unknownDeliveryCount, notification.otherDeliveryCount,
    notification.persistedDeliveryCount ?? 0, notification.deliveredDeliveryCount ?? 0,
    notification.readDeliveryCount ?? 0, notification.suppressedDeliveryCount ?? 0];
  return counts.every(count => Number.isSafeInteger(count) && count >= 0 && count <= 2147483647)
    && counts.slice(1).reduce((total, count) => total + count, 0) === notification.totalDeliveryCount
    && (notification.pendingDeliveryCount > 0 || notification.nextAttemptAtUtc === null);
}
