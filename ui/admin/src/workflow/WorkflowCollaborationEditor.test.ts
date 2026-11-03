import { flushPromises, mount } from '@vue/test-utils';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { WorkflowCollaborationGrant } from '@fullnet/client-contracts';
import WorkflowCollaborationEditor from './WorkflowCollaborationEditor.vue';

const mocks = vi.hoisted(() => ({ get: vi.fn(), warning: vi.fn(), problem: vi.fn() }));
vi.mock('../api/workflow-collaboration-grants', () => ({ getWorkflowCollaborationGrant: mocks.get }));
vi.mock('../feedback/fullNetMessage', () => ({ showWarning: mocks.warning, showProblem: mocks.problem }));
const id = '11111111-1111-4111-8111-111111111111'; const form = '22222222-2222-4222-8222-222222222222';
const grantId = '33333333-3333-4333-8333-333333333333'; const hash = 'a'.repeat(64);
const grant = (): WorkflowCollaborationGrant => ({ id: grantId, revision: 2, statusKey: 'active', sourceTenantScopeKey: 'host', targetTenantScopeKey: 'tenant:' + 'b'.repeat(32),
  sourceActorId: id, targetActorId: form, offer: { schemaVersion: 1, sourceDefinitionId: id, sourceFormVersionId: form, targetWorkflowKey: 'target',
    inputFieldKeys: ['title'], outputFieldKeys: ['answer'], maximumTargetActivations: 256, expiresAtUtc: new Date(Date.now() + 3_600_000).toISOString() },
  acceptance: { schemaVersion: 1, targetDefinitionVersionId: form, starterUserId: id, maximumActivities: 256, allowedUserIds: [id],
    inputFields: { title: 'title' }, outputFields: { title: 'answer' }, targetSummaryHash: hash }, policyHash: hash,
  targetSummary: { definitionVersionId: form, definitionKey: 'target', definitionContentHash: hash, formContentHash: hash, inputFields: { title: 'text' }, outputFields: { title: 'text' } },
  expiresAtUtc: new Date(Date.now() + 3_600_000).toISOString(), createdAtUtc: new Date().toISOString(), acceptedAtUtc: new Date().toISOString(),
  revokedAtUtc: null, revokedById: null, revokedByScopeKey: null });
const props = { definitionId: id, formVersionId: form, canRead: true, disabled: false };
const mounted: ReturnType<typeof mount>[] = [];
function create(overrides = {}) { const wrapper = mount(WorkflowCollaborationEditor, { props: { ...props, ...overrides } }); mounted.push(wrapper); return wrapper; }
async function lookup(wrapper: ReturnType<typeof create>) { await wrapper.get('input').setValue(grantId); await wrapper.get('[data-testid="workflow-collaboration-load"]').trigger('click'); await flushPromises(); }
beforeEach(() => { vi.resetAllMocks(); mocks.get.mockResolvedValue(grant()); });
afterEach(() => { for (const wrapper of mounted.splice(0)) wrapper.unmount(); });

describe('公开协作授权配置', () => {
  it('无读取权限不创建读取和保存操作', () => {
    const wrapper = create({ canRead: false }); expect(wrapper.find('[data-testid="workflow-collaboration-load"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="workflow-collaboration-save"]').exists()).toBe(false); expect(wrapper.get('input').attributes('disabled')).toBeDefined(); expect(mocks.get).not.toHaveBeenCalled();
  });
  it('未选择固定表单时不能读取或保存', async () => {
    const wrapper = create({ formVersionId: '' }); await wrapper.get('input').setValue(grantId);
    expect(wrapper.get('[data-testid="workflow-collaboration-load"]').attributes('disabled')).toBeDefined(); expect(mocks.get).not.toHaveBeenCalled();
  });
  it('读取并在保存前重新核对，只发出四个固定政策字段', async () => {
    const wrapper = create(); await lookup(wrapper); await wrapper.get('[data-testid="workflow-collaboration-save"]').trigger('click'); await flushPromises();
    expect(mocks.get).toHaveBeenCalledTimes(2); expect(wrapper.emitted('save')?.[0]?.[0]).toEqual({ collaborationPolicy: { schemaVersion: 1, grantId, grantRevision: 2, policyHash: hash } });
  });
  it.each(['definition', 'form', 'revoked', 'expired', 'unsigned'])('拒绝当前绑定或授权异常：%s', async defect => {
    const value = grant(); if (defect === 'definition') value.offer.sourceDefinitionId = form;
    if (defect === 'form') value.offer.sourceFormVersionId = id; if (defect === 'revoked') value.statusKey = 'revoked';
    if (defect === 'expired') value.expiresAtUtc = new Date(Date.now() - 1000).toISOString(); if (defect === 'unsigned') value.policyHash = null;
    mocks.get.mockResolvedValue(value); const wrapper = create(); await lookup(wrapper);
    expect(wrapper.get('[data-testid="workflow-collaboration-save"]').attributes('disabled')).toBeDefined(); expect(wrapper.emitted('save')).toBeUndefined(); expect(mocks.warning).toHaveBeenCalledOnce();
  });
  it('保存前撤权不复用旧抽屉许可', async () => {
    const wrapper = create(); await lookup(wrapper); mocks.get.mockResolvedValue({ ...grant(), statusKey: 'revoked', revision: 3 });
    await wrapper.get('[data-testid="workflow-collaboration-save"]').trigger('click'); await flushPromises(); expect(wrapper.emitted('save')).toBeUndefined(); expect(wrapper.find('dl').exists()).toBe(false);
  });
  it('切换源表单取消旧请求，迟到授权不污染新资源', async () => {
    let resolve!: (value: WorkflowCollaborationGrant) => void; mocks.get.mockReturnValue(new Promise<WorkflowCollaborationGrant>(done => { resolve = done; }));
    const wrapper = create(); await wrapper.get('input').setValue(grantId); await wrapper.get('[data-testid="workflow-collaboration-load"]').trigger('click');
    const signal = mocks.get.mock.calls[0]![1] as AbortSignal; await wrapper.setProps({ formVersionId: id }); expect(signal.aborted).toBe(true);
    resolve(grant()); await flushPromises(); expect(wrapper.find('dl').exists()).toBe(false); expect(wrapper.emitted('save')).toBeUndefined();
  });
});
