import { mount, flushPromises } from '@vue/test-utils';
import { reactive, defineComponent } from 'vue';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import WorkflowVersionCompare from './WorkflowVersionCompare.vue';
import { comparisonDraft, comparisonForm, comparisonIds as ids, comparisonVersion } from './workflow-version-comparison.fixtures';

const api = vi.hoisted(() => ({ definition: vi.fn(), form: vi.fn() }));
const user = () => ({ id: ids.definition, sessionId: ids.before, tenantId: null as string | null, scope: 'host', profileVersion: 1,
  permissions: ['workflow.definitions.read', 'workflow.forms.read'] });
const session = reactive({ currentUser: user() as ReturnType<typeof user> | undefined, switching: false,
  can(permission: string): boolean { return this.currentUser?.permissions.includes(permission) ?? false; } });
vi.mock('../auth/session', () => ({ useSessionStore: () => session }));
vi.mock('../i18n/adminI18n', () => ({ useAdminI18n: () => ({ t: (key: string) => key }) }));
vi.mock('../api/workflow-definitions', () => ({ getWorkflowDefinitionVersion: api.definition }));
vi.mock('../api/workflow-runtime', () => ({ getWorkflowStartForm: api.form }));
const drawer = defineComponent({ name: 'ElDrawer', props: ['modelValue'], emits: ['update:modelValue'], template: '<aside v-if="modelValue"><slot/></aside>' });
const wrappers: ReturnType<typeof mount>[] = [];
function create() {
  const wrapper = mount(WorkflowVersionCompare, { props: { definitionId: ids.definition,
    versions: [comparisonVersion(ids.before), comparisonVersion(ids.after)] }, global: { stubs: { ElDrawer: drawer } } });
  wrappers.push(wrapper); return wrapper;
}
async function start(wrapper: ReturnType<typeof create>) {
  await wrapper.get('[data-testid="workflow-version-compare-open"]').trigger('click');
  await wrapper.get('[data-testid="workflow-version-compare-run"]').trigger('click');
}
beforeEach(() => {
  vi.resetAllMocks(); session.currentUser = user(); session.switching = false;
  api.definition.mockImplementation(async (id: string) => comparisonVersion(id));
  api.form.mockImplementation(async (id: string) => comparisonForm(id));
});
afterEach(() => { wrappers.splice(0).forEach(wrapper => wrapper.unmount()); });

describe('固定版本对比读取与隔离', () => {
  it('显式点击后重读两个定义版本并去重固定表单，不依赖列表内的旧JSON', async () => {
    const wrapper = create(); expect(api.definition).not.toHaveBeenCalled();
    await start(wrapper); await flushPromises();
    expect(api.definition.mock.calls.map(call => call[0])).toEqual([ids.before, ids.after]);
    expect(api.form).toHaveBeenCalledTimes(1);
    expect(wrapper.get('[data-testid="workflow-version-compare-result"]').text()).toContain('workflowVersionCompare.empty');
  });
  it('没有表单读取权限不能发起读取', async () => {
    session.currentUser!.permissions = ['workflow.definitions.read']; const wrapper = create();
    expect(wrapper.find('[data-testid="workflow-version-compare-open"]').exists()).toBe(false);
    expect(wrapper.text()).toContain('workflowVersionCompare.permission'); expect(api.definition).not.toHaveBeenCalled();
  });
  it.each(['identity', 'permission', 'resource'] as const)('失去%s上下文后撤下结果并忽略迟到响应', async reason => {
    let resolve!: (value: ReturnType<typeof comparisonVersion>) => void;
    api.definition.mockImplementation((id: string) => id === ids.before
      ? new Promise(done => { resolve = done; }) : Promise.resolve(comparisonVersion(id)));
    const wrapper = create(); await start(wrapper);
    const signal = api.definition.mock.calls[0]![1] as AbortSignal;
    if (reason === 'identity') session.currentUser = { ...user(), tenantId: ids.after, scope: 'tenant' };
    if (reason === 'permission') session.currentUser!.permissions = ['workflow.definitions.read'];
    if (reason === 'resource') await wrapper.setProps({ definitionId: ids.after, versions: [] });
    await flushPromises(); expect(signal.aborted).toBe(true);
    resolve(comparisonVersion(ids.before)); await flushPromises();
    expect(api.form).not.toHaveBeenCalled(); expect(wrapper.find('[data-testid="workflow-version-compare-result"]').exists()).toBe(false);
  });
  it('切换选择和关闭抽屉会清除已读投影', async () => {
    const wrapper = create(); await start(wrapper); await flushPromises();
    expect(wrapper.find('[data-testid="workflow-version-compare-result"]').exists()).toBe(true);
    await wrapper.get('[data-testid="workflow-version-compare-before"]').setValue(ids.after);
    expect(wrapper.find('[data-testid="workflow-version-compare-result"]').exists()).toBe(false);
    expect(wrapper.get('[data-testid="workflow-version-compare-run"]').attributes('disabled')).toBeDefined();
    wrapper.getComponent({ name: 'ElDrawer' }).vm.$emit('update:modelValue', false); await flushPromises();
    expect(wrapper.find('aside').exists()).toBe(false);
  });
  it('渲染版本中的HTML为纯文本，读取404不展示旧结果', async () => {
    const draft = comparisonDraft(); draft.nodes[1]!.config.nodeName = '<img src=x onerror="window.__unsafe=true">';
    api.definition.mockImplementation(async (id: string) => comparisonVersion(id, id === ids.after ? JSON.stringify(draft) : undefined));
    const wrapper = create(); await start(wrapper); await flushPromises();
    expect(wrapper.find('img').exists()).toBe(false); expect(wrapper.html()).toContain('&lt;img');
    api.definition.mockRejectedValue({ status: 404 });
    await wrapper.get('[data-testid="workflow-version-compare-run"]').trigger('click'); await flushPromises();
    expect(wrapper.find('[data-testid="workflow-version-compare-result"]').exists()).toBe(false);
    expect(wrapper.get('[role="alert"]').text()).toBe('workflowVersionCompare.failed');
  });
});
