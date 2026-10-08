import { mount, flushPromises } from '@vue/test-utils';
import { ElDialog, ElLoading } from 'element-plus';
import { watch } from 'vue';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import EnterpriseRequestsView from './EnterpriseRequestsView.vue';
import { enterpriseRequestsHttp } from '../api/enterprise-requests';
import { createOutputSession, deferred, outputId } from '../test/data-output-fixtures';
import { useAdminI18n } from '../i18n/adminI18n';

vi.mock('../api/enterprise-requests', async original => ({
  ...await original<typeof import('../api/enterprise-requests')>(), enterpriseRequestsHttp: { request: vi.fn() }
}));
const request = vi.mocked(enterpriseRequestsHttp.request);
const row = { id: outputId, tenantId: outputId, organizationUnitId: outputId,
  requestNumber: 'REQ-1', title: '旧租户资料', status: 'Draft', totalAmount: 12,
  applicantUserId: outputId, version: 1, createdAtUtc: '2026-10-08T00:00:00Z',
  createdById: outputId, updatedAtUtc: null, updatedById: null, isDeleted: false,
  deletedAtUtc: null, deletedById: null };
const list = { items: [row], page: 1, pageSize: 20, total: 1 };
const permissions = ['read', 'create', 'update', 'disable'].map(x => 'enterprise_request.enterprise_requests.' + x);
function fixture(granted = permissions) {
  const { pinia, session } = createOutputSession(granted);
  const wrapper = mount(EnterpriseRequestsView, { global: { plugins: [pinia], stubs: { teleport: true }, directives: { loading: ElLoading.directive } } });
  return { wrapper, session };
}
const click = async (wrapper: ReturnType<typeof mount>, text: string) => {
  const button = wrapper.findAll('button').find(b => b.text() === text);
  expect(button, text).toBeDefined(); await button!.trigger('click');
};
const createDialog = (wrapper: ReturnType<typeof mount>) => wrapper.findAllComponents(ElDialog)
  .find(dialog => dialog.props('title') === '创建')!;

describe('企业样例对话框归属', () => {
  it('详情入口从服务端读取最新单据，不能直接展示列表快照', async () => {
    const f = fixture();
    try {
      await flushPromises(); request.mockResolvedValueOnce({ ...row, title: '最新资料', version: '2' });
      await click(f.wrapper, '详情'); await flushPromises();
      expect(request.mock.calls[1]![0]).toBe(`/api/v1/enterprise_request/enterprise-requests/${outputId}`);
      expect(f.wrapper.findAllComponents(ElDialog).find(dialog => dialog.props('title') === '申请详情')!.text()).toContain('最新资料');
    } finally { f.wrapper.unmount(); }
  });
  it('提交审批先确认，取消不会写入，确认正文不携带客户端状态', async () => {
    const f = fixture(['read', 'submit'].map(x => 'enterprise_request.enterprise_requests.' + x));
    try {
      await flushPromises(); await click(f.wrapper, '提交审批'); await flushPromises();
      expect(request).toHaveBeenCalledTimes(1);
      await click(f.wrapper, '取消'); expect(request).toHaveBeenCalledTimes(1);
      await click(f.wrapper, '提交审批'); request.mockResolvedValueOnce({ ...row, status: 'Submitted' });
      await click(f.wrapper, '确认提交'); await flushPromises();
      expect(request.mock.calls[1]![0]).toContain('/submit-for-approval');
      expect(request.mock.calls[1]![1]).toEqual({ method: 'POST' });
    } finally { f.wrapper.unmount(); }
  });
  it('撤销提交权限关闭确认框，旧确认动作不能继续提交', async () => {
    const f = fixture(['read', 'submit'].map(x => 'enterprise_request.enterprise_requests.' + x));
    try {
      await flushPromises(); await click(f.wrapper, '提交审批'); await flushPromises();
      f.session.currentUser = { ...f.session.currentUser!, permissions: ['enterprise_request.enterprise_requests.read'] };
      await flushPromises();
      expect(f.wrapper.findAllComponents(ElDialog).every(dialog => !dialog.props('modelValue'))).toBe(true);
      expect(request.mock.calls.some(call => call[0].endsWith('/submit-for-approval'))).toBe(false);
    } finally { f.wrapper.unmount(); }
  });
  it('关闭提交确认取消写请求，迟到错误不污染重新打开的确认框', async () => {
    const pending = deferred<unknown>();
    const f = fixture(['read', 'submit'].map(x => 'enterprise_request.enterprise_requests.' + x));
    try {
      await flushPromises(); await click(f.wrapper, '提交审批'); request.mockReturnValueOnce(pending.promise);
      await click(f.wrapper, '确认提交'); await click(f.wrapper, '取消');
      expect(request.mock.calls[1]![2]?.aborted).toBe(true);
      await click(f.wrapper, '提交审批');
      pending.reject({ status: 409, code: 'old.request.conflict', title: '旧提交失败' }); await flushPromises();
      expect(f.wrapper.findAllComponents(ElDialog).find(dialog => dialog.props('title') === '确认提交审批')!.props('modelValue')).toBe(true);
      expect(f.wrapper.text()).not.toContain('旧提交失败'); expect(request).toHaveBeenCalledTimes(2);
    } finally { f.wrapper.unmount(); }
  });
  it('新提交处理中旧成功到达，不刷新列表、不关闭新确认或结束新加载态', async () => {
    const old = deferred<unknown>(); const next = deferred<unknown>();
    const f = fixture(['read', 'submit'].map(x => 'enterprise_request.enterprise_requests.' + x));
    try {
      await flushPromises(); await click(f.wrapper, '提交审批'); request.mockReturnValueOnce(old.promise);
      await click(f.wrapper, '确认提交'); await click(f.wrapper, '取消');
      await click(f.wrapper, '提交审批'); request.mockReturnValueOnce(next.promise); await click(f.wrapper, '确认提交');
      old.resolve({ ...row, status: 'Submitted' }); await flushPromises();
      expect(request).toHaveBeenCalledTimes(3);
      expect(f.wrapper.findAllComponents(ElDialog).find(dialog => dialog.props('title') === '确认提交审批')!.props('modelValue')).toBe(true);
      expect((f.wrapper.vm as unknown as { changing: boolean }).changing).toBe(true);
      next.resolve({ ...row, status: 'Submitted' }); await flushPromises();
      expect(request).toHaveBeenCalledTimes(4);
      expect((f.wrapper.vm as unknown as { changing: boolean }).changing).toBe(false);
    } finally { f.wrapper.unmount(); }
  });
  it('语言切换覆盖字段、状态与操作，机器标识保持原值', async () => {
    const f = fixture();
    try {
      await flushPromises(); useAdminI18n().setLocale('en-US'); await flushPromises();
      expect(f.wrapper.text()).toContain('Request number'); expect(f.wrapper.text()).toContain('Details');
      expect(f.wrapper.text()).toContain('Draft'); expect(f.wrapper.text()).toContain(outputId);
      expect(f.wrapper.text()).not.toContain('OrganizationUnitId'); expect(f.wrapper.text()).not.toContain('编辑');
    } finally { f.wrapper.unmount(); useAdminI18n().setLocale('zh-CN'); }
  });
  it.each(['Submitted', 'Approved'])('状态 %s 可查看审批进度，读取权限足够', async status => {
    request.mockResolvedValue({ ...list, items: [{ ...row, status }] });
    const f = fixture(['enterprise_request.enterprise_requests.read']);
    try {
      await flushPromises();
      expect(f.wrapper.findAll('button').map(button => button.text())).toContain('审批进度');
    } finally { f.wrapper.unmount(); }
  });
  it.each(['Submitted', 'Approved', 'Rejected', 'Cancelled'])('状态 %s 没有编辑或删除入口', async status => {
    request.mockResolvedValue({ ...list, items: [{ ...row, status }] });
    const f = fixture();
    try {
      await flushPromises();
      const labels = f.wrapper.findAll('button').map(button => button.text());
      expect(labels).not.toContain('编辑'); expect(labels).not.toContain('删除');
    } finally { f.wrapper.unmount(); }
  });
  it('草稿状态字段只读，创建正文保持 Draft', async () => {
    const f = fixture();
    try {
      await flushPromises(); await click(f.wrapper, '创建'); await flushPromises();
      const field = createDialog(f.wrapper).findAll('.el-form-item')
        .find(item => item.text().includes('单据状态'))!;
      expect(field.find('input').attributes('disabled')).toBeDefined();
      await click(f.wrapper, '保存');
      expect(JSON.parse(request.mock.calls[1]![1]!.body as string)).toMatchObject({ status: 'Draft' });
    } finally { f.wrapper.unmount(); }
  });
  it.each([['update', false], ['submit', true]] as const)('只有 %s 时审批按钮可见性为 %s', async (action, visible) => {
    const f = fixture(['read', action].map(value => 'enterprise_request.enterprise_requests.' + value));
    try {
      await flushPromises();
      expect(f.wrapper.findAll('button').some(button => button.text() === '提交审批')).toBe(visible);
    } finally { f.wrapper.unmount(); }
  });
  beforeEach(() => { request.mockReset(); request.mockResolvedValue(list); });
  it('实际点击进度读取当前行；切换租户关闭弹窗并取消旧查询', async () => {
    const pending = deferred<unknown>(); const f = fixture(['enterprise_request.enterprise_requests.read']);
    try {
      await flushPromises(); request.mockReturnValueOnce(pending.promise);
      await click(f.wrapper, '审批进度'); await flushPromises();
      expect(request.mock.calls[1]![0]).toBe(`/api/v1/enterprise_request/enterprise-requests/${outputId}/approval-progress`);
      f.session.currentUser = { ...f.session.currentUser!, tenantId: '019bc2b1-2a40-7cc3-8992-a80de51bf300' };
      expect(request.mock.calls[1]![2]?.aborted).toBe(true);
      await flushPromises();
      pending.resolve({ requestId: outputId }); await flushPromises();
      expect(f.wrapper.findAllComponents(ElDialog).every(dialog => !dialog.props('modelValue'))).toBe(true);
    } finally { f.wrapper.unmount(); }
  });
  it('切换租户关闭对话框并清空表单，重新创建不得携带旧输入', async () => {
    const f = fixture(); await flushPromises(); await click(f.wrapper, '创建'); await flushPromises();
    await createDialog(f.wrapper).get('input').setValue(outputId);
    f.session.currentUser = { ...f.session.currentUser!, tenantId: '019bc2b1-2a40-7cc3-8992-a80de51bf300' };
    await flushPromises();
    expect(createDialog(f.wrapper).props('modelValue')).toBe(false);
    await click(f.wrapper, '创建'); await flushPromises();
    expect((createDialog(f.wrapper).get('input').element as HTMLInputElement).value).toBe('');
    f.wrapper.unmount();
  });
  it('关闭创建弹窗取消写请求，迟到成功不得关闭后来重新打开的弹窗', async () => {
    const pending = deferred<unknown>(); const f = fixture(); await flushPromises();
    request.mockReturnValueOnce(pending.promise);
    await click(f.wrapper, '创建'); await flushPromises(); await click(f.wrapper, '保存');
    await click(f.wrapper, '取消');
    expect(request.mock.calls[1]![2]?.aborted).toBe(true);
    await click(f.wrapper, '创建'); await flushPromises();
    pending.resolve(row); await flushPromises();
    expect(createDialog(f.wrapper).props('modelValue')).toBe(true);
    expect(request).toHaveBeenCalledTimes(2); f.wrapper.unmount();
  });
  it('删除先展示确认，取消不得发送删除请求', async () => {
    const f = fixture(); await flushPromises(); await click(f.wrapper, '删除'); await flushPromises();
    expect(request).toHaveBeenCalledTimes(1);
    await click(f.wrapper, '取消'); expect(request).toHaveBeenCalledTimes(1); f.wrapper.unmount();
  });
  it('模型成功与页面继续执行之间重开弹窗，旧成功不得关闭新弹窗', async () => {
    const f = fixture(); await flushPromises(); await click(f.wrapper, '创建'); await flushPromises();
    request.mockResolvedValueOnce(row);
    const state = f.wrapper.vm as unknown as { changing: boolean };
    expect(state.changing).toBe(false);
    let reopened = false;
    const stop = watch(() => state.changing, (current, previous) => {
      if (previous && !current && !reopened) queueMicrotask(() => {
        reopened = true;
        f.wrapper.findAll('button').find(b => b.text() === '取消')!.element.click();
        f.wrapper.findAll('button').find(b => b.text() === '创建')!.element.click();
      });
    }, { flush: 'sync' });
    await click(f.wrapper, '保存'); await flushPromises();
    expect(reopened).toBe(true);
    expect(createDialog(f.wrapper).props('modelValue')).toBe(true);
    stop(); f.wrapper.unmount();
  });
});
