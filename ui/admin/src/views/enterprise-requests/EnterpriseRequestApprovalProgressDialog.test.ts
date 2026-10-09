import { mount, flushPromises } from '@vue/test-utils';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ElMessageBox } from 'element-plus';
import { defineComponent, h, KeepAlive, ref } from 'vue';
import { enterpriseRequestsHttp } from '../../api/enterprise-requests';
import { useAdminI18n } from '../../i18n/adminI18n';
import { createOutputSession, deferred, outputId } from '../../test/data-output-fixtures';
import Dialog from './EnterpriseRequestApprovalProgressDialog.vue';

vi.mock('../../api/enterprise-requests', async original => ({
  ...await original<typeof import('../../api/enterprise-requests')>(), enterpriseRequestsHttp: { request: vi.fn() }
}));
const request = vi.mocked(enterpriseRequestsHttp.request);
const push = vi.fn();
const confirm = vi.spyOn(ElMessageBox, 'confirm');
vi.mock('vue-router', () => ({ useRouter: () => ({ push }) }));
const instanceId = '019bc2b1-2a40-7cc3-8992-a80de51bf330';
const response = { requestId: outputId, requestStatus: 'Submitted', requestVersion: '2', deliveryState: 'queued',
  workflowDefinitionVersionId: instanceId, workflowInstanceId: instanceId, submittedVersion: '2',
  submittedAtUtc: '2026-10-08T00:00:00Z', startedAtUtc: null, completedAtUtc: null };
const permission = 'enterprise_request.enterprise_requests.read';
const unbound = { ...response, requestStatus: 'Draft', requestVersion: '1', deliveryState: 'not_submitted',
  workflowDefinitionVersionId: null, workflowInstanceId: null, submittedVersion: null, submittedAtUtc: null };
const notificationSummary = { intentId: instanceId, acceptedAtUtc: '2026-10-08T00:00:04Z', totalDeliveryCount: 7,
  pendingDeliveryCount: 1, sentDeliveryCount: 2, failedDeliveryCount: 1, deadLetteredDeliveryCount: 1,
  unknownDeliveryCount: 1, otherDeliveryCount: 1, nextAttemptAtUtc: '2026-10-08T00:01:00Z' };
function fixture(permissions = [permission]) {
  const { pinia, session } = createOutputSession(permissions);
  return { session, wrapper: mount(Dialog, { props: { requestId: outputId }, global: { plugins: [pinia], stubs: { teleport: true } } }) };
}
async function click(wrapper: ReturnType<typeof mount>, text: string) {
  const button = wrapper.findAll('button').find(value => value.text() === text);
  expect(button).toBeDefined(); await button!.trigger('click');
}

describe('审批结果与终态通知分别展示', () => {
  const final = { ...response, requestStatus: 'Approved', requestVersion: '3', deliveryState: 'finalized', completedAtUtc: '2026-10-08T00:00:03Z' };
  beforeEach(() => { request.mockReset(); useAdminI18n().setLocale('zh-CN'); });
  afterEach(() => { vi.useRealTimers(); useAdminI18n().setLocale('zh-CN'); });
  it('分别显示通知内核已有的送达、已读、抑制和持久化状态', async () => {
    request.mockResolvedValue({ ...final, finalNotification: { ...notificationSummary, totalDeliveryCount: 11,
      persistedDeliveryCount: 1, deliveredDeliveryCount: 1, readDeliveryCount: 1, suppressedDeliveryCount: 1 } });
    const f = fixture();
    try { await flushPromises();
      for (const state of ['persisted', 'delivered', 'read', 'suppressed']) expect(f.wrapper.get(`[data-testid="notification-${state}"]`).text()).toBe('1');
    } finally { f.wrapper.unmount(); }
  });
  it.each(['zh-CN', 'en-US'] as const)('展示独立投递分类并保留审批结果：%s', async locale => {
    useAdminI18n().setLocale(locale); request.mockResolvedValue({ ...final, finalNotification: notificationSummary });
    const f = fixture();
    try {
      await flushPromises();
      expect(f.wrapper.get('[data-testid="notification-pending"]').text()).toBe('1');
      expect(f.wrapper.get('[data-testid="notification-sent"]').text()).toBe('2');
      expect(f.wrapper.get('[data-testid="notification-failed"]').text()).toBe('1');
      expect(f.wrapper.get('[data-testid="notification-dead-lettered"]').text()).toBe('1');
      expect(f.wrapper.get('[data-testid="notification-unknown"]').text()).toBe('1');
      expect(f.wrapper.get('[data-testid="notification-other"]').text()).toBe('1');
      expect(f.wrapper.text()).toContain('Approved');
      expect(f.wrapper.text()).toContain(locale === 'zh-CN' ? '已发送不代表收件人已阅读' : 'Sent does not mean it was read');
    } finally { f.wrapper.unmount(); }
  });
  it('未受理不显示通知已发送，业务结果仍保持已回写', async () => {
    request.mockResolvedValue({ ...final, finalNotification: null }); const f = fixture();
    try { await flushPromises(); expect(f.wrapper.text()).toContain('尚未查到通知受理意图');
      expect(f.wrapper.text()).toContain('审批结果已回写'); expect(f.wrapper.find('[data-testid="notification-sent"]').exists()).toBe(false); }
    finally { f.wrapper.unmount(); }
  });
  it('旧服务未提供字段时明确提示兼容状态，不推定受理失败', async () => {
    request.mockResolvedValue(final); const f = fixture();
    try { await flushPromises(); expect(f.wrapper.text()).toContain('当前服务尚未提供通知状态');
      expect(f.wrapper.text()).not.toContain('尚未查到通知受理意图'); }
    finally { f.wrapper.unmount(); }
  });
  it('审批完成后继续跟踪未受理和待投递，全部送出后停止刷新', async () => {
    vi.useFakeTimers(); request.mockResolvedValueOnce({ ...final, finalNotification: null })
      .mockResolvedValueOnce({ ...final, finalNotification: notificationSummary })
      .mockResolvedValue({ ...final, finalNotification: { ...notificationSummary, totalDeliveryCount: 7, pendingDeliveryCount: 0,
        sentDeliveryCount: 7, failedDeliveryCount: 0, deadLetteredDeliveryCount: 0, unknownDeliveryCount: 0, otherDeliveryCount: 0, nextAttemptAtUtc: null } });
    const f = fixture();
    try {
      await flushPromises(); await vi.advanceTimersByTimeAsync(5000); await flushPromises(); expect(request).toHaveBeenCalledTimes(2);
      await vi.advanceTimersByTimeAsync(5000); await flushPromises(); expect(request).toHaveBeenCalledTimes(3);
      expect(f.wrapper.get('[data-testid="notification-sent"]').text()).toBe('7');
      await vi.advanceTimersByTimeAsync(20000); expect(request).toHaveBeenCalledTimes(3);
    } finally { f.wrapper.unmount(); }
  });
  it('只有明确失败或未知终态时停止后台刷新，保留手动刷新', async () => {
    vi.useFakeTimers(); request.mockResolvedValue({ ...final, finalNotification: { ...notificationSummary,
      totalDeliveryCount: 6, pendingDeliveryCount: 0, nextAttemptAtUtc: null } }); const f = fixture();
    try { await flushPromises(); await vi.advanceTimersByTimeAsync(20000); expect(request).toHaveBeenCalledTimes(1);
      await click(f.wrapper, '刷新'); await flushPromises(); expect(request).toHaveBeenCalledTimes(2);
      expect(f.wrapper.get('[data-testid="notification-unknown"]').text()).toBe('1'); }
    finally { f.wrapper.unmount(); }
  });
});

describe('审批进度弹窗的读取与生命周期', () => {
  beforeEach(() => { confirm.mockReset(); confirm.mockResolvedValue('confirm' as never); push.mockReset(); useAdminI18n().setLocale('zh-CN'); request.mockReset(); request.mockResolvedValue(response); });
  it('单独恢复权限和原因确认后，使用加载版本恢复并重新读取进度', async () => {
    request.mockResolvedValueOnce({ ...unbound, requestStatus: 'Submitted', requestVersion: '2', deliveryState: 'recovery_required' })
      .mockResolvedValueOnce(undefined).mockResolvedValueOnce({ ...response, deliveryState: 'started', startedAtUtc: '2026-10-08T00:00:01Z' });
    const f = fixture([permission, 'enterprise_request.enterprise_requests.repair_approval']);
    try {
      await flushPromises();
      await f.wrapper.get('input[placeholder="原流程实例标识"]').setValue(instanceId);
      await f.wrapper.get('textarea[placeholder="恢复原因"]').setValue('已核对原启动记录');
      await click(f.wrapper, '恢复与对账'); await flushPromises();
      expect(confirm).toHaveBeenCalledTimes(1);
      expect(request.mock.calls[1]?.[0]).toBe(`/api/v1/enterprise_request/enterprise-requests/${outputId}/repair-approval`);
      expect(request.mock.calls[1]?.[1]?.method).toBe('POST');
      expect(JSON.parse(request.mock.calls[1]![1]!.body as string)).toEqual({ workflowInstanceId: instanceId, expectedVersion: 2, reason: '已核对原启动记录' });
      expect(request).toHaveBeenCalledTimes(3); expect(f.wrapper.text()).toContain('流程已启动');
    } finally { f.wrapper.unmount(); }
  });
  it('恢复权限在确认期间撤销，不发送写请求并清空原因', async () => {
    const pending = deferred<Awaited<ReturnType<typeof ElMessageBox.confirm>>>(); confirm.mockReturnValueOnce(pending.promise);
    request.mockResolvedValue({ ...response, deliveryState: 'started', startedAtUtc: '2026-10-08T00:00:01Z' });
    const f = fixture([permission, 'enterprise_request.enterprise_requests.repair_approval']);
    try {
      await flushPromises(); await f.wrapper.get('textarea[placeholder="恢复原因"]').setValue('受控恢复');
      await click(f.wrapper, '恢复与对账');
      f.session.currentUser = { ...f.session.currentUser!, permissions: [permission] };
      pending.resolve('confirm' as never); await flushPromises();
      expect(request.mock.calls.every(call => call[1]?.method === 'GET')).toBe(true);
      expect(f.wrapper.find('textarea[placeholder="恢复原因"]').exists()).toBe(false);
    } finally { f.wrapper.unmount(); }
  });
  it('只有读取权限时不渲染恢复输入和动作', async () => {
    const f = fixture();
    try { await flushPromises(); expect(f.wrapper.find('textarea').exists()).toBe(false);
      expect(f.wrapper.findAll('button').some(button => button.text() === '恢复与对账')).toBe(false); }
    finally { f.wrapper.unmount(); }
  });
  it.each(['', ' ', '原因\n换行'])('无效恢复原因不能提交：%j', async reason => {
    request.mockResolvedValue({ ...response, deliveryState: 'started', startedAtUtc: '2026-10-08T00:00:01Z' });
    const f = fixture([permission, 'enterprise_request.enterprise_requests.repair_approval']);
    try {
      await flushPromises(); await f.wrapper.get('textarea').setValue(reason);
      const button = f.wrapper.findAll('button').find(button => button.text() === '恢复与对账')!;
      expect(button.attributes('disabled')).toBeDefined(); expect(confirm).not.toHaveBeenCalled(); expect(request).toHaveBeenCalledTimes(1);
    } finally { f.wrapper.unmount(); }
  });
  it('取消人工确认不执行恢复且保留当前输入', async () => {
    confirm.mockRejectedValueOnce('cancel');
    const f = fixture([permission, 'enterprise_request.enterprise_requests.repair_approval']);
    try {
      await flushPromises(); await f.wrapper.get('textarea').setValue('受控对账');
      await click(f.wrapper, '恢复与对账'); await flushPromises();
      expect(request).toHaveBeenCalledTimes(1); expect(f.wrapper.text()).not.toContain('client.enterprise_request_approval_repair_failed');
      expect((f.wrapper.get('textarea').element as HTMLTextAreaElement).value).toBe('受控对账');
    } finally { f.wrapper.unmount(); }
  });
  it('关闭期间的迟到恢复响应不刷新或接入旧错误', async () => {
    const pending = deferred<unknown>(); request.mockResolvedValueOnce(response).mockReturnValueOnce(pending.promise);
    const f = fixture([permission, 'enterprise_request.enterprise_requests.repair_approval']);
    try {
      await flushPromises(); await f.wrapper.get('textarea').setValue('受控对账');
      await click(f.wrapper, '恢复与对账'); await flushPromises();
      await click(f.wrapper, '取消'); expect(request.mock.calls[1]![2]?.aborted).toBe(true);
      pending.reject({ status: 409, code: 'old.repair.conflict', title: '旧单据恢复错误' }); await flushPromises();
      expect(request).toHaveBeenCalledTimes(2); expect(f.wrapper.text()).not.toContain('旧单据恢复错误'); expect(f.wrapper.text()).not.toContain(instanceId);
    } finally { f.wrapper.unmount(); }
  });
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

describe('审批进度自动刷新', () => {
  let visibility: ReturnType<typeof vi.spyOn>;
  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
    visibility = vi.spyOn(document, 'hidden', 'get').mockReturnValue(false);
    request.mockReset(); request.mockResolvedValue(response);
    confirm.mockReset(); confirm.mockResolvedValue('confirm' as never);
    useAdminI18n().setLocale('zh-CN');
  });
  afterEach(() => { visibility.mockRestore(); vi.useRealTimers(); });
  async function advance(milliseconds = 5000) {
    await vi.advanceTimersByTimeAsync(milliseconds); await flushPromises();
  }
  const started = { ...response, deliveryState: 'started', startedAtUtc: '2026-10-08T00:00:01Z' };
  const finalized = { ...started, deliveryState: 'finalized', requestStatus: 'Approved', requestVersion: '3', completedAtUtc: '2026-10-08T00:00:02Z' };

  it('排队和审批中每五秒更新，结果回写后停止', async () => {
    request.mockResolvedValueOnce(response).mockResolvedValueOnce(started).mockResolvedValue(finalized);
    const f = fixture();
    try {
      await flushPromises(); await advance(); expect(f.wrapper.text()).toContain('流程已启动');
      await advance(); expect(f.wrapper.text()).toContain('审批结果已回写');
      await advance(20000); expect(request).toHaveBeenCalledTimes(3);
    } finally { f.wrapper.unmount(); }
  });
  it.each(['not_submitted', 'finalized', 'recovery_required'])('阶段 %s 不自动查询', async state => {
    request.mockResolvedValue(state === 'not_submitted' ? unbound : state === 'finalized' ? finalized
      : { ...unbound, requestStatus: 'Submitted', requestVersion: '2', deliveryState: state });
    const f = fixture();
    try { await flushPromises(); await advance(20000); expect(request).toHaveBeenCalledTimes(1); }
    finally { f.wrapper.unmount(); }
  });
  it('后台查询保留当前快照，慢请求和手动刷新不重叠', async () => {
    const pending = deferred<unknown>(); request.mockResolvedValueOnce(response).mockReturnValueOnce(pending.promise).mockResolvedValue(finalized);
    const f = fixture();
    try {
      await flushPromises(); await advance(); expect(request).toHaveBeenCalledTimes(2);
      expect(f.wrapper.text()).toContain(instanceId); await click(f.wrapper, '刷新');
      await advance(20000); expect(request).toHaveBeenCalledTimes(2);
      pending.resolve(started); await flushPromises(); await advance(); expect(request).toHaveBeenCalledTimes(3);
    } finally { f.wrapper.unmount(); }
  });
  it('自动读取失败停止重试并清空旧快照，手动成功后恢复', async () => {
    request.mockResolvedValueOnce(response).mockRejectedValueOnce({ status: 409, code: 'snapshot.changed', title: '请重试' }).mockResolvedValue(started);
    const f = fixture();
    try {
      await flushPromises(); await advance(); expect(f.wrapper.text()).toContain('请重试');
      expect(f.wrapper.text()).not.toContain(instanceId); await advance(20000); expect(request).toHaveBeenCalledTimes(2);
      await click(f.wrapper, '刷新'); await flushPromises(); await advance(); expect(request).toHaveBeenCalledTimes(4);
    } finally { f.wrapper.unmount(); }
  });
  it('切到后台丢弃在途结果，回到前台重新查询', async () => {
    const pending = deferred<unknown>(); request.mockResolvedValueOnce(response).mockReturnValueOnce(pending.promise).mockResolvedValue(finalized);
    const f = fixture();
    try {
      await flushPromises(); await advance(); visibility.mockReturnValue(true); document.dispatchEvent(new Event('visibilitychange'));
      pending.resolve(started); await flushPromises(); expect(f.wrapper.text()).toContain('等待流程启动回执');
      await advance(20000); expect(request).toHaveBeenCalledTimes(2);
      visibility.mockReturnValue(false); document.dispatchEvent(new Event('visibilitychange'));
      await advance(); expect(f.wrapper.text()).toContain('审批结果已回写');
    } finally { f.wrapper.unmount(); }
  });
  it.each(['permission', 'close'])('%s 取消自动请求，不接入迟到响应或继续查询', async action => {
    const pending = deferred<unknown>(); request.mockResolvedValueOnce(response).mockReturnValueOnce(pending.promise);
    const f = fixture();
    try {
      await flushPromises(); await advance(); expect(request).toHaveBeenCalledTimes(2);
      if (action === 'permission') f.session.currentUser = { ...f.session.currentUser!, permissions: [] };
      else await click(f.wrapper, '取消');
      expect(request.mock.calls[1]![2]?.aborted).toBe(true);
      pending.resolve(started); await flushPromises(); await advance(20000);
      expect(request).toHaveBeenCalledTimes(2); expect(f.wrapper.text()).not.toContain(instanceId);
    } finally { f.wrapper.unmount(); }
  });
  it('编辑恢复原因暂停自动查询，保留输入，清空后恢复', async () => {
    const f = fixture([permission, 'enterprise_request.enterprise_requests.repair_approval']);
    try {
      await flushPromises(); await f.wrapper.get('textarea').setValue('已核对历史记录');
      await advance(20000); expect(request).toHaveBeenCalledTimes(1);
      expect((f.wrapper.get('textarea').element as HTMLTextAreaElement).value).toBe('已核对历史记录');
      await f.wrapper.get('textarea').setValue(''); await advance(); expect(request).toHaveBeenCalledTimes(2);
    } finally { f.wrapper.unmount(); }
  });
  it('在途刷新后开始填写恢复原因，迟到快照不能覆盖输入', async () => {
    const pending = deferred<unknown>(); request.mockResolvedValueOnce(response).mockReturnValueOnce(pending.promise);
    const f = fixture([permission, 'enterprise_request.enterprise_requests.repair_approval']);
    try {
      await flushPromises(); await advance(); expect(request).toHaveBeenCalledTimes(2);
      await f.wrapper.get('textarea').setValue('保持当前恢复原因'); pending.resolve(finalized); await flushPromises();
      expect(f.wrapper.text()).toContain('等待流程启动回执');
      expect((f.wrapper.get('textarea').element as HTMLTextAreaElement).value).toBe('保持当前恢复原因');
      await advance(20000); expect(request).toHaveBeenCalledTimes(2);
    } finally { f.wrapper.unmount(); }
  });
  it('切换单据中止自动请求，旧响应不替换新单据', async () => {
    const pending = deferred<unknown>(); request.mockResolvedValueOnce(response).mockReturnValueOnce(pending.promise).mockResolvedValueOnce({ ...unbound, requestId: instanceId });
    const f = fixture();
    try {
      await flushPromises(); await advance(); expect(request).toHaveBeenCalledTimes(2);
      await f.wrapper.setProps({ requestId: instanceId }); await flushPromises();
      expect(request.mock.calls[1]![2]?.aborted).toBe(true);
      pending.resolve(finalized); await flushPromises(); await advance(20000);
      expect(f.wrapper.text()).toContain('尚未提交审批'); expect(request).toHaveBeenCalledTimes(3);
    } finally { f.wrapper.unmount(); }
  });
  it('自动读取期间切换租户，取消旧请求且旧终态不污染新租户', async () => {
    const pending = deferred<unknown>(); request.mockResolvedValueOnce(response).mockReturnValueOnce(pending.promise).mockResolvedValueOnce(unbound);
    const f = fixture();
    try {
      await flushPromises(); await advance(); expect(request).toHaveBeenCalledTimes(2);
      f.session.currentUser = { ...f.session.currentUser!, tenantId: instanceId };
      await flushPromises(); expect(request.mock.calls[1]![2]?.aborted).toBe(true);
      pending.resolve(finalized); await flushPromises(); await advance(20000);
      expect(f.wrapper.text()).toContain('尚未提交审批'); expect(request).toHaveBeenCalledTimes(3);
      expect(f.wrapper.text()).not.toContain('审批结果已回写');
    } finally { f.wrapper.unmount(); }
  });
  it('KeepAlive 停用取消自动查询，重新激活仅恢复当前代次', async () => {
    const pending = deferred<unknown>(); request.mockResolvedValueOnce(response).mockReturnValueOnce(pending.promise)
      .mockResolvedValueOnce(started).mockResolvedValue(finalized);
    const { pinia } = createOutputSession([permission]); const show = ref(true);
    const Other = defineComponent({ render: () => h('span', '其他页面') });
    const Host = defineComponent({ setup: () => () => h(KeepAlive, null, { default: () => show.value ? h(Dialog, { requestId: outputId }) : h(Other) }) });
    const wrapper = mount(Host, { global: { plugins: [pinia], stubs: { teleport: true } } });
    try {
      await flushPromises(); await advance(); expect(request).toHaveBeenCalledTimes(2);
      show.value = false; await flushPromises(); expect(request.mock.calls[1]![2]?.aborted).toBe(true);
      pending.resolve(finalized); await flushPromises(); await advance(20000); expect(request).toHaveBeenCalledTimes(2);
      show.value = true; await flushPromises(); expect(request).toHaveBeenCalledTimes(3);
      expect(wrapper.text()).toContain('流程已启动'); await advance(); expect(request).toHaveBeenCalledTimes(4);
      expect(wrapper.text()).toContain('审批结果已回写'); await advance(20000); expect(request).toHaveBeenCalledTimes(4);
    } finally { wrapper.unmount(); }
  });
});
