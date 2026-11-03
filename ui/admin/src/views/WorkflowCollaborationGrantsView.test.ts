import { beforeEach, describe, expect, it, vi } from 'vitest';
import { mount, flushPromises } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import View from './WorkflowCollaborationGrantsView.vue';
import { useSessionStore } from '../auth/session';
import * as api from '../api/workflow-collaboration-grants';
import * as feedback from '../feedback/fullNetMessage';
vi.mock('../api/workflow-collaboration-grants', () => ({ getWorkflowCollaborationGrant: vi.fn(), proposeWorkflowCollaborationGrant: vi.fn(), acceptWorkflowCollaborationGrant: vi.fn(), revokeWorkflowCollaborationGrant: vi.fn() }));
vi.mock('../feedback/fullNetMessage', () => ({ showProblem: vi.fn(), showError: vi.fn(), showSuccess: vi.fn(), showWarning: vi.fn() }));
const id = '0198f36e-f7a7-7c52-9cbb-774e67411203';
const target = '0198f36e-f7a7-7c52-9cbb-774e67411204';
const grant = (): any => ({ id, revision: 1, statusKey: 'proposed', sourceTenantScopeKey: 'host', targetTenantScopeKey: `tenant:${target.replaceAll('-', '')}`, sourceActorId: id, targetActorId: null, offer: { schemaVersion: 1, sourceDefinitionId: id, sourceFormVersionId: id, targetWorkflowKey: 'approval', inputFieldKeys: ['title'], outputFieldKeys: ['title'], maximumTargetActivations: 64, expiresAtUtc: '2030-01-01T00:00:00Z' }, acceptance: null, policyHash: null, targetSummary: null, expiresAtUtc: '2030-01-01T00:00:00Z', createdAtUtc: '2026-01-01T00:00:00Z', acceptedAtUtc: null, revokedAtUtc: null, revokedById: null, revokedByScopeKey: null });
function setup(actions = ['read', 'propose', 'accept', 'revoke'], tenantId: string | null = null) {
  const pinia = createPinia(); setActivePinia(pinia); const session = useSessionStore();
  session.currentUser = { id, username: 'actor', displayName: 'Actor', tenantId, actorScope: tenantId ? 'tenant' : 'host', scope: tenantId ? 'tenant' : 'host', isSuperAdministrator: false, passwordChangeRequired: false, permissions: actions.map(a => `workflow.collaboration_grants.${a}`), sessionId: id, preferredLocale: 'zh-CN', profileVersion: 1 };
  return { wrapper: mount(View, { global: { plugins: [pinia] } }), session };
}
async function load(wrapper: ReturnType<typeof setup>['wrapper']) { await wrapper.get('[data-testid="grant-id"]').setValue(id); await wrapper.get('form').trigger('submit'); await flushPromises(); }
describe('协作授权页', () => {
  beforeEach(() => { vi.resetAllMocks(); vi.mocked(api.getWorkflowCollaborationGrant).mockResolvedValue(grant()); });
  it('hides the page operations without read permission', () => {
    const { wrapper } = setup(['propose', 'accept', 'revoke']); expect(wrapper.find('form').exists()).toBe(false); expect(api.getWorkflowCollaborationGrant).not.toHaveBeenCalled(); wrapper.unmount();
  });
  it('renders exact permissions and keeps source from accepting its own offer', async () => {
    const { wrapper } = setup(['read', 'accept']); await load(wrapper); expect(wrapper.find('[data-testid="grant-accept"]').exists()).toBe(false); expect(wrapper.find('[data-testid="grant-revoke"]').exists()).toBe(false); expect(wrapper.find('[data-testid="grant-propose"]').exists()).toBe(false); wrapper.unmount();
  });
  it('allows target acceptance with structured fields and the original offer', async () => {
    const { wrapper } = setup(['read', 'accept'], target); await load(wrapper);
    await wrapper.get('[data-testid="grant-version"]').setValue(id); await wrapper.get('[data-testid="grant-starter"]').setValue(id); await wrapper.get('[data-testid="grant-members"]').setValue(id);
    vi.mocked(api.acceptWorkflowCollaborationGrant).mockResolvedValue({ ...grant(), revision: 2, statusKey: 'active' });
    await wrapper.get('[data-testid="grant-accept-form"]').trigger('submit'); await flushPromises();
    expect(api.acceptWorkflowCollaborationGrant).toHaveBeenCalledWith(id, expect.any(String), expect.objectContaining({ targetDefinitionVersionId: id, allowedUserIds: [id], inputFields: { title: 'title' }, outputFields: { title: 'title' } }), grant().offer, expect.any(AbortSignal)); wrapper.unmount();
  });
  it('reuses an idempotency key after an uncertain network failure', async () => {
    const { wrapper } = setup(['read', 'revoke']); await load(wrapper); await wrapper.get('[data-testid="grant-reason"]').setValue('停止共享');
    vi.mocked(api.revokeWorkflowCollaborationGrant).mockRejectedValue(new Error('network'));
    const form = wrapper.get('[data-testid="grant-revoke"]').element.closest('form')!;
    form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })); await flushPromises(); form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })); await flushPromises();
    expect(api.revokeWorkflowCollaborationGrant).toHaveBeenCalledTimes(2); expect(vi.mocked(api.revokeWorkflowCollaborationGrant).mock.calls[0][2]).toBe(vi.mocked(api.revokeWorkflowCollaborationGrant).mock.calls[1][2]); wrapper.unmount();
  });
  it('requires a fresh read after a CAS conflict', async () => {
    const { wrapper } = setup(['read', 'revoke']); await load(wrapper); await wrapper.get('[data-testid="grant-reason"]').setValue('竞争');
    vi.mocked(api.revokeWorkflowCollaborationGrant).mockRejectedValue({ status: 409, title: 'Conflict' });
    wrapper.get('[data-testid="grant-revoke"]').element.closest('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })); await flushPromises();
    expect(wrapper.find('[data-testid="grant-revoke"]').exists()).toBe(false); expect(feedback.showProblem).toHaveBeenCalled(); wrapper.unmount();
  });
  it('clears loaded policies and discards an in-flight response on context change', async () => {
    let resolve!: (value: any) => void; vi.mocked(api.getWorkflowCollaborationGrant).mockReturnValue(new Promise(r => { resolve = r; }));
    const { wrapper, session } = setup(); await wrapper.get('[data-testid="grant-id"]').setValue(id); await wrapper.get('form').trigger('submit');
    session.currentUser = { ...session.currentUser!, tenantId: target }; resolve(grant()); await flushPromises();
    expect(wrapper.find('[data-testid="grant-detail"]').exists()).toBe(false); expect(wrapper.get('[data-testid="grant-id"]').element).toHaveProperty('value', ''); wrapper.unmount();
  });
  it('does not display a third-scope grant or permit mutation', async () => {
    const { wrapper } = setup(['read', 'accept', 'revoke'], id); await load(wrapper); expect(wrapper.find('[data-testid="grant-detail"]').exists()).toBe(false); expect(feedback.showWarning).toHaveBeenCalled(); wrapper.unmount();
  });
  it('removes stale policies when the server revokes permission', async () => {
    const { wrapper } = setup(['read', 'revoke']); await load(wrapper); await wrapper.get('[data-testid="grant-reason"]').setValue('停止');
    vi.mocked(api.revokeWorkflowCollaborationGrant).mockRejectedValue({ status: 403, title: 'Denied' });
    wrapper.get('[data-testid="grant-revoke"]').element.closest('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })); await flushPromises();
    expect(wrapper.find('[data-testid="grant-detail"]').exists()).toBe(false); expect(wrapper.find('[data-testid="grant-receipt"]').exists()).toBe(false); wrapper.unmount();
  });
  it('uses authoritative current state after replaying an earlier receipt', async () => {
    const { wrapper } = setup(['read', 'revoke']); await load(wrapper); await wrapper.get('[data-testid="grant-reason"]').setValue('停止');
    vi.mocked(api.revokeWorkflowCollaborationGrant).mockResolvedValue({ ...grant(), statusKey: 'revoked', revision: 2 });
    vi.mocked(api.getWorkflowCollaborationGrant).mockResolvedValue({ ...grant(), statusKey: 'revoked', revision: 3 });
    wrapper.get('[data-testid="grant-revoke"]').element.closest('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })); await flushPromises();
    expect(wrapper.get('[data-testid="grant-detail"]').text()).toContain('3'); expect(wrapper.get('[data-testid="grant-receipt"]').text()).toContain('2');
    expect(wrapper.find('[data-testid="grant-revoke"]').exists()).toBe(false); wrapper.unmount();
  });
});
