import { mount, flushPromises } from '@vue/test-utils';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { enterpriseRequestsHttp } from '../../api/enterprise-requests';
import { useAdminI18n } from '../../i18n/adminI18n';
import { createOutputSession, deferred, outputId } from '../../test/data-output-fixtures';
import Dialog from './EnterpriseRequestApprovalProgressDialog.vue';

vi.mock('../../api/enterprise-requests', async original => ({
  ...await original<typeof import('../../api/enterprise-requests')>(), enterpriseRequestsHttp: { request: vi.fn() }
}));
const request = vi.mocked(enterpriseRequestsHttp.request);
const push = vi.fn();
vi.mock('vue-router', () => ({ useRouter: () => ({ push }) }));
const instanceId = '019bc2b1-2a40-7cc3-8992-a80de51bf330';
const response = { requestId: outputId, requestStatus: 'Submitted', requestVersion: '2', deliveryState: 'queued',
  workflowDefinitionVersionId: instanceId, workflowInstanceId: instanceId, submittedVersion: '2',
  submittedAtUtc: '2026-10-08T00:00:00Z', startedAtUtc: null, completedAtUtc: null };
const permission = 'enterprise_request.enterprise_requests.read';
const unbound = { ...response, requestStatus: 'Draft', requestVersion: '1', deliveryState: 'not_submitted',
  workflowDefinitionVersionId: null, workflowInstanceId: null, submittedVersion: null, submittedAtUtc: null };
function fixture(permissions = [permission]) {
  const { pinia, session } = createOutputSession(permissions);
  return { session, wrapper: mount(Dialog, { props: { requestId: outputId }, global: { plugins: [pinia], stubs: { teleport: true } } }) };
}
async function click(wrapper: ReturnType<typeof mount>, text: string) {
  const button = wrapper.findAll('button').find(value => value.text() === text);
  expect(button).toBeDefined(); await button!.trigger('click');
}

describe('审批进度弹窗的读取与生命周期', () => {
  beforeEach(() => { push.mockReset(); useAdminI18n().setLocale('zh-CN'); request.mockReset(); request.mockResolvedValue(response); });
  it('通知入口需要独立收件箱权限，导航只使用白名单路由', async () => {
    const f = fixture([permission, 'notifications.inbox.read']);
    try {
      await flushPromises(); await click(f.wrapper, '查看站内信');
      expect(push).toHaveBeenCalledWith({ name: 'inbox-messages' });
      expect(f.wrapper.emitted('close')).toHaveLength(1);
      expect(f.wrapper.text()).not.toContain(instanceId);
    } finally { f.wrapper.unmount(); }
  });
  it('撤销收件箱权限立即隐藏通知入口', async () => {
    const f = fixture([permission, 'notifications.inbox.read']);
    try {
      await flushPromises(); expect(f.wrapper.findAll('button').some(button => button.text() === '查看站内信')).toBe(true);
      f.session.currentUser = { ...f.session.currentUser!, permissions: [permission] };
      await flushPromises(); expect(f.wrapper.findAll('button').some(button => button.text() === '查看站内信')).toBe(false); expect(push).not.toHaveBeenCalled();
    } finally { f.wrapper.unmount(); }
  });
  it.each([
    ['not_submitted', '尚未提交审批'], ['queued', '等待流程启动回执'], ['started', '流程已启动，等待审批结果回写'],
    ['finalized', '审批结果已回写'], ['recovery_required', '需要恢复历史流程绑定']
  ])('展示服务端阶段 %s，不推断流程节点或通知成功', async (deliveryState, label) => {
    request.mockResolvedValue(deliveryState === 'not_submitted' ? unbound
      : deliveryState === 'recovery_required' ? { ...unbound, requestStatus: 'Submitted', deliveryState }
      : deliveryState === 'started' ? { ...response, deliveryState, startedAtUtc: '2026-10-08T00:00:01Z' }
      : deliveryState === 'finalized' ? { ...response, deliveryState, requestStatus: 'Approved', requestVersion: '3', completedAtUtc: '2026-10-08T00:00:02Z' }
      : response);
    const f = fixture();
    try { await flushPromises(); expect(f.wrapper.text()).toContain(label); expect(request).toHaveBeenCalledTimes(1); }
    finally { f.wrapper.unmount(); }
  });
  it('缺少读取权限不请求，也不展示旧快照', async () => {
    const f = fixture([]);
    try {
      await flushPromises(); expect(request).not.toHaveBeenCalled(); expect(f.wrapper.text()).toContain('当前已无权');
      expect(f.wrapper.findAll('button').some(button => button.text() === '刷新')).toBe(false);
    }
    finally { f.wrapper.unmount(); }
  });
  it('关闭立即取消请求，迟到响应不得显示内容', async () => {
    const pending = deferred<unknown>(); request.mockReturnValueOnce(pending.promise); const f = fixture();
    try {
      await flushPromises(); await click(f.wrapper, '取消');
      expect(request.mock.calls[0]![2]?.aborted).toBe(true); expect(f.wrapper.emitted('close')).toHaveLength(1);
      pending.resolve(response); await flushPromises(); expect(f.wrapper.text()).not.toContain(instanceId);
    } finally { f.wrapper.unmount(); }
  });
  it.each(['tenant', 'permission', 'deactivate'])('%s 变化丢弃迟到错误与敏感结果', async change => {
    const pending = deferred<unknown>(); request.mockReturnValueOnce(pending.promise); const f = fixture();
    try {
      await flushPromises();
      if (change === 'tenant') {
        request.mockResolvedValueOnce(unbound);
        f.session.currentUser = { ...f.session.currentUser!, tenantId: instanceId };
      } else if (change === 'permission') {
        f.session.currentUser = { ...f.session.currentUser!, permissions: [] };
      } else { f.wrapper.unmount(); }
      expect(request.mock.calls[0]![2]?.aborted).toBe(true);
      pending.reject({ status: 403, code: 'old.scope.denied', title: '旧租户错误' }); await flushPromises();
      if (change !== 'deactivate') { expect(f.wrapper.text()).not.toContain('旧租户错误'); expect(f.wrapper.text()).not.toContain(instanceId); }
    } finally { if (change !== 'deactivate') f.wrapper.unmount(); }
  });
  it('切换单据取消旧查询，旧 finally 不关闭新查询的加载态', async () => {
    const old = deferred<unknown>(); const next = deferred<unknown>();
    request.mockReturnValueOnce(old.promise).mockReturnValueOnce(next.promise); const f = fixture();
    try {
      await flushPromises(); await f.wrapper.setProps({ requestId: instanceId });
      expect(request.mock.calls[0]![2]?.aborted).toBe(true);
      old.resolve(response); await flushPromises(); expect(f.wrapper.text()).toContain('加载中');
      next.resolve({ ...response, requestId: instanceId }); await flushPromises(); expect(f.wrapper.text()).toContain('等待流程启动回执');
    } finally { f.wrapper.unmount(); }
  });
  it('刷新互斥，失败清空旧快照，后续刷新可以恢复', async () => {
    const f = fixture();
    try {
      await flushPromises(); expect(f.wrapper.text()).toContain(instanceId);
      const pending = deferred<unknown>(); request.mockReturnValueOnce(pending.promise);
      await click(f.wrapper, '刷新'); await click(f.wrapper, '刷新'); expect(request).toHaveBeenCalledTimes(2);
      expect(f.wrapper.text()).not.toContain(instanceId);
      pending.reject({ status: 409, code: 'snapshot.changed', title: '请重试' }); await flushPromises(); expect(f.wrapper.text()).toContain('请重试');
      await click(f.wrapper, '刷新'); await flushPromises(); expect(f.wrapper.text()).toContain(instanceId); expect(f.wrapper.text()).not.toContain('请重试');
    } finally { f.wrapper.unmount(); }
  });
  it('显示文本随语言切换，稳定实例身份保持原值', async () => {
    const f = fixture();
    try {
      await flushPromises(); useAdminI18n().setLocale('en-US'); await flushPromises();
      expect(f.wrapper.text()).toContain('Awaiting workflow start receipt'); expect(f.wrapper.text()).toContain(instanceId);
    } finally { f.wrapper.unmount(); useAdminI18n().setLocale('zh-CN'); }
  });
});
