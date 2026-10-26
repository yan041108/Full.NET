/** 领取池的身份和修订只由服务端返回；候选不能在请求中覆盖办理人或租户。 */
export interface WorkflowClaimPool {
  readonly id: string; readonly instanceId: string; readonly nodeKey: string; readonly definitionKey: string;
  readonly businessType: string; readonly businessId: string; readonly businessTitle: string | null;
  readonly revision: number; readonly claimCycle: number; readonly createdAtUtc: string; readonly dueAtUtc: string | null;
}
export interface WorkflowClaimProgress {
  readonly poolStatusKey: 'available' | 'claimed' | 'completed' | 'cancelled'; readonly poolRevision: number;
  readonly claimCycle: number; readonly allowRelease: boolean;
}
export interface WorkflowClaimRequest { readonly expectedRevision: number; readonly idempotencyKey: string }
export interface WorkflowReleaseRequest {
  readonly expectedRevision: number; readonly expectedPoolRevision: number; readonly expectedFormRevision: number;
  readonly idempotencyKey: string; readonly comment?: string | null; readonly signatureFileId?: string;
}
const guid = /^(?!00000000-0000-0000-0000-000000000000$)[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/iu;
const record = (value: unknown): value is Record<string, unknown> => typeof value === 'object' && value !== null && !Array.isArray(value);
const positive = (value: unknown): value is number => Number.isSafeInteger(value) && Number(value) >= 1;
const date = (value: unknown): value is string => typeof value === 'string' && Number.isFinite(Date.parse(value));
const key = (value: unknown): value is string => typeof value === 'string' && value.trim().length > 0 && value.length <= 128;
const invalid = (): never => { throw new TypeError('workflow.claim.invalid-contract'); };

export function isWorkflowClaimProgress(value: unknown): value is WorkflowClaimProgress {
  return record(value) && ['available', 'claimed', 'completed', 'cancelled'].includes(String(value.poolStatusKey)) &&
    positive(value.poolRevision) && Number.isInteger(value.claimCycle) && Number(value.claimCycle) >= 0 && Number(value.claimCycle) <= 32 &&
    (value.poolStatusKey !== 'claimed' && value.poolStatusKey !== 'completed' || Number(value.claimCycle) >= 1) &&
    (value.poolStatusKey !== 'available' || Number(value.claimCycle) < 32) && typeof value.allowRelease === 'boolean' &&
    (!value.allowRelease || Number(value.claimCycle) < 32);
}

export function readWorkflowClaimPoolPage(value: unknown): { items: readonly WorkflowClaimPool[]; totalCount: number; page: number; pageSize: number } {
  if (!record(value) || !Array.isArray(value.items) || !positive(value.page) || value.page > 100_000 ||
    !positive(value.pageSize) || value.pageSize > 100 || value.items.length > value.pageSize ||
    !Number.isSafeInteger(value.totalCount) || Number(value.totalCount) < value.items.length ||
    value.items.some(item => !record(item) || typeof item.id !== 'string' || !guid.test(item.id) ||
      typeof item.instanceId !== 'string' || !guid.test(item.instanceId) ||
      !['nodeKey', 'definitionKey', 'businessType', 'businessId'].every(name => typeof item[name] === 'string' && String(item[name]).length > 0) ||
      item.businessTitle !== null && typeof item.businessTitle !== 'string' || !positive(item.revision) ||
      !Number.isInteger(item.claimCycle) || Number(item.claimCycle) < 0 || Number(item.claimCycle) >= 32 ||
      !date(item.createdAtUtc) || item.dueAtUtc !== null && !date(item.dueAtUtc))) return invalid();
  return value as unknown as { items: readonly WorkflowClaimPool[]; totalCount: number; page: number; pageSize: number };
}

export function readWorkflowClaimRequest(value: unknown): WorkflowClaimRequest {
  if (!record(value) || Object.keys(value).some(name => !['expectedRevision', 'idempotencyKey'].includes(name)) ||
    !positive(value.expectedRevision) || !key(value.idempotencyKey)) return invalid();
  return value as unknown as WorkflowClaimRequest;
}

export function readWorkflowReleaseRequest(value: unknown): WorkflowReleaseRequest {
  if (!record(value) || Object.keys(value).some(name => !['expectedRevision', 'expectedPoolRevision', 'expectedFormRevision', 'idempotencyKey', 'comment', 'signatureFileId'].includes(name)) ||
    !positive(value.expectedRevision) || !positive(value.expectedPoolRevision) || !positive(value.expectedFormRevision) || !key(value.idempotencyKey) ||
    value.comment != null && (typeof value.comment !== 'string' || value.comment.length > 512) ||
    value.signatureFileId !== undefined && (typeof value.signatureFileId !== 'string' || !guid.test(value.signatureFileId))) return invalid();
  return value as unknown as WorkflowReleaseRequest;
}
