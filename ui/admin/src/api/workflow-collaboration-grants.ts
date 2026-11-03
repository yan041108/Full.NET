import { workflowGetCollaborationGrant, workflowProposeCollaborationGrant, workflowAcceptCollaborationGrant, workflowRevokeCollaborationGrant,
  isWorkflowCollaborationId, readWorkflowCollaborationGrant, readWorkflowCollaborationOffer, readWorkflowCollaborationAcceptance,
  type WorkflowCollaborationOffer, type WorkflowCollaborationAcceptance } from '@fullnet/client-contracts';
import { http } from './http';
const invalid = (): never => { throw new TypeError('client.invalid_workflow_collaboration_grant'); };
function id(value: string): void { if (!isWorkflowCollaborationId(value)) invalid(); }
function key(value: string): void { if (!/^[A-Za-z0-9_:.-]{1,128}$/u.test(value)) invalid(); }
export async function getWorkflowCollaborationGrant(grantId: string, signal?: AbortSignal) {
  id(grantId); return readWorkflowCollaborationGrant(await workflowGetCollaborationGrant(http, { grantId }, signal), grantId);
}
export async function proposeWorkflowCollaborationGrant(targetTenantId: string | null, idempotencyKey: string, offer: WorkflowCollaborationOffer, signal?: AbortSignal) {
  if (targetTenantId !== null) id(targetTenantId); key(idempotencyKey); readWorkflowCollaborationOffer(offer);
  return readWorkflowCollaborationGrant(await workflowProposeCollaborationGrant(http, { body: { targetTenantId, targetScopeKey: targetTenantId === null ? 'host' : 'tenant', idempotencyKey, offer: { ...offer } } }, signal));
}
export async function acceptWorkflowCollaborationGrant(grantId: string, idempotencyKey: string, acceptance: WorkflowCollaborationAcceptance, offer: WorkflowCollaborationOffer, signal?: AbortSignal) {
  id(grantId); key(idempotencyKey); readWorkflowCollaborationAcceptance(acceptance, offer);
  return readWorkflowCollaborationGrant(await workflowAcceptCollaborationGrant(http, { grantId, body: { expectedRevision: 1, idempotencyKey, acceptance: { ...acceptance } } }, signal), grantId);
}
export async function revokeWorkflowCollaborationGrant(grantId: string, expectedRevision: number, idempotencyKey: string, reason: string, signal?: AbortSignal) {
  id(grantId); key(idempotencyKey); if (![1, 2].includes(expectedRevision) || !reason.trim() || reason.trim().length > 512) invalid();
  return readWorkflowCollaborationGrant(await workflowRevokeCollaborationGrant(http, { grantId, body: { expectedRevision, idempotencyKey, reason: reason.trim() } }, signal), grantId);
}
