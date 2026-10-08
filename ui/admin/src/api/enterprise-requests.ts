import {
  enterpriseRequestCreateEnterpriseRequest,
  enterpriseRequestDeleteEnterpriseRequest,
  enterpriseRequestGetApprovalProgress,
  enterpriseRequestGetEnterpriseRequest,
  enterpriseRequestGetLines,
  enterpriseRequestReplaceLines,
  enterpriseRequestListEnterpriseRequests,
  enterpriseRequestUpdateEnterpriseRequest,
  submitEnterpriseRequestForApproval,
  type CreateEnterpriseRequestRequest,
  type DeleteEnterpriseRequestRequest,
  type EnterpriseRequestResponse,
  type EnterpriseRequestApprovalProgressResponse,
  type EnterpriseRequestLinesResponse,
  type ReplaceEnterpriseRequestLinesRequest,
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
    lines: async (id: string, signal?: AbortSignal) =>
      validateLines(id, await enterpriseRequestGetLines(http, { id }, signal)),
    replaceLines: async (id: string, body: ReplaceEnterpriseRequestLinesRequest, signal?: AbortSignal) => {
      const response = validateLines(id, await enterpriseRequestReplaceLines(http, { id, body }, signal));
      if (response.requestVersion !== body.version + 1 || response.requestStatus !== 'Draft')
        throw new Error('client.invalid_enterprise_request_lines');
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
