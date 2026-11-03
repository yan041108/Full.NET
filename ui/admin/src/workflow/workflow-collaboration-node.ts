import { isWorkflowCollaborationId } from '@fullnet/client-contracts';

export interface WorkflowCollaborationConfiguration {
  collaborationPolicy: { schemaVersion: 1; grantId: string; grantRevision: 2; policyHash: string };
}
/** 设计器仅保存固定授权引用；不能携带目标租户、成员或投影正文来替代双方政策。 */
export function readWorkflowCollaboration(value: unknown): WorkflowCollaborationConfiguration {
  if (typeof value !== 'object' || value === null || !('collaborationPolicy' in value)) throw new TypeError('client.invalid_workflow_collaboration');
  const policy = value.collaborationPolicy;
  if (typeof policy !== 'object' || policy === null || Array.isArray(policy) || Object.keys(policy).length !== 4 ||
    Object.keys(policy).some(key => !['schemaVersion', 'grantId', 'grantRevision', 'policyHash'].includes(key)) ||
    !('schemaVersion' in policy) || policy.schemaVersion !== 1 || !('grantRevision' in policy) || policy.grantRevision !== 2 ||
    !('grantId' in policy) || !isWorkflowCollaborationId(policy.grantId) || !('policyHash' in policy) ||
    typeof policy.policyHash !== 'string' || !/^[0-9a-f]{64}$/u.test(policy.policyHash)) throw new TypeError('client.invalid_workflow_collaboration');
  return { collaborationPolicy: { schemaVersion: 1, grantId: policy.grantId, grantRevision: 2, policyHash: policy.policyHash } };
}
