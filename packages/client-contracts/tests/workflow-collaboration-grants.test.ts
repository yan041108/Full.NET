import { describe, expect, it } from 'vitest';
import { readWorkflowCollaborationGrant, readWorkflowCollaborationOffer, readWorkflowCollaborationAcceptance } from '../src/workflow-collaboration-grants';
const id = '0198f36e-f7a7-7c52-9cbb-774e67411203';
const offer = { schemaVersion: 1, sourceDefinitionId: id, sourceFormVersionId: id, targetWorkflowKey: 'approval', inputFieldKeys: ['title'], outputFieldKeys: ['title'], maximumTargetActivations: 64, expiresAtUtc: '2030-01-02T00:00:00Z' };
const acceptance = { schemaVersion: 1, targetDefinitionVersionId: id, starterUserId: id, maximumActivities: 32, allowedUserIds: [id], inputFields: { title: 'title' }, outputFields: { title: 'title' } };
const proposed = () => ({ id, revision: 1, statusKey: 'proposed', sourceTenantScopeKey: 'host', targetTenantScopeKey: `tenant:${id.replaceAll('-', '')}`, sourceActorId: id, targetActorId: null, offer, acceptance: null, policyHash: null, targetSummary: null, expiresAtUtc: offer.expiresAtUtc, createdAtUtc: '2030-01-01T00:00:00Z', acceptedAtUtc: null, revokedAtUtc: null, revokedById: null, revokedByScopeKey: null });
describe('双边协作授权契约', () => {
  it('reads proposed and revoked-before-acceptance receipts without inventing target consent', () => {
    expect(readWorkflowCollaborationGrant(proposed()).revision).toBe(1);
    expect(readWorkflowCollaborationGrant({ ...proposed(), revision: 2, statusKey: 'revoked', revokedAtUtc: '2030-01-01T01:00:00Z', revokedById: id, revokedByScopeKey: 'host' }).statusKey).toBe('revoked');
  });
  it('rejects foreign identity, malformed scope and inconsistent state', () => {
    for (const change of [{ id: 'bad' }, { targetTenantScopeKey: 'tenant:bad' }, { statusKey: 'active' }, { revision: 3 }, { revokedById: id }]) expect(() => readWorkflowCollaborationGrant({ ...proposed(), ...change })).toThrow();
  });
  it('rejects expanded fields, duplicate recipients and budgets outside the offer', () => {
    const policy = readWorkflowCollaborationOffer(offer);
    for (const change of [{ inputFields: { secret: 'title' } }, { outputFields: { title: 'secret' } }, { allowedUserIds: [id, id.toUpperCase()] }, { maximumActivities: 65 }, { targetSummaryHash: 'a'.repeat(64) }]) expect(() => readWorkflowCollaborationAcceptance({ ...acceptance, ...change }, policy)).toThrow();
  });
  it('rejects duplicate proposed fields and caller-supplied policy extensions', () => {
    expect(() => readWorkflowCollaborationOffer({ ...offer, inputFieldKeys: ['title', 'title'] })).toThrow();
    expect(() => readWorkflowCollaborationOffer({ ...offer, actorUserId: id })).toThrow();
  });
  it('binds accepted state to the fixed target version and summary', () => {
    const active = { ...proposed(), revision: 2, statusKey: 'active', targetActorId: id, acceptedAtUtc: '2030-01-01T01:00:00Z',
      acceptance: { ...acceptance, targetSummaryHash: 'a'.repeat(64) }, policyHash: 'b'.repeat(64), targetSummary: { definitionVersionId: id, definitionKey: 'approval', definitionContentHash: 'c'.repeat(64), formContentHash: 'd'.repeat(64), inputFields: { title: 'text' }, outputFields: { title: 'text' } } };
    expect(readWorkflowCollaborationGrant(active).statusKey).toBe('active');
    expect(() => readWorkflowCollaborationGrant({ ...active, targetSummary: { ...active.targetSummary, definitionVersionId: '00000000-0000-0000-0000-000000000000' } })).toThrow();
  });
});
