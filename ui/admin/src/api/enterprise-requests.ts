import {
  enterpriseRequestCreateEnterpriseRequest,
  enterpriseRequestDeleteEnterpriseRequest,
  enterpriseRequestGetApprovalProgress,
  enterpriseRequestRepairApproval,
  enterpriseRequestGetEnterpriseRequest,
  enterpriseRequestGetLines,
  enterpriseRequestReplaceLines,
  enterpriseRequestListAttachments,
  enterpriseRequestUploadAttachment,
  enterpriseRequestRemoveAttachment,
  enterpriseRequestDownloadAttachment,
  enterpriseRequestListEnterpriseRequests,
  enterpriseRequestUpdateEnterpriseRequest,
  submitEnterpriseRequestForApproval,
  type CreateEnterpriseRequestRequest,
  type DeleteEnterpriseRequestRequest,
  type EnterpriseRequestResponse,
  type EnterpriseRequestApprovalProgressResponse,
  type RepairEnterpriseRequestApprovalRequest,
  type EnterpriseRequestLinesResponse,
  type ReplaceEnterpriseRequestLinesRequest,
  type EnterpriseRequestAttachmentResponse,
  type HttpClient,
  type UpdateEnterpriseRequestRequest
} from '@fullnet/client-contracts';

export {
  type CreateEnterpriseRequestRequest,
  type DeleteEnterpriseRequestRequest,
  type EnterpriseRequestResponse,
  type EnterpriseRequestApprovalProgressResponse,
  type EnterpriseRequestLineInput,
  type EnterpriseRequestLinesResponse,
  type ReplaceEnterpriseRequestLinesRequest,
  type EnterpriseRequestAttachmentsResponse,
  type EnterpriseRequestAttachmentResponse,
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
  repairApproval: 'enterprise_request.enterprise_requests.repair_approval',
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
    repairApproval: (id: string, body: RepairEnterpriseRequestApprovalRequest, signal?: AbortSignal) =>
      enterpriseRequestRepairApproval(http, { id, body }, signal),
    lines: async (id: string, signal?: AbortSignal) =>
      validateLines(id, await enterpriseRequestGetLines(http, { id }, signal)),
    replaceLines: async (id: string, body: ReplaceEnterpriseRequestLinesRequest, signal?: AbortSignal) => {
      const response = validateLines(id, await enterpriseRequestReplaceLines(http, { id, body }, signal));
      if (response.requestVersion !== body.version + 1 || response.requestStatus !== 'Draft')
        throw new Error('client.invalid_enterprise_request_lines');
      return response;
    },
    attachments: async (id: string, signal?: AbortSignal) => {
      const response = await enterpriseRequestListAttachments(http, { id }, signal);
      const ids = new Set<string>(), files = new Set<string>();
      if (response.requestId !== id || response.requestVersion < 1 || response.items.length > 20)
        throw new Error('client.invalid_enterprise_request_attachments');
      for (const item of response.items) {
        validateAttachment(item);
        if (ids.has(item.id) || files.has(item.fileId)) throw new Error('client.invalid_enterprise_request_attachments');
        ids.add(item.id); files.add(item.fileId);
      }
      return response;
    },
    uploadAttachment: async (id: string, version: number, file: File, signal?: AbortSignal) => {
      const response = await enterpriseRequestUploadAttachment(http, { id, version, file }, signal);
      if (response.requestId !== id || response.requestVersion !== version + 1)
        throw new Error('client.invalid_enterprise_request_attachments');
      validateAttachment(response.attachment);
      return response;
    },
    removeAttachment: async (id: string, attachmentId: string, version: number, signal?: AbortSignal) => {
      const response = await enterpriseRequestRemoveAttachment(http, { id, attachmentId, body: { version } }, signal);
      if (response.requestId !== id || response.requestVersion !== version + 1)
        throw new Error('client.invalid_enterprise_request_attachments');
      return response;
    },
    downloadAttachment: async (id: string, attachmentId: string, signal?: AbortSignal) => {
      const blob = await enterpriseRequestDownloadAttachment(http, { id, attachmentId }, signal);
      if (!(blob instanceof Blob)) throw new Error('client.invalid_enterprise_request_attachment_content');
      return blob;
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

/** 附件描述只允许安全下载文件名与本批声明的有界内容，不接受地址或物理路径。 */
function validateAttachment(item: EnterpriseRequestAttachmentResponse): void {
  if (!item.originalFileName.trim() || item.originalFileName.length > 255 || /[\\/\x00-\x1f\x7f]/.test(item.originalFileName)
    || item.sizeBytes < 1 || item.sizeBytes > 10 * 1024 * 1024 || !Number.isFinite(Date.parse(item.createdAtUtc)))
    throw new Error('client.invalid_enterprise_request_attachments');
}

/** 精确十进制用整数验证行金额与合计，不经浮点计算制造金额误差。 */
function decimalUnits(value: number | string, scale: number): bigint {
  if (String(value).length > 50) throw new Error('client.invalid_enterprise_request_lines');
  const match = /^(\d+)(?:\.(\d+))?$/.exec(String(value));
  const fraction = (match?.[2] ?? '').replace(/0+$/, '');
  if (!match || fraction.length > scale) throw new Error('client.invalid_enterprise_request_lines');
  return BigInt(match[1]! + fraction.padEnd(scale, '0'));
}
function validateLines(id: string, value: EnterpriseRequestLinesResponse): EnterpriseRequestLinesResponse {
  const fail = () => { throw new Error('client.invalid_enterprise_request_lines'); };
  if (value.requestId !== id || value.requestVersion < 1 || value.items.length > 200) fail();
  const ids = new Set<string>(); let total = 0n;
  for (const [index, line] of value.items.entries()) {
    if (line.lineNumber !== index + 1 || ids.has(line.id) || !line.itemDescription.trim() || line.itemDescription.length > 200) fail();
    ids.add(line.id);
    const quantity = decimalUnits(line.quantity, 4), price = decimalUnits(line.unitPrice, 2), amount = decimalUnits(line.lineAmount, 2);
    if (quantity <= 0n || quantity > 999999999999999999n || price > 999999999999999999n ||
      amount > 999999999999999999n || amount !== (quantity * price + 5000n) / 10000n) fail();
    total += amount;
  }
  if (value.items.length && (total > 999999999999999999n || total !== decimalUnits(value.totalAmount, 2))) fail();
  return value;
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
