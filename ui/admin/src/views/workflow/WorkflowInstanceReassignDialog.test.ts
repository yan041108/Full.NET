import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { ElButton, ElPagination } from 'element-plus';
import { listWorkflowReassignmentCandidates } from '../../api/workflow-reassignment-candidates';
import { deferred } from '../../test/data-output-fixtures';
import { createShareSession } from '../../test/document-share-fixtures';
import { workflowInstance as instance } from '../../test/workflow-instance-fixtures';
import WorkflowInstanceReassignDialog from './WorkflowInstanceReassignDialog.vue';
vi.mock('../../api/workflow-reassignment-candidates', () => ({ listWorkflowReassignmentCandidates: vi.fn() }));
const target = '01912345-6789-7abc-8def-0123456789bb', all = ['workflow.instances.read','workflow.instances.recover']; let wrapper: VueWrapper;
async function setup(permissions = all) { const context = createShareSession(permissions); wrapper = mount(WorkflowInstanceReassignDialog, { props: { instance }, global: { plugins: [context.pinia], stubs: { teleport: true } } }); await flushPromises(); return context; }
const candidate = { id: target, username: 'reviewer', displayName: '核对人员' };
const candidatePage = { instanceId: instance.id, instanceRevision: instance.revision, todoId: instance.activeTodoId!, todoRevision: 7, items: [candidate], page: 1, pageSize: 20, total: 1 };
beforeEach(() => { vi.resetAllMocks(); vi.mocked(listWorkflowReassignmentCandidates).mockResolvedValue(candidatePage); }); afterEach(() => { wrapper?.unmount(); });
describe('FD-01 改派意图表单与父页面单一写入边界', () => {
  it('按姓名账号选择后只提交冻结意图，写入和确认交给父页面', async () => { await setup(); expect(wrapper.get('select').text()).toContain('核对人员 · reviewer'); await wrapper.get('[data-testid="workflow-reassign-user"]').setValue(target); await wrapper.get('[data-testid="workflow-reassign-reason"]').setValue(' 核对人员 '); await wrapper.get('[data-testid="workflow-reassign-save"]').trigger('click'); expect(wrapper.emitted('submit')).toEqual([[{ assigneeUserId: target, reason: '核对人员', todoId: instance.activeTodoId, expectedTodoRevision: 7 }]]); });
  it.each(['invalid','00000000-0000-0000-0000-000000000000',''])('拒绝非法用户 %s', async value => { await setup(); await wrapper.get('[data-testid="workflow-reassign-user"]').setValue(value); await wrapper.get('[data-testid="workflow-reassign-save"]').trigger('click'); expect(wrapper.emitted('submit')).toBeUndefined(); });
  it.each(['x'.repeat(501),'invalid\u0000reason','invalid\nreason'])('拒绝越界或控制字符原因 %s', async value => { await setup(); await wrapper.get('[data-testid="workflow-reassign-user"]').setValue(target); await wrapper.get('[data-testid="workflow-reassign-reason"]').setValue(value); await wrapper.get('[data-testid="workflow-reassign-save"]').trigger('click'); expect(wrapper.emitted('submit')).toBeUndefined(); });
  it('只有读取权限不能提交', async () => { await setup([all[0]!]); await wrapper.get('[data-testid="workflow-reassign-user"]').setValue(target); await wrapper.get('[data-testid="workflow-reassign-save"]').trigger('click'); expect(wrapper.emitted('submit')).toBeUndefined(); });
  it('矛盾 Host 租户上下文不能提交', async () => { const { session } = await setup(); session.currentUser!.tenantId = target; await flushPromises(); expect(wrapper.emitted('close')).toHaveLength(1); expect(wrapper.emitted('submit')).toBeUndefined(); });
  it('忙碌期间不能重复提交', async () => { await setup(); await wrapper.get('[data-testid="workflow-reassign-user"]').setValue(target); await wrapper.setProps({ busy: true }); await wrapper.get('[data-testid="workflow-reassign-save"]').trigger('click'); expect(wrapper.emitted('submit')).toBeUndefined(); });
  it('撤权后关闭，旧提交不能借用权限', async () => { const { session } = await setup(); await wrapper.get('[data-testid="workflow-reassign-user"]').setValue(target); const click = wrapper.findAllComponents(ElButton).find(value => value.attributes('data-testid') === 'workflow-reassign-save')!.vm.$.vnode.props!.onClick as () => void; session.currentUser!.permissions = [all[0]!]; await flushPromises(); click(); expect(wrapper.emitted('submit')).toBeUndefined(); expect(wrapper.emitted('close')).toHaveLength(1); });
  it.each(['revision','activeTodoId'] as const)('父快照 %s 改变后关闭旧意图', async key => { await setup(); await wrapper.setProps({ instance: { ...instance, [key]: key === 'revision' ? 4 : target } }); expect(wrapper.emitted('close')).toHaveLength(1); });
  it('已关闭表单的旧回调不能再提交', async () => { await setup(); await wrapper.get('[data-testid="workflow-reassign-user"]').setValue(target); const click = wrapper.findAllComponents(ElButton).find(value => value.attributes('data-testid') === 'workflow-reassign-save')!.vm.$.vnode.props!.onClick as () => void; await wrapper.get('[data-testid="workflow-reassign-close"]').trigger('click'); click(); expect(wrapper.emitted('close')).toHaveLength(1); expect(wrapper.emitted('submit')).toBeUndefined(); });
  it('缺少恢复权限不枚举用户', async () => { await setup([all[0]!]); expect(listWorkflowReassignmentCandidates).not.toHaveBeenCalled(); });
  it('候选返回更高实例修订时关闭旧意图，不能借用新待办修订', async () => {
    vi.mocked(listWorkflowReassignmentCandidates).mockResolvedValueOnce({ ...candidatePage, instanceRevision: instance.revision + 1 });
    await setup(); expect(wrapper.emitted('close')).toHaveLength(1); expect(wrapper.emitted('submit')).toBeUndefined();
  });
  it('失败后手动重读，空目录保持不可提交', async () => {
    vi.mocked(listWorkflowReassignmentCandidates).mockRejectedValueOnce(Error('unavailable')).mockResolvedValueOnce({ ...candidatePage, items: [], total: 0 });
    await setup(); expect(wrapper.find('[role="alert"]').exists()).toBe(true);
    await wrapper.get('[data-testid="workflow-reassign-retry"]').trigger('click'); await flushPromises();
    expect(wrapper.find('[role="alert"]').exists()).toBe(false); expect(wrapper.get('select').findAll('option')).toHaveLength(1);
    expect(wrapper.get('[data-testid="workflow-reassign-save"]').attributes('disabled')).toBeDefined();
  });
  it('翻页清除旧选择，不自动选择新页用户', async () => {
    vi.mocked(listWorkflowReassignmentCandidates).mockResolvedValueOnce({ ...candidatePage, total: 21 }); await setup();
    await wrapper.get('select').setValue(target); const other = { ...candidate, id: instance.id };
    vi.mocked(listWorkflowReassignmentCandidates).mockResolvedValueOnce({ ...candidatePage, items: [other], page: 2, total: 21 });
    wrapper.getComponent(ElPagination).vm.$emit('current-change', 2); await flushPromises();
    expect(listWorkflowReassignmentCandidates).toHaveBeenLastCalledWith(instance.id, instance.activeTodoId, 2, 20, expect.any(AbortSignal));
    expect((wrapper.get('select').element as HTMLSelectElement).value).toBe('');
    await wrapper.get('[data-testid="workflow-reassign-save"]').trigger('click'); expect(wrapper.emitted('submit')).toBeUndefined();
  });
  it('关闭时取消候选读取，迟到结果不能重开或提交', async () => {
    const pending = deferred<typeof candidatePage>(); vi.mocked(listWorkflowReassignmentCandidates).mockReturnValueOnce(pending.promise);
    await setup(); expect(wrapper.get('select').attributes('disabled')).toBeDefined();
    const signal = vi.mocked(listWorkflowReassignmentCandidates).mock.calls[0]![4]!;
    await wrapper.get('[data-testid="workflow-reassign-close"]').trigger('click'); expect(signal.aborted).toBe(true);
    pending.resolve(candidatePage); await flushPromises(); expect(wrapper.emitted('close')).toHaveLength(1);
    expect(wrapper.emitted('submit')).toBeUndefined(); expect(wrapper.findAll('option')).toHaveLength(1);
  });
  it('成员数收缩后空尾页只回退一次，且保留未选中状态', async () => {
    vi.mocked(listWorkflowReassignmentCandidates).mockResolvedValueOnce({ ...candidatePage, total: 21 }); await setup();
    vi.mocked(listWorkflowReassignmentCandidates).mockResolvedValueOnce({ ...candidatePage, items: [], page: 2 }).mockResolvedValueOnce(candidatePage);
    wrapper.getComponent(ElPagination).vm.$emit('current-change', 2); await flushPromises();
    expect(vi.mocked(listWorkflowReassignmentCandidates).mock.calls.map(call => call[2])).toEqual([1, 2, 1]);
    expect((wrapper.get('select').element as HTMLSelectElement).value).toBe(''); expect(wrapper.find('[role="alert"]').exists()).toBe(false);
  });
});
