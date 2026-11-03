import { mount, flushPromises } from '@vue/test-utils';
import { defineComponent, reactive, ref } from 'vue';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import WorkflowSimulation from './WorkflowSimulation.vue';

const api = vi.hoisted(() => ({ form: vi.fn(), simulate: vi.fn(), metadata: vi.fn(), candidates: vi.fn() }));
const id = '0198f955-899d-7000-8000-000000000001', formId = '0198f955-899d-7000-8000-000000000002';
const permissions = ['workflow.definitions.simulate', 'workflow.definitions.read', 'workflow.forms.read'];
const session = reactive({ switching: false, currentUser: { id, sessionId: id, tenantId: null as string | null, scope: 'host', profileVersion: 1, permissions: [...permissions] }, can(code: string) { return this.currentUser.permissions.includes(code); } });
vi.mock('../auth/session', () => ({ useSessionStore: () => session }));
vi.mock('../i18n/adminI18n', () => ({ useAdminI18n: () => ({ t: (key: string) => key }) }));
vi.mock('../api/workflow-runtime', () => ({ getWorkflowStartForm: api.form }));
vi.mock('../api/workflow-observability', () => ({ simulateWorkflowDefinition: api.simulate,
  getWorkflowSimulationStarterSelections: api.metadata, listWorkflowSimulationStarterSelectionCandidates: api.candidates }));
const drawer = defineComponent({ props: ['modelValue'], template: '<aside v-if="modelValue"><slot/></aside>' });
const renderer = defineComponent({ emits: ['update:patch'], template: '<button data-testid="sample" @click="$emit(\'update:patch\', { amount: 12 })">sample</button>' });
const response = () => ({ definitionId: id, draftRevision: 3, formVersionId: formId, trace: [{ nodeKey: 'approve', nodeTypeKey: 'human.approval', outcomeKey: 'human_input_required', memberCount: 1, errorCode: null, branchKey: null, fixedDefinitionVersionId: null }] });
const wrappers: ReturnType<typeof mount>[] = [];
function create() { const wrapper = mount(WorkflowSimulation, { props: { definitionId: id, draftRevision: 3, formVersionId: formId, draftState: 'saved' }, global: { stubs: { ElDrawer: drawer, WorkflowFormRenderer: renderer } } }); wrappers.push(wrapper); return wrapper; }
async function open(wrapper: ReturnType<typeof create>) { await wrapper.get('[data-testid="workflow-simulation-open"]').trigger('click'); await flushPromises(); }
beforeEach(() => { vi.resetAllMocks(); session.switching = false; session.currentUser.tenantId = null; session.currentUser.permissions = [...permissions]; api.form.mockResolvedValue({ schema: { sections: [{ fields: [{ fieldKey: 'amount', required: true }] }] } }); api.simulate.mockResolvedValue(response());
  api.metadata.mockResolvedValue({ definitionId: id, draftRevision: 3, formVersionId: formId, items: [] });
  api.candidates.mockResolvedValue({ items: [{ id, username: 'sample-user', displayName: 'Sample' }], page: 1, pageSize: 20, total: 1 }); });
afterEach(() => wrappers.splice(0).forEach(wrapper => wrapper.unmount()));
describe('发布前仿真交互', () => {
  it('只在明确操作后读取固定表单和服务器草稿，不调用发布或启动', async () => {
    const wrapper = create(); expect(api.form).not.toHaveBeenCalled(); await open(wrapper);
    expect(api.form.mock.calls[0]![0]).toBe(formId); await wrapper.get('[data-testid="sample"]').trigger('click');
    await wrapper.get('[data-testid="workflow-simulation-run"]').trigger('click'); await flushPromises();
    expect(api.simulate).toHaveBeenCalledWith(id, { expectedRevision: 3, formVersionId: formId, values: { amount: 12 }, assigneeSelections: {} }, expect.any(AbortSignal));
    expect(wrapper.get('[data-testid="workflow-simulation-result"]').text()).toContain('approve');
  });
  it('精确仿真权限缺失时不创建入口', () => { session.currentUser.permissions = ['workflow.definitions.read', 'workflow.forms.read']; const wrapper = create(); expect(wrapper.find('button').exists()).toBe(false); expect(api.form).not.toHaveBeenCalled(); });
  it('合法自选草稿从当前候选选择后提交实际选择值，不借用实例启动权限', async () => {
    api.metadata.mockResolvedValue({ definitionId: id, draftRevision: 3, formVersionId: formId, items: [{ selectionKey: 'reviewChoice', nodeKey: 'claim', nodeName: 'Claim', modeKey: 'single', maximumUsers: 1, rangeKey: 'specified_users' }] });
    const wrapper = create(); await open(wrapper);
    expect(wrapper.get('[data-testid="workflow-simulation-run"]').attributes('disabled')).toBeDefined();
    await wrapper.get(`[data-testid="workflow-starter-candidate-${id}"]`).trigger('click'); await flushPromises();
    await wrapper.get('[data-testid="sample"]').trigger('click');
    await wrapper.get('[data-testid="workflow-simulation-run"]').trigger('click'); await flushPromises();
    expect(api.simulate.mock.calls[0]![1]).toMatchObject({ assigneeSelections: { reviewChoice: [id] } });
    expect(session.can('workflow.instances.start')).toBe(false);
    expect(wrapper.find('[data-testid="workflow-simulation-result"]').exists()).toBe(true);
  });
  it('KeepAlive切离页面后关闭抽屉并取消请求，返回页面也不恢复旧轨迹', async () => {
    let resolve!: (value: ReturnType<typeof response>) => void; api.simulate.mockImplementation(() => new Promise(done => { resolve = done; }));
    const harness = defineComponent({ components: { WorkflowSimulation }, setup: () => ({ active: ref(true), id, formId }),
      template: '<button data-testid="toggle" @click="active = !active">toggle</button><KeepAlive><WorkflowSimulation v-if="active" :definition-id="id" :draft-revision="3" :form-version-id="formId" draft-state="saved"/><div v-else>other</div></KeepAlive>' });
    const wrapper = mount(harness, { global: { stubs: { ElDrawer: drawer, WorkflowFormRenderer: renderer } } }); wrappers.push(wrapper);
    await wrapper.get('[data-testid="workflow-simulation-open"]').trigger('click'); await flushPromises();
    await wrapper.get('[data-testid="workflow-simulation-run"]').trigger('click'); const signal = api.simulate.mock.calls[0]![2] as AbortSignal;
    await wrapper.get('[data-testid="toggle"]').trigger('click'); expect(signal.aborted).toBe(true); resolve(response()); await flushPromises();
    await wrapper.get('[data-testid="toggle"]').trigger('click'); expect(wrapper.find('aside').exists()).toBe(false);
    expect(wrapper.find('[data-testid="workflow-simulation-result"]').exists()).toBe(false);
  });
  it.each(['identity', 'revision', 'sample', 'draft'] as const)('%s变化后取消并丢弃旧求值', async reason => {
    let resolve!: (value: ReturnType<typeof response>) => void; api.simulate.mockImplementation(() => new Promise(done => { resolve = done; }));
    const wrapper = create(); await open(wrapper); await wrapper.get('[data-testid="workflow-simulation-run"]').trigger('click');
    const signal = api.simulate.mock.calls[0]![2] as AbortSignal;
    if (reason === 'identity') session.currentUser.tenantId = formId;
    if (reason === 'revision') await wrapper.setProps({ draftRevision: 4 });
    if (reason === 'draft') await wrapper.setProps({ draftState: 'edited' });
    if (reason === 'sample') await wrapper.get('[data-testid="sample"]').trigger('click');
    await flushPromises(); expect(signal.aborted).toBe(true); resolve(response()); await flushPromises();
    expect(wrapper.find('[data-testid="workflow-simulation-result"]').exists()).toBe(false);
  });
  it('响应绑定不匹配或403时关闭旧投影', async () => {
    const wrapper = create(); await open(wrapper); api.simulate.mockResolvedValue({ ...response(), formVersionId: id });
    await wrapper.get('[data-testid="workflow-simulation-run"]').trigger('click'); await flushPromises();
    expect(wrapper.find('[data-testid="workflow-simulation-result"]').exists()).toBe(false); expect(wrapper.find('[role="alert"]').exists()).toBe(true);
    api.simulate.mockRejectedValue({ status: 403 }); await wrapper.get('[data-testid="workflow-simulation-run"]').trigger('click'); await flushPromises();
    expect(wrapper.find('[data-testid="workflow-simulation-result"]').exists()).toBe(false);
  });
});
