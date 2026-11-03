import { beforeEach, describe, expect, it, vi } from 'vitest';
import { http } from './http';
import { getWorkflowCollaborationGrant, proposeWorkflowCollaborationGrant, acceptWorkflowCollaborationGrant, revokeWorkflowCollaborationGrant } from './workflow-collaboration-grants';
vi.mock('./http', () => ({ http: { request: vi.fn() } }));
const id = '0198f36e-f7a7-7c52-9cbb-774e67411203';
const offer = { schemaVersion: 1 as const, sourceDefinitionId: id, sourceFormVersionId: id, targetWorkflowKey: 'approval', inputFieldKeys: ['title'], outputFieldKeys: ['title'], maximumTargetActivations: 64, expiresAtUtc: '2030-01-01T00:00:00Z' };
const proposed = { id, revision: 1, statusKey: 'proposed', sourceTenantScopeKey: 'host', targetTenantScopeKey: `tenant:${id.replaceAll('-', '')}`, sourceActorId: id, targetActorId: null, offer, acceptance: null, policyHash: null, targetSummary: null, expiresAtUtc: offer.expiresAtUtc, createdAtUtc: '2026-01-01T00:00:00Z', acceptedAtUtc: null, revokedAtUtc: null, revokedById: null, revokedByScopeKey: null };
beforeEach(() => vi.clearAllMocks());
describe('协作授权API', () => {
  it('uses generated endpoints and derives the target scope without changing authentication context', async () => {
    vi.mocked(http.request).mockResolvedValue(proposed);
    await proposeWorkflowCollaborationGrant(id, 'proposal', offer);
    expect(http.request).toHaveBeenCalledWith('/api/v1/workflow/collaboration-grants', expect.objectContaining({ method: 'POST', body: JSON.stringify({ targetTenantId: id, targetScopeKey: 'tenant', idempotencyKey: 'proposal', offer }) }), undefined);
    await getWorkflowCollaborationGrant(id);
    expect(http.request).toHaveBeenLastCalledWith(`/api/v1/workflow/collaboration-grants/${id}`, expect.objectContaining({ method: 'GET' }), undefined);
  });
  it('rejects wrong resource responses and invalid revisions before sending', async () => {
    vi.mocked(http.request).mockResolvedValue({ ...proposed, id: '0198f36e-f7a7-7c52-9cbb-774e67411204' });
    await expect(getWorkflowCollaborationGrant(id)).rejects.toThrow(); vi.clearAllMocks();
    await expect(revokeWorkflowCollaborationGrant(id, 3, 'revoke', '停止')).rejects.toThrow();
    await expect(revokeWorkflowCollaborationGrant(id, 1, 'revoke', ' ')).rejects.toThrow(); expect(http.request).not.toHaveBeenCalled();
  });
  it('rejects caller-supplied summaries and expanded mappings before sending', async () => {
    const acceptance = { schemaVersion: 1 as const, targetDefinitionVersionId: id, starterUserId: id, allowedUserIds: [id], maximumActivities: 32, inputFields: { secret: 'title' }, outputFields: { title: 'title' } };
    await expect(acceptWorkflowCollaborationGrant(id, 'accept', acceptance, offer)).rejects.toThrow(); expect(http.request).not.toHaveBeenCalled();
  });
});
