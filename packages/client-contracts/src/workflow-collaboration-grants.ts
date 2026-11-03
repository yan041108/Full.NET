/** 双方同意的固定政策；跨租户地址只描述目标，不改变客户端当前认证作用域。 */
export interface WorkflowCollaborationOffer {
  schemaVersion: 1; sourceDefinitionId: string; sourceFormVersionId: string; targetWorkflowKey: string;
  inputFieldKeys: string[]; outputFieldKeys: string[]; maximumTargetActivations: number; expiresAtUtc: string;
}
export interface WorkflowCollaborationAcceptance {
  schemaVersion: 1; targetDefinitionVersionId: string; starterUserId: string; maximumActivities: number;
  allowedUserIds: string[]; inputFields: Record<string, string>; outputFields: Record<string, string>;
}
export interface WorkflowCollaborationGrant {
  id: string; revision: number; statusKey: 'proposed' | 'active' | 'revoked'; sourceTenantScopeKey: string; targetTenantScopeKey: string;
  sourceActorId: string; targetActorId: string | null; offer: WorkflowCollaborationOffer;
  acceptance: (WorkflowCollaborationAcceptance & { targetSummaryHash: string }) | null;
  policyHash: string | null; targetSummary: { definitionVersionId: string; definitionKey: string; definitionContentHash: string;
    formContentHash: string; inputFields: Record<string, string>; outputFields: Record<string, string> } | null;
  expiresAtUtc: string; createdAtUtc: string; acceptedAtUtc: string | null; revokedAtUtc: string | null;
  revokedById: string | null; revokedByScopeKey: string | null;
}
const object = (v: unknown): v is Record<string, unknown> => typeof v === 'object' && v !== null && !Array.isArray(v);
export const isWorkflowCollaborationId = (v: unknown): v is string => typeof v === 'string' && /^(?!00000000-0000-0000-0000-000000000000$)[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}$/iu.test(v);
const key = (v: unknown): v is string => typeof v === 'string' && /^[A-Za-z][A-Za-z0-9_.-]{0,127}$/u.test(v);
const hash = (v: unknown): v is string => typeof v === 'string' && /^[0-9a-f]{64}$/u.test(v);
const instant = (v: unknown): v is string => typeof v === 'string' && Number.isFinite(Date.parse(v));
const scope = (v: unknown): v is string => v === 'host' || typeof v === 'string' && /^tenant:[0-9a-f]{32}$/u.test(v);
const budget = (v: unknown, max = 4096): v is number => Number.isSafeInteger(v) && Number(v) >= 1 && Number(v) <= max;
const invalid = (): never => { throw new TypeError('client.invalid_workflow_collaboration_grant'); };
const field = (v: unknown): v is string => key(v) && v.length <= 64 && !['constructor', 'prototype', '__proto__'].includes(v);
const fields = (v: unknown): v is string[] => Array.isArray(v) && v.length <= 64 && v.every(field) && new Set(v).size === v.length;
const mapping = (v: unknown): v is Record<string, string> => object(v) && Object.keys(v).length <= 64 && Object.entries(v).every(([k, value]) => field(k) && field(value)) && new Set(Object.values(v)).size === Object.values(v).length;
export function readWorkflowCollaborationOffer(v: unknown): WorkflowCollaborationOffer {
  if (!object(v) || Object.keys(v).length !== 8 || v.schemaVersion !== 1 || !isWorkflowCollaborationId(v.sourceDefinitionId) ||
    !isWorkflowCollaborationId(v.sourceFormVersionId) || !key(v.targetWorkflowKey) || !fields(v.inputFieldKeys) || !fields(v.outputFieldKeys) ||
    !budget(v.maximumTargetActivations) || !instant(v.expiresAtUtc)) return invalid();
  return v as unknown as WorkflowCollaborationOffer;
}
export function readWorkflowCollaborationAcceptance(v: unknown, offer: WorkflowCollaborationOffer, confirmed = false): WorkflowCollaborationAcceptance {
  if (!object(v) || Object.keys(v).length !== (confirmed ? 8 : 7) || !confirmed && 'targetSummaryHash' in v || v.schemaVersion !== 1 || !isWorkflowCollaborationId(v.targetDefinitionVersionId) || !isWorkflowCollaborationId(v.starterUserId) ||
    !budget(v.maximumActivities, offer.maximumTargetActivations) || !Array.isArray(v.allowedUserIds) || v.allowedUserIds.length < 1 || v.allowedUserIds.length > 100 ||
    !v.allowedUserIds.every(isWorkflowCollaborationId) || new Set(v.allowedUserIds.map(id => id.toLowerCase())).size !== v.allowedUserIds.length ||
    !v.allowedUserIds.some(id => id.toLowerCase() === String(v.starterUserId).toLowerCase()) || !mapping(v.inputFields) || !mapping(v.outputFields) ||
    Object.keys(v.inputFields).some(k => !offer.inputFieldKeys.includes(k)) || Object.values(v.outputFields).some(k => !offer.outputFieldKeys.includes(k))) return invalid();
  return v as unknown as WorkflowCollaborationAcceptance;
}
/** 线格式先验证状态与政策身份，不能将未知JSON直接提升为授权事实。 */
export function readWorkflowCollaborationGrant(v: unknown, expectedId?: string): WorkflowCollaborationGrant {
  if (!object(v) || !isWorkflowCollaborationId(v.id) || expectedId && v.id.toLowerCase() !== expectedId.toLowerCase() ||
    !scope(v.sourceTenantScopeKey) || !scope(v.targetTenantScopeKey) || v.sourceTenantScopeKey === v.targetTenantScopeKey ||
    !isWorkflowCollaborationId(v.sourceActorId) || !instant(v.createdAtUtc) || !instant(v.expiresAtUtc) ||
    !['proposed', 'active', 'revoked'].includes(String(v.statusKey))) return invalid();
  const offer = readWorkflowCollaborationOffer(v.offer);
  if (Date.parse(offer.expiresAtUtc) !== Date.parse(v.expiresAtUtc)) return invalid();
  if (v.acceptance === null) {
    if (v.targetActorId !== null || v.policyHash !== null || v.targetSummary !== null || v.acceptedAtUtc !== null ||
      !(v.statusKey === 'proposed' && v.revision === 1 || v.statusKey === 'revoked' && v.revision === 2)) return invalid();
  } else {
    const acceptance = readWorkflowCollaborationAcceptance(v.acceptance, offer, true);
    if (!object(v.acceptance) || !hash(v.acceptance.targetSummaryHash) || !isWorkflowCollaborationId(v.targetActorId) || !hash(v.policyHash) ||
      !instant(v.acceptedAtUtc) || !object(v.targetSummary) || v.targetSummary.definitionVersionId !== acceptance.targetDefinitionVersionId ||
      v.targetSummary.definitionKey !== offer.targetWorkflowKey || !hash(v.targetSummary.definitionContentHash) || !hash(v.targetSummary.formContentHash) ||
      !object(v.targetSummary.inputFields) || !object(v.targetSummary.outputFields) ||
      ![...Object.values(v.targetSummary.inputFields), ...Object.values(v.targetSummary.outputFields)].every(key) ||
      !(v.statusKey === 'active' && v.revision === 2 || v.statusKey === 'revoked' && v.revision === 3)) return invalid();
  }
  if (v.statusKey === 'revoked' ? !instant(v.revokedAtUtc) || !isWorkflowCollaborationId(v.revokedById) ||
    typeof v.revokedByScopeKey !== 'string' || ![v.sourceTenantScopeKey, v.targetTenantScopeKey].includes(v.revokedByScopeKey) : v.revokedAtUtc !== null || v.revokedById !== null || v.revokedByScopeKey !== null) return invalid();
  return v as unknown as WorkflowCollaborationGrant;
}
